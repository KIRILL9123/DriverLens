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

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
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

        _ = LoadDataAsync();
    }

    // For testability / injection if needed
    public MainWindowViewModel(IDeviceScanner scanner, IIndexRepository indexRepository, IMatchingEngine matchingEngine)
    {
        _scanner = scanner;
        _indexRepository = indexRepository;
        _matchingEngine = matchingEngine;

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var devices = await _scanner.ScanAsync();
            var candidates = await _indexRepository.LoadCandidatesAsync();

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading devices: {ex}");
        }
        finally
        {
            IsLoading = false;
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
