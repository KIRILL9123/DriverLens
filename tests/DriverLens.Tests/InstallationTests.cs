using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using DriverLens.Core;
using DriverLens.Install;

namespace DriverLens.Tests;

public sealed class InstallationTests
{
    [Fact]
    public async Task DownloadAsync_with_correct_hash_succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DriverLensTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var content = "dummy-driver-content";
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var sha256Bytes = SHA256.HashData(contentBytes);
            var expectedHash = Convert.ToHexString(sha256Bytes).ToLowerInvariant();

            var candidate = new DriverCandidate
            {
                Id = "test-pkg",
                SourceUrl = "https://example.com/test-pkg.cab",
                Sha256 = expectedHash
            };

            var httpHandler = new MockHttpMessageHandler
            {
                HandlerFunc = req =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(contentBytes)
                    };
                    return Task.FromResult(response);
                }
            };

            var downloader = new HttpPackageDownloader(new HttpClient(httpHandler));
            var verifiedPath = await downloader.DownloadAsync(candidate, tempDir);

            Assert.True(File.Exists(verifiedPath));
            var downloadedText = await File.ReadAllTextAsync(verifiedPath);
            Assert.Equal(content, downloadedText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadAsync_with_incorrect_hash_throws_and_deletes_temp_file()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DriverLensTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var content = "dummy-driver-content";
            var contentBytes = Encoding.UTF8.GetBytes(content);

            var candidate = new DriverCandidate
            {
                Id = "test-pkg",
                SourceUrl = "https://example.com/test-pkg.cab",
                Sha256 = "wronghash1234567890123456789012345678901234567890123456789012345"
            };

            var httpHandler = new MockHttpMessageHandler
            {
                HandlerFunc = req =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(contentBytes)
                    };
                    return Task.FromResult(response);
                }
            };

            var downloader = new HttpPackageDownloader(new HttpClient(httpHandler));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await downloader.DownloadAsync(candidate, tempDir);
            });

            Assert.Contains("Hash mismatch", exception.Message);

            // Verify no temp file or final file remains in directory
            var remainingFiles = Directory.GetFiles(tempDir);
            Assert.Empty(remainingFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SnapshotRecord_mapping_produces_correct_output_with_and_without_prior_inf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DriverLensTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Case 1: Prior INF present
            var deviceWithInf = new DeviceInfo
            {
                DeviceInstanceId = "PCI\\VEN_1234&DEV_5678",
                FriendlyName = "Test PCI Device",
                CurrentInfName = "oem42.inf",
                CurrentProvider = "Intel",
                CurrentDriverVersion = "10.1.2.3",
                HardwareIds = new[] { "PCI\\VEN_1234&DEV_5678" }
            };

            var mockPnpUtil = new MockPnpUtilWrapper();
            var snapshotService = new PnpUtilSnapshotService(mockPnpUtil);

            var recordPathWithInf = await snapshotService.SnapshotAsync(deviceWithInf, tempDir);
            Assert.NotNull(recordPathWithInf);
            Assert.True(File.Exists(recordPathWithInf));

            var recordJsonWithInf = await File.ReadAllTextAsync(recordPathWithInf);
            var recordWithInf = JsonSerializer.Deserialize<SnapshotRecord>(recordJsonWithInf);
            Assert.NotNull(recordWithInf);
            Assert.Equal("PCI\\VEN_1234&DEV_5678", recordWithInf.DeviceInstanceId);
            Assert.Equal("oem42.inf", recordWithInf.PreviousInfName);
            Assert.Equal("Intel", recordWithInf.PreviousProvider);
            Assert.Equal("10.1.2.3", recordWithInf.PreviousVersion);
            Assert.Single(recordWithInf.PreviousHwids);
            Assert.Equal("PCI\\VEN_1234&DEV_5678", recordWithInf.PreviousHwids[0]);
            Assert.NotNull(recordWithInf.ExportedPackagePath);
            Assert.Contains("exported", recordWithInf.ExportedPackagePath);
            Assert.True(mockPnpUtil.ExportCalled);

            // Case 2: Inbox driver (no prior INF)
            var deviceNoInf = new DeviceInfo
            {
                DeviceInstanceId = "USB\\VID_0000&PID_0000",
                FriendlyName = "Inbox USB Mouse",
                CurrentInfName = null,
                CurrentProvider = "Microsoft",
                CurrentDriverVersion = "10.0.19041.1",
                HardwareIds = new[] { "USB\\VID_0000&PID_0000" }
            };

            var mockPnpUtilNoInf = new MockPnpUtilWrapper();
            var snapshotServiceNoInf = new PnpUtilSnapshotService(mockPnpUtilNoInf);

            var recordPathNoInf = await snapshotServiceNoInf.SnapshotAsync(deviceNoInf, tempDir);
            Assert.NotNull(recordPathNoInf);
            Assert.True(File.Exists(recordPathNoInf));

            var recordJsonNoInf = await File.ReadAllTextAsync(recordPathNoInf);
            var recordNoInf = JsonSerializer.Deserialize<SnapshotRecord>(recordJsonNoInf);
            Assert.NotNull(recordNoInf);
            Assert.Equal("USB\\VID_0000&PID_0000", recordNoInf.DeviceInstanceId);
            Assert.Null(recordNoInf.PreviousInfName);
            Assert.Null(recordNoInf.ExportedPackagePath);
            Assert.False(mockPnpUtilNoInf.ExportCalled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OperationLogWriter_appends_valid_json_lines()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "DriverLens", "logs");
        var logPath = Path.Combine(logDir, "operations.jsonl");

        // Clear existing log file for clean testing
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var device = new DeviceInfo
        {
            DeviceInstanceId = "PCI\\VEN_1111",
            FriendlyName = "Test Log Device",
            CurrentDriverVersion = "1.0.0"
        };

        var candidate = new DriverCandidate
        {
            Id = "candidate-1",
            Version = "2.0.0"
        };

        // Instantiating DriverInstallService requires some dependencies, let's mock them minimal
        var downloader = new MockDownloader();
        var extractor = new MockExtractor();
        var snapshot = new MockSnapshot();
        var restore = new MockRestore();
        var scanner = new MockScanner();
        var pnpUtil = new MockPnpUtilWrapper { ExitCode = 0, Output = "pnputil output log" };

        var service = new DriverInstallService(downloader, extractor, snapshot, restore, scanner, pnpUtil, bypassAdminCheck: true);

        // Run successful install flow
        await service.InstallAsync(device, candidate, _ => {});

        // Run failing install flow (simulate download failure)
        downloader.ShouldFail = true;
        await service.InstallAsync(device, candidate, _ => {});

        Assert.True(File.Exists(logPath));
        var lines = await File.ReadAllLinesAsync(logPath);
        Assert.Equal(2, lines.Length);

        // Verify Line 1 (Success)
        var entry1 = JsonSerializer.Deserialize<OperationLogEntry>(lines[0]);
        Assert.NotNull(entry1);
        Assert.Equal(device.DeviceInstanceId, entry1.DeviceInstanceId);
        Assert.Equal(device.FriendlyName, entry1.FriendlyName);
        Assert.Equal("1.0.0", entry1.PreviousVersion);
        Assert.Equal("2.0.0", entry1.AttemptedVersion);
        Assert.Equal(InstallResult.Success, entry1.Result);
        Assert.True(entry1.RestorePointCreated);
        Assert.Equal("test-snapshot-path", entry1.SnapshotPath);
        Assert.Contains("Success", lines[0]); // Check enum string conversion

        // Verify Line 2 (Failure)
        var entry2 = JsonSerializer.Deserialize<OperationLogEntry>(lines[1]);
        Assert.NotNull(entry2);
        Assert.Equal(InstallResult.DownloadFailed, entry2.Result);
        Assert.Contains("Simulated download failure", entry2.Details);
        Assert.Contains("DownloadFailed", lines[1]); // Check enum string conversion
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> HandlerFunc { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            return HandlerFunc(request);
        }
    }

    private sealed class MockPnpUtilWrapper : PnpUtilWrapper
    {
        public bool ExportCalled { get; private set; }
        public int ExitCode { get; set; } = 0;
        public string Output { get; set; } = "Success";

        public override Task<string> ExportDriverAsync(string infName, string destDir)
        {
            ExportCalled = true;
            Directory.CreateDirectory(destDir);
            var exportedFilePath = Path.Combine(destDir, "exported.inf");
            File.WriteAllText(exportedFilePath, "; dummy inf");
            return Task.FromResult(destDir);
        }

        public override Task<(int ExitCode, string Output)> AddDriverAsync(string infPath)
        {
            return Task.FromResult((ExitCode, Output));
        }
    }

    private sealed class MockDownloader : IPackageDownloader
    {
        public bool ShouldFail { get; set; }

        public Task<string> DownloadAsync(DriverCandidate candidate, string destDir)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated download failure");
            }
            Directory.CreateDirectory(destDir);
            var cabPath = Path.Combine(destDir, "package.cab");
            File.WriteAllText(cabPath, "dummy cab");
            return Task.FromResult(cabPath);
        }
    }

    private sealed class MockExtractor : ICabExtractor
    {
        public Task<string> ExtractAsync(string cabPath, string destDir)
        {
            Directory.CreateDirectory(destDir);
            var infPath = Path.Combine(destDir, "driver.inf");
            File.WriteAllText(infPath, "; dummy inf");
            return Task.FromResult(infPath);
        }
    }

    private sealed class MockSnapshot : ISnapshotService
    {
        public Task<string?> SnapshotAsync(DeviceInfo device, string destDir)
        {
            return Task.FromResult<string?>("test-snapshot-path");
        }
    }

    private sealed class MockRestore : IRestorePointService
    {
        public Task<bool> CreateRestorePointAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> IsSystemRestoreEnabledHeuristicAsync()
        {
            return Task.FromResult(true);
        }
    }

    private sealed class MockScanner : IDeviceScanner
    {
        public Task<IReadOnlyList<DeviceInfo>> ScanAsync()
        {
            IReadOnlyList<DeviceInfo> list = new[]
            {
                new DeviceInfo
                {
                    DeviceInstanceId = "PCI\\VEN_1111",
                    FriendlyName = "Test Log Device",
                    CurrentDriverVersion = "2.0.0", // changed version
                    HasProblem = false
                }
            };
            return Task.FromResult(list);
        }
    }
}
