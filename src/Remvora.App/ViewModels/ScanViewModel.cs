using System.Collections.ObjectModel;
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

    public ScanItemViewModel(ScanItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        IsSelected = model.IsSelected;
    }
}

public sealed partial class ScanGroupViewModel : ObservableObject
{
    public ScanCategory Category { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    public partial ObservableCollection<ScanItemViewModel> Items { get; set; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsAllSelected { get; set; } = true;

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
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(SelectedBytes));
                OnPropertyChanged(nameof(FormattedSelectedSize));
                IsAllSelected = Items.Count > 0 && Items.All(i => i.IsSelected);
                GroupSelectionChanged?.Invoke();
            };
            Items.Add(vm);
        }
    }

    [RelayCommand]
    public void ToggleGroupSelection()
    {
        bool newState = !IsAllSelected;
        foreach (var item in Items)
        {
            item.IsSelected = newState;
        }
        IsAllSelected = newState;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(FormattedSelectedSize));
        GroupSelectionChanged?.Invoke();
    }
}

public sealed partial class ScanViewModel : ObservableObject, IDisposable
{
    private readonly ISystemScanService _scanService;
    private readonly ICleaningStatsRepository _statsRepository;
    private readonly ILogger<ScanViewModel> _logger;
    private CancellationTokenSource? _scanCts;

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
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsScanning = true;
        HasScanned = false;
        ScanProgressPercent = 0;
        CurrentStepText = "Initializing deep system scan...";
        CurrentTargetText = string.Empty;
        Groups.Clear();

        var progress = new Progress<ScanProgressReport>(report =>
        {
            CurrentStepText = report.CurrentStep;
            CurrentTargetText = report.CurrentTarget;
            ScanProgressPercent = report.PercentComplete;
            TotalFoundCount = report.ItemsFound;
            TotalFoundSizeText = ScanItem.FormatBytes(report.BytesFound);
        });

        try
        {
            var rawGroups = await _scanService.ScanSystemAsync(progress, token).ConfigureAwait(true);

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
            UpdateMetrics();
        }
        catch (OperationCanceledException)
        {
            CurrentStepText = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            LogScanError(_logger, ex.Message, ex);
            CurrentStepText = $"Scan encountered an error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
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
        });

        try
        {
            var itemsToClean = selectedVms.Select(v => v.Model).ToList();
            var result = await _scanService.CleanSelectedItemsAsync(itemsToClean, progress, token).ConfigureAwait(true);

            CleanResultItemsRemoved = result.ItemsRemoved;
            CleanResultBytesReclaimedText = ScanItem.FormatBytes(result.BytesReclaimed);
            CleanResultErrors = new ObservableCollection<string>(result.Errors);

            // Remove successfully deleted items from VM groups
            foreach (var group in Groups)
            {
                var remaining = group.Items.Where(i => !i.IsSelected).ToList();
                group.Items = new ObservableCollection<ScanItemViewModel>(remaining);
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
        foreach (var g in Groups)
        {
            foreach (var item in g.Items)
            {
                item.IsSelected = true;
            }
            g.IsAllSelected = true;
        }
        UpdateMetrics();
    }

    [RelayCommand]
    public void ClearAll()
    {
        foreach (var g in Groups)
        {
            foreach (var item in g.Items)
            {
                item.IsSelected = false;
            }
            g.IsAllSelected = false;
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
        int count = 0;
        long bytes = 0;
        int totalCount = 0;
        long totalBytes = 0;

        foreach (var g in Groups)
        {
            totalCount += g.TotalCount;
            totalBytes += g.Items.Sum(i => i.SizeBytes);
            count += g.SelectedCount;
            bytes += g.SelectedBytes;
        }

        TotalFoundCount = totalCount;
        TotalFoundSizeText = ScanItem.FormatBytes(totalBytes);
        TotalSelectedCount = count;
        TotalSelectedSizeText = ScanItem.FormatBytes(bytes);
        HasSelectedItems = count > 0;
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
