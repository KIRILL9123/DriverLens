using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DriverLens.Core;

namespace DriverLens.App;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IDeviceScanner _scanner;
    private readonly IIndexRepository _indexRepository;
    private readonly IMatchingEngine _matchingEngine;
    private readonly ILocalCacheStore _cacheStore;
    private readonly IIndexSyncService _syncService;

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

        _ = LoadDataAsync();
    }

    // For testability / injection
    public MainWindowViewModel(
        IDeviceScanner scanner, 
        IIndexRepository indexRepository, 
        IMatchingEngine matchingEngine,
        ILocalCacheStore cacheStore,
        IIndexSyncService syncService)
    {
        _scanner = scanner;
        _indexRepository = indexRepository;
        _matchingEngine = matchingEngine;
        _cacheStore = cacheStore;
        _syncService = syncService;

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

            // Trigger background sync
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
