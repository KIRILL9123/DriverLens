using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DriverLens.Core;

namespace DriverLens.Install;

public sealed class PnpUtilSnapshotService : ISnapshotService
{
    private readonly PnpUtilWrapper _pnpUtil;

    public PnpUtilSnapshotService(PnpUtilWrapper? pnpUtil = null)
    {
        _pnpUtil = pnpUtil ?? new PnpUtilWrapper();
    }

    public async Task<string?> SnapshotAsync(DeviceInfo device, string destDir)
    {
        string? exportedPackagePath = null;

        if (device.CurrentInfName != null)
        {
            string exportTempDir = Path.Combine(destDir, "exported");
            exportedPackagePath = await _pnpUtil.ExportDriverAsync(device.CurrentInfName, exportTempDir);
        }

        var record = new SnapshotRecord
        {
            DeviceInstanceId = device.DeviceInstanceId,
            PreviousInfName = device.CurrentInfName,
            PreviousProvider = device.CurrentProvider,
            PreviousVersion = device.CurrentDriverVersion,
            PreviousHwids = device.HardwareIds ?? Array.Empty<string>(),
            ExportedPackagePath = exportedPackagePath,
            TimestampUtc = DateTime.UtcNow
        };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var snapshotsDir = Path.Combine(localAppData, "DriverLens", "snapshots");
        var sanitizedId = SanitizeFileName(device.DeviceInstanceId);
        var targetDir = Path.Combine(snapshotsDir, sanitizedId);
        Directory.CreateDirectory(targetDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var recordFilePath = Path.Combine(targetDir, $"{timestamp}.json");

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(record, options);
        await File.WriteAllTextAsync(recordFilePath, json);

        return recordFilePath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }
}
