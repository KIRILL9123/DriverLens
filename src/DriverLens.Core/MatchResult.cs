namespace DriverLens.Core;

public enum MatchTier
{
    ExactHardwareId,
    CompatibleId,
    None
}

public enum MatchStatus
{
    UpToDate,
    UpdateAvailable,
    NoSafeCandidate,
    NotInIndex
}

public sealed class MatchResult
{
    public DeviceInfo Device { get; set; } = null!;
    public DriverCandidate? SelectedCandidate { get; set; }
    public MatchTier MatchTier { get; set; }
    public MatchStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
}
