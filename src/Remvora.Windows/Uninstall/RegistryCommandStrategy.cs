using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Uninstall;
using Remvora.Core.CommandLine;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Policies;

namespace Remvora.Windows.Uninstall;

/// <summary>
/// Executes standard uninstallation for executable-based installers using registry uninstall commands.
/// </summary>
public sealed partial class RegistryCommandStrategy : IUninstallStrategy
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);
    private readonly ILogger<RegistryCommandStrategy> _logger;

    public RegistryCommandStrategy(ILogger<RegistryCommandStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public InstallerType SupportedType => InstallerType.InnoSetup; // General exe-based uninstaller

    public bool CanHandle(ApplicationRecord application)
    {
        ArgumentNullException.ThrowIfNull(application);

        // Do not handle MSI or Store packages with this strategy
        if (application.InstallerType == InstallerType.StorePackage || !string.IsNullOrWhiteSpace(application.Identity.PackageFamilyName))
            return false;

        if (application.InstallerType == InstallerType.Msi && !string.IsNullOrWhiteSpace(application.Identity.ProductCode))
            return false;

        return !string.IsNullOrWhiteSpace(application.Uninstall.UninstallString) ||
               !string.IsNullOrWhiteSpace(application.Uninstall.QuietUninstallString);
    }

    public async Task<UninstallExecutionResult> ExecuteAsync(
        ApplicationRecord application,
        UninstallOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        // 1. Select command string
        string? rawCommand;
        var usingExplicitQuietString = false;

        if (options.RequestQuiet && !string.IsNullOrWhiteSpace(application.Uninstall.QuietUninstallString))
        {
            rawCommand = application.Uninstall.QuietUninstallString;
            usingExplicitQuietString = true;
        }
        else
        {
            rawCommand = application.Uninstall.UninstallString ?? application.Uninstall.QuietUninstallString;
        }

        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return UninstallExecutionResult.Failure(
                exitCode: -1,
                message: $"No uninstall command string found for '{application.DisplayName}'.",
                duration: TimeSpan.Zero);
        }

        // 2. Parse command line
        var parsed = CommandLineParser.Parse(rawCommand);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.ExecutablePath))
        {
            return UninstallExecutionResult.Failure(
                exitCode: -1,
                message: $"Failed to parse uninstall command: '{rawCommand}'.",
                duration: TimeSpan.Zero);
        }

        var exePath = parsed.ExecutablePath;
        var arguments = parsed.Arguments;

        // Safety: ensure executable path is not a critical protected OS root
        if (ProtectedPathsPolicy.IsProtected(exePath))
        {
            var msg = $"Refusing to execute uninstaller: executable path '{exePath}' is in a protected system directory.";
            LogProtectedPathBlocked(_logger, msg);
            return UninstallExecutionResult.Failure(exitCode: -1, message: msg, duration: TimeSpan.Zero);
        }

        // 3. Inject quiet parameters if quiet was requested but explicit quiet string wasn't used
        if (options.RequestQuiet && !usingExplicitQuietString)
        {
            arguments = AppendSilentArguments(arguments, application.InstallerType);
        }

        // 4. Resolve working directory
        string? workingDir = null;
        if (!string.IsNullOrWhiteSpace(application.InstallLocation) && Directory.Exists(application.InstallLocation))
        {
            workingDir = application.InstallLocation;
        }
        else
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
            {
                workingDir = exeDir;
            }
        }

        progress?.Report($"Launching uninstaller: {exePath} {arguments}".Trim());
        LogExecutingUninstaller(_logger, exePath, arguments, workingDir ?? string.Empty);

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDir ?? string.Empty,
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
            LogUninstallerExited(_logger, exitCode, stopwatch.ElapsedMilliseconds);

            var rebootRequired = exitCode == 3010;
            var isSuccess = exitCode == 0 || rebootRequired;
            var message = isSuccess
                ? (rebootRequired ? "Uninstaller completed. System restart is required." : "Uninstaller completed successfully.")
                : $"Uninstaller process returned non-zero exit code: {exitCode}.";

            return new UninstallExecutionResult(
                IsSuccess: isSuccess,
                ExitCode: exitCode,
                RebootRequired: rebootRequired,
                Message: message,
                Duration: stopwatch.Elapsed);
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
                // Ignore kill error on canceled process
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
            LogUninstallerFailed(_logger, ex, application.DisplayName);
            return UninstallExecutionResult.Failure(-1, $"Failed to launch uninstaller: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private static string AppendSilentArguments(string currentArgs, InstallerType installerType)
    {
        var lower = currentArgs.ToLowerInvariant();
        if (lower.Contains("/silent") || lower.Contains("/verysilent") || lower.Contains("/s") || lower.Contains("/quiet") || lower.Contains("/qn"))
        {
            return currentArgs;
        }

        return installerType switch
        {
            InstallerType.InnoSetup => $"{currentArgs} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART".Trim(),
            InstallerType.Nsis => $"{currentArgs} /S".Trim(),
            InstallerType.InstallShield => $"{currentArgs} /s /v/qn".Trim(),
            _ => $"{currentArgs} /quiet /norestart".Trim()
        };
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "{Message}")]
    private static partial void LogProtectedPathBlocked(ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Executing uninstaller: '{Exe}' with args '{Args}' in '{Dir}'")]
    private static partial void LogExecutingUninstaller(ILogger logger, string exe, string args, string dir);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Uninstaller process exited with code {ExitCode} in {ElapsedMs}ms")]
    private static partial void LogUninstallerExited(ILogger logger, int exitCode, long elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to execute uninstaller for '{AppName}'")]
    private static partial void LogUninstallerFailed(ILogger logger, Exception ex, string appName);
}
