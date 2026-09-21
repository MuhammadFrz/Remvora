using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Remvora.Application.Tools;

namespace Remvora.App.ViewModels;

public sealed partial class WindowsToolsViewModel : ObservableObject
{
    private readonly IWindowsToolsService _toolsService;
    private readonly IReadOnlyList<WindowsToolItem> _allTools;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsNotificationOpen { get; set; }

    [ObservableProperty]
    public partial string NotificationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity NotificationSeverity { get; set; } = InfoBarSeverity.Informational;

    public ObservableCollection<WindowsToolItem> FilteredTools { get; } = [];

    public WindowsToolsViewModel(IWindowsToolsService toolsService)
    {
        _toolsService = toolsService ?? throw new ArgumentNullException(nameof(toolsService));
        _allTools = _toolsService.GetAllTools();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryIndexChanged(int value) => ApplyFilter();

    [RelayCommand]
    public void LaunchTool(WindowsToolItem tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ExecuteLaunch(tool, runAsAdmin: false);
    }

    [RelayCommand]
    public void LaunchAsAdmin(WindowsToolItem tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ExecuteLaunch(tool, runAsAdmin: true);
    }

    private void ExecuteLaunch(WindowsToolItem tool, bool runAsAdmin)
    {
        var result = _toolsService.LaunchTool(tool, runAsAdmin);
        if (result.IsSuccess)
        {
            ShowNotification($"Launched {tool.Name} successfully.", InfoBarSeverity.Success);
        }
        else
        {
            ShowNotification(result.Error?.Message ?? $"Failed to launch {tool.Name}.", InfoBarSeverity.Error);
        }
    }

    private void ApplyFilter()
    {
        FilteredTools.Clear();
        var query = SearchText.Trim();

        foreach (var tool in _allTools)
        {
            // Category filter
            if (SelectedCategoryIndex == 1 && tool.Category != ToolCategory.SystemAdministration) continue;
            if (SelectedCategoryIndex == 2 && tool.Category != ToolCategory.HardwareDiagnostics) continue;
            if (SelectedCategoryIndex == 3 && tool.Category != ToolCategory.StorageMaintenance) continue;
            if (SelectedCategoryIndex == 4 && tool.Category != ToolCategory.NetworkTerminal) continue;

            // Search filter
            if (!string.IsNullOrEmpty(query))
            {
                bool matches = tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || tool.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || tool.Executable.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
            }

            FilteredTools.Add(tool);
        }
    }

    private void ShowNotification(string message, InfoBarSeverity severity)
    {
        NotificationMessage = message;
        NotificationSeverity = severity;
        IsNotificationOpen = true;
    }
}
