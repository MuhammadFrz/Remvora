using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Monitoring;
using Remvora.Core.Domain.Monitoring;
using Remvora.Core.Domain.Results;
using Remvora.Core.Policies;

namespace Remvora.Windows.Monitoring;

/// <summary>
/// Implements installation session recording using before/after snapshot diffing and noise filtering.
/// </summary>
public sealed partial class WindowsInstallationMonitorService : IInstallationMonitorService
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ILogger<WindowsInstallationMonitorService> _logger;
    private readonly string _storageDir;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _syncLock = new();
    private InstallationSession? _activeSession;
    private BaselineSnapshot? _baseline;

    public bool IsMonitoringActive => _activeSession is not null && _activeSession.Status == MonitorSessionStatus.Active;
    public InstallationSession? ActiveSession => _activeSession;

    public WindowsInstallationMonitorService(
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<WindowsInstallationMonitorService> logger)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storageDir = Path.Combine(localAppData, "Remvora", "InstallLogs");
        Directory.CreateDirectory(_storageDir);
    }

    public Task<InstallationSession> StartMonitoringAsync(
        string sessionName,
        string? installerPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        lock (_syncLock)
        {
            if (IsMonitoringActive)
            {
                throw new InvalidOperationException("An installation monitoring session is already active.");
            }

            LogStartingSession(_logger, sessionName);

            // Capture baseline snapshot
            _baseline = CaptureSnapshot();

            _activeSession = new InstallationSession(
                Id: Guid.NewGuid(),
                SessionName: sessionName.Trim(),
                InstallerPath: installerPath,
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                Status: MonitorSessionStatus.Active,
                CreatedFiles: [],
                ModifiedFiles: [],
                CreatedRegistryKeys: [],
                ModifiedRegistryValues: [],
                CreatedServices: [],
                CreatedTasks: [],
                CreatedStartupEntries: []);

            // Optionally launch installer
            if (!string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LogLaunchInstallerFailed(_logger, ex, installerPath);
                }
            }

            return Task.FromResult(_activeSession);
        }
    }

    public async Task<InstallationSession> StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        InstallationSession sessionToSave;
        lock (_syncLock)
        {
            if (!IsMonitoringActive || _baseline is null || _activeSession is null)
            {
                throw new InvalidOperationException("No active monitoring session to stop.");
            }

            LogStoppingSession(_logger, _activeSession.SessionName);

            // Capture post snapshot
            var postSnapshot = CaptureSnapshot();

            // Calculate diff
            var diff = CalculateDiff(_baseline, postSnapshot);

            _activeSession = _activeSession with
            {
                CompletedAt = DateTimeOffset.UtcNow,
                Status = MonitorSessionStatus.Completed,
                CreatedFiles = diff.CreatedFiles,
                ModifiedFiles = diff.ModifiedFiles,
                CreatedRegistryKeys = diff.CreatedRegistryKeys,
                ModifiedRegistryValues = diff.ModifiedRegistryValues,
                CreatedServices = diff.CreatedServices,
                CreatedTasks = diff.CreatedTasks,
                CreatedStartupEntries = diff.CreatedStartupEntries
            };

            sessionToSave = _activeSession;
            _baseline = null;
        }

        // Persist session log
        await SaveSessionToFileAsync(sessionToSave, cancellationToken).ConfigureAwait(false);
        return sessionToSave;
    }

    public async Task<IReadOnlyList<InstallationSession>> GetSavedSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = new List<InstallationSession>();
        if (!Directory.Exists(_storageDir))
        {
            return sessions;
        }

        var files = Directory.GetFiles(_storageDir, "*.json");
        foreach (var file in files)
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var session = await JsonSerializer.DeserializeAsync<InstallationSession>(stream, JsonOpts, cancellationToken).ConfigureAwait(false);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex)
            {
                LogReadSessionFailed(_logger, ex, file);
            }
        }

        return sessions.OrderByDescending(s => s.StartedAt).ToList();
    }

    public Task<OperationResult> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_storageDir, $"{sessionId:N}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return Task.FromResult(OperationResult.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Failure(ErrorCode.OperationFailed, ex.Message));
        }
    }

    public async Task<OperationResult> ExportSessionAsync(Guid sessionId, string targetFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourcePath = Path.Combine(_storageDir, $"{sessionId:N}.json");
            if (!File.Exists(sourcePath))
            {
                return OperationResult.Failure(ErrorCode.NotFound, "Session log not found.");
            }

            var content = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(targetFilePath, content, cancellationToken).ConfigureAwait(false);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(ErrorCode.OperationFailed, ex.Message);
        }
    }

    private async Task SaveSessionToFileAsync(InstallationSession session, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = Path.Combine(_storageDir, $"{session.Id:N}.json");
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, session, JsonOpts, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveSessionFailed(_logger, ex, session.Id);
        }
    }

    private static BaselineSnapshot CaptureSnapshot()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Filesystem targeted roots
        var targetRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        foreach (var root in targetRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                var subs = Directory.GetDirectories(root);
                foreach (var sub in subs)
                {
                    dirs.Add(sub);
                    // Add level 2
                    try
                    {
                        foreach (var sub2 in Directory.GetDirectories(sub))
                        {
                            dirs.Add(sub2);
                        }
                    }
                    catch
                    {
                        // Ignore access errors
                    }
                }
            }
            catch
            {
                // Ignore access errors
            }
        }

        // 2. Registry targeted software keys
        SnapshotRegistryKey(Microsoft.Win32.Registry.CurrentUser, @"Software", regKeys);
        SnapshotRegistryKey(Microsoft.Win32.Registry.LocalMachine, @"Software", regKeys);
        SnapshotRegistryKey(Microsoft.Win32.Registry.LocalMachine, @"Software\WOW6432Node", regKeys);
        SnapshotRegistryKey(Microsoft.Win32.Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services", services);

        return new BaselineSnapshot(dirs, regKeys, services);
    }

    private static void SnapshotRegistryKey(RegistryKey rootKey, string subKeyPath, HashSet<string> targetSet)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath);
            if (key is null) return;

            foreach (var subName in key.GetSubKeyNames())
            {
                targetSet.Add($@"{rootKey.Name}\{subKeyPath}\{subName}");
            }
        }
        catch
        {
            // Ignore access errors
        }
    }

    private static SnapshotDiff CalculateDiff(BaselineSnapshot before, BaselineSnapshot after)
    {
        var createdFiles = new List<string>();
        var modifiedFiles = new List<string>();
        var createdReg = new List<string>();
        var modifiedReg = new List<string>();
        var createdServices = new List<string>();

        // Files / Dirs diff
        foreach (var dir in after.Directories)
        {
            if (!before.Directories.Contains(dir) && !IsNoisePath(dir))
            {
                createdFiles.Add(dir);
            }
        }

        // Registry diff
        foreach (var key in after.RegistryKeys)
        {
            if (!before.RegistryKeys.Contains(key) && !IsNoiseRegistry(key))
            {
                createdReg.Add(key);
            }
        }

        // Services diff
        foreach (var srv in after.Services)
        {
            if (!before.Services.Contains(srv))
            {
                createdServices.Add(srv);
            }
        }

        return new SnapshotDiff(createdFiles, modifiedFiles, createdReg, modifiedReg, createdServices, [], []);
    }

    private static bool IsNoisePath(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains(@"\temp\") ||
               lower.Contains(@"\microsoft\windows\inetcache") ||
               lower.Contains(@"\microsoft\windows defender") ||
               lower.Contains(@"\cryptneturlcache");
    }

    private static bool IsNoiseRegistry(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains(@"\microsoft\windows\currentversion\explorer\sessioninfo") ||
               lower.Contains(@"\microsoft\windows\currentversion\diagnostics");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Starting installation monitoring session '{SessionName}'")]
    private static partial void LogStartingSession(ILogger logger, string sessionName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Stopping installation monitoring session '{SessionName}'")]
    private static partial void LogStoppingSession(ILogger logger, string sessionName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to launch installer process '{InstallerPath}'")]
    private static partial void LogLaunchInstallerFailed(ILogger logger, Exception ex, string installerPath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to read saved session log '{FilePath}'")]
    private static partial void LogReadSessionFailed(ILogger logger, Exception ex, string filePath);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to save installation session log {SessionId}")]
    private static partial void LogSaveSessionFailed(ILogger logger, Exception ex, Guid sessionId);

    private sealed record BaselineSnapshot(
        HashSet<string> Directories,
        HashSet<string> RegistryKeys,
        HashSet<string> Services);

    private sealed record SnapshotDiff(
        IReadOnlyList<string> CreatedFiles,
        IReadOnlyList<string> ModifiedFiles,
        IReadOnlyList<string> CreatedRegistryKeys,
        IReadOnlyList<string> ModifiedRegistryValues,
        IReadOnlyList<string> CreatedServices,
        IReadOnlyList<string> CreatedTasks,
        IReadOnlyList<string> CreatedStartupEntries);
}
