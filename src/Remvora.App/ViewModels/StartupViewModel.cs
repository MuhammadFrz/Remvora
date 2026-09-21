using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Startup;
using Remvora.Core.Domain.Startup;

namespace Remvora.App.ViewModels;

public sealed partial class StartupItemViewModel : ObservableObject
{
    public StartupEntry Model { get; }

    public string Name => Model.Name;
    public string Command => Model.Command;
    public string? ExecutablePath => Model.ExecutablePath;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "Unknown Publisher" : Model.Publisher;
    public StartupLocationType LocationType => Model.LocationType;
    public string LocationDescription => Model.LocationType switch
    {
        StartupLocationType.RegistryCurrentUser => "Registry (User)",
        StartupLocationType.RegistryLocalMachine => "Registry (Machine)",
        StartupLocationType.StartupFolderUser => "Startup Folder (User)",
        StartupLocationType.StartupFolderCommon => "Startup Folder (All Users)",
        StartupLocationType.TaskScheduler => "Task Scheduler",
        _ => "Unknown"
    };

    public string ImpactDescription => Model.Impact switch
    {
        StartupImpact.High => "High Impact",
        StartupImpact.Medium => "Medium Impact",
        StartupImpact.Low => "Low Impact",
        _ => "No Impact"
    };

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public StartupItemViewModel(StartupEntry model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        IsEnabled = model.IsEnabled;
    }
}

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupManager _startupManager;
    private readonly ILogger<StartupViewModel> _logger;
    private readonly List<StartupItemViewModel> _allEntries = [];

    [ObservableProperty]
    public partial ObservableCollection<StartupItemViewModel> FilteredEntries { get; set; } = [];

    [ObservableProperty]
    public partial StartupItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedFilter { get; set; } = "All";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "0 items";

    [ObservableProperty]
    public partial string EnabledCountText { get; set; } = "0 enabled";

    [ObservableProperty]
    public partial string HighImpactCountText { get; set; } = "0 high impact";

    public StartupViewModel(
        IStartupManager startupManager,
        ILogger<StartupViewModel> logger)
    {
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_allEntries.Count > 0)
            return;

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            var entries = await _startupManager.GetStartupEntriesAsync();
            _allEntries.Clear();

            foreach (var entry in entries)
            {
                _allEntries.Add(new StartupItemViewModel(entry));
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            SummaryText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = _allEntries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(e =>
                e.Name.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                e.Publisher.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                e.Command.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        query = SelectedFilter switch
        {
            "Enabled" => query.Where(e => e.IsEnabled),
            "Disabled" => query.Where(e => !e.IsEnabled),
            "Registry" => query.Where(e => e.LocationType is StartupLocationType.RegistryCurrentUser or StartupLocationType.RegistryLocalMachine),
            "Folder" => query.Where(e => e.LocationType is StartupLocationType.StartupFolderUser or StartupLocationType.StartupFolderCommon),
            "High Impact" => query.Where(e => e.Model.Impact == StartupImpact.High),
            _ => query
        };

        var list = query.OrderBy(e => e.Name).ToList();
        FilteredEntries = new ObservableCollection<StartupItemViewModel>(list);

        int enabledCount = _allEntries.Count(e => e.IsEnabled);
        int highImpactCount = _allEntries.Count(e => e.Model.Impact == StartupImpact.High);

        SummaryText = $"{list.Count} of {_allEntries.Count} startup programs";
        EnabledCountText = $"{enabledCount} enabled";
        HighImpactCountText = $"{highImpactCount} high impact";

        if (SelectedItem != null && !list.Contains(SelectedItem))
        {
            SelectedItem = list.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task ToggleEnabledAsync(StartupItemViewModel? item)
    {
        var target = item ?? SelectedItem;
        if (target is null)
            return;

        bool newState = !target.IsEnabled;
        var result = await _startupManager.SetEnabledAsync(target.Model, newState);

        if (result.IsSuccess)
        {
            target.IsEnabled = newState;
            ApplyFilter();
        }
    }

    [RelayCommand]
    public async Task RemoveEntryAsync(StartupItemViewModel? item)
    {
        var target = item ?? SelectedItem;
        if (target is null)
            return;

        var result = await _startupManager.RemoveEntryAsync(target.Model);
        if (result.IsSuccess)
        {
            _allEntries.Remove(target);
            ApplyFilter();
        }
    }
}
