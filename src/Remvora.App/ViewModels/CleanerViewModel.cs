using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Cleaning;
using Remvora.Core.Domain.Cleaning;

namespace Remvora.App.ViewModels;

public sealed partial class JunkGroupViewModel : ObservableObject
{
    public JunkGroup Model { get; private set; }

    public JunkCategory Category => Model.Category;
    public string Title => Model.Title;
    public string Description => Model.Description;
    public long TotalSizeBytes => Model.TotalSizeBytes;
    public int ItemCount => Model.ItemCount;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string SizeFormatted => ApplicationItemViewModel.FormatBytes(TotalSizeBytes);
    public string ItemCountFormatted => $"{ItemCount:N0} files";

    public JunkGroupViewModel(JunkGroup model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        IsSelected = model.DefaultSelected;
    }

    public void Update(JunkGroup model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(SizeFormatted));
        OnPropertyChanged(nameof(ItemCountFormatted));
    }
}

public sealed partial class PrivacyItemViewModel : ObservableObject
{
    public PrivacyItem Model { get; private set; }

    public string Key => Model.Key;
    public string Title => Model.Title;
    public string Description => Model.Description;
    public int TracesCount => Model.TracesCount;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string CountFormatted => $"{TracesCount:N0} traces";

    public PrivacyItemViewModel(PrivacyItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        IsSelected = model.DefaultSelected;
    }

    public void Update(PrivacyItem model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        OnPropertyChanged(nameof(TracesCount));
        OnPropertyChanged(nameof(CountFormatted));
    }
}

public sealed partial class CleanerViewModel : ObservableObject
{
    private readonly IJunkCleaner _junkCleaner;
    private readonly IPrivacyCleaner _privacyCleaner;
    private readonly ISecureShredder _shredder;
    private readonly ILogger<CleanerViewModel> _logger;

    [ObservableProperty]
    public partial ObservableCollection<JunkGroupViewModel> JunkGroups { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<PrivacyItemViewModel> PrivacyItems { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ready";

    [ObservableProperty]
    public partial double ProgressPercentage { get; set; }

    [ObservableProperty]
    public partial string ProgressPercentageText { get; set; } = "0%";

    [ObservableProperty]
    public partial string CurrentActionDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsProgressVisible { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial string TotalJunkSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string TotalJunkFilesText { get; set; } = "0 files";

    [ObservableProperty]
    public partial string TotalPrivacyTracesText { get; set; } = "0 traces";

    [ObservableProperty]
    public partial bool? IsAllJunkSelected { get; set; } = true;

    [ObservableProperty]
    public partial string JunkSelectionSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? IsAllPrivacySelected { get; set; } = true;

    [ObservableProperty]
    public partial string PrivacySelectionSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedAlgorithm { get; set; } = "DoD 5220.22-M (3 Passes)";

    [ObservableProperty]
    public partial string ShredFilePath { get; set; } = string.Empty;

    public CleanerViewModel(
        IJunkCleaner junkCleaner,
        IPrivacyCleaner privacyCleaner,
        ISecureShredder shredder,
        ILogger<CleanerViewModel> logger)
    {
        _junkCleaner = junkCleaner ?? throw new ArgumentNullException(nameof(junkCleaner));
        _privacyCleaner = privacyCleaner ?? throw new ArgumentNullException(nameof(privacyCleaner));
        _shredder = shredder ?? throw new ArgumentNullException(nameof(shredder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (JunkGroups.Count > 0)
            return;

        await ScanAllAsync();
    }

    [RelayCommand]
    public async Task ScanAllAsync()
    {
        IsBusy = true;
        IsScanning = true;
        IsCleaning = false;
        IsProgressVisible = true;
        ProgressPercentage = 0;
        ProgressPercentageText = "0%";
        StatusMessage = "Starting deep junk scan...";
        CurrentActionDetail = "Enumerating directories...";

        try
        {
            var scanProgress = new Progress<JunkScanProgress>(p =>
            {
                ProgressPercentage = p.PercentComplete;
                ProgressPercentageText = $"{Math.Round(p.PercentComplete)}%";
                if (p.PercentComplete >= 100 || p.CurrentCategory.StartsWith("Scan Complete", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Scan completed";
                }
                else
                {
                    StatusMessage = $"Scanning {p.CurrentCategory}...";
                }
                CurrentActionDetail = p.CurrentPath ?? $"Found {p.ItemsFound:N0} items ({ApplicationItemViewModel.FormatBytes(p.BytesFound)})";
            });

            var junk = await _junkCleaner.ScanJunkAsync(scanProgress);
            JunkGroups.Clear();

            long totalBytes = 0;
            int totalFiles = 0;

            foreach (var grp in junk)
            {
                var vm = new JunkGroupViewModel(grp);
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(JunkGroupViewModel.IsSelected))
                        UpdateJunkTotals();
                };
                JunkGroups.Add(vm);
                totalBytes += grp.TotalSizeBytes;
                totalFiles += grp.ItemCount;
            }

            var privacy = await _privacyCleaner.ScanPrivacyTracesAsync();
            PrivacyItems.Clear();

            int totalTraces = 0;
            foreach (var item in privacy)
            {
                var pVm = new PrivacyItemViewModel(item);
                pVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PrivacyItemViewModel.IsSelected))
                        UpdatePrivacyTotals();
                };
                PrivacyItems.Add(pVm);
                totalTraces += item.TracesCount;
            }

            UpdateJunkTotals();
            UpdatePrivacyTotals();

            ProgressPercentage = 100;
            ProgressPercentageText = "100%";
            StatusMessage = $"Scan completed • Found {TotalJunkSizeText} in {totalFiles:N0} files and {totalTraces:N0} privacy traces.";
            CurrentActionDetail = "Ready for cleanup.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            CurrentActionDetail = string.Empty;
        }
        finally
        {
            IsBusy = false;
            IsScanning = false;
        }
    }

    private bool _isSyncingSelection;

    partial void OnIsAllJunkSelectedChanged(bool? value)
    {
        if (_isSyncingSelection || value is null) return;
        _isSyncingSelection = true;
        try
        {
            foreach (var grp in JunkGroups)
            {
                grp.IsSelected = value.Value;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdateJunkTotals();
    }

    partial void OnIsAllPrivacySelectedChanged(bool? value)
    {
        if (_isSyncingSelection || value is null) return;
        _isSyncingSelection = true;
        try
        {
            foreach (var item in PrivacyItems)
            {
                item.IsSelected = value.Value;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdatePrivacyTotals();
    }

    [RelayCommand]
    public void SelectAllJunk()
    {
        _isSyncingSelection = true;
        try
        {
            foreach (var grp in JunkGroups)
            {
                grp.IsSelected = true;
            }
            IsAllJunkSelected = true;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdateJunkTotals();
    }

    [RelayCommand]
    public void DeselectAllJunk()
    {
        _isSyncingSelection = true;
        try
        {
            foreach (var grp in JunkGroups)
            {
                grp.IsSelected = false;
            }
            IsAllJunkSelected = false;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdateJunkTotals();
    }

    [RelayCommand]
    public void ToggleSelectAllJunk()
    {
        bool targetState = IsAllJunkSelected != true;
        if (targetState) SelectAllJunk();
        else DeselectAllJunk();
    }

    [RelayCommand]
    public void SelectAllPrivacy()
    {
        _isSyncingSelection = true;
        try
        {
            foreach (var item in PrivacyItems)
            {
                item.IsSelected = true;
            }
            IsAllPrivacySelected = true;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdatePrivacyTotals();
    }

    [RelayCommand]
    public void DeselectAllPrivacy()
    {
        _isSyncingSelection = true;
        try
        {
            foreach (var item in PrivacyItems)
            {
                item.IsSelected = false;
            }
            IsAllPrivacySelected = false;
        }
        finally
        {
            _isSyncingSelection = false;
        }
        UpdatePrivacyTotals();
    }

    [RelayCommand]
    public void ToggleSelectAllPrivacy()
    {
        bool targetState = IsAllPrivacySelected != true;
        if (targetState) SelectAllPrivacy();
        else DeselectAllPrivacy();
    }

    private void UpdateJunkTotals()
    {
        int totalCount = JunkGroups.Count;
        int selectedCount = JunkGroups.Count(j => j.IsSelected);
        long selectedBytes = JunkGroups.Where(j => j.IsSelected).Sum(j => j.TotalSizeBytes);
        int selectedFiles = JunkGroups.Where(j => j.IsSelected).Sum(j => j.ItemCount);

        TotalJunkSizeText = ApplicationItemViewModel.FormatBytes(selectedBytes);
        TotalJunkFilesText = $"{selectedFiles:N0} files selected ({selectedCount} of {totalCount} categories)";
        JunkSelectionSummaryText = $"({selectedCount} of {totalCount} categories • {TotalJunkSizeText})";

        if (!_isSyncingSelection)
        {
            _isSyncingSelection = true;
            try
            {
                if (totalCount == 0 || selectedCount == 0)
                {
                    IsAllJunkSelected = false;
                }
                else if (selectedCount == totalCount)
                {
                    IsAllJunkSelected = true;
                }
                else
                {
                    IsAllJunkSelected = null;
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    private void UpdatePrivacyTotals()
    {
        int totalCount = PrivacyItems.Count;
        int selectedCount = PrivacyItems.Count(p => p.IsSelected);
        int selectedTraces = PrivacyItems.Where(p => p.IsSelected).Sum(p => p.TracesCount);

        TotalPrivacyTracesText = $"{selectedTraces:N0} traces selected ({selectedCount} of {totalCount} categories)";
        PrivacySelectionSummaryText = $"({selectedCount} of {totalCount} trace categories • {selectedTraces:N0} traces)";

        if (!_isSyncingSelection)
        {
            _isSyncingSelection = true;
            try
            {
                if (totalCount == 0 || selectedCount == 0)
                {
                    IsAllPrivacySelected = false;
                }
                else if (selectedCount == totalCount)
                {
                    IsAllPrivacySelected = true;
                }
                else
                {
                    IsAllPrivacySelected = null;
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    [RelayCommand]
    public async Task CleanJunkAsync()
    {
        var selectedCats = JunkGroups.Where(j => j.IsSelected).Select(j => j.Category).ToList();
        if (selectedCats.Count == 0)
            return;

        IsBusy = true;
        IsCleaning = true;
        IsScanning = false;
        IsProgressVisible = true;
        ProgressPercentage = 0;
        ProgressPercentageText = "0%";
        StatusMessage = "Cleaning selected junk artifacts...";
        CurrentActionDetail = "Preparing deletion queue...";

        try
        {
            var cleanProgress = new Progress<JunkCleanProgress>(p =>
            {
                ProgressPercentage = p.PercentComplete;
                ProgressPercentageText = $"{Math.Round(p.PercentComplete)}%";
                StatusMessage = $"Cleaning {p.CurrentCategory} ({p.CleanedCount:N0}/{p.TotalCount:N0})";
                CurrentActionDetail = $"{p.CurrentItem} • {ApplicationItemViewModel.FormatBytes(p.BytesReclaimed)} reclaimed";
            });

            var result = await _junkCleaner.CleanJunkAsync(selectedCats, cleanProgress);

            if (result.IsSuccess)
            {
                var freed = ApplicationItemViewModel.FormatBytes(result.Value);
                ProgressPercentage = 100;
                ProgressPercentageText = "100%";
                StatusMessage = $"Cleanup complete! Successfully freed {freed} of disk space.";
                CurrentActionDetail = $"Reclaimed {freed} on disk. Selected categories updated.";

                // Re-scan remaining items and update in place so the completion state is not wiped
                var rescanTargets = await _junkCleaner.ScanJunkAsync().ConfigureAwait(true);
                var rescanMap = rescanTargets.ToDictionary(r => r.Category);

                foreach (var groupVm in JunkGroups)
                {
                    if (rescanMap.TryGetValue(groupVm.Category, out var updatedGroup))
                    {
                        groupVm.Update(updatedGroup);
                        if (selectedCats.Contains(groupVm.Category))
                        {
                            groupVm.IsSelected = false;
                        }
                    }
                }

                UpdateJunkTotals();
            }
            else
            {
                StatusMessage = $"Cleanup error: {result.Error?.Message}";
                CurrentActionDetail = string.Empty;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cleanup error: {ex.Message}";
            CurrentActionDetail = string.Empty;
        }
        finally
        {
            IsBusy = false;
            IsCleaning = false;
        }
    }

    [RelayCommand]
    public async Task CleanPrivacyAsync()
    {
        var selectedKeys = PrivacyItems.Where(p => p.IsSelected).Select(p => p.Key).ToList();
        if (selectedKeys.Count == 0)
            return;

        IsBusy = true;
        StatusMessage = "Erasing activity traces and MRUs...";

        try
        {
            var result = await _privacyCleaner.CleanPrivacyTracesAsync(selectedKeys);
            if (result.IsSuccess)
            {
                StatusMessage = $"Privacy cleanup complete! Erased {result.Value} history traces.";
                CurrentActionDetail = $"Cleared {result.Value} activity items.";

                var rescanPrivacy = await _privacyCleaner.ScanPrivacyTracesAsync().ConfigureAwait(true);
                var privacyMap = rescanPrivacy.ToDictionary(p => p.Key);

                foreach (var itemVm in PrivacyItems)
                {
                    if (privacyMap.TryGetValue(itemVm.Key, out var updatedItem))
                    {
                        itemVm.Update(updatedItem);
                        if (selectedKeys.Contains(itemVm.Key))
                        {
                            itemVm.IsSelected = false;
                        }
                    }
                }

                UpdatePrivacyTotals();
            }
            else
            {
                StatusMessage = $"Privacy cleanup error: {result.Error?.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Privacy cleanup error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ShredFileAsync()
    {
        if (string.IsNullOrWhiteSpace(ShredFilePath) || !File.Exists(ShredFilePath))
        {
            StatusMessage = "Please specify a valid file path to shred.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Shredding {Path.GetFileName(ShredFilePath)}...";

        var algo = SelectedAlgorithm switch
        {
            "Zero Fill (1 Pass)" => ShredderAlgorithm.ZeroFill,
            "Pseudorandom (1 Pass)" => ShredderAlgorithm.Pseudorandom,
            "NIST 800-88 (2 Passes)" => ShredderAlgorithm.Nist80088,
            _ => ShredderAlgorithm.Dod522022M
        };

        try
        {
            var progress = new Progress<ShredProgress>(p =>
            {
                StatusMessage = $"Shredding: Pass {p.CurrentPass}/{p.TotalPasses} ({p.PercentComplete}%)";
            });

            var result = await _shredder.ShredFilesAsync([ShredFilePath], algo, progress);
            if (result.IsSuccess)
            {
                StatusMessage = "File successfully obliterated using cryptographic multi-pass shredding.";
                ShredFilePath = string.Empty;
            }
            else
            {
                StatusMessage = $"Shredding failed: {result.Error?.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
