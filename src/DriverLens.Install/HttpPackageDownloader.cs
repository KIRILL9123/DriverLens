using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DriverLens.Core;

namespace DriverLens.Install;

public sealed class HttpPackageDownloader : IPackageDownloader
{
    private readonly HttpClient _httpClient;
    private const long MaxFileSize = 200 * 1024 * 1024; // 200 MB

    public HttpPackageDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> DownloadAsync(DriverCandidate candidate, string destDir)
    {
        Directory.CreateDirectory(destDir);
        
        string fileName = "package.cab";
        try
        {
            var uri = new Uri(candidate.SourceUrl);
            var path = uri.LocalPath;
            var parsedName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parsedName))
            {
                fileName = parsedName;
            }
        }
        catch
        {
            fileName = $"{candidate.Id}.cab";
        }

        string tempFilePath = Path.Combine(destDir, $"{fileName}.tmp");
        string finalFilePath = Path.Combine(destDir, fileName);

        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }

        try
        {
            using var response = await _httpClient.GetAsync(candidate.SourceUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxFileSize)
            {
                throw new InvalidOperationException($"Download size limit exceeded. Max: {MaxFileSize} bytes, actual: {contentLength.Value} bytes.");
            }

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[8192];
            int bytesRead;
            long totalBytesRead = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > MaxFileSize)
                {
                    throw new InvalidOperationException($"Download size limit exceeded during stream. Max: {MaxFileSize} bytes.");
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);
                sha256.AppendData(buffer, 0, bytesRead);
            }

            await fileStream.FlushAsync();
            fileStream.Close();

            var hashBytes = sha256.GetHashAndReset();
            var computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            if (!string.Equals(computedHash, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                throw new InvalidOperationException($"Hash mismatch! Expected: {candidate.Sha256}, actual: {computedHash}");
            }

            if (File.Exists(finalFilePath))
            {
                File.Delete(finalFilePath);
            }
            File.Move(tempFilePath, finalFilePath);

            return finalFilePath;
        }
        catch
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            throw;
        }
    }
}
