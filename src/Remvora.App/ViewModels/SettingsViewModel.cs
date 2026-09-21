using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Application.Updates;
using Remvora.Contracts.Updates;

namespace Remvora.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private UpdateCheckResult? _currentUpdate;

    [ObservableProperty]
    public partial string CurrentVersion { get; set; } = "1.1.0";

    [ObservableProperty]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsUpToDate { get; set; }

    [ObservableProperty]
    public partial bool IsReadyToRestart { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Click 'Check for Updates' to query the release server.";

    [ObservableProperty]
    public partial string AvailableVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReleaseDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReleaseNotes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDeltaUpdate { get; set; }

    [ObservableProperty]
    public partial string DownloadSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    public SettingsViewModel(IUpdateService updateService)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        CurrentVersion = GetAppVersion();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        HasError = false;
        IsUpdateAvailable = false;
        IsUpToDate = false;
        IsReadyToRestart = false;
        StatusMessage = "Connecting to release server...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            _currentUpdate = result;

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                HasError = true;
                StatusMessage = $"Update Check Note: {result.ErrorMessage}";
                return;
            }

            if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                AvailableVersion = result.LatestVersion;
                ReleaseDate = result.ReleaseDate;
                ReleaseNotes = result.ReleaseNotes;
                IsDeltaUpdate = result.IsDeltaAvailable;

                var sizeFormatted = FormatBytes(result.DownloadSizeBytes);
                DownloadSizeText = result.IsDeltaAvailable
                    ? $"{sizeFormatted} (Differential Delta Patch)"
                    : $"{sizeFormatted} (Full Installer Package)";

                StatusMessage = $"New update {result.LatestVersion} is available!";
            }
            else
            {
                IsUpToDate = true;
                StatusMessage = $"You are running the latest version of Remvora (v{CurrentVersion}).";
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to check for updates: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public async Task DownloadUpdateAsync()
    {
        if (_currentUpdate == null || !_currentUpdate.IsUpdateAvailable)
        {
            return;
        }

        IsDownloading = true;
        HasError = false;
        DownloadProgress = 0;
        StatusMessage = IsDeltaUpdate
            ? "Downloading differential patch (~2 MB)..."
            : "Downloading complete package...";

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                DownloadProgress = p.PercentComplete;
                StatusMessage = $"Downloading: {FormatBytes(p.BytesReceived)} of {FormatBytes(p.TotalBytesToReceive)} ({p.PercentComplete:F0}%)";
            });

            var result = await _updateService.DownloadAndStageUpdateAsync(_currentUpdate, progress);
            if (result.IsSuccess)
            {
                IsUpdateAvailable = false;
                IsReadyToRestart = true;
                StatusMessage = "Update downloaded and cryptographically verified. Ready to apply!";
            }
            else
            {
                HasError = true;
                StatusMessage = $"Download failed: {result.Error?.Message ?? "Verification error"}";
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Download error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    public void RestartAndApply()
    {
        try
        {
            _updateService.ApplyUpdateAndRestart();
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to trigger restart: {ex.Message}";
        }
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plus = infoVer.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? infoVer[..plus] : infoVer;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private static string FormatBytes(long bytes)
    {
        return ApplicationItemViewModel.FormatBytes(bytes);
    }
}
