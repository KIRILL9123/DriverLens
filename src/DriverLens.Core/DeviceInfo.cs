namespace DriverLens.Core;

public sealed class DeviceInfo
{
    public string DeviceInstanceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string DeviceClass { get; set; } = string.Empty;
    public string[] HardwareIds { get; set; } = Array.Empty<string>();
    public string[] CompatibleIds { get; set; } = Array.Empty<string>();
    public string? CurrentDriverVersion { get; set; }
    public DateOnly? CurrentDriverDate { get; set; }
    public string? CurrentProvider { get; set; }
    public bool HasProblem { get; set; }
    public int? ProblemCode { get; set; }
}
