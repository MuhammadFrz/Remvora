using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Leftovers;
using Remvora.Application.Processes;
using Remvora.Application.Transactions;
using Remvora.Application.Workflows;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public enum UninstallFlowStage
{
    Idle = 0,
    ProcessesWarning = 1,
    ExecutingWorkflow = 2,
    ReviewingLeftovers = 3,
    ExecutingCleanup = 4,
    CompletedSummary = 5
}

public sealed partial class AppsViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationDiscoveryService _discoveryService;
    private readonly IApplicationRepository _repository;
    private readonly IProcessDetector _processDetector;
    private readonly IUninstallOrchestrator _uninstallOrchestrator;
    private readonly ILeftoverScanner _leftoverScanner;
    private readonly ITransactionExecutor _transactionExecutor;
    private readonly ITransactionRollbackService _rollbackService;
    private readonly ILogger<AppsViewModel> _logger;

    private readonly List<ApplicationItemViewModel> _allApplications = [];
    private CancellationTokenSource? _flowCts;

    [ObservableProperty]
    public partial ObservableCollection<ApplicationItemViewModel> FilteredApplications { get; set; } = [];

    [ObservableProperty]
    public partial ApplicationItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    [ObservableProperty]
    public partial string SelectedSort { get; set; } = "Name";

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "0 applications";

    [ObservableProperty]
    public partial string TotalSizeText { get; set; } = "0 MB";

    // Flow State Properties
    [ObservableProperty]
    public partial UninstallFlowStage FlowStage { get; set; } = UninstallFlowStage.Idle;

    [ObservableProperty]
    public partial bool IsFlowActive { get; set; }

    [ObservableProperty]
    public partial string FlowTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlowStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<RunningProcessInfo> ActiveProcesses { get; set; } = [];

    [ObservableProperty]
    public partial CleanupPreviewViewModel CleanupPreview { get; set; }

    [ObservableProperty]
    public partial string SummaryTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RebootRequired { get; set; }

    [ObservableProperty]
    public partial bool CanRollback { get; set; }

    [ObservableProperty]
    public partial Guid? LastTransactionId { get; set; }

    public AppsViewModel(
        IApplicationDiscoveryService discoveryService,
        IApplicationRepository repository,
        IProcessDetector processDetector,
        IUninstallOrchestrator uninstallOrchestrator,
        ILeftoverScanner leftoverScanner,
        ITransactionExecutor transactionExecutor,
        ITransactionRollbackService rollbackService,
        CleanupPreviewViewModel cleanupPreview,
        ILogger<AppsViewModel> logger)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processDetector = processDetector ?? throw new ArgumentNullException(nameof(processDetector));
        _uninstallOrchestrator = uninstallOrchestrator ?? throw new ArgumentNullException(nameof(uninstallOrchestrator));
        _leftoverScanner = leftoverScanner ?? throw new ArgumentNullException(nameof(leftoverScanner));
        _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
        _rollbackService = rollbackService ?? throw new ArgumentNullException(nameof(rollbackService));
        CleanupPreview = cleanupPreview ?? throw new ArgumentNullException(nameof(cleanupPreview));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_allApplications.Count > 0)
            return;

        IsLoading = true;
        ProgressText = "Loading application inventory...";

        try
        {
            var cached = await _repository.GetAllAsync();
            if (cached.Count > 0)
            {
                SetApplications(cached);
            }
            else
            {
                await RefreshAsync();
            }
        }
        catch
        {
            await RefreshAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ProgressText = "Discovering installed applications...";

        var progress = new Progress<DiscoveryProgress>(p =>
        {
            ProgressText = $"Scanning {p.CurrentSource}: {p.CurrentItemName}";
        });

        try
        {
            var filter = new DiscoveryFilter
            {
                IncludePerMachine = true,
                IncludePerUser = true,
                IncludeStoreApps = true,
                IncludeSystemComponents = false
            };

            var discovered = await _discoveryService.DiscoverAllAsync(filter, progress);
            await _repository.SaveAsync(discovered);

            SetApplications(discovered);
            ProgressText = $"Inventory refreshed. Found {discovered.Count} applications.";
        }
        catch (Exception ex)
        {
            ProgressText = $"Discovery error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetApplications(IEnumerable<ApplicationRecord> applications)
    {
        _allApplications.Clear();
        long totalBytes = 0;

        foreach (var app in applications)
        {
            _allApplications.Add(new ApplicationItemViewModel(app));
            totalBytes += app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L;
        }

        TotalSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);
        ApplyFiltersAndSort();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedCategoryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedSortChanged(string value) => ApplyFiltersAndSort();

    private void ApplyFiltersAndSort()
    {
        var query = _allApplications.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(a =>
                a.DisplayName.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase) ||
                a.Publisher.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        query = SelectedCategory switch
        {
            "Desktop" => query.Where(a => !a.IsStoreApp),
            "Store" => query.Where(a => a.IsStoreApp),
            "Large" => query.Where(a => (a.Model.EstimatedSizeBytes ?? 0) >= 100 * 1024 * 1024),
            _ => query
        };

        query = SelectedSort switch
        {
            "Publisher" => query.OrderBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase)
                                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Size" => query.OrderByDescending(a => a.Model.EstimatedSizeBytes ?? a.Model.CalculatedSizeBytes ?? 0L),
            "Date" => query.OrderByDescending(a => a.Model.InstallDate ?? DateTimeOffset.MinValue),
            _ => query.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        var list = query.ToList();
        FilteredApplications = new ObservableCollection<ApplicationItemViewModel>(list);
        SummaryText = $"{list.Count} of {_allApplications.Count} apps";

        if (SelectedItem != null && !list.Contains(SelectedItem))
        {
            SelectedItem = list.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void OpenInstallLocation()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Model.InstallLocation))
            return;

        var path = SelectedItem.Model.InstallLocation;
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
    }

    // ==========================================
    // UNINSTALL WORKFLOW INTEGRATION (PHASE 9 & 10)
    // ==========================================

    [RelayCommand]
    public async Task StartCompleteUninstallAsync(ApplicationItemViewModel? targetItem)
    {
        var item = targetItem ?? SelectedItem;
        if (item is null)
            return;

        SelectedItem = item;
        _flowCts?.Dispose();
        _flowCts = new CancellationTokenSource();

        IsFlowActive = true;
        FlowTitle = $"Uninstalling {item.DisplayName}";
        FlowStatusMessage = "Checking for active running processes...";
        FlowStage = UninstallFlowStage.ExecutingWorkflow;

        try
        {
            var processes = await _processDetector.DetectProcessesAsync(item.Model, _flowCts.Token).ConfigureAwait(true);
            if (processes.Count > 0)
            {
                ActiveProcesses = new ObservableCollection<RunningProcessInfo>(processes);
                FlowStage = UninstallFlowStage.ProcessesWarning;
                return;
            }

            await ExecuteUninstallPipelineAsync(terminateProcesses: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            CancelFlow();
        }
        catch (Exception ex)
        {
            ShowFailure("Uninstall Error", ex.Message);
        }
    }

    [RelayCommand]
    public async Task StartForcedUninstallAsync(ApplicationItemViewModel? targetItem)
    {
        var item = targetItem ?? SelectedItem;
        if (item is null)
            return;

        SelectedItem = item;
        _flowCts?.Dispose();
        _flowCts = new CancellationTokenSource();

        IsFlowActive = true;
        FlowTitle = $"Forced Uninstall — {item.DisplayName}";
        FlowStatusMessage = "Bypassing vendor uninstaller. Performing deep leftover scan...";
        FlowStage = UninstallFlowStage.ExecutingWorkflow;

        try
        {
            var scanProgress = new Progress<ScanProgress>(p =>
            {
                FlowStatusMessage = $"Scanning {p.CurrentKind} remnants: {p.CurrentTarget}";
            });

            var leftovers = await _leftoverScanner.ScanLeftoversAsync(
                item.Model,
                new ScanOptions(ScanLevel.Thorough),
                scanProgress,
                _flowCts.Token).ConfigureAwait(true);

            if (leftovers.Count == 0)
            {
                await _repository.DeleteAsync(item.Model.Id).ConfigureAwait(true);
                _allApplications.Remove(item);
                ApplyFiltersAndSort();

                ShowSuccess("Forced Uninstall Finished", $"No remnant traces were discovered for '{item.DisplayName}'. Removed from inventory.", false, false);
                return;
            }

            CleanupPreview.LoadPlan(item.Model, leftovers);
            FlowStage = UninstallFlowStage.ReviewingLeftovers;
        }
        catch (OperationCanceledException)
        {
            CancelFlow();
        }
        catch (Exception ex)
        {
            ShowFailure("Forced Uninstall Failed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task StartStandardUninstallAsync(ApplicationItemViewModel? targetItem)
    {
        var item = targetItem ?? SelectedItem;
        if (item is null)
            return;

        SelectedItem = item;
        _flowCts?.Dispose();
        _flowCts = new CancellationTokenSource();

        IsFlowActive = true;
        FlowTitle = $"Standard Uninstall — {item.DisplayName}";
        FlowStatusMessage = "Executing vendor uninstaller...";
        FlowStage = UninstallFlowStage.ExecutingWorkflow;

        try
        {
            var progress = new Progress<UninstallWorkflowProgress>(p =>
            {
                FlowStatusMessage = p.Message;
            });

            var result = await _uninstallOrchestrator.UninstallAsync(
                item.Model,
                new UninstallWorkflowOptions(WarnIfProcessesRunning: false, CreateRestorePoint: true),
                progress,
                _flowCts.Token).ConfigureAwait(true);

            if (result.IsSuccess)
            {
                await _repository.DeleteAsync(item.Model.Id).ConfigureAwait(true);
                _allApplications.Remove(item);
                ApplyFiltersAndSort();

                ShowSuccess("Uninstallation Completed", result.Message, result.RebootRequired, false);
            }
            else
            {
                ShowFailure("Uninstallation Failed", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            CancelFlow();
        }
        catch (Exception ex)
        {
            ShowFailure("Uninstall Error", ex.Message);
        }
    }

    [RelayCommand]
    public async Task TerminateAndContinueAsync()
    {
        FlowStage = UninstallFlowStage.ExecutingWorkflow;
        await ExecuteUninstallPipelineAsync(terminateProcesses: true).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task IgnoreAndContinueAsync()
    {
        FlowStage = UninstallFlowStage.ExecutingWorkflow;
        await ExecuteUninstallPipelineAsync(terminateProcesses: false).ConfigureAwait(true);
    }

    [RelayCommand]
    public void CancelFlow()
    {
        _flowCts?.Cancel();
        _flowCts?.Dispose();
        _flowCts = null;

        FlowStage = UninstallFlowStage.Idle;
        IsFlowActive = false;
        ActiveProcesses.Clear();
    }

    private async Task ExecuteUninstallPipelineAsync(bool terminateProcesses)
    {
        if (SelectedItem is null)
            return;

        var app = SelectedItem.Model;

        var uninstallProgress = new Progress<UninstallWorkflowProgress>(p =>
        {
            FlowStatusMessage = p.Message;
        });

        var uninstallResult = await _uninstallOrchestrator.UninstallAsync(
            app,
            new UninstallWorkflowOptions(
                TerminateProcesses: terminateProcesses,
                WarnIfProcessesRunning: false,
                CreateRestorePoint: true),
            uninstallProgress,
            _flowCts?.Token ?? default).ConfigureAwait(true);

        // Even if vendor uninstaller returns non-zero, we check for residual leftovers
        FlowStatusMessage = "Scanning filesystem, registry, services, and tasks for leftover traces...";

        var scanProgress = new Progress<ScanProgress>(p =>
        {
            FlowStatusMessage = $"Scanning {p.CurrentKind} remnants: {p.CurrentTarget}";
        });

        var leftovers = await _leftoverScanner.ScanLeftoversAsync(
            app,
            ScanOptions.Default,
            scanProgress,
            _flowCts?.Token ?? default).ConfigureAwait(true);

        if (leftovers.Count == 0)
        {
            await _repository.DeleteAsync(app.Id).ConfigureAwait(true);
            _allApplications.Remove(SelectedItem);
            ApplyFiltersAndSort();

            ShowSuccess(
                "Uninstallation Completed",
                $"{app.DisplayName} was uninstalled cleanly. No leftover traces were found on this system.",
                uninstallResult.RebootRequired,
                false);
            return;
        }

        CleanupPreview.LoadPlan(app, leftovers);
        FlowTitle = $"Review Leftovers for {app.DisplayName}";
        FlowStage = UninstallFlowStage.ReviewingLeftovers;
    }

    [RelayCommand]
    public async Task ConfirmCleanupAsync()
    {
        if (CleanupPreview.Plan is null || SelectedItem is null)
            return;

        FlowStage = UninstallFlowStage.ExecutingCleanup;
        FlowTitle = $"Cleaning Up Leftovers for {SelectedItem.DisplayName}";
        FlowStatusMessage = "Backing up reversible items and removing selected traces...";

        var selectedIds = CleanupPreview.GetSelectedCandidateIds();

        var cleanupProgress = new Progress<CleanupExecutionProgress>(p =>
        {
            FlowStatusMessage = $"[{p.CurrentIndex}/{p.TotalCount}] {p.StatusMessage}";
        });

        try
        {
            var result = await _transactionExecutor.ExecuteCleanupAsync(
                CleanupPreview.Plan,
                selectedIds,
                cleanupProgress,
                _flowCts?.Token ?? default).ConfigureAwait(true);

            // App uninstalled and leftovers cleaned: remove from repository
            await _repository.DeleteAsync(SelectedItem.Model.Id).ConfigureAwait(true);
            _allApplications.Remove(SelectedItem);
            ApplyFiltersAndSort();

            LastTransactionId = result.PlanId;
            var reclaimed = ApplicationItemViewModel.FormatBytes(result.ReclaimedSizeBytes);

            ShowSuccess(
                result.IsSuccess ? "Cleanup Completed Successfully" : "Cleanup Finished with Warnings",
                $"Successfully removed {result.SucceededItems} of {result.TotalItems} leftover traces. Reclaimed {reclaimed} of disk space. Backups were preserved for rollback.",
                result.RebootRequired,
                canRollback: true);
        }
        catch (Exception ex)
        {
            ShowFailure("Cleanup Execution Failed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task SkipCleanupAsync()
    {
        if (SelectedItem is not null)
        {
            await _repository.DeleteAsync(SelectedItem.Model.Id).ConfigureAwait(true);
            _allApplications.Remove(SelectedItem);
            ApplyFiltersAndSort();
        }

        ShowSuccess(
            "Uninstallation Finished",
            $"The vendor uninstaller completed. Residual leftovers were kept on the system as requested.",
            false,
            false);
    }

    [RelayCommand]
    public async Task RollbackLastTransactionAsync()
    {
        if (!LastTransactionId.HasValue)
            return;

        FlowStatusMessage = "Restoring backed-up items from transaction...";

        try
        {
            var rollbackResult = await _rollbackService.RollbackTransactionAsync(
                LastTransactionId.Value,
                _flowCts?.Token ?? default).ConfigureAwait(true);

            if (rollbackResult.IsSuccess && rollbackResult.Value is not null)
            {
                var summary = rollbackResult.Value;
                SummaryDetails = $"Rollback completed! Restored {summary.ItemsRestored} item(s) to original locations. Failed: {summary.ItemsFailed}.";
                CanRollback = false;
                await RefreshAsync();
            }
            else
            {
                SummaryDetails = $"Rollback failed: {rollbackResult.Error?.Message}";
            }
        }
        catch (Exception ex)
        {
            SummaryDetails = $"Rollback error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CloseSummary()
    {
        FlowStage = UninstallFlowStage.Idle;
        IsFlowActive = false;
        ActiveProcesses.Clear();
    }

    private void ShowSuccess(string title, string details, bool rebootRequired, bool canRollback)
    {
        SummaryTitle = title;
        SummaryDetails = details;
        RebootRequired = rebootRequired;
        CanRollback = canRollback;
        FlowStage = UninstallFlowStage.CompletedSummary;
    }

    private void ShowFailure(string title, string details)
    {
        SummaryTitle = title;
        SummaryDetails = details;
        RebootRequired = false;
        CanRollback = false;
        FlowStage = UninstallFlowStage.CompletedSummary;
    }

    public void Dispose()
    {
        _flowCts?.Cancel();
        _flowCts?.Dispose();
        _flowCts = null;
    }
}
