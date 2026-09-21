using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Application.Cleaning;
using Remvora.Application.Startup;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IApplicationRepository _repository;
    private readonly IApplicationDiscoveryService _discoveryService;
    private readonly IStartupManager _startupManager;
    private readonly IJunkCleaner _junkCleaner;

    [ObservableProperty]
    public partial int TotalAppsCount { get; set; }

    [ObservableProperty]
    public partial int DesktopAppsCount { get; set; }

    [ObservableProperty]
    public partial int StoreAppsCount { get; set; }

    [ObservableProperty]
    public partial int StartupCount { get; set; }

    [ObservableProperty]
    public partial string TotalInstalledSizeText { get; set; } = "0 MB";

    [ObservableProperty]
    public partial string JunkEstimateText { get; set; } = "0 MB";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public event Action<string>? RequestNavigate;

    public DashboardViewModel(
        IApplicationRepository repository,
        IApplicationDiscoveryService discoveryService,
        IStartupManager startupManager,
        IJunkCleaner junkCleaner)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _junkCleaner = junkCleaner ?? throw new ArgumentNullException(nameof(junkCleaner));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var appsTask = _repository.GetAllAsync();
            var startupTask = _startupManager.GetStartupEntriesAsync();
            var junkTask = _junkCleaner.ScanJunkAsync();

            await Task.WhenAll(appsTask, startupTask, junkTask);

            var apps = appsTask.Result;
            if (apps.Count == 0)
            {
                apps = await _discoveryService.DiscoverAllAsync();
                await _repository.SaveAsync(apps);
            }

            TotalAppsCount = apps.Count;
            DesktopAppsCount = apps.Count(a => a.InstallerType != InstallerType.StorePackage);
            StoreAppsCount = apps.Count(a => a.InstallerType == InstallerType.StorePackage);

            long totalBytes = apps.Sum(a => a.EstimatedSizeBytes ?? a.CalculatedSizeBytes ?? 0L);
            TotalInstalledSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);

            var startups = startupTask.Result;
            StartupCount = startups.Count;

            var junkCategories = junkTask.Result;
            long totalJunkBytes = junkCategories.Sum(c => c.TotalSizeBytes);
            JunkEstimateText = ApplicationItemViewModel.FormatBytes(totalJunkBytes);
        }
        catch
        {
            // Handled gracefully
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void Navigate(string destination)
    {
        if (!string.IsNullOrWhiteSpace(destination))
        {
            RequestNavigate?.Invoke(destination);
        }
    }
}
