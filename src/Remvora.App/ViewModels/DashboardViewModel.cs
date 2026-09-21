using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Application.Cleaning;
using Remvora.Application.Startup;
using Remvora.Application.Stats;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Startup;
using Remvora.Core.Domain.Stats;

namespace Remvora.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IApplicationRepository _repository;
    private readonly IApplicationDiscoveryService _discoveryService;
    private readonly IStartupManager _startupManager;
    private readonly IJunkCleaner _junkCleaner;
    private readonly ICleaningStatsRepository _statsRepository;

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
    public partial string SystemDriveName { get; set; } = "C:";

    [ObservableProperty]
    public partial string DriveTotalSpaceText { get; set; } = "0 GB";

    [ObservableProperty]
    public partial string DriveUsedSpaceText { get; set; } = "0 GB";

    [ObservableProperty]
    public partial string DriveFreeSpaceText { get; set; } = "0 GB";

    [ObservableProperty]
    public partial double DriveUsedPercentage { get; set; } = 0;

    [ObservableProperty]
    public partial double DriveAppsPercentage { get; set; } = 0;

    [ObservableProperty]
    public partial double DriveJunkPercentage { get; set; } = 0;

    [ObservableProperty]
    public partial double DesktopAppsPercentage { get; set; } = 0;

    [ObservableProperty]
    public partial double StoreAppsPercentage { get; set; } = 0;

    [ObservableProperty]
    public partial int HighImpactStartupCount { get; set; }

    [ObservableProperty]
    public partial int EnabledStartupCount { get; set; }

    [ObservableProperty]
    public partial int DisabledStartupCount { get; set; }

    // Lifetime Reclamation & Cleanup Statistics
    [ObservableProperty]
    public partial string LifetimeBytesSavedText { get; set; } = "0 B";

    [ObservableProperty]
    public partial int LifetimeAppsUninstalled { get; set; }

    [ObservableProperty]
    public partial int LifetimeLeftoversCleaned { get; set; }

    [ObservableProperty]
    public partial int LifetimeJunkFilesPurged { get; set; }

    [ObservableProperty]
    public partial bool HasLifetimeStats { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public event Action<string>? RequestNavigate;

    public DashboardViewModel(
        IApplicationRepository repository,
        IApplicationDiscoveryService discoveryService,
        IStartupManager startupManager,
        IJunkCleaner junkCleaner,
        ICleaningStatsRepository statsRepository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));
        _junkCleaner = junkCleaner ?? throw new ArgumentNullException(nameof(junkCleaner));
        _statsRepository = statsRepository ?? throw new ArgumentNullException(nameof(statsRepository));
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
            var statsTask = _statsRepository.GetLifetimeStatsAsync();

            await Task.WhenAll(appsTask, startupTask, junkTask, statsTask);

            var lifetime = statsTask.Result;
            LifetimeBytesSavedText = ApplicationItemViewModel.FormatBytes(lifetime.TotalBytesSaved);
            LifetimeAppsUninstalled = lifetime.TotalAppsUninstalled;
            LifetimeLeftoversCleaned = lifetime.TotalLeftoversCleaned;
            LifetimeJunkFilesPurged = lifetime.TotalJunkAndCacheFilesPurged;
            HasLifetimeStats = lifetime.TotalBytesSaved > 0 || lifetime.TotalAppsUninstalled > 0 || lifetime.TotalLeftoversCleaned > 0 || lifetime.TotalJunkAndCacheFilesPurged > 0;

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
            HighImpactStartupCount = startups.Count(s => s.Impact == StartupImpact.High);
            EnabledStartupCount = startups.Count(s => s.IsEnabled);
            DisabledStartupCount = startups.Count(s => !s.IsEnabled);

            var junkCategories = junkTask.Result;
            long totalJunkBytes = junkCategories.Sum(c => c.TotalSizeBytes);
            JunkEstimateText = ApplicationItemViewModel.FormatBytes(totalJunkBytes);

            DesktopAppsPercentage = TotalAppsCount > 0 ? Math.Round((double)DesktopAppsCount / TotalAppsCount * 100, 1) : 0;
            StoreAppsPercentage = TotalAppsCount > 0 ? Math.Round((double)StoreAppsCount / TotalAppsCount * 100, 1) : 0;

            // Drive Metrics
            try
            {
                var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed) ?? new DriveInfo("C");
                long driveTotal = drive.TotalSize;
                long driveFree = drive.AvailableFreeSpace;
                long driveUsed = driveTotal - driveFree;

                SystemDriveName = drive.Name.TrimEnd('\\');
                DriveTotalSpaceText = ApplicationItemViewModel.FormatBytes(driveTotal);
                DriveUsedSpaceText = ApplicationItemViewModel.FormatBytes(driveUsed);
                DriveFreeSpaceText = ApplicationItemViewModel.FormatBytes(driveFree);
                DriveUsedPercentage = driveTotal > 0 ? Math.Round((double)driveUsed / driveTotal * 100, 1) : 0;
                DriveAppsPercentage = driveTotal > 0 ? Math.Min(100, Math.Round((double)totalBytes / driveTotal * 100, 1)) : 0;
                DriveJunkPercentage = driveTotal > 0 ? Math.Min(100, Math.Round((double)totalJunkBytes / driveTotal * 100, 1)) : 0;
            }
            catch
            {
                // Fallback for drive reading
            }
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
