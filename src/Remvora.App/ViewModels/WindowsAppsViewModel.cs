using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.WindowsApps;
using Remvora.Core.Domain.WindowsApps;

namespace Remvora.App.ViewModels;

public sealed partial class WindowsAppItemViewModel : ObservableObject
{
    public WindowsAppPackage Model { get; }

    public string DisplayName => Model.DisplayName;
    public string Publisher => Model.Publisher;
    public string Version => Model.Version;
    public string PackageFamilyName => Model.PackageFamilyName;
    public string? InstallLocation => Model.InstallLocation;
    public bool IsSystemProtected => Model.IsSystemProtected;
    public bool IsBloatware => Model.IsBloatware;

    public string BloatwareCategoryText => Model.BloatwareType switch
    {
        BloatwareCategory.Sponsored => "Sponsored Adware",
        BloatwareCategory.Entertainment => "Consumer Entertainment",
        BloatwareCategory.TelemetryOrDiagnostic => "Diagnostics / Telemetry",
        BloatwareCategory.Redundant => "Redundant Pre-install",
        _ => "Standard App"
    };

    public string StatusBadgeText => IsSystemProtected
        ? "Protected System Component"
        : (IsBloatware ? $"Bloatware ({BloatwareCategoryText})" : "Store App");

    public bool CanUninstall => !IsSystemProtected;

    public WindowsAppItemViewModel(WindowsAppPackage model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }
}

public sealed partial class WindowsAppsViewModel : ObservableObject
{
    private readonly IWindowsAppsManager _appsManager;
    private readonly ILogger<WindowsAppsViewModel> _logger;
    private readonly List<WindowsAppItemViewModel> _allPackages = [];

    [ObservableProperty]
    public partial ObservableCollection<WindowsAppItemViewModel> FilteredPackages { get; set; } = [];

    [ObservableProperty]
    public partial WindowsAppItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "0 packages";

    [ObservableProperty]
    public partial string BloatwareCountText { get; set; } = "0 bloatware detected";

    public WindowsAppsViewModel(
        IWindowsAppsManager appsManager,
        ILogger<WindowsAppsViewModel> logger)
    {
        _appsManager = appsManager ?? throw new ArgumentNullException(nameof(appsManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_allPackages.Count > 0)
            return;

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ProgressText = "Querying Windows package repository...";

        try
        {
            var packages = await _appsManager.GetPackagesAsync();
            _allPackages.Clear();

            foreach (var pkg in packages)
            {
                _allPackages.Add(new WindowsAppItemViewModel(pkg));
            }

            ApplyFilter();
            ProgressText = $"Discovered {packages.Count} modern packages.";
        }
        catch (Exception ex)
        {
            ProgressText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = _allPackages.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(p =>
                p.DisplayName.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                p.Publisher.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                p.PackageFamilyName.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        query = SelectedCategory switch
        {
            "Bloatware" => query.Where(p => p.IsBloatware),
            "Store Apps" => query.Where(p => !p.IsSystemProtected && !p.IsBloatware),
            "System" => query.Where(p => p.IsSystemProtected),
            _ => query
        };

        var list = query.OrderBy(p => p.DisplayName).ToList();
        FilteredPackages = new ObservableCollection<WindowsAppItemViewModel>(list);

        int bloatCount = _allPackages.Count(p => p.IsBloatware);
        SummaryText = $"{list.Count} of {_allPackages.Count} packages";
        BloatwareCountText = $"{bloatCount} bloatware detected";

        if (SelectedItem != null && !list.Contains(SelectedItem))
        {
            SelectedItem = list.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task UninstallPackageAsync(WindowsAppItemViewModel? item)
    {
        var target = item ?? SelectedItem;
        if (target is null || target.IsSystemProtected)
            return;

        IsLoading = true;
        ProgressText = $"Removing package '{target.DisplayName}'...";

        try
        {
            var result = await _appsManager.RemovePackageAsync(target.Model);
            if (result.IsSuccess)
            {
                _allPackages.Remove(target);
                ApplyFilter();
                ProgressText = $"Successfully removed '{target.DisplayName}'.";
            }
            else
            {
                ProgressText = $"Failed: {result.Error?.Message}";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
