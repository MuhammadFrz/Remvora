using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Remvora.Application.Hunter;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public sealed partial class HunterViewModel : ObservableObject
{
    private readonly IHunterModeService _hunterService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial HunterTargetResult? CurrentTarget { get; set; }

    public bool HasTarget => CurrentTarget is not null;

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCapturingCrosshair { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial HunterTargetResult? SelectedWindowTarget { get; set; }

    public ObservableCollection<HunterTargetResult> ActiveWindowTargets { get; } = [];

    public event Action<ApplicationRecord, bool>? RequestUninstall;

    public HunterViewModel(IHunterModeService hunterService)
    {
        _hunterService = hunterService ?? throw new ArgumentNullException(nameof(hunterService));
    }

    [RelayCommand]
    public async Task LoadActiveWindowsAsync()
    {
        IsScanning = true;
        try
        {
            ActiveWindowTargets.Clear();
            var targets = await _hunterService.GetActiveWindowTargetsAsync();
            foreach (var target in targets)
            {
                ActiveWindowTargets.Add(target);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to inspect active windows: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task AcquireTargetFromPointAsync((int X, int Y) point)
    {
        IsScanning = true;
        try
        {
            var target = await _hunterService.ResolveTargetFromPointAsync(point.X, point.Y);
            if (target is not null)
            {
                CurrentTarget = target;
                ShowStatus($"Target acquired: {target.ProcessName} ({target.Confidence})", InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus("No process target found at the specified location.", InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Target resolution failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task AcquireTargetFromPathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        IsScanning = true;
        try
        {
            var target = await _hunterService.ResolveTargetFromPathAsync(filePath);
            if (target is not null)
            {
                CurrentTarget = target;
                ShowStatus($"Target acquired from file: {target.ProcessName} ({target.Confidence})", InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus($"Could not resolve an application target from '{filePath}'.", InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"File resolution error: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSelectedWindowTargetChanged(HunterTargetResult? value)
    {
        if (value is not null)
        {
            CurrentTarget = value;
            ShowStatus($"Target selected: {value.ProcessName}", InfoBarSeverity.Informational);
        }
    }

    [RelayCommand]
    public void OpenInstallLocation()
    {
        if (CurrentTarget is null) return;

        string? dir = null;
        if (!string.IsNullOrWhiteSpace(CurrentTarget.MatchedApplication?.InstallLocation) &&
            Directory.Exists(CurrentTarget.MatchedApplication.InstallLocation))
        {
            dir = CurrentTarget.MatchedApplication.InstallLocation;
        }
        else if (!string.IsNullOrWhiteSpace(CurrentTarget.ExecutablePath) &&
                 File.Exists(CurrentTarget.ExecutablePath))
        {
            dir = Path.GetDirectoryName(CurrentTarget.ExecutablePath);
        }

        if (dir is not null && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        else
        {
            ShowStatus("Target directory does not exist or is inaccessible.", InfoBarSeverity.Warning);
        }
    }

    [RelayCommand]
    public void TerminateProcess()
    {
        if (CurrentTarget?.ProcessId is null)
        {
            ShowStatus("No active process ID associated with this target.", InfoBarSeverity.Warning);
            return;
        }

        var result = _hunterService.TerminateTargetProcess(CurrentTarget.ProcessId.Value);
        if (result.IsSuccess)
        {
            ShowStatus($"Process {CurrentTarget.ProcessName} (PID: {CurrentTarget.ProcessId}) was terminated.", InfoBarSeverity.Success);
            // Refresh list
            _ = LoadActiveWindowsAsync();
        }
        else
        {
            ShowStatus(result.Error?.Message ?? "Failed to terminate process.", InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    public void CompleteUninstallTarget()
    {
        UninstallTarget(forced: false);
    }

    [RelayCommand]
    public void ForcedUninstallTarget()
    {
        UninstallTarget(forced: true);
    }

    public void UninstallTarget(bool forced)
    {
        if (CurrentTarget?.MatchedApplication is null)
        {
            ShowStatus("No application record available for uninstall.", InfoBarSeverity.Warning);
            return;
        }

        RequestUninstall?.Invoke(CurrentTarget.MatchedApplication, forced);
    }

    [RelayCommand]
    public void ClearTarget()
    {
        CurrentTarget = null;
        SelectedWindowTarget = null;
        IsStatusOpen = false;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
