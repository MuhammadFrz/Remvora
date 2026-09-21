using Remvora.Application.Processes;

namespace Remvora.Application.Workflows;

/// <summary>
/// Step within the uninstallation workflow.
/// </summary>
public enum UninstallStep
{
    Starting,
    CheckingRunningProcesses,
    TerminatingRunningProcesses,
    CreatingRestorePoint,
    ExecutingVendorUninstaller,
    PostUninstallVerification,
    Completed,
    Failed
}

/// <summary>
/// Progress reporting payload for the uninstallation workflow.
/// </summary>
public sealed record UninstallWorkflowProgress(
    UninstallStep Step,
    string Message);

/// <summary>
/// Options configuring the overall uninstallation workflow.
/// </summary>
public sealed record UninstallWorkflowOptions(
    bool RequestQuiet = false,
    bool CreateRestorePoint = true,
    bool WarnIfProcessesRunning = true,
    bool TerminateProcesses = false,
    TimeSpan? Timeout = null)
{
    public static UninstallWorkflowOptions Default => new();
}

/// <summary>
/// Comprehensive outcome of an uninstallation workflow execution.
/// </summary>
public sealed record UninstallWorkflowResult(
    bool IsSuccess,
    int ExitCode,
    bool RebootRequired,
    long? RestorePointSequence,
    IReadOnlyList<RunningProcessInfo> RemainingProcesses,
    string Message,
    TimeSpan Duration)
{
    public static UninstallWorkflowResult Success(
        int exitCode,
        TimeSpan duration,
        bool rebootRequired = false,
        long? restorePointSequence = null,
        string message = "Application uninstalled successfully.")
        => new(
            IsSuccess: true,
            ExitCode: exitCode,
            RebootRequired: rebootRequired,
            RestorePointSequence: restorePointSequence,
            RemainingProcesses: Array.Empty<RunningProcessInfo>(),
            Message: message,
            Duration: duration);

    public static UninstallWorkflowResult Failure(
        int exitCode,
        string message,
        TimeSpan duration,
        IReadOnlyList<RunningProcessInfo>? remainingProcesses = null,
        long? restorePointSequence = null)
        => new(
            IsSuccess: false,
            ExitCode: exitCode,
            RebootRequired: false,
            RestorePointSequence: restorePointSequence,
            RemainingProcesses: remainingProcesses ?? Array.Empty<RunningProcessInfo>(),
            Message: message,
            Duration: duration);
}

/// <summary>
/// Orchestrates complete vendor uninstallation lifecycle including pre-checks,
/// system restore, execution, and audit logging.
/// </summary>
public interface IUninstallOrchestrator
{
    Task<UninstallWorkflowResult> UninstallAsync(
        Core.Domain.Applications.ApplicationRecord application,
        UninstallWorkflowOptions options,
        IProgress<UninstallWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
