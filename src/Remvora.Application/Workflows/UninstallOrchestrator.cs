using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Auditing;
using Remvora.Application.Processes;
using Remvora.Application.RestorePoint;
using Remvora.Application.Uninstall;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Workflows;

/// <summary>
/// Orchestrates complete vendor uninstallation lifecycle including pre-checks,
/// system restore, strategy selection, execution, and audit logging.
/// </summary>
public sealed partial class UninstallOrchestrator : IUninstallOrchestrator
{
    private readonly IEnumerable<IUninstallStrategy> _strategies;
    private readonly IProcessDetector _processDetector;
    private readonly IRestorePointService _restorePointService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UninstallOrchestrator> _logger;

    public UninstallOrchestrator(
        IEnumerable<IUninstallStrategy> strategies,
        IProcessDetector processDetector,
        IRestorePointService restorePointService,
        IAuditLogRepository auditLogRepository,
        ILogger<UninstallOrchestrator> logger)
    {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        _processDetector = processDetector ?? throw new ArgumentNullException(nameof(processDetector));
        _restorePointService = restorePointService ?? throw new ArgumentNullException(nameof(restorePointService));
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UninstallWorkflowResult> UninstallAsync(
        ApplicationRecord application,
        UninstallWorkflowOptions options,
        IProgress<UninstallWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        progress?.Report(new UninstallWorkflowProgress(UninstallStep.Starting, $"Starting uninstall of {application.DisplayName}"));

        // 1. Check running processes
        progress?.Report(new UninstallWorkflowProgress(UninstallStep.CheckingRunningProcesses, "Checking for active running processes..."));
        var runningProcesses = await _processDetector.DetectProcessesAsync(application, cancellationToken).ConfigureAwait(false);

        if (runningProcesses.Count > 0)
        {
            if (options.TerminateProcesses)
            {
                progress?.Report(new UninstallWorkflowProgress(UninstallStep.TerminatingRunningProcesses, $"Closing {runningProcesses.Count} running process(es)..."));
                foreach (var proc in runningProcesses)
                {
                    await _processDetector.TerminateProcessAsync(proc.ProcessId, force: true, cancellationToken).ConfigureAwait(false);
                }

                runningProcesses = await _processDetector.DetectProcessesAsync(application, cancellationToken).ConfigureAwait(false);
            }
            else if (options.WarnIfProcessesRunning)
            {
                stopwatch.Stop();
                var msg = $"Cannot proceed with uninstall: {runningProcesses.Count} process(es) associated with '{application.DisplayName}' are currently running.";
                LogProcessesRunningWarning(_logger, msg);
                return UninstallWorkflowResult.Failure(
                    exitCode: -1,
                    message: msg,
                    duration: stopwatch.Elapsed,
                    remainingProcesses: runningProcesses);
            }
        }

        // 2. Create System Restore Point if requested
        long? restorePointSequence = null;
        if (options.CreateRestorePoint)
        {
            progress?.Report(new UninstallWorkflowProgress(UninstallStep.CreatingRestorePoint, "Creating Windows System Restore point..."));
            var restoreResult = await _restorePointService.CreateRestorePointAsync(
                $"Remvora: Uninstall {application.DisplayName}",
                cancellationToken).ConfigureAwait(false);

            if (restoreResult.IsSuccess && restoreResult.Value is not null)
            {
                restorePointSequence = restoreResult.Value.SequenceNumber;
                LogRestorePointCreated(_logger, restorePointSequence.Value);
            }
            else
            {
                LogRestorePointFailed(_logger, restoreResult.Error?.Message ?? "Unknown reason");
            }
        }

        // 3. Resolve strategy
        var strategy = ResolveStrategy(application);
        if (strategy is null)
        {
            stopwatch.Stop();
            var noStrategyMsg = $"No suitable uninstall strategy found for application '{application.DisplayName}' (Type: {application.InstallerType}).";
            LogStrategyNotFound(_logger, noStrategyMsg);

            await RecordAuditAsync(
                application: application,
                action: "Uninstall Application",
                result: "Failed - Strategy not found",
                severity: AuditSeverity.Error,
                errorCode: ErrorCode.UninstallStrategyNotFound,
                details: noStrategyMsg,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return UninstallWorkflowResult.Failure(
                exitCode: -1,
                message: noStrategyMsg,
                duration: stopwatch.Elapsed,
                restorePointSequence: restorePointSequence);
        }

        // 4. Execute uninstaller
        progress?.Report(new UninstallWorkflowProgress(UninstallStep.ExecutingVendorUninstaller, $"Running vendor uninstaller using {strategy.GetType().Name}..."));
        var uninstallOptions = new UninstallOptions(RequestQuiet: options.RequestQuiet, Timeout: options.Timeout);

        var strategyProgress = progress is not null
            ? new Progress<string>(msg => progress.Report(new UninstallWorkflowProgress(UninstallStep.ExecutingVendorUninstaller, msg)))
            : null;

        UninstallExecutionResult executionResult;
        try
        {
            executionResult = await strategy.ExecuteAsync(
                application,
                uninstallOptions,
                strategyProgress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogExecutionException(_logger, ex, application.DisplayName);

            await RecordAuditAsync(
                application: application,
                action: "Uninstall Application",
                result: "Exception",
                severity: AuditSeverity.Critical,
                errorCode: ErrorCode.ExecutionFailed,
                details: ex.Message,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return UninstallWorkflowResult.Failure(
                exitCode: -1,
                message: $"Execution failed: {ex.Message}",
                duration: stopwatch.Elapsed,
                restorePointSequence: restorePointSequence);
        }

        stopwatch.Stop();

        // 5. Post-uninstall verification & Audit
        progress?.Report(new UninstallWorkflowProgress(UninstallStep.PostUninstallVerification, "Verifying uninstallation results..."));

        var finalProcesses = await _processDetector.DetectProcessesAsync(application, cancellationToken).ConfigureAwait(false);

        await RecordAuditAsync(
            application: application,
            action: "Uninstall Application",
            result: executionResult.IsSuccess ? "Success" : "Failed",
            severity: executionResult.IsSuccess ? AuditSeverity.Information : AuditSeverity.Warning,
            errorCode: executionResult.IsSuccess ? ErrorCode.None : ErrorCode.UninstallerExitCodeNonZero,
            details: $"ExitCode: {executionResult.ExitCode}, RebootRequired: {executionResult.RebootRequired}, Duration: {executionResult.Duration}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var finalStep = executionResult.IsSuccess ? UninstallStep.Completed : UninstallStep.Failed;
        progress?.Report(new UninstallWorkflowProgress(finalStep, executionResult.Message));

        if (executionResult.IsSuccess)
        {
            return UninstallWorkflowResult.Success(
                exitCode: executionResult.ExitCode,
                duration: stopwatch.Elapsed,
                rebootRequired: executionResult.RebootRequired,
                restorePointSequence: restorePointSequence,
                message: executionResult.Message);
        }

        return UninstallWorkflowResult.Failure(
            exitCode: executionResult.ExitCode,
            message: executionResult.Message,
            duration: stopwatch.Elapsed,
            remainingProcesses: finalProcesses,
            restorePointSequence: restorePointSequence);
    }

    private IUninstallStrategy? ResolveStrategy(ApplicationRecord application)
    {
        // 1. MSI
        if (application.InstallerType == InstallerType.Msi || application.Uninstall.IsMsi || !string.IsNullOrWhiteSpace(application.Identity.ProductCode))
        {
            var msiStrat = _strategies.FirstOrDefault(s => s.SupportedType == InstallerType.Msi && s.CanHandle(application));
            if (msiStrat is not null)
                return msiStrat;
        }

        // 2. Packaged App
        if (application.InstallerType == InstallerType.StorePackage || !string.IsNullOrWhiteSpace(application.Identity.PackageFamilyName))
        {
            var pkgStrat = _strategies.FirstOrDefault(s => s.SupportedType == InstallerType.StorePackage && s.CanHandle(application));
            if (pkgStrat is not null)
                return pkgStrat;
        }

        // 3. Any strategy that declares it can handle
        return _strategies.FirstOrDefault(s => s.CanHandle(application));
    }

    private async Task RecordAuditAsync(
        ApplicationRecord application,
        string action,
        string result,
        AuditSeverity severity,
        ErrorCode errorCode,
        string details,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new AuditEvent(
                Id: Guid.NewGuid(),
                Timestamp: DateTimeOffset.UtcNow,
                Category: AuditCategory.UninstallSubprocess,
                Severity: severity,
                Action: action,
                Target: application.InstallLocation ?? application.DisplayName,
                ApplicationId: application.Id,
                TransactionId: null,
                Result: result,
                ErrorCode: errorCode,
                Details: details);

            await _auditLogRepository.AppendAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditPersistenceFailed(_logger, ex, action);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogProcessesRunningWarning(ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Created restore point sequence #{Sequence}")]
    private static partial void LogRestorePointCreated(ILogger logger, long sequence);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "System restore point creation failed or was skipped: {Error}")]
    private static partial void LogRestorePointFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "{Message}")]
    private static partial void LogStrategyNotFound(ILogger logger, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Exception during uninstaller execution for '{AppName}'")]
    private static partial void LogExecutionException(ILogger logger, Exception ex, string appName);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to persist audit event for {Action}")]
    private static partial void LogAuditPersistenceFailed(ILogger logger, Exception ex, string action);
}
