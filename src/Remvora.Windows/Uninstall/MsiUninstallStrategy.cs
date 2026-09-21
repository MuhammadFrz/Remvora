using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Remvora.Application.Uninstall;
using Remvora.Core.Domain.Applications;

namespace Remvora.Windows.Uninstall;

/// <summary>
/// Executes standard uninstallation for Windows Installer (MSI) packages via msiexec.exe.
/// </summary>
public sealed partial class MsiUninstallStrategy : IUninstallStrategy
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);
    private readonly ILogger<MsiUninstallStrategy> _logger;

    [GeneratedRegex(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}")]
    private static partial Regex GuidRegex();

    public MsiUninstallStrategy(ILogger<MsiUninstallStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public InstallerType SupportedType => InstallerType.Msi;

    public bool CanHandle(ApplicationRecord application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!string.IsNullOrWhiteSpace(application.Identity.ProductCode))
            return true;

        if (application.Uninstall.IsMsi || application.InstallerType == InstallerType.Msi)
            return true;

        if (!string.IsNullOrWhiteSpace(application.Uninstall.UninstallString) &&
            application.Uninstall.UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase) &&
            GuidRegex().IsMatch(application.Uninstall.UninstallString))
        {
            return true;
        }

        return false;
    }

    public async Task<UninstallExecutionResult> ExecuteAsync(
        ApplicationRecord application,
        UninstallOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var productCode = ExtractProductCode(application);
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return UninstallExecutionResult.Failure(
                exitCode: -1,
                message: $"Cannot perform MSI uninstall for '{application.DisplayName}': missing or invalid MSI ProductCode.",
                duration: TimeSpan.Zero);
        }

        var arguments = $"/x {productCode}";
        if (options.RequestQuiet)
        {
            arguments += " /qn /norestart";
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var msiexecPath = Path.Combine(systemRoot, "System32", "msiexec.exe");
        if (!File.Exists(msiexecPath))
        {
            msiexecPath = "msiexec.exe";
        }

        progress?.Report($"Launching MSI uninstaller: {msiexecPath} {arguments}");
        LogExecutingMsi(_logger, msiexecPath, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = msiexecPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = options.RequestQuiet,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var stopwatch = Stopwatch.StartNew();
        var timeout = options.Timeout ?? DefaultTimeout;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var exitCode = process.ExitCode;
            LogMsiExited(_logger, exitCode, stopwatch.ElapsedMilliseconds);

            return InterpretMsiExitCode(exitCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore failure killing canceled process
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return UninstallExecutionResult.Failure(-1, "Uninstall operation was canceled by the user.", stopwatch.Elapsed);
            }

            return UninstallExecutionResult.Failure(-1, $"Uninstall timed out after {timeout.TotalMinutes:N0} minute(s).", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogMsiExecutionFailed(_logger, ex, application.DisplayName);
            return UninstallExecutionResult.Failure(-1, $"Failed to launch msiexec: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private static string? ExtractProductCode(ApplicationRecord application)
    {
        if (!string.IsNullOrWhiteSpace(application.Identity.ProductCode))
            return application.Identity.ProductCode.Trim();

        if (!string.IsNullOrWhiteSpace(application.Uninstall.UninstallString))
        {
            var match = GuidRegex().Match(application.Uninstall.UninstallString);
            if (match.Success)
                return match.Value;
        }

        return null;
    }

    private static UninstallExecutionResult InterpretMsiExitCode(int exitCode, TimeSpan duration)
    {
        return exitCode switch
        {
            0 => UninstallExecutionResult.Success(0, duration, rebootRequired: false, "MSI uninstallation succeeded."),
            3010 => UninstallExecutionResult.Success(3010, duration, rebootRequired: true, "MSI uninstallation succeeded. System restart is required to complete removal."),
            1641 => UninstallExecutionResult.Success(1641, duration, rebootRequired: true, "MSI uninstallation initiated a system restart."),
            1602 => UninstallExecutionResult.Failure(1602, "Uninstallation was cancelled by the user.", duration),
            1603 => UninstallExecutionResult.Failure(1603, "Fatal error occurred during MSI uninstallation (Exit code 1603).", duration),
            1605 => UninstallExecutionResult.Failure(1605, "This product is not currently installed.", duration),
            1618 => UninstallExecutionResult.Failure(1618, "Another installation or uninstallation is already in progress.", duration),
            _ => UninstallExecutionResult.Failure(exitCode, $"msiexec returned exit code {exitCode}.", duration)
        };
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Executing MSI uninstall: {Exe} {Args}")]
    private static partial void LogExecutingMsi(ILogger logger, string exe, string args);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "msiexec.exe exited with code {ExitCode} in {ElapsedMs}ms")]
    private static partial void LogMsiExited(ILogger logger, int exitCode, long elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to execute msiexec for {AppName}")]
    private static partial void LogMsiExecutionFailed(ILogger logger, Exception ex, string appName);
}
