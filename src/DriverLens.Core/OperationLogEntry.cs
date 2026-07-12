using System;
using System.Text.Json.Serialization;

namespace DriverLens.Core;

public sealed class OperationLogEntry
{
    public DateTime TimestampUtc { get; set; }
    public string DeviceInstanceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public string AttemptedVersion { get; set; } = string.Empty;
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InstallResult Result { get; set; }
    public string? SnapshotPath { get; set; }
    public bool RestorePointCreated { get; set; }
    public string Details { get; set; } = string.Empty;
}
