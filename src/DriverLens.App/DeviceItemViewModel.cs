using CommunityToolkit.Mvvm.ComponentModel;
using DriverLens.Core;

namespace DriverLens.App;

public sealed partial class DeviceItemViewModel : ObservableObject
{
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public DeviceInfo Device => Result.Device;
    public MatchResult Result { get; }

    public DeviceItemViewModel(MatchResult result)
    {
        Result = result;
    }

    public string FriendlyName => Device.FriendlyName;
    public string DeviceClass => Device.DeviceClass;

    public string CurrentDriverText => 
        (Device.CurrentDriverVersion != null || Device.CurrentProvider != null)
        ? $"{Device.CurrentProvider ?? "Unknown"} {Device.CurrentDriverVersion ?? "unknown version"} ({Device.CurrentDriverDate?.ToString("yyyy-MM-dd") ?? "unknown date"})"
        : "No driver installed";

    public string ProposedDriverText => 
        Result.SelectedCandidate != null
        ? $"{Result.SelectedCandidate.Provider} {Result.SelectedCandidate.Version} ({Result.SelectedCandidate.ReleaseDate:yyyy-MM-dd})"
        : "Not available";

    public MatchStatus Status => Result.Status;

    public string StatusText => Status switch
    {
        MatchStatus.UpToDate => "Up to Date",
        MatchStatus.UpdateAvailable => "Update Available",
        MatchStatus.NoSafeCandidate => "No Safe Candidate",
        MatchStatus.NotInIndex => "Not In Index",
        _ => "Unknown"
    };

    public string Reason => Result.Reason;

    public bool HasProposedDriver => Result.SelectedCandidate != null;
    public bool IsNotInIndex => Status == MatchStatus.NotInIndex;
}
