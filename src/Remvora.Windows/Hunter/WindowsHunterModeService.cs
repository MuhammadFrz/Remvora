using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Extensions.Logging;
using Remvora.Application.Hunter;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Results;
using Remvora.Core.Policies;

namespace Remvora.Windows.Hunter;

/// <summary>
/// Implements Hunter Mode target resolution using Win32 window APIs, process inspection, and application mapping.
/// </summary>
public sealed partial class WindowsHunterModeService : IHunterModeService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ILogger<WindowsHunterModeService> _logger;

    private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "smss", "services", "lsass", "winlogon", "system", "idle", "wininit",
        "dwm", "fontdrvhost", "sihost", "taskhostw", "explorer", "remvora", "remvora.elevatedworker"
    };

    public WindowsHunterModeService(
        IApplicationRepository applicationRepository,
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<WindowsHunterModeService> logger)
    {
        _applicationRepository = applicationRepository ?? throw new ArgumentNullException(nameof(applicationRepository));
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HunterTargetResult?> ResolveTargetFromPointAsync(
        int screenX,
        int screenY,
        CancellationToken cancellationToken = default)
    {
        var point = new POINT { X = screenX, Y = screenY };
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        // Get root / top-level window
        var rootHwnd = GetAncestor(hwnd, 2 /* GA_ROOT */);
        if (rootHwnd == IntPtr.Zero)
        {
            rootHwnd = hwnd;
        }

        GetWindowThreadProcessId(rootHwnd, out uint pid);
        if (pid == 0)
        {
            return null;
        }

        var windowTitle = GetWindowTextSafe(rootHwnd);
        return await ResolveTargetFromProcessIdInternalAsync((int)pid, windowTitle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HunterTargetResult?> ResolveTargetFromPathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string resolvedPath = filePath;
        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = TryResolveShortcutTarget(filePath);
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
            {
                resolvedPath = target;
            }
        }

        if (!File.Exists(resolvedPath))
        {
            return null;
        }

        string processName = Path.GetFileNameWithoutExtension(resolvedPath);
        bool isProtected = _protectedPathsPolicy.IsPathProtected(resolvedPath, out _) ||
                           CriticalSystemProcesses.Contains(processName);

        var matchedApp = await FindMatchingApplicationAsync(resolvedPath, processName, cancellationToken).ConfigureAwait(false);
        string confidence = matchedApp is not null ? "Registered Application Match" : "File Target (Standalone)";

        return new HunterTargetResult(
            ProcessId: null,
            ProcessName: processName,
            ExecutablePath: resolvedPath,
            WindowTitle: null,
            MatchedApplication: matchedApp ?? CreateSyntheticApplication(resolvedPath, processName),
            Confidence: confidence,
            IsSystemProtected: isProtected);
    }

    public async Task<HunterTargetResult?> ResolveTargetFromProcessIdAsync(
        int processId,
        CancellationToken cancellationToken = default)
    {
        return await ResolveTargetFromProcessIdInternalAsync(processId, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HunterTargetResult>> GetActiveWindowTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<HunterTargetResult>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            LogEnumerateProcessesFailed(_logger, ex);
            return Array.Empty<HunterTargetResult>();
        }

        var apps = await _applicationRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var proc in processes)
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                {
                    proc.Dispose();
                    continue;
                }

                string? exePath = null;
                try
                {
                    exePath = proc.MainModule?.FileName;
                }
                catch
                {
                    // Access denied or 32/64 bit mismatch
                }

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    proc.Dispose();
                    continue;
                }

                string procName = proc.ProcessName;
                bool isProtected = CriticalSystemProcesses.Contains(procName) ||
                                   _protectedPathsPolicy.IsPathProtected(exePath, out _);

                var matched = MatchApplication(apps, exePath, procName);

                results.Add(new HunterTargetResult(
                    ProcessId: proc.Id,
                    ProcessName: procName,
                    ExecutablePath: exePath,
                    WindowTitle: proc.MainWindowTitle,
                    MatchedApplication: matched ?? CreateSyntheticApplication(exePath, procName),
                    Confidence: matched is not null ? "Registered Application Match" : "Active Desktop Window",
                    IsSystemProtected: isProtected));
            }
            catch
            {
                // Ignore transient process exit
            }
            finally
            {
                proc.Dispose();
            }
        }

        return results;
    }

    public OperationResult TerminateTargetProcess(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            if (proc.HasExited)
            {
                return OperationResult.Success();
            }

            if (CriticalSystemProcesses.Contains(proc.ProcessName))
            {
                return OperationResult.Failure(ErrorCode.ProtectedTarget, $"Process '{proc.ProcessName}' is a protected Windows system component.");
            }

            string? exePath = null;
            try
            {
                exePath = proc.MainModule?.FileName;
            }
            catch
            {
                // Ignore access errors on module info
            }

            if (!string.IsNullOrWhiteSpace(exePath) && _protectedPathsPolicy.IsPathProtected(exePath, out _))
            {
                return OperationResult.Failure(ErrorCode.ProtectedTarget, $"Process executable path '{exePath}' is protected.");
            }

            // Attempt graceful close first
            proc.CloseMainWindow();
            if (!proc.WaitForExit(1500))
            {
                proc.Kill(entireProcessTree: true);
            }

            return OperationResult.Success();
        }
        catch (ArgumentException)
        {
            // Already exited
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogTerminateProcessFailed(_logger, ex, processId);
            return OperationResult.Failure(ErrorCode.ExecutionFailed, $"Failed to terminate process: {ex.Message}");
        }
    }

    private async Task<HunterTargetResult?> ResolveTargetFromProcessIdInternalAsync(
        int processId,
        string? windowTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            string procName = proc.ProcessName;
            string? exePath = null;

            try
            {
                exePath = proc.MainModule?.FileName;
            }
            catch
            {
                // Fallback for limited access
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            string? title = windowTitle ?? (proc.MainWindowHandle != IntPtr.Zero ? proc.MainWindowTitle : null);
            bool isProtected = CriticalSystemProcesses.Contains(procName) ||
                               _protectedPathsPolicy.IsPathProtected(exePath, out _);

            var matched = await FindMatchingApplicationAsync(exePath, procName, cancellationToken).ConfigureAwait(false);

            return new HunterTargetResult(
                ProcessId: processId,
                ProcessName: procName,
                ExecutablePath: exePath,
                WindowTitle: title,
                MatchedApplication: matched ?? CreateSyntheticApplication(exePath, procName),
                Confidence: matched is not null ? "Registered Application Match" : "Active Process Target",
                IsSystemProtected: isProtected);
        }
        catch (Exception ex)
        {
            LogResolveProcessFailed(_logger, ex, processId);
            return null;
        }
    }

    private async Task<ApplicationRecord?> FindMatchingApplicationAsync(
        string executablePath,
        string processName,
        CancellationToken cancellationToken)
    {
        var apps = await _applicationRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return MatchApplication(apps, executablePath, processName);
    }

    private static ApplicationRecord? MatchApplication(
        IReadOnlyList<ApplicationRecord> apps,
        string executablePath,
        string processName)
    {
        // 1. Direct InstallLocation match
        foreach (var app in apps)
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                var normalizedInstall = Path.TrimEndingDirectorySeparator(app.InstallLocation);
                if (executablePath.StartsWith(normalizedInstall, StringComparison.OrdinalIgnoreCase))
                {
                    return app;
                }
            }
        }

        // 2. UninstallString contains executable name
        string exeFileName = Path.GetFileName(executablePath);
        foreach (var app in apps)
        {
            if (!string.IsNullOrWhiteSpace(app.Uninstall.UninstallString) &&
                app.Uninstall.UninstallString.Contains(exeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return app;
            }
        }

        // 3. DisplayName equals process name
        foreach (var app in apps)
        {
            if (string.Equals(app.DisplayName, processName, StringComparison.OrdinalIgnoreCase))
            {
                return app;
            }
        }

        return null;
    }

    private static ApplicationRecord CreateSyntheticApplication(string executablePath, string processName)
    {
        string displayName = processName;
        string publisher = "Unknown Publisher";
        string version = "1.0";
        long? sizeBytes = null;
        string? installDir = Path.GetDirectoryName(executablePath);

        try
        {
            var fileInfo = new FileInfo(executablePath);
            if (fileInfo.Exists)
            {
                sizeBytes = fileInfo.Length;
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                displayName = versionInfo.FileDescription;
            }
            if (!string.IsNullOrWhiteSpace(versionInfo.CompanyName))
            {
                publisher = versionInfo.CompanyName;
            }
            if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion))
            {
                version = versionInfo.FileVersion;
            }
        }
        catch
        {
            // Ignore metadata extraction errors
        }

        var identity = new ApplicationIdentity(displayName, publisher, version, installDir);
        var uninstall = new UninstallInfo(null, null);

        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: displayName,
            publisher: publisher,
            displayVersion: version,
            installDate: DateTimeOffset.UtcNow,
            installLocation: installDir,
            estimatedSizeBytes: sizeBytes,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.Unknown,
            identity: identity,
            uninstall: uninstall);
    }

    private static string? GetWindowTextSafe(IntPtr hwnd)
    {
        try
        {
            var buffer = new char[256];
            int length = GetWindowTextW(hwnd, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveShortcutTarget(string lnkPath)
    {
        try
        {
            var link = (IShellLinkW)new CShellLink();
            var persistFile = (IPersistFile)link;
            persistFile.Load(lnkPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, out _, 0);
            string target = sb.ToString();
            return !string.IsNullOrWhiteSpace(target) ? target : null;
        }
        catch
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to enumerate running processes for Hunter Mode")]
    private static partial void LogEnumerateProcessesFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to terminate target process ID {ProcessId}")]
    private static partial void LogTerminateProcessFailed(ILogger logger, Exception ex, int processId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to resolve process target for PID {ProcessId}")]
    private static partial void LogResolveProcessFailed(ILogger logger, Exception ex, int processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATAW pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
}
