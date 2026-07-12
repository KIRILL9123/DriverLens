using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DriverLens.Core;

namespace DriverLens.Install;

public sealed class DriverInstallService : IInstallService
{
    private readonly IPackageDownloader _downloader;
    private readonly ICabExtractor _extractor;
    private readonly ISnapshotService _snapshotService;
    private readonly IRestorePointService _restorePointService;
    private readonly IDeviceScanner _scanner;
    private readonly PnpUtilWrapper _pnpUtil;
    private readonly bool _bypassAdminCheck;

    private static readonly object LogLock = new();

    public DriverInstallService(
        IPackageDownloader downloader,
        ICabExtractor extractor,
        ISnapshotService snapshotService,
        IRestorePointService restorePointService,
        IDeviceScanner scanner,
        PnpUtilWrapper? pnpUtil = null,
        bool bypassAdminCheck = false)
    {
        _downloader = downloader;
        _extractor = extractor;
        _snapshotService = snapshotService;
        _restorePointService = restorePointService;
        _scanner = scanner;
        _pnpUtil = pnpUtil ?? new PnpUtilWrapper();
        _bypassAdminCheck = bypassAdminCheck;
    }

    public async Task<(InstallResult Result, string Details, bool RestorePointCreated, string? SnapshotPath)> InstallAsync(
        DeviceInfo device,
        DriverCandidate candidate,
        Action<string> progressCallback)
    {
        bool restorePointCreated = false;
        string? snapshotPath = null;
        InstallResult result = InstallResult.Success;
        string details = "Driver installed successfully.";

        string tempSessionDir = Path.Combine(Path.GetTempPath(), "DriverLens", Guid.NewGuid().ToString());
        string downloadDir = Path.Combine(tempSessionDir, "download");
        string extractDir = Path.Combine(tempSessionDir, "extracted");

        try
        {
            // 1. Preflight - admin check
            progressCallback("Проверка прав администратора…");
            if (!_bypassAdminCheck && !IsProcessElevated())
            {
                result = InstallResult.PreflightAdminRequired;
                details = "Требуются права администратора для установки драйверов.";
                return (result, details, restorePointCreated, snapshotPath);
            }

            // 2. Download
            progressCallback("Скачивание пакета…");
            string cabFilePath;
            try
            {
                cabFilePath = await _downloader.DownloadAsync(candidate, downloadDir);
            }
            catch (Exception ex) when (ex.Message.Contains("Hash mismatch"))
            {
                result = InstallResult.HashMismatch;
                details = ex.Message;
                return (result, details, restorePointCreated, snapshotPath);
            }
            catch (Exception ex)
            {
                result = InstallResult.DownloadFailed;
                details = $"Ошибка скачивания: {ex.Message}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            // 3. Extract
            progressCallback("Распаковка пакета…");
            string infPath;
            try
            {
                infPath = await _extractor.ExtractAsync(cabFilePath, extractDir);
            }
            catch (NotSupportedException ex)
            {
                result = InstallResult.UnsupportedPackageStructure;
                details = ex.Message;
                return (result, details, restorePointCreated, snapshotPath);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("expected exactly one INF"))
            {
                result = InstallResult.UnsupportedPackageStructure;
                details = ex.Message;
                return (result, details, restorePointCreated, snapshotPath);
            }
            catch (Exception ex)
            {
                result = InstallResult.ExtractionFailed;
                details = $"Ошибка распаковки: {ex.Message}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            // 4. Snapshot
            progressCallback("Резервное копирование старого драйвера…");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var sanitizedId = SanitizeFileName(device.DeviceInstanceId);
            var backupDir = Path.Combine(localAppData, "DriverLens", "snapshots", sanitizedId, $"backup_{timestamp}");
            try
            {
                snapshotPath = await _snapshotService.SnapshotAsync(device, backupDir);
            }
            catch (Exception ex)
            {
                result = InstallResult.SnapshotFailed;
                details = $"Ошибка создания резервной копии: {ex.Message}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            // 5. Restore point (supplementary)
            progressCallback("Создание точки восстановления системы…");
            try
            {
                restorePointCreated = await _restorePointService.CreateRestorePointAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create restore point: {ex.Message}");
                // Ignore failure as it is supplementary
            }

            // 6. Install
            progressCallback("Установка драйвера…");
            var pnpResult = await _pnpUtil.AddDriverAsync(infPath);
            if (pnpResult.ExitCode != 0)
            {
                result = InstallResult.InstallCommandFailed;
                details = $"pnputil /add-driver failed with code {pnpResult.ExitCode}.\n{pnpResult.Output}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            // 7. Post-install verification
            progressCallback("Проверка установленного драйвера…");
            var freshDevices = await _scanner.ScanAsync();
            var freshDevice = freshDevices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId, device.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
            
            if (freshDevice == null)
            {
                result = InstallResult.PostInstallVerificationFailed;
                details = "Устройство не обнаружено после пересканирования системы.";
                return (result, details, restorePointCreated, snapshotPath);
            }

            if (freshDevice.HasProblem)
            {
                result = InstallResult.PostInstallVerificationFailed;
                details = $"Устройство сообщает об ошибке после установки. Код проблемы: {freshDevice.ProblemCode}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            if (string.Equals(freshDevice.CurrentDriverVersion, device.CurrentDriverVersion, StringComparison.Ordinal))
            {
                result = InstallResult.PostInstallVerificationFailed;
                details = $"Версия драйвера не изменилась и осталась прежней: {device.CurrentDriverVersion}";
                return (result, details, restorePointCreated, snapshotPath);
            }

            result = InstallResult.Success;
            details = $"Драйвер успешно обновлен с версии {device.CurrentDriverVersion} до {freshDevice.CurrentDriverVersion}.\nOutput:\n{pnpResult.Output}";
        }
        catch (Exception ex)
        {
            result = InstallResult.PostInstallVerificationFailed;
            details = $"Непредвиденная ошибка при установке: {ex.Message}";
        }
        finally
        {
            // Clean up temp session directory
            try
            {
                if (Directory.Exists(tempSessionDir))
                {
                    Directory.Delete(tempSessionDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete temp session dir: {ex.Message}");
            }

            // Write operation log
            var logEntry = new OperationLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                DeviceInstanceId = device.DeviceInstanceId,
                FriendlyName = device.FriendlyName,
                PreviousVersion = device.CurrentDriverVersion,
                AttemptedVersion = candidate.Version,
                Result = result,
                SnapshotPath = snapshotPath,
                RestorePointCreated = restorePointCreated,
                Details = details
            };

            await WriteOperationLogAsync(logEntry);
        }

        return (result, details, restorePointCreated, snapshotPath);
    }

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteOperationLogAsync(OperationLogEntry entry)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "DriverLens", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "operations.jsonl");

        var jsonLine = JsonSerializer.Serialize(entry) + "\n";
        
        await Task.Run(() =>
        {
            lock (LogLock)
            {
                File.AppendAllText(logPath, jsonLine, Encoding.UTF8);
            }
        });
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }
}
