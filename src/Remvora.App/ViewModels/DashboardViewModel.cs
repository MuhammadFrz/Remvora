using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IApplicationRepository _repository;
    private readonly IApplicationDiscoveryService _discoveryService;

    [ObservableProperty]
    public partial int TotalAppsCount { get; set; }

    [ObservableProperty]
    public partial int DesktopAppsCount { get; set; }

    [ObservableProperty]
    public partial int StoreAppsCount { get; set; }

    [ObservableProperty]
    public partial string TotalInstalledSizeText { get; set; } = "0 MB";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public DashboardViewModel(
        IApplicationRepository repository,
        IApplicationDiscoveryService discoveryService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var apps = await _repository.GetAllAsync();
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
}
