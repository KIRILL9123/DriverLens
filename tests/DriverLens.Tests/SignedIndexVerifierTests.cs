using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using DriverLens.Core;

namespace DriverLens.Tests;

public sealed class SignedIndexVerifierTests
{
    private static readonly byte[] TestPublicKey1;
    private static readonly byte[] TestPrivateKey1;

    private static readonly byte[] TestPublicKey2;
    private static readonly byte[] TestPrivateKey2;

    static SignedIndexVerifierTests()
    {
        // Keypair 1
        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            TestPublicKey1 = ecdsa.ExportSubjectPublicKeyInfo();
            TestPrivateKey1 = ecdsa.ExportECPrivateKey();
        }

        // Keypair 2
        using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            TestPublicKey2 = ecdsa.ExportSubjectPublicKeyInfo();
            TestPrivateKey2 = ecdsa.ExportECPrivateKey();
        }
    }

    [Fact]
    public void Valid_payload_and_signature_verifies_successfully()
    {
        var payload = "{\"test\": 123}";
        var signature = SignPayload(payload, TestPrivateKey1);

        bool result = SignedIndexVerifier.Verify(payload, signature, TestPublicKey1);
        Assert.True(result);
    }

    [Fact]
    public void Modified_payload_fails_verification()
    {
        var payload = "{\"test\": 123}";
        var signature = SignPayload(payload, TestPrivateKey1);
        var tamperedPayload = "{\"test\": 124}";

        bool result = SignedIndexVerifier.Verify(tamperedPayload, signature, TestPublicKey1);
        Assert.False(result);
    }

    [Fact]
    public void Valid_payload_signed_by_different_key_fails_verification()
    {
        var payload = "{\"test\": 123}";
        var signature = SignPayload(payload, TestPrivateKey2); // Signed by key 2

        bool result = SignedIndexVerifier.Verify(payload, signature, TestPublicKey1); // Verifying using key 1
        Assert.False(result);
    }

    [Fact]
    public void Malformed_signature_string_fails_verification_and_does_not_throw()
    {
        var payload = "{\"test\": 123}";
        var malformedSignature = "not-a-valid-base-64-string-!!!";

        var exception = Record.Exception(() =>
        {
            bool result = SignedIndexVerifier.Verify(payload, malformedSignature, TestPublicKey1);
            Assert.False(result);
        });

        Assert.Null(exception);
    }

    private static string SignPayload(string payload, byte[] privateKeyBytes)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(privateKeyBytes, out _);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signatureBytes);
    }
}
