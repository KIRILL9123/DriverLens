using System;
using System.Threading.Tasks;

namespace DriverLens.Core;

public enum InstallResult
{
    Success,
    PreflightAdminRequired,
    DownloadFailed,
    HashMismatch,
    ExtractionFailed,
    UnsupportedPackageStructure,
    SnapshotFailed,
    InstallCommandFailed,
    PostInstallVerificationFailed
}

public interface IInstallService
{
    Task<(InstallResult Result, string Details, bool RestorePointCreated, string? SnapshotPath)> InstallAsync(
        DeviceInfo device, 
        DriverCandidate candidate, 
        Action<string> progressCallback);
}
