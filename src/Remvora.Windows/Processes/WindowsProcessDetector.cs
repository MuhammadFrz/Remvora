using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Processes;
using Remvora.Core.CommandLine;
using Remvora.Core.Domain.Applications;

namespace Remvora.Windows.Processes;

/// <summary>
/// Windows implementation of process detection and termination for applications being uninstalled.
/// </summary>
public sealed partial class WindowsProcessDetector : IProcessDetector
{
    private readonly ILogger<WindowsProcessDetector> _logger;

    public WindowsProcessDetector(ILogger<WindowsProcessDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<RunningProcessInfo>> DetectProcessesAsync(
        ApplicationRecord application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);

        var matches = new List<RunningProcessInfo>();

        var normalizedInstallLocation = !string.IsNullOrWhiteSpace(application.InstallLocation)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(application.InstallLocation))
            : null;

        string? uninstallExeName = null;
        if (!string.IsNullOrWhiteSpace(application.Uninstall.UninstallString))
        {
            var parsed = CommandLineParser.Parse(application.Uninstall.UninstallString);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.ExecutablePath))
            {
                try
                {
                    uninstallExeName = Path.GetFileNameWithoutExtension(parsed.ExecutablePath);
                }
                catch
                {
                    // Ignore path parse errors on raw registry strings
                }
            }
        }

        Process[] allProcesses;
        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            LogEnumerateProcessesFailed(_logger, ex);
            return Task.FromResult<IReadOnlyList<RunningProcessInfo>>(Array.Empty<RunningProcessInfo>());
        }

        foreach (var proc in allProcesses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Skip system idle and system process
                if (proc.Id <= 4)
                {
                    proc.Dispose();
                    continue;
                }

                string? exePath = null;
                try
                {
                    exePath = proc.MainModule?.FileName;
                }
                catch (Win32Exception)
                {
                    // Access denied for elevated/system processes - safe to ignore
                }
                catch (InvalidOperationException)
                {
                    // Process terminated between enumeration and inspection
                }

                var isMatch = false;

                // 1. Path match: executable resides under application InstallLocation
                if (normalizedInstallLocation is not null && exePath is not null)
                {
                    try
                    {
                        var normalizedExe = Path.GetFullPath(exePath);
                        if (normalizedExe.StartsWith(normalizedInstallLocation, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                    }
                    catch
                    {
                        // Path normalization fallback
                    }
                }

                // 2. Uninstaller executable name match
                if (!isMatch && uninstallExeName is not null)
                {
                    if (string.Equals(proc.ProcessName, uninstallExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                    }
                }

                if (isMatch)
                {
                    string? title = null;
                    try
                    {
                        title = proc.MainWindowTitle;
                    }
                    catch
                    {
                        // Ignore title query error
                    }

                    matches.Add(new RunningProcessInfo(
                        ProcessId: proc.Id,
                        ProcessName: proc.ProcessName,
                        ExecutablePath: exePath,
                        MainWindowTitle: string.IsNullOrWhiteSpace(title) ? null : title));
                }
            }
            finally
            {
                proc.Dispose();
            }
        }

        return Task.FromResult<IReadOnlyList<RunningProcessInfo>>(matches);
    }

    public async Task<bool> TerminateProcessAsync(
        int processId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            if (proc.HasExited)
                return true;

            if (!force)
            {
                // Try graceful close first
                proc.CloseMainWindow();
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                    await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    // Graceful close timed out
                }
            }

            // Force kill
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ArgumentException)
        {
            // Process already exited
            return true;
        }
        catch (Exception ex)
        {
            LogTerminateProcessFailed(_logger, ex, processId);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to enumerate running processes.")]
    private static partial void LogEnumerateProcessesFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to terminate process {ProcessId}")]
    private static partial void LogTerminateProcessFailed(ILogger logger, Exception ex, int processId);
}
