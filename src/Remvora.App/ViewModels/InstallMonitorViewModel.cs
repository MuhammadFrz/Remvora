using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Remvora.Application.Monitoring;
using Remvora.Core.Domain.Monitoring;

namespace Remvora.App.ViewModels;

public sealed partial class InstallMonitorViewModel : ObservableObject
{
    private readonly IInstallationMonitorService _monitorService;

    [ObservableProperty]
    public partial string NewSessionName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SelectedInstallerPath { get; set; }

    [ObservableProperty]
    public partial bool IsMonitoringActive { get; set; }

    [ObservableProperty]
    public partial InstallationSession? ActiveSession { get; set; }

    [ObservableProperty]
    public partial InstallationSession? SelectedSavedSession { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    public ObservableCollection<InstallationSession> SavedSessions { get; } = [];

    public InstallMonitorViewModel(IInstallationMonitorService monitorService)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        IsMonitoringActive = _monitorService.IsMonitoringActive;
        ActiveSession = _monitorService.ActiveSession;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        IsLoading = true;
        try
        {
            SavedSessions.Clear();
            var sessions = await _monitorService.GetSavedSessionsAsync();
            foreach (var session in sessions)
            {
                SavedSessions.Add(session);
            }

            if (SelectedSavedSession is null && SavedSessions.Count > 0)
            {
                SelectedSavedSession = SavedSessions[0];
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load sessions: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StartMonitoringAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSessionName))
        {
            ShowStatus("Please enter an application or session name.", InfoBarSeverity.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            ActiveSession = await _monitorService.StartMonitoringAsync(NewSessionName, SelectedInstallerPath);
            IsMonitoringActive = _monitorService.IsMonitoringActive;
            ShowStatus($"Monitoring session '{NewSessionName}' started. Baseline snapshot captured.", InfoBarSeverity.Success);
            NewSessionName = string.Empty;
            SelectedInstallerPath = null;
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to start monitoring: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StopMonitoringAsync()
    {
        IsLoading = true;
        try
        {
            var completed = await _monitorService.StopMonitoringAsync();
            IsMonitoringActive = false;
            ActiveSession = null;
            ShowStatus($"Session '{completed.SessionName}' completed! {completed.TotalChangesCount} changes captured.", InfoBarSeverity.Success);
            await LoadSessionsAsync();
            SelectedSavedSession = completed;
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to stop monitoring: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSessionAsync(InstallationSession? session)
    {
        var target = session ?? SelectedSavedSession;
        if (target is null) return;

        var result = await _monitorService.DeleteSessionAsync(target.Id);
        if (result.IsSuccess)
        {
            SavedSessions.Remove(target);
            if (SelectedSavedSession == target)
            {
                SelectedSavedSession = SavedSessions.Count > 0 ? SavedSessions[0] : null;
            }
            ShowStatus("Installation log deleted.", InfoBarSeverity.Informational);
        }
        else
        {
            ShowStatus(result.Error?.Message ?? "Failed to delete session log.", InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
