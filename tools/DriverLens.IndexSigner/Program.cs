using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DriverLens.IndexSigner;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            if (args[0] == "--generate-keypair")
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Error: Missing output directory argument.");
                    return 1;
                }
                return GenerateKeyPair(args[1]);
            }
            else if (args[0] == "--sign")
            {
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("Error: Missing arguments for sign mode. Required: <inputShardJsonPath> <privateKeyPemPath> <outputSignedJsonPath>");
                    return 1;
                }
                return SignShard(args[1], args[2], args[3]);
            }
            else
            {
                Console.Error.WriteLine($"Error: Unknown command '{args[0]}'");
                PrintUsage();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled exception: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("DriverLens IndexSigner Tool");
        Console.WriteLine("Usage:");
        Console.WriteLine("  --generate-keypair <outputDir>");
        Console.WriteLine("  --sign <inputShardJsonPath> <privateKeyPemPath> <outputSignedJsonPath>");
    }

    static int GenerateKeyPair(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            string privateKeyPem = ecdsa.ExportECPrivateKeyPem();
            string publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();

            string privateKeyPath = Path.Combine(outputDir, "devkey.private.pem");
            string publicKeyPath = Path.Combine(outputDir, "devkey.public.pem");

            File.WriteAllText(privateKeyPath, privateKeyPem);
            File.WriteAllText(publicKeyPath, publicKeyPem);

            Console.WriteLine($"Private key exported to: {privateKeyPath}");
            Console.WriteLine($"Public key exported to: {publicKeyPath}");

            byte[] publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            StringBuilder sb = new StringBuilder();
            sb.Append("public static readonly byte[] TrustedPublicKey = { ");
            for (int i = 0; i < publicKeyBytes.Length; i++)
            {
                sb.Append($"0x{publicKeyBytes[i]:X2}");
                if (i < publicKeyBytes.Length - 1)
                {
                    sb.Append(", ");
                }
            }
            sb.Append(" };");

            Console.WriteLine("\nPaste the following C# code into SignedIndexVerifier.cs:\n");
            Console.WriteLine(sb.ToString());
            Console.WriteLine();
        }

        return 0;
    }

    static int SignShard(string inputShardJsonPath, string privateKeyPemPath, string outputSignedJsonPath)
    {
        if (!File.Exists(inputShardJsonPath))
        {
            Console.Error.WriteLine($"Error: Input shard file not found: {inputShardJsonPath}");
            return 1;
        }

        if (!File.Exists(privateKeyPemPath))
        {
            Console.Error.WriteLine($"Error: Private key PEM file not found: {privateKeyPemPath}");
            return 1;
        }

        string rawJson = File.ReadAllText(inputShardJsonPath, Encoding.UTF8);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(rawJson);

        string privateKeyPem = File.ReadAllText(privateKeyPemPath);

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportFromPem(privateKeyPem);
            byte[] signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);
            string base64Signature = Convert.ToBase64String(signatureBytes);

            var signedShard = new SignedShardModel
            {
                algorithm = "ECDSA-P256-SHA256",
                payload = rawJson,
                signature = base64Signature
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string signedJson = JsonSerializer.Serialize(signedShard, options);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputSignedJsonPath))!);
            File.WriteAllText(outputSignedJsonPath, signedJson, Encoding.UTF8);

            Console.WriteLine($"Successfully signed and written to: {outputSignedJsonPath}");
        }

        return 0;
    }

    private sealed class SignedShardModel
    {
        public string algorithm { get; set; } = "ECDSA-P256-SHA256";
        public string payload { get; set; } = string.Empty;
        public string signature { get; set; } = string.Empty;
    }
}
