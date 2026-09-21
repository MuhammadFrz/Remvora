using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.RestorePoint;
using Remvora.Application.Transactions;
using Remvora.Core.Domain.Transactions;

namespace Remvora.App.ViewModels;

public sealed partial class BackupItemViewModel : ObservableObject
{
    public OperationTransaction Model { get; }

    public Guid Id => Model.Id;
    public string OperationType => string.IsNullOrWhiteSpace(Model.OperationType) ? "Cleanup" : Model.OperationType;
    public string SummaryNotes => string.IsNullOrWhiteSpace(Model.SummaryNotes) ? "Uninstall & Cleanup Transaction" : Model.SummaryNotes;
    public string StartedAtFormatted => Model.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string CompletedAtFormatted => Model.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Pending";

    public string StatusText => Model.Phase switch
    {
        TransactionPhase.Completed => "Completed",
        TransactionPhase.RolledBack => "Rolled Back",
        TransactionPhase.Failed => "Failed",
        TransactionPhase.RollbackPartial => "Partial Rollback",
        _ => "In Progress"
    };

    public string StatusBadgeBackground => Model.Phase switch
    {
        TransactionPhase.Completed => "#107C41",
        TransactionPhase.RolledBack => "#8A3B00",
        TransactionPhase.Failed => "#C42B1C",
        _ => "#005A9E"
    };

    public int TotalItemsCount => Model.Items.Count;
    public int FileCount => Model.Items.Count(i => i.ItemType is TransactionItemType.File or TransactionItemType.Directory);
    public int RegistryCount => Model.Items.Count(i => i.ItemType is TransactionItemType.RegistryKey or TransactionItemType.RegistryValue);

    public bool CanRollback => Model.Phase == TransactionPhase.Completed && Model.Items.Any(i => i.IsReversible);

    public IReadOnlyList<TransactionItem> Items => Model.Items;

    public BackupItemViewModel(OperationTransaction model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }
}

public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ITransactionRollbackService _rollbackService;
    private readonly IRestorePointService _restorePointService;
    private readonly ILogger<BackupsViewModel> _logger;
    private readonly List<BackupItemViewModel> _allBackups = [];

    [ObservableProperty]
    public partial ObservableCollection<BackupItemViewModel> FilteredBackups { get; set; } = [];

    [ObservableProperty]
    public partial BackupItemViewModel? SelectedBackup { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ready";

    [ObservableProperty]
    public partial int TotalBackupsCount { get; set; }

    [ObservableProperty]
    public partial int TotalRestorableItemsCount { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "0 backups available";

    public BackupsViewModel(
        ITransactionRepository transactionRepository,
        ITransactionRollbackService rollbackService,
        IRestorePointService restorePointService,
        ILogger<BackupsViewModel> logger)
    {
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _rollbackService = rollbackService ?? throw new ArgumentNullException(nameof(rollbackService));
        _restorePointService = restorePointService ?? throw new ArgumentNullException(nameof(restorePointService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_allBackups.Count > 0)
            return;

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading backup transactions...";

        try
        {
            var transactions = await _transactionRepository.GetTransactionsAsync(limit: 100);
            _allBackups.Clear();

            int totalItems = 0;
            foreach (var tx in transactions.OrderByDescending(t => t.StartedAt))
            {
                _allBackups.Add(new BackupItemViewModel(tx));
                totalItems += tx.Items.Count;
            }

            TotalBackupsCount = _allBackups.Count;
            TotalRestorableItemsCount = totalItems;
            SummaryText = $"{_allBackups.Count} backup journals ({totalItems} recorded items)";
            StatusMessage = $"Loaded {_allBackups.Count} transactions.";

            ApplyFilter();
        }
        catch (Exception ex)
        {
            LogLoadBackupsFailed(_logger, ex);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = _allBackups.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(b =>
                b.SummaryNotes.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                b.OperationType.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                b.StartedAtFormatted.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        var list = query.ToList();
        FilteredBackups = new ObservableCollection<BackupItemViewModel>(list);

        if (SelectedBackup != null && !list.Contains(SelectedBackup))
        {
            SelectedBackup = list.FirstOrDefault();
        }
        else if (SelectedBackup == null)
        {
            SelectedBackup = list.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task RollbackBackupAsync(BackupItemViewModel? target)
    {
        var item = target ?? SelectedBackup;
        if (item == null || !item.CanRollback)
            return;

        IsLoading = true;
        StatusMessage = $"Rolling back backup {item.Id}...";

        try
        {
            var result = await _rollbackService.RollbackTransactionAsync(item.Id);
            if (result.IsSuccess && result.Value != null)
            {
                var summary = result.Value;
                StatusMessage = $"Rollback complete: {summary.ItemsRestored} items restored, {summary.ItemsFailed} failed.";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = $"Rollback failed: {result.Error?.Message ?? "Unknown error"}";
            }
        }
        catch (Exception ex)
        {
            LogRollbackFailed(_logger, ex, item.Id);
            StatusMessage = $"Rollback error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteBackupAsync(BackupItemViewModel? target)
    {
        var item = target ?? SelectedBackup;
        if (item == null)
            return;

        IsLoading = true;
        StatusMessage = $"Deleting backup {item.Id}...";

        try
        {
            var result = await _transactionRepository.DeleteTransactionAsync(item.Id);
            if (result.IsSuccess)
            {
                _allBackups.Remove(item);
                ApplyFilter();
                StatusMessage = "Backup deleted successfully.";
            }
            else
            {
                StatusMessage = $"Failed to delete backup: {result.Error?.Message ?? "Unknown error"}";
            }
        }
        catch (Exception ex)
        {
            LogDeleteBackupFailed(_logger, ex, item.Id);
            StatusMessage = $"Delete error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        IsLoading = true;
        StatusMessage = "Creating Windows System Restore Point...";

        try
        {
            var supported = await _restorePointService.IsRestorePointSupportedAsync();
            if (!supported)
            {
                StatusMessage = "Windows System Restore is not enabled on this system drive.";
                return;
            }

            var result = await _restorePointService.CreateRestorePointAsync("Remvora Manual Recovery Checkpoint");
            if (result.IsSuccess)
            {
                StatusMessage = "Windows System Restore point created successfully.";
            }
            else
            {
                StatusMessage = $"System Restore error: {result.Error?.Message ?? "Failed to create checkpoint"}";
            }
        }
        catch (Exception ex)
        {
            LogCreateRestorePointFailed(_logger, ex);
            StatusMessage = $"System Restore error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Failed to load backups")]
    private static partial void LogLoadBackupsFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to rollback transaction {Id}")]
    private static partial void LogRollbackFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to delete backup {Id}")]
    private static partial void LogDeleteBackupFailed(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to create restore point")]
    private static partial void LogCreateRestorePointFailed(ILogger logger, Exception ex);
}
