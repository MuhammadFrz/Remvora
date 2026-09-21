using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public sealed partial class AppsViewModel : ObservableObject
{
    private readonly IApplicationDiscoveryService _discoveryService;
    private readonly IApplicationRepository _repository;
    private readonly List<ApplicationItemViewModel> _allApplications = [];

    [ObservableProperty]
    public partial ObservableCollection<ApplicationItemViewModel> FilteredApplications { get; set; } = [];

    [ObservableProperty]
    public partial ApplicationItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    [ObservableProperty]
    public partial string SelectedSort { get; set; } = "Name";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "0 applications";

    [ObservableProperty]
    public partial string TotalSizeText { get; set; } = "0 MB";

    public AppsViewModel(
        IApplicationDiscoveryService discoveryService,
        IApplicationRepository repository)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_allApplications.Count > 0)
            return;

        IsLoading = true;
        ProgressText = "Loading application cache...";

        try
        {
            var cached = await _repository.GetAllAsync();
            if (cached.Count > 0)
            {
                SetApplications(cached);
            }
            else
            {
                await RefreshAsync();
            }
        }
        catch
        {
            await RefreshAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ProgressText = "Discovering installed applications...";

        var progress = new Progress<DiscoveryProgress>(p =>
        {
            ProgressText = $"Scanning {p.CurrentSource}: {p.CurrentItemName}";
        });

        try
        {
            var filter = new DiscoveryFilter
            {
                IncludePerMachine = true,
                IncludePerUser = true,
                IncludeStoreApps = true,
                IncludeSystemComponents = false
            };

            var discovered = await _discoveryService.DiscoverAllAsync(filter, progress);
            await _repository.SaveAsync(discovered);

            SetApplications(discovered);
            ProgressText = $"Discovery complete. Found {discovered.Count} applications.";
        }
        catch (Exception ex)
        {
            ProgressText = $"Discovery error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetApplications(IEnumerable<ApplicationRecord> applications)
    {
        _allApplications.Clear();
        long totalBytes = 0;

        foreach (var app in applications)
        {
            _allApplications.Add(new ApplicationItemViewModel(app));
            totalBytes += app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L;
        }

        TotalSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);
        ApplyFiltersAndSort();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedCategoryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedSortChanged(string value) => ApplyFiltersAndSort();

    private void ApplyFiltersAndSort()
    {
        var query = _allApplications.AsEnumerable();

        // 1. Search Query Filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(a =>
                a.DisplayName.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                a.Publisher.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        // 2. Category Filter
        query = SelectedCategory switch
        {
            "Desktop" => query.Where(a => !a.IsStoreApp),
            "Store" => query.Where(a => a.IsStoreApp),
            "Large" => query.Where(a => (a.Model.EstimatedSizeBytes ?? 0) >= 100 * 1024 * 1024), // >= 100MB
            _ => query
        };

        // 3. Sorting
        query = SelectedSort switch
        {
            "Publisher" => query.OrderBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase)
                                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Size" => query.OrderByDescending(a => a.Model.EstimatedSizeBytes ?? a.Model.CalculatedSizeBytes ?? 0L),
            "Date" => query.OrderByDescending(a => a.Model.InstallDate ?? DateTimeOffset.MinValue),
            _ => query.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        var list = query.ToList();
        FilteredApplications = new ObservableCollection<ApplicationItemViewModel>(list);
        SummaryText = $"{list.Count} of {_allApplications.Count} apps";

        if (SelectedItem != null && !list.Contains(SelectedItem))
        {
            SelectedItem = list.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void OpenInstallLocation()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Model.InstallLocation))
            return;

        var path = SelectedItem.Model.InstallLocation;
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
    }
}
