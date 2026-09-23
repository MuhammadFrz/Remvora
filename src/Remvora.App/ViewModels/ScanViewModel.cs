using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Scanning;
using Remvora.Application.Stats;
using Remvora.Core.Domain.Scanning;
using Remvora.Core.Domain.Stats;

namespace Remvora.App.ViewModels;

public sealed partial class ScanItemViewModel : ObservableObject
{
    public ScanItem Model { get; }
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string TargetPath => Model.TargetPath;
    public long SizeBytes => Model.SizeBytes;
    public string FormattedSize => Model.FormattedSize;
    public string? ExtraInfo => Model.ExtraInfo;
    public bool IsRemovable => Model.IsRemovable;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        SelectionChanged?.Invoke();
    }

    [RelayCommand]
    public void OpenLocation()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;

        try
        {
            var resolvedPath = Environment.ExpandEnvironmentVariables(TargetPath);

            if (Directory.Exists(resolvedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{resolvedPath}\"",
                    UseShellExecute = true
                });
            }
            else if (File.Exists(resolvedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{resolvedPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var parent = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{parent}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch
        {
            // Silently swallow process launch errors
        }
    }

    public ScanItemViewModel(ScanItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        IsSelected = model.IsSelected;
    }
}

public sealed partial class ScanGroupViewModel : ObservableObject
{
    private bool _isUpdatingSelection;

    public ScanCategory Category { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    public partial ObservableCollection<ScanItemViewModel> Items { get; set; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool? IsAllSelected { get; set; } = true;

    public int TotalCount => Items.Count;
    public string FormattedTotalSize => ScanItem.FormatBytes(Items.Sum(i => i.SizeBytes));

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.SizeBytes);
    public string FormattedSelectedSize => ScanItem.FormatBytes(SelectedBytes);

    public string BadgeGlyph => Category switch
    {
        ScanCategory.WindowsRedundant => "\uE770",
        ScanCategory.AppCache => "\uE81E",
        ScanCategory.OrphanedLeftovers => "\uE74D",
        ScanCategory.AppIssues => "\uE783",
        _ => "\uE773"
    };

    public event Action? GroupSelectionChanged;

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (_isUpdatingSelection || value is null) return;
        _isUpdatingSelection = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsSelected = value.Value;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        UpdateGroupMetrics();
        GroupSelectionChanged?.Invoke();
    }

    public ScanGroupViewModel(ScanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Category = group.Category;
        Title = group.Title;
        Description = group.Description;

        foreach (var item in group.Items)
        {
            var vm = new ScanItemViewModel(item);
            vm.SelectionChanged += () =>
            {
                UpdateGroupMetrics();
                GroupSelectionChanged?.Invoke();
            };
            Items.Add(vm);
        }
        UpdateGroupMetrics();
    }

    public void UpdateGroupMetrics()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(FormattedSelectedSize));

        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        try
        {
            int selected = Items.Count(i => i.IsSelected);
            if (Items.Count == 0 || selected == 0)
                IsAllSelected = false;
            else if (selected == Items.Count)
                IsAllSelected = true;
            else
                IsAllSelected = null;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    public void SetSelection(bool isSelected)
    {
        _isUpdatingSelection = true;
        try
        {
            foreach (var item in Items)
            {
                item.IsSelected = isSelected;
            }
            IsAllSelected = isSelected;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(FormattedSelectedSize));
    }

    [RelayCommand]
    public void ToggleGroupSelection()
    {
        bool targetState = IsAllSelected != true;
        SetSelection(targetState);
        GroupSelectionChanged?.Invoke();
    }
}

public sealed partial class ScanViewModel : ObservableObject, IDisposable
{
    private readonly ISystemScanService _scanService;
    private readonly ICleaningStatsRepository _statsRepository;
    private readonly ILogger<ScanViewModel> _logger;
    private CancellationTokenSource? _scanCts;
    private bool _isMasterSyncingSelection;

    // Scan Category Pre-Selection Options
    [ObservableProperty]
    public partial bool IsScanRedundantSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScanAppCacheSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScanOrphansSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScanIssuesSelected { get; set; } = true;

    [ObservableProperty]
    public partial string ScanButtonText { get; set; } = "Start Full System Scan";

    [ObservableProperty]
    public partial bool CanStartScan { get; set; } = true;

    partial void OnIsScanRedundantSelectedChanged(bool value) => UpdateScanTargetOptions();
    partial void OnIsScanAppCacheSelectedChanged(bool value) => UpdateScanTargetOptions();
    partial void OnIsScanOrphansSelectedChanged(bool value) => UpdateScanTargetOptions();
    partial void OnIsScanIssuesSelectedChanged(bool value) => UpdateScanTargetOptions();

    private void UpdateScanTargetOptions()
    {
        if (IsScanning)
        {
            CanStartScan = false;
            return;
        }

        int count = 0;
        if (IsScanRedundantSelected) count++;
        if (IsScanAppCacheSelected) count++;
        if (IsScanOrphansSelected) count++;
        if (IsScanIssuesSelected) count++;

        CanStartScan = count > 0;

        if (count == 4)
        {
            ScanButtonText = "Start Full System Scan";
        }
        else if (count == 0)
        {
            ScanButtonText = "Select Categories to Scan";
        }
        else if (count == 1)
        {
            if (IsScanOrphansSelected) ScanButtonText = "Scan Orphaned Leftovers";
            else if (IsScanRedundantSelected) ScanButtonText = "Scan Redundant Files";
            else if (IsScanAppCacheSelected) ScanButtonText = "Scan Application Caches";
            else ScanButtonText = "Scan Damaged Registrations";
        }
        else
        {
            ScanButtonText = $"Scan Selected Categories ({count})";
        }
    }

    [ObservableProperty]
    public partial ObservableCollection<ScanGroupViewModel> Groups { get; set; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial double ScanProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ScanProgressPercentText { get; set; } = "0%";

    [ObservableProperty]
    public partial string CurrentStepText { get; set; } = "Ready to scan";

    [ObservableProperty]
    public partial string CurrentTargetText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TotalFoundCount { get; set; }

    [ObservableProperty]
    public partial string TotalFoundSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial int TotalSelectedCount { get; set; }

    [ObservableProperty]
    public partial string TotalSelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial bool HasSelectedItems { get; set; }

    [ObservableProperty]
    public partial bool? IsAllSelected { get; set; } = true;

    [ObservableProperty]
    public partial string SelectionSummaryText { get; set; } = string.Empty;

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (_isMasterSyncingSelection || value is null) return;
        _isMasterSyncingSelection = true;
        try
        {
            foreach (var grp in Groups)
            {
                grp.SetSelection(value.Value);
            }
        }
        finally
        {
            _isMasterSyncingSelection = false;
        }
        UpdateMetrics();
    }

    // Cleaning Result Modal Properties
    [ObservableProperty]
    public partial bool IsCleanSummaryOpen { get; set; }

    [ObservableProperty]
    public partial int CleanResultItemsRemoved { get; set; }

    [ObservableProperty]
    public partial string CleanResultBytesReclaimedText { get; set; } = "0 B";

    [ObservableProperty]
    public partial ObservableCollection<string> CleanResultErrors { get; set; } = [];

    public ScanViewModel(
        ISystemScanService scanService,
        ICleaningStatsRepository statsRepository,
        ILogger<ScanViewModel> logger)
    {
        _scanService = scanService ?? throw new ArgumentNullException(nameof(scanService));
        _statsRepository = statsRepository ?? throw new ArgumentNullException(nameof(statsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task StartScanAsync()
    {
        if (!CanStartScan) return;

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsScanning = true;
        CanStartScan = false;
        HasScanned = false;
        ScanProgressPercent = 0;
        CurrentStepText = "Initializing deep system scan...";
        CurrentTargetText = string.Empty;
        Groups.Clear();

        var selectedCats = new List<ScanCategory>();
        if (IsScanRedundantSelected) selectedCats.Add(ScanCategory.WindowsRedundant);
        if (IsScanAppCacheSelected) selectedCats.Add(ScanCategory.AppCache);
        if (IsScanOrphansSelected) selectedCats.Add(ScanCategory.OrphanedLeftovers);
        if (IsScanIssuesSelected) selectedCats.Add(ScanCategory.AppIssues);

        var progress = new Progress<ScanProgressReport>(report =>
        {
            CurrentStepText = report.CurrentStep;
            CurrentTargetText = report.CurrentTarget;
            ScanProgressPercent = report.PercentComplete;
            ScanProgressPercentText = $"{report.PercentComplete}%";
            TotalFoundCount = report.ItemsFound;
            TotalFoundSizeText = ScanItem.FormatBytes(report.BytesFound);
        });

        try
        {
            var rawGroups = await _scanService.ScanSystemAsync(selectedCats, progress, token).ConfigureAwait(true);

            Groups.Clear();
            foreach (var g in rawGroups)
            {
                var groupVm = new ScanGroupViewModel(g);
                groupVm.GroupSelectionChanged += UpdateMetrics;
                Groups.Add(groupVm);
            }

            HasScanned = true;
            CurrentStepText = $"Scan complete. Found {TotalFoundCount} items ({TotalFoundSizeText}).";
            CurrentTargetText = string.Empty;
            ScanProgressPercent = 100;
            ScanProgressPercentText = "100%";
            UpdateMetrics();
        }
        catch (OperationCanceledException)
        {
            CurrentStepText = "Scan cancelled.";
            CurrentTargetText = string.Empty;
        }
        catch (Exception ex)
        {
            LogScanError(_logger, ex.Message, ex);
            CurrentStepText = $"Scan error: {ex.Message}";
            CurrentTargetText = string.Empty;
        }
        finally
        {
            IsScanning = false;
            UpdateScanTargetOptions();
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    public async Task CleanSelectedAsync()
    {
        var selectedVms = Groups.SelectMany(g => g.Items).Where(i => i.IsSelected).ToList();
        if (selectedVms.Count == 0)
            return;

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsCleaning = true;
        CurrentStepText = "Cleaning selected items...";

        var progress = new Progress<ScanProgressReport>(report =>
        {
            CurrentStepText = report.CurrentStep;
            CurrentTargetText = report.CurrentTarget;
            ScanProgressPercent = report.PercentComplete;
            ScanProgressPercentText = $"{report.PercentComplete}%";
        });

        try
        {
            var itemsToClean = selectedVms.Select(v => v.Model).ToList();
            var result = await _scanService.CleanSelectedItemsAsync(itemsToClean, progress, token).ConfigureAwait(true);

            CleanResultItemsRemoved = result.ItemsRemoved;
            CleanResultBytesReclaimedText = ScanItem.FormatBytes(result.BytesReclaimed);
            CleanResultErrors = new ObservableCollection<string>(result.Errors);

            // In-place update: remove successfully cleaned items from groups
            foreach (var group in Groups)
            {
                var remaining = group.Items.Where(i => !i.IsSelected).ToList();
                group.Items = new ObservableCollection<ScanItemViewModel>(remaining);
                group.UpdateGroupMetrics();
            }

            UpdateMetrics();
            IsCleanSummaryOpen = true;
        }
        catch (OperationCanceledException)
        {
            CurrentStepText = "Cleaning cancelled.";
        }
        catch (Exception ex)
        {
            LogScanError(_logger, ex.Message, ex);
            CurrentStepText = $"Cleaning error: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    public void SelectAll()
    {
        _isMasterSyncingSelection = true;
        try
        {
            foreach (var g in Groups)
            {
                g.SetSelection(true);
            }
            IsAllSelected = true;
        }
        finally
        {
            _isMasterSyncingSelection = false;
        }
        UpdateMetrics();
    }

    [RelayCommand]
    public void ClearAll()
    {
        _isMasterSyncingSelection = true;
        try
        {
            foreach (var g in Groups)
            {
                g.SetSelection(false);
            }
            IsAllSelected = false;
        }
        finally
        {
            _isMasterSyncingSelection = false;
        }
        UpdateMetrics();
    }

    [RelayCommand]
    public void CloseCleanSummary()
    {
        IsCleanSummaryOpen = false;
    }

    private void UpdateMetrics()
    {
        int totalFound = 0;
        long totalFoundBytes = 0;
        int totalSelected = 0;
        long totalSelectedBytes = 0;

        foreach (var g in Groups)
        {
            totalFound += g.TotalCount;
            totalFoundBytes += g.Items.Sum(i => i.SizeBytes);
            totalSelected += g.SelectedCount;
            totalSelectedBytes += g.SelectedBytes;
        }

        TotalFoundCount = totalFound;
        TotalFoundSizeText = ScanItem.FormatBytes(totalFoundBytes);
        TotalSelectedCount = totalSelected;
        TotalSelectedSizeText = ScanItem.FormatBytes(totalSelectedBytes);
        HasSelectedItems = totalSelected > 0;
        SelectionSummaryText = $"({totalSelected} of {totalFound} items • {TotalSelectedSizeText})";

        if (!_isMasterSyncingSelection)
        {
            _isMasterSyncingSelection = true;
            try
            {
                if (totalFound == 0 || totalSelected == 0)
                {
                    IsAllSelected = false;
                }
                else if (totalSelected == totalFound)
                {
                    IsAllSelected = true;
                }
                else
                {
                    IsAllSelected = null;
                }
            }
            finally
            {
                _isMasterSyncingSelection = false;
            }
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Deep system scan error: {ErrorMessage}")]
    private static partial void LogScanError(ILogger logger, string errorMessage, Exception? ex);

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }
}
