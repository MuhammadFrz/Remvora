using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Cleaning;
using Remvora.Core.Domain.Cleaning;

namespace Remvora.App.ViewModels;

public sealed partial class JunkGroupViewModel : ObservableObject
{
    public JunkGroup Model { get; }

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
}

public sealed partial class PrivacyItemViewModel : ObservableObject
{
    public PrivacyItem Model { get; }

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
                StatusMessage = $"Scanning {p.CurrentCategory}...";
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

            TotalJunkSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);
            TotalJunkFilesText = $"{totalFiles:N0} files";

            var privacy = await _privacyCleaner.ScanPrivacyTracesAsync();
            PrivacyItems.Clear();

            int totalTraces = 0;
            foreach (var item in privacy)
            {
                PrivacyItems.Add(new PrivacyItemViewModel(item));
                totalTraces += item.TracesCount;
            }

            TotalPrivacyTracesText = $"{totalTraces:N0} traces";
            ProgressPercentage = 100;
            ProgressPercentageText = "100%";
            StatusMessage = $"Scan completed. Found {TotalJunkSizeText} in {totalFiles:N0} files and {totalTraces:N0} privacy traces.";
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

    private void UpdateJunkTotals()
    {
        long selectedBytes = JunkGroups.Where(j => j.IsSelected).Sum(j => j.TotalSizeBytes);
        int selectedFiles = JunkGroups.Where(j => j.IsSelected).Sum(j => j.ItemCount);

        TotalJunkSizeText = ApplicationItemViewModel.FormatBytes(selectedBytes);
        TotalJunkFilesText = $"{selectedFiles:N0} files selected";
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
                CurrentActionDetail = $"Reclaimed {freed} on disk.";
                await ScanAllAsync();
            }
            else
            {
                StatusMessage = $"Cleanup error: {result.Error?.Message}";
                CurrentActionDetail = string.Empty;
            }
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
                StatusMessage = $"Privacy cleanup complete! Erased {result.Value} history items.";
                await ScanAllAsync();
            }
            else
            {
                StatusMessage = $"Privacy cleanup error: {result.Error?.Message}";
            }
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
