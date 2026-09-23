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
    public partial string CurrentVersion { get; set; } = "1.2.2";

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

    [ObservableProperty]
    public partial string GitHubTokenInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitHubTokenStatus { get; set; } = "Public access only";

    [ObservableProperty]
    public partial bool HasConfiguredToken { get; set; }

    public string AboutVersionText => $"Version {CurrentVersion} • .NET 10 LTS • Windows App SDK 2.5.1";

    public SettingsViewModel(IUpdateService updateService)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        CurrentVersion = GetAppVersion();
        RefreshTokenStatus();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        HasError = false;
        IsUpdateAvailable = false;
        IsUpToDate = false;
        IsReadyToRestart = false;
        StatusMessage = "Connecting to release endpoints...";

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
    public void ReinstallOrRepair()
    {
        if (_currentUpdate != null && !string.IsNullOrWhiteSpace(_currentUpdate.DownloadUrl))
        {
            IsUpToDate = false;
            IsUpdateAvailable = true;
            AvailableVersion = _currentUpdate.LatestVersion;
            ReleaseDate = _currentUpdate.ReleaseDate;
            ReleaseNotes = string.IsNullOrWhiteSpace(_currentUpdate.ReleaseNotes)
                ? "Reinstalling current release package."
                : _currentUpdate.ReleaseNotes;
            IsDeltaUpdate = _currentUpdate.IsDeltaAvailable;

            var sizeFormatted = FormatBytes(_currentUpdate.DownloadSizeBytes);
            DownloadSizeText = _currentUpdate.IsDeltaAvailable
                ? $"{sizeFormatted} (Differential Delta Patch)"
                : $"{sizeFormatted} (Full Installer Package)";

            StatusMessage = $"Ready to re-download and reinstall Remvora v{AvailableVersion}.";
        }
        else
        {
            StatusMessage = "Please click 'Check for Updates' first to locate the release packages.";
        }
    }

    [RelayCommand]
    public async Task DownloadUpdateAsync()
    {
        if (_currentUpdate == null || string.IsNullOrWhiteSpace(_currentUpdate.DownloadUrl))
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
                StatusMessage = "Update downloaded and cryptographically verified. Ready to install!";
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
            StatusMessage = "Installing update files and restarting Remvora...";
            _updateService.ApplyUpdateAndRestart();
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to install update: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SaveGitHubToken()
    {
        if (string.IsNullOrWhiteSpace(GitHubTokenInput))
        {
            ClearGitHubToken();
            return;
        }

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "Remvora");
            Directory.CreateDirectory(dir);
            var tokenFile = Path.Combine(dir, "github_token.txt");
            File.WriteAllText(tokenFile, GitHubTokenInput.Trim());
            HasConfiguredToken = true;
            GitHubTokenStatus = "Configured (Active)";
            StatusMessage = "GitHub Personal Access Token saved successfully.";
            GitHubTokenInput = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save token: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearGitHubToken()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var tokenFile = Path.Combine(localAppData, "Remvora", "github_token.txt");
            if (File.Exists(tokenFile))
            {
                File.Delete(tokenFile);
            }
            HasConfiguredToken = false;
            GitHubTokenStatus = "Public access only";
            StatusMessage = "GitHub token removed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to remove token: {ex.Message}";
        }
    }

    private void RefreshTokenStatus()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tokenFile = Path.Combine(localAppData, "Remvora", "github_token.txt");
        if (File.Exists(tokenFile))
        {
            HasConfiguredToken = true;
            GitHubTokenStatus = "Configured (Active)";
            return;
        }

        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN")
            ?? Environment.GetEnvironmentVariable("REMVORA_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            HasConfiguredToken = true;
            GitHubTokenStatus = "Environment variable (Active)";
            return;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (File.Exists(Path.Combine(userProfile, "Desktop", "gh", ".env")) ||
            File.Exists(Path.Combine(userProfile, ".remvora", "token.txt")))
        {
            HasConfiguredToken = true;
            GitHubTokenStatus = "Discovered in local config";
            return;
        }

        HasConfiguredToken = false;
        GitHubTokenStatus = "Public access only";
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIdx = infoVer.IndexOf('+', StringComparison.Ordinal);
            return plusIdx > 0 ? infoVer[..plusIdx] : infoVer;
        }

        var ver = assembly.GetName().Version;
        if (ver != null && ver.Major > 0)
        {
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }

        return "1.2.2";
    }

    private static string FormatBytes(long bytes)
    {
        return ApplicationItemViewModel.FormatBytes(bytes);
    }
}
