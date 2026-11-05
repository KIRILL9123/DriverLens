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
    public MatchResult Result { get; private set; }

    public DeviceItemViewModel(MatchResult result)
    {
        Result = result;
    }

    public void UpdateMatchResult(MatchResult newResult)
    {
        Result = newResult;
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(CurrentDriverText));
        OnPropertyChanged(nameof(ProposedDriverText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(HasProposedDriver));
        OnPropertyChanged(nameof(IsNotInIndex));
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

    // Installation Progress Properties
    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetProperty(ref _isInstalling, value);
    }

    private string _installProgressText = string.Empty;
    public string InstallProgressText
    {
        get => _installProgressText;
        set => SetProperty(ref _installProgressText, value);
    }

    private bool _installFailed;
    public bool InstallFailed
    {
        get => _installFailed;
        set => SetProperty(ref _installFailed, value);
    }

    private bool _installSucceeded;
    public bool InstallSucceeded
    {
        get => _installSucceeded;
        set => SetProperty(ref _installSucceeded, value);
    }

    private string _installDetails = string.Empty;
    public string InstallDetails
    {
        get => _installDetails;
        set => SetProperty(ref _installDetails, value);
    }

    private bool _showDetailsExpanded;
    public bool ShowDetailsExpanded
    {
        get => _showDetailsExpanded;
        set => SetProperty(ref _showDetailsExpanded, value);
    }
}
