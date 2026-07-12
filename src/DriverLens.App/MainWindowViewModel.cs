using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriverLens.Core;

namespace DriverLens.App;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IDeviceScanner _scanner;
    private readonly IIndexRepository _indexRepository;
    private readonly IMatchingEngine _matchingEngine;
    private readonly ILocalCacheStore _cacheStore;
    private readonly IIndexSyncService _syncService;
    private readonly IInstallService _installService;
    private readonly IRestorePointService _restorePointService;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _syncStatusText = "Проверка обновлений...";
    public string SyncStatusText
    {
        get => _syncStatusText;
        set => SetProperty(ref _syncStatusText, value);
    }

    public ObservableCollection<DeviceGroupViewModel> Groups { get; } = new();

    public IAsyncRelayCommand<DeviceItemViewModel> UpdateDeviceCommand { get; }

    public Func<DeviceItemViewModel, bool, Task<bool>>? ConfirmInstallCallback { get; set; }

    private static readonly List<string> GroupOrderPriority = new()
    {
        "Net", "Display", "Media", "Bluetooth", "USB", "DiskDrive", "SCSIAdapter", "System"
    };

    public MainWindowViewModel()
    {
        _scanner = new DriverLens.Scanner.SetupApiDeviceScanner();
        _indexRepository = new DriverLens.Core.JsonFixtureIndexRepository();
        _matchingEngine = new DriverLens.Core.RankedMatchingEngine();
        _cacheStore = new DriverLens.Data.LocalCacheStore();
        _syncService = new DriverLens.Data.GithubIndexSyncService(_cacheStore);
        
        var pnpUtil = new DriverLens.Install.PnpUtilWrapper();
        _restorePointService = new DriverLens.Install.WmiRestorePointService();
        _installService = new DriverLens.Install.DriverInstallService(
            new DriverLens.Install.HttpPackageDownloader(),
            new DriverLens.Install.ExpandCabExtractor(),
            new DriverLens.Install.PnpUtilSnapshotService(pnpUtil),
            _restorePointService,
            _scanner,
            pnpUtil);

        UpdateDeviceCommand = new AsyncRelayCommand<DeviceItemViewModel>(ExecuteUpdateDeviceAsync);

        _ = LoadDataAsync();
    }

    // For testability / injection
    public MainWindowViewModel(
        IDeviceScanner scanner, 
        IIndexRepository indexRepository, 
        IMatchingEngine matchingEngine,
        ILocalCacheStore cacheStore,
        IIndexSyncService syncService,
        IInstallService installService,
        IRestorePointService restorePointService)
    {
        _scanner = scanner;
        _indexRepository = indexRepository;
        _matchingEngine = matchingEngine;
        _cacheStore = cacheStore;
        _syncService = syncService;
        _installService = installService;
        _restorePointService = restorePointService;

        UpdateDeviceCommand = new AsyncRelayCommand<DeviceItemViewModel>(ExecuteUpdateDeviceAsync);

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        SyncStatusText = "Запуск сканирования...";
        try
        {
            var devices = await _scanner.ScanAsync();
            var candidates = await _cacheStore.GetCachedCandidatesAsync();

            if (candidates == null || candidates.Count == 0)
            {
                candidates = await _indexRepository.LoadCandidatesAsync();
                SyncStatusText = "Локальный кэш пуст. Загружена встроенная база.";
            }
            else
            {
                SyncStatusText = "Загружена база из локального кэша.";
            }

            UpdateDeviceList(devices, candidates);

            _ = SyncInBackgroundAsync(devices);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading devices: {ex}");
            SyncStatusText = "Ошибка инициализации.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SyncInBackgroundAsync(IReadOnlyList<DeviceInfo> devices)
    {
        SyncStatusText = "Синхронизация базы драйверов...";
        var result = await _syncService.SyncAsync();

        switch (result)
        {
            case SyncResult.NotModified:
                SyncStatusText = "База драйверов актуальна (кэш).";
                break;

            case SyncResult.Updated:
                SyncStatusText = "База драйверов успешно обновлена с GitHub.";
                var newCandidates = await _cacheStore.GetCachedCandidatesAsync();
                UpdateDeviceList(devices, newCandidates);
                break;

            case SyncResult.VerificationFailed:
                SyncStatusText = "Внимание: подпись загруженного индекса не подтверждена!";
                break;

            case SyncResult.NetworkUnavailable:
                SyncStatusText = "Автономный режим — сервер синхронизации недоступен.";
                break;
        }
    }

    private async Task ExecuteUpdateDeviceAsync(DeviceItemViewModel? item)
    {
        if (item == null || item.Result.SelectedCandidate == null) return;

        // 1. Heuristic System Restore check
        bool restoreDisabled = !await _restorePointService.IsSystemRestoreEnabledHeuristicAsync();

        // 2. Confirmation dialog callback
        if (ConfirmInstallCallback != null)
        {
            bool confirmed = await ConfirmInstallCallback(item, restoreDisabled);
            if (!confirmed) return;
        }

        try
        {
            item.IsInstalling = true;
            item.InstallFailed = false;
            item.InstallSucceeded = false;
            item.InstallDetails = string.Empty;

            var installResult = await _installService.InstallAsync(
                item.Device,
                item.Result.SelectedCandidate,
                progress =>
                {
                    item.InstallProgressText = progress;
                });

            item.IsInstalling = false;
            item.InstallProgressText = string.Empty;
            item.InstallDetails = installResult.Details;

            if (installResult.Result == InstallResult.Success)
            {
                item.InstallSucceeded = true;
                
                // Refresh specific device in lists
                var freshDevices = await _scanner.ScanAsync();
                var freshDevice = freshDevices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId, item.Device.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
                if (freshDevice != null)
                {
                    var candidates = await _cacheStore.GetCachedCandidatesAsync();
                    var newMatchResult = _matchingEngine.Match(freshDevice, candidates);
                    item.UpdateMatchResult(newMatchResult);
                }
            }
            else
            {
                item.InstallFailed = true;
                // Add specific warning detail if restore point creation failed
                if (!installResult.RestorePointCreated)
                {
                    item.InstallDetails = "[Предупреждение: Точка восстановления не создана]\n" + item.InstallDetails;
                }
            }
        }
        catch (Exception ex)
        {
            item.IsInstalling = false;
            item.InstallFailed = true;
            item.InstallDetails = $"Критическая ошибка: {ex.Message}";
        }
    }

    private void UpdateDeviceList(IReadOnlyList<DeviceInfo> devices, IReadOnlyList<DriverCandidate> candidates)
    {
        var matchedItems = devices.Select(d =>
        {
            var result = _matchingEngine.Match(d, candidates);
            return new DeviceItemViewModel(result);
        }).ToList();

        var grouped = matchedItems.GroupBy(d => d.DeviceClass)
            .Select(g => new DeviceGroupViewModel(g.Key, g.OrderByDescending(d => d.Status == MatchStatus.UpdateAvailable).ThenBy(d => d.FriendlyName).ToList()))
            .ToList();

        var orderedGroups = grouped
            .OrderBy(g => GetGroupPriority(g.DeviceClass))
            .ThenBy(g => g.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (System.Windows.Application.Current != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateGroups(orderedGroups));
        }
        else
        {
            UpdateGroups(orderedGroups);
        }
    }

    private void UpdateGroups(List<DeviceGroupViewModel> orderedGroups)
    {
        Groups.Clear();
        foreach (var group in orderedGroups)
        {
            Groups.Add(group);
        }
    }

    private static int GetGroupPriority(string deviceClass)
    {
        int index = GroupOrderPriority.FindIndex(x => string.Equals(x, deviceClass, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }
}
