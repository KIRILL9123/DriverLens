namespace DriverLens.Core;

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public sealed class DriverCandidate
{
    public string Id { get; set; } = string.Empty;
    public string[] Hwids { get; set; } = Array.Empty<string>();
    public string[] CompatibleIds { get; set; } = Array.Empty<string>();
    public string Provider { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateOnly ReleaseDate { get; set; }
    public int MinOsBuild { get; set; }
    public string[] SupportedArch { get; set; } = Array.Empty<string>();
    public string SourceUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string? AuthenticodePublisher { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public bool KnownGood { get; set; }
}
