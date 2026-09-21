using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Remvora.Application.Leftovers;
using Remvora.Application.Processes;
using Remvora.Application.Stats;
using Remvora.Application.Transactions;
using Remvora.Application.Workflows;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Domain.Stats;

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
    private readonly ICleaningStatsRepository _cleaningStatsRepository;
    private readonly ILogger<AppsViewModel> _logger;

    private readonly List<ApplicationItemViewModel> _allApplications = [];
    private CancellationTokenSource? _flowCts;
    private CancellationTokenSource? _batchCts;

    [ObservableProperty]
    public partial ObservableCollection<ApplicationItemViewModel> FilteredApplications { get; set; } = [];

    [ObservableProperty]
    public partial ApplicationItemViewModel? SelectedItem { get; set; }

    // Batch Selection Properties
    [ObservableProperty]
    public partial int SelectedAppsCount { get; set; }

    [ObservableProperty]
    public partial string SelectedAppsSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial bool HasSelectedApps { get; set; }

    [ObservableProperty]
    public partial bool IsAllSelected { get; set; }

    // Batch Execution State
    [ObservableProperty]
    public partial bool IsBatchRunning { get; set; }

    [ObservableProperty]
    public partial bool IsBatchModalOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBatchSummaryOpen { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<BatchAppItemViewModel> BatchQueue { get; set; } = [];

    [ObservableProperty]
    public partial int BatchCurrentIndex { get; set; }

    [ObservableProperty]
    public partial int BatchTotalCount { get; set; }

    [ObservableProperty]
    public partial double BatchProgressPercent { get; set; }

    [ObservableProperty]
    public partial string BatchStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int BatchSuccessCount { get; set; }

    [ObservableProperty]
    public partial int BatchFailureCount { get; set; }

    [ObservableProperty]
    public partial string BatchReclaimedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial ObservableCollection<BatchAppItemViewModel> BatchFailedApps { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    [ObservableProperty]
    public partial bool FilterDesktop { get; set; } = true;

    [ObservableProperty]
    public partial bool FilterStore { get; set; } = true;

    [ObservableProperty]
    public partial bool FilterLarge { get; set; } = false;

    [ObservableProperty]
    public partial bool FilterSystem { get; set; } = false;

    [ObservableProperty]
    public partial string FilterSummaryText { get; set; } = "All Apps";

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = false;

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
        ICleaningStatsRepository cleaningStatsRepository,
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
        _cleaningStatsRepository = cleaningStatsRepository ?? throw new ArgumentNullException(nameof(cleaningStatsRepository));
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
            if (IsInternalSystemComponent(app))
                continue;

            var item = new ApplicationItemViewModel(app);
            item.SelectionChanged += OnItemSelectionChanged;
            _allApplications.Add(item);
            totalBytes += app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L;
        }

        TotalSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);
        ApplyFiltersAndSort();
        UpdateSelectionMetrics();
    }

    private void OnItemSelectionChanged(ApplicationItemViewModel item)
    {
        UpdateSelectionMetrics();
    }

    private void UpdateSelectionMetrics()
    {
        var selected = _allApplications.Where(a => a.IsSelected).ToList();
        SelectedAppsCount = selected.Count;
        HasSelectedApps = selected.Count > 0;
        long totalBytes = selected.Sum(a => a.Model.EstimatedSizeBytes ?? a.Model.CalculatedSizeBytes ?? 0L);
        SelectedAppsSizeText = ApplicationItemViewModel.FormatBytes(totalBytes);
        IsAllSelected = FilteredApplications.Count > 0 && FilteredApplications.All(a => a.IsSelected);
    }

    [RelayCommand]
    public void SelectAllApps()
    {
        foreach (var app in FilteredApplications)
        {
            app.IsSelected = true;
        }
        UpdateSelectionMetrics();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var app in _allApplications)
        {
            app.IsSelected = false;
        }
        UpdateSelectionMetrics();
    }

    [RelayCommand]
    public void ToggleSelectAll()
    {
        if (IsAllSelected)
            ClearSelection();
        else
            SelectAllApps();
    }

    private static bool IsInternalSystemComponent(ApplicationRecord app)
    {
        if (app.IsSystemComponent)
            return true;

        if (Guid.TryParse(app.DisplayName, out _))
            return true;

        if (!string.IsNullOrWhiteSpace(app.InstallLocation) &&
            (app.InstallLocation.Contains(@"\SystemApps\", StringComparison.OrdinalIgnoreCase) ||
             app.InstallLocation.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (app.DisplayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ||
            app.DisplayName.StartsWith("@{", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedCategoryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedSortChanged(string value) => ApplyFiltersAndSort();

    partial void OnFilterDesktopChanged(bool value) => OnFilterStateChanged();
    partial void OnFilterStoreChanged(bool value) => OnFilterStateChanged();
    partial void OnFilterLargeChanged(bool value) => OnFilterStateChanged();
    partial void OnFilterSystemChanged(bool value) => OnFilterStateChanged();

    private void OnFilterStateChanged()
    {
        var active = new List<string>();
        if (FilterDesktop && FilterStore) active.Add("All Apps");
        else if (FilterDesktop) active.Add("Desktop");
        else if (FilterStore) active.Add("Store");

        if (FilterLarge) active.Add("Large");
        if (FilterSystem) active.Add("System");

        FilterSummaryText = active.Count == 0 ? "Filters (None)" : string.Join(", ", active);
        ApplyFiltersAndSort();
    }

    [RelayCommand]
    public void SetListView() => IsGridView = false;

    [RelayCommand]
    public void SetGridView() => IsGridView = true;

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

        // Multi-select scope/category filters
        if (!FilterDesktop && !FilterStore)
        {
            // If both are unchecked, display all by default
        }
        else if (FilterDesktop && !FilterStore)
        {
            query = query.Where(a => !a.IsStoreApp);
        }
        else if (!FilterDesktop && FilterStore)
        {
            query = query.Where(a => a.IsStoreApp);
        }

        if (FilterLarge)
        {
            query = query.Where(a => (a.Model.EstimatedSizeBytes ?? a.Model.CalculatedSizeBytes ?? 0) >= 500 * 1024 * 1024);
        }

        if (!FilterSystem)
        {
            query = query.Where(a => !a.Model.IsSystemComponent);
        }

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

            await _cleaningStatsRepository.RecordEventAsync(new CleaningStatEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                CleaningCategory.AppUninstall,
                1,
                app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L,
                $"Clean uninstallation of {app.DisplayName}"), _flowCts?.Token ?? default).ConfigureAwait(true);

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
            var app = SelectedItem.Model;
            await _repository.DeleteAsync(app.Id).ConfigureAwait(true);
            _allApplications.Remove(SelectedItem);
            ApplyFiltersAndSort();

            LastTransactionId = result.PlanId;
            var reclaimed = ApplicationItemViewModel.FormatBytes(result.ReclaimedSizeBytes);

            var totalReclaimed = (app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L) + result.ReclaimedSizeBytes;
            await _cleaningStatsRepository.RecordEventAsync(new CleaningStatEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                CleaningCategory.AppUninstall,
                result.SucceededItems + 1,
                totalReclaimed,
                $"Uninstallation and leftover cleanup for {app.DisplayName}"), _flowCts?.Token ?? default).ConfigureAwait(true);

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
            var app = SelectedItem.Model;
            await _repository.DeleteAsync(app.Id).ConfigureAwait(true);
            _allApplications.Remove(SelectedItem);
            ApplyFiltersAndSort();

            await _cleaningStatsRepository.RecordEventAsync(new CleaningStatEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                CleaningCategory.AppUninstall,
                1,
                app.EstimatedSizeBytes ?? app.CalculatedSizeBytes ?? 0L,
                $"Vendor uninstallation of {app.DisplayName}"), default).ConfigureAwait(true);
        }

        ShowSuccess(
            "Uninstallation Finished",
            $"The vendor uninstaller completed. Residual leftovers were kept on the system as requested.",
            false,
            false);
    }

    // ==========================================
    // BATCH UNINSTALL EXECUTION & ISOLATION
    // ==========================================

    [RelayCommand]
    public async Task StartBatchUninstallAsync()
    {
        var selected = _allApplications.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;

        BatchQueue.Clear();
        BatchFailedApps.Clear();
        foreach (var s in selected)
        {
            BatchQueue.Add(new BatchAppItemViewModel(s.Model));
        }

        BatchTotalCount = BatchQueue.Count;
        BatchCurrentIndex = 0;
        BatchSuccessCount = 0;
        BatchFailureCount = 0;
        BatchProgressPercent = 0;
        IsBatchRunning = true;
        IsBatchModalOpen = true;
        IsBatchSummaryOpen = false;

        long totalReclaimed = 0;
        var succeededModels = new List<ApplicationRecord>();

        for (int i = 0; i < BatchQueue.Count; i++)
        {
            if (token.IsCancellationRequested)
                break;

            var currentItem = BatchQueue[i];
            BatchCurrentIndex = i + 1;
            BatchProgressPercent = ((double)i / BatchTotalCount) * 100.0;
            BatchStatusMessage = $"Uninstalling {currentItem.DisplayName} ({BatchCurrentIndex}/{BatchTotalCount})...";
            currentItem.IsActive = true;
            currentItem.Status = "Uninstalling...";

            try
            {
                // 1. Process termination if any
                var processes = await _processDetector.DetectProcessesAsync(currentItem.Model, token).ConfigureAwait(true);

                // 2. Vendor uninstaller execution
                var uninstallProgress = new Progress<UninstallWorkflowProgress>(p =>
                {
                    currentItem.Status = p.Message;
                });

                var result = await _uninstallOrchestrator.UninstallAsync(
                    currentItem.Model,
                    new UninstallWorkflowOptions(
                        TerminateProcesses: true,
                        WarnIfProcessesRunning: false,
                        CreateRestorePoint: false),
                    uninstallProgress,
                    token).ConfigureAwait(true);

                // 3. Scan leftovers
                currentItem.Status = "Scanning leftover traces...";
                var leftovers = await _leftoverScanner.ScanLeftoversAsync(
                    currentItem.Model,
                    ScanOptions.Default,
                    null,
                    token).ConfigureAwait(true);

                long appReclaimed = currentItem.Model.EstimatedSizeBytes ?? currentItem.Model.CalculatedSizeBytes ?? 0L;
                int itemsRemoved = 1;

                if (leftovers.Count > 0)
                {
                    currentItem.Status = "Cleaning remnants...";
                    var plan = new CleanupPlan(Guid.NewGuid(), currentItem.Model.Id, DateTimeOffset.UtcNow, leftovers);
                    var candidateIds = plan.SelectedCandidates.Select(c => c.Id).ToList();

                    var cleanupResult = await _transactionExecutor.ExecuteCleanupAsync(
                        plan,
                        candidateIds,
                        null,
                        token).ConfigureAwait(true);

                    appReclaimed += cleanupResult.ReclaimedSizeBytes;
                    itemsRemoved += cleanupResult.SucceededItems;
                }

                // Remove from repository
                await _repository.DeleteAsync(currentItem.Model.Id).ConfigureAwait(true);
                succeededModels.Add(currentItem.Model);
                totalReclaimed += appReclaimed;
                currentItem.ReclaimedBytes = appReclaimed;

                // Record cleaning statistics
                await _cleaningStatsRepository.RecordEventAsync(new CleaningStatEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    CleaningCategory.BatchUninstall,
                    itemsRemoved,
                    appReclaimed,
                    $"Batch Uninstallation of {currentItem.DisplayName}"), token).ConfigureAwait(true);

                currentItem.Status = "Completed";
                currentItem.IsSuccess = true;
                currentItem.IsCompleted = true;
                currentItem.IsActive = false;
                BatchSuccessCount++;
            }
            catch (OperationCanceledException)
            {
                currentItem.Status = "Cancelled";
                currentItem.IsActive = false;
                currentItem.IsCompleted = true;
                break;
            }
            catch (Exception ex)
            {
                LogBatchItemError(_logger, currentItem.DisplayName, ex.Message, ex);
                currentItem.Status = "Failed";
                currentItem.ErrorMessage = ex.Message;
                currentItem.IsSuccess = false;
                currentItem.IsCompleted = true;
                currentItem.IsActive = false;
                BatchFailureCount++;
                BatchFailedApps.Add(currentItem);
            }
        }

        BatchProgressPercent = 100.0;
        BatchReclaimedSizeText = ApplicationItemViewModel.FormatBytes(totalReclaimed);
        IsBatchRunning = false;
        IsBatchSummaryOpen = true;

        if (succeededModels.Count > 0)
        {
            _allApplications.RemoveAll(vm => succeededModels.Any(m => m.Id == vm.Id));
            ApplyFiltersAndSort();
            UpdateSelectionMetrics();
        }
    }

    [RelayCommand]
    public void CancelBatch()
    {
        _batchCts?.Cancel();
        BatchStatusMessage = "Cancelling remaining batch items...";
    }

    [RelayCommand]
    public void CloseBatchSummary()
    {
        IsBatchModalOpen = false;
        IsBatchSummaryOpen = false;
        BatchQueue.Clear();
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

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Batch uninstallation failed for application {AppName}: {ErrorMessage}")]
    private static partial void LogBatchItemError(ILogger logger, string appName, string errorMessage, Exception? ex);

    public void Dispose()
    {
        _flowCts?.Cancel();
        _flowCts?.Dispose();
        _flowCts = null;

        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = null;
    }
}
