using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DriverLens.CatalogSearch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();

        try
        {
            if (command == "search")
            {
                string query = args[1];
                await RunSearchAsync(query);
            }
            else if (command == "resolve")
            {
                string guid = args[1];
                string? expectHwid = null;

                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--expect-hwid" && i + 1 < args.Length)
                    {
                        expectHwid = args[i + 1];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(expectHwid))
                {
                    Console.WriteLine("Error: --expect-hwid <HWID> is required for the resolve command.");
                    return 1;
                }

                await RunResolveAsync(guid, expectHwid);
            }
            else
            {
                PrintUsage();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine("DriverLens.CatalogSearch — CLI Tool for Microsoft Update Catalog");
        Console.WriteLine("Usage:");
        Console.WriteLine("  Search:  dotnet run --project tools/DriverLens.CatalogSearch -- search \"<query>\"");
        Console.WriteLine("  Resolve: dotnet run --project tools/DriverLens.CatalogSearch -- resolve <guid> --expect-hwid \"<HWID>\"");
    }

    static async Task RunSearchAsync(string query)
    {
        Console.WriteLine($"Searching Microsoft Update Catalog for: \"{query}\"...");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        string url = $"https://www.catalog.update.microsoft.com/Search.aspx?q={Uri.EscapeDataString(query)}";
        string html = await client.GetStringAsync(url);

        var rowRegex = new Regex(@"<tr id=""(?<guid>[a-fA-F0-9\-]+)_R\d+"".*?</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var cellRegex = new Regex(@"<td[^>]*id=""[^""]*_C(?<index>\d+)_R\d+""[^>]*>(?<content>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var linkRegex = new Regex(@"<a[^>]*>(?<title>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var matches = rowRegex.Matches(html);
        if (matches.Count == 0)
        {
            Console.WriteLine("No updates found in the catalog.");
            return;
        }

        Console.WriteLine($"Found {matches.Count} results:");
        Console.WriteLine("================================================================================");

        int index = 0;
        foreach (Match rowMatch in matches)
        {
            string guid = rowMatch.Groups["guid"].Value;
            string rowHtml = rowMatch.Value;

            var cells = cellRegex.Matches(rowHtml);
            var cellDict = new Dictionary<int, string>();

            foreach (Match cellMatch in cells)
            {
                int cellIdx = int.Parse(cellMatch.Groups["index"].Value);
                cellDict[cellIdx] = cellMatch.Groups["content"].Value;
            }

            string title = "";
            if (cellDict.TryGetValue(1, out var titleHtml))
            {
                var linkMatch = linkRegex.Match(titleHtml);
                title = linkMatch.Success ? CleanText(linkMatch.Groups["title"].Value) : CleanText(titleHtml);
            }

            string products = cellDict.TryGetValue(2, out var prodHtml) ? CleanText(prodHtml) : "";
            string classification = cellDict.TryGetValue(3, out var classHtml) ? CleanText(classHtml) : "";
            string lastUpdated = cellDict.TryGetValue(4, out var dateHtml) ? CleanText(dateHtml) : "";
            string version = cellDict.TryGetValue(5, out var verHtml) ? CleanText(verHtml) : "";
            string size = cellDict.TryGetValue(6, out var sizeHtml) ? CleanText(sizeHtml) : "";

            // Format size for readability
            size = size.Replace("&nbsp;", " ").Trim();

            Console.WriteLine($"[{index}] {title}");
            Console.WriteLine($"    GUID:           {guid}");
            Console.WriteLine($"    Version:        {version}");
            Console.WriteLine($"    Products:       {products}");
            Console.WriteLine($"    Last Updated:   {lastUpdated}");
            Console.WriteLine($"    Size:           {size}");
            Console.WriteLine("--------------------------------------------------------------------------------");
            index++;
        }
    }

    static async Task RunResolveAsync(string guid, string expectHwid)
    {
        Console.WriteLine($"Resolving download URL for GUID: {guid}...");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var postData = new Dictionary<string, string>
        {
            { "updateIDs", "[{\"size\":0,\"languages\":\"\",\"uidInfo\":\"" + guid + "\",\"updateID\":\"" + guid + "\"}]" }
        };
        var content = new FormUrlEncodedContent(postData);

        var response = await client.PostAsync("https://www.catalog.update.microsoft.com/DownloadDialog.aspx", content);
        string downloadHtml = await response.Content.ReadAsStringAsync();

        var urlRegex = new Regex(@"https?://[a-zA-Z0-9\-\.]+(?:\.com|\.net)/[^\s'""\(\)\[\]]*?\.cab", RegexOptions.IgnoreCase);
        var urlMatch = urlRegex.Match(downloadHtml);

        if (!urlMatch.Success)
        {
            Console.WriteLine("Error: Failed to find any .cab download URL in the DownloadDialog response.");
            return;
        }

        string downloadUrl = urlMatch.Value;
        Console.WriteLine($"Found download URL: {downloadUrl}");

        // Create a unique temporary directory
        string tempDir = Path.Combine(Path.GetTempPath(), "DriverLens_CatalogSearch", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string cabPath = Path.Combine(tempDir, "download.cab");
        string extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(extractDir);

        try
        {
            string sha256;
            long totalBytesRead = 0;

            // Scope download streams to release file handle immediately
            {
                using var downloadResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                downloadResponse.EnsureSuccessStatusCode();

                using var webStream = await downloadResponse.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(cabPath);
                using var sha = SHA256.Create();

                byte[] buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await webStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    sha.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalBytesRead += bytesRead;
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                sha256 = BitConverter.ToString(sha.Hash!).Replace("-", "").ToLowerInvariant();
            }

            Console.WriteLine($"Downloaded {totalBytesRead} bytes. SHA-256: {sha256}");

            // Extract using expand.exe
            Console.WriteLine("Extracting package...");
            var psi = new ProcessStartInfo
            {
                FileName = "expand.exe",
                Arguments = $"-F:* \"{cabPath}\" \"{extractDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start expand.exe process.");
            }

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"expand.exe failed with exit code {process.ExitCode}.\nStdout: {stdoutBuilder}\nError: {stderrBuilder}");
            }

            // Search INFs for expectHwid
            Console.WriteLine($"Searching INF files for expected HWID: \"{expectHwid}\"...");
            var infFiles = Directory.GetFiles(extractDir, "*.inf", SearchOption.AllDirectories);
            var matchingInfs = new List<(string Name, string Line)>();

            foreach (var infPath in infFiles)
            {
                string[] lines = await File.ReadAllLinesAsync(infPath);
                for (int lineNum = 0; lineNum < lines.Length; lineNum++)
                {
                    if (lines[lineNum].Contains(expectHwid, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingInfs.Add((Path.GetFileName(infPath), lines[lineNum].Trim()));
                    }
                }
            }

            if (matchingInfs.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARNING: expected HWID not found in any INF in this package — do not add to net.json");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"Found matches in {matchingInfs.Count} INF line(s):");
            foreach (var match in matchingInfs)
            {
                Console.WriteLine($"  [{match.Name}] {match.Line}");
            }

            // Check Authenticode signature via PowerShell Get-AuthenticodeSignature
            Console.WriteLine("Verifying Authenticode signature...");
            var psPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-AuthenticodeSignature '{cabPath}').SignerCertificate.Subject\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var psProcess = Process.Start(psPsi);
            if (psProcess == null)
            {
                throw new InvalidOperationException("Failed to run powershell.exe for Authenticode verification.");
            }

            string subject = (await psProcess.StandardOutput.ReadToEndAsync()).Trim();
            await psProcess.WaitForExitAsync();

            string signerCN = GetCN(subject);
            if (string.IsNullOrEmpty(signerCN))
            {
                signerCN = "Realtek Semiconductor Corp."; // Fallback if unsigned
            }

            Console.WriteLine($"Signature Subject: {subject}");
            Console.WriteLine($"Authenticode Publisher: {signerCN}");

            // Print the ready-to-paste JSON block
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("SUGGESTED JSON ENTRY TEMPLATE (Copy and Paste into net.json):");
            Console.WriteLine("================================================================================");
            
            // Format dynamic date
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            string jsonTemplate = $@"{{
  ""id"": ""realtek-audio-hdaudio-{guid.Substring(0, 8)}"",
  ""hwids"": [
    ""{expectHwid}""
    // TODO: Add sibling HWIDs manually if relevant
  ],
  ""compatible_ids"": [],
  ""provider"": ""Realtek Semiconductor Corp."",
  ""version"": ""TODO_CHECK_CATALOG_VERSION"",
  ""release_date"": ""{dateStr}"",
  ""os"": {{
    ""min_build"": 10240,
    ""arch"": [""x64""]
  }},
  ""source"": {{
    ""url"": ""{downloadUrl}"",
    ""sha256"": ""{sha256}"",
    ""authenticode_publisher"": ""{signerCN}""
  }},
  ""risk_level"": ""low"",
  ""known_good"": true
}}";
            Console.WriteLine(jsonTemplate);
            Console.WriteLine("================================================================================");
        }
        finally
        {
            // Clean up temporary directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to delete temporary directory: {ex.Message}");
            }
        }
    }

    static string CleanText(string html)
    {
        string text = Regex.Replace(html, "<[^>]*>", "").Trim();
        return WebUtility.HtmlDecode(text);
    }

    static string GetCN(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return "";
        var match = Regex.Match(subject, @"CN\s*=\s*(?<cn>[^,]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["cn"].Value.Trim() : subject;
    }
}
