using Remvora.Core.Domain.Applications;

namespace Remvora.Application.Uninstall;

/// <summary>
/// Options controlling the execution of an uninstallation workflow.
/// </summary>
public sealed record UninstallOptions(
    bool RequestQuiet = false,
    TimeSpan? Timeout = null)
{
    public static UninstallOptions Default => new();
}

/// <summary>
/// Structured result capturing the outcome of an uninstaller execution.
/// </summary>
public sealed record UninstallExecutionResult(
    bool IsSuccess,
    int ExitCode,
    bool RebootRequired,
    string Message,
    TimeSpan Duration)
{
    public static UninstallExecutionResult Success(int exitCode, TimeSpan duration, bool rebootRequired = false, string message = "Uninstaller completed successfully.")
        => new(true, exitCode, rebootRequired, message, duration);

    public static UninstallExecutionResult Failure(int exitCode, string message, TimeSpan duration)
        => new(false, exitCode, false, message, duration);
}

/// <summary>
/// Defines a strategy for executing an application vendor uninstaller.
/// </summary>
public interface IUninstallStrategy
{
    InstallerType SupportedType { get; }
    bool CanHandle(ApplicationRecord application);
    Task<UninstallExecutionResult> ExecuteAsync(
        ApplicationRecord application,
        UninstallOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
