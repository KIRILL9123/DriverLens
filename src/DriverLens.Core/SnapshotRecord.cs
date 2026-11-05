using System;

namespace DriverLens.Core;

public sealed class SnapshotRecord
{
    public string DeviceInstanceId { get; set; } = string.Empty;
    public string? PreviousInfName { get; set; }
    public string? PreviousProvider { get; set; }
    public string? PreviousVersion { get; set; }
    public string[] PreviousHwids { get; set; } = Array.Empty<string>();
    public string? ExportedPackagePath { get; set; }
    public DateTime TimestampUtc { get; set; }
}
