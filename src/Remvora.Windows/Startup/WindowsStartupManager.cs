using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Elevation;
using Remvora.Application.Startup;
using Remvora.Application.Transactions;
using Remvora.Contracts;
using Remvora.Core.CommandLine;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Startup;
using Remvora.Core.Domain.Transactions;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Startup;

/// <summary>
/// Windows implementation for inspecting and managing startup items across registry keys and startup folders.
/// </summary>
public sealed partial class WindowsStartupManager : IStartupManager
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWow64SubKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedRun32SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string StartupApprovedFolderSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly IRegistryAccessor _registryAccessor;
    private readonly ITransactionBackupService _backupService;
    private readonly IElevatedWorkerClient _workerClient;
    private readonly ILogger<WindowsStartupManager> _logger;

    public WindowsStartupManager(
        IRegistryAccessor registryAccessor,
        ITransactionBackupService backupService,
        IElevatedWorkerClient workerClient,
        ILogger<WindowsStartupManager> logger)
    {
        _registryAccessor = registryAccessor ?? throw new ArgumentNullException(nameof(registryAccessor));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<StartupEntry>();

        // 1. HKCU Run
        ScanRegistryRunKey(entries, RegistryHive.CurrentUser, RegistryView.Default, RunSubKey, StartupLocationType.RegistryCurrentUser, is32Bit: false);

        // 2. HKLM Run (64-bit)
        ScanRegistryRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey, StartupLocationType.RegistryLocalMachine, is32Bit: false);

        // 3. HKLM Run (32-bit Wow64)
        if (Environment.Is64BitOperatingSystem)
        {
            ScanRegistryRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry32, RunWow64SubKey, StartupLocationType.RegistryLocalMachine, is32Bit: true);
        }

        // 4. User Startup Folder
        var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        ScanStartupFolder(entries, userStartup, StartupLocationType.StartupFolderUser);

        // 5. Common Startup Folder
        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        ScanStartupFolder(entries, commonStartup, StartupLocationType.StartupFolderCommon);

        return Task.FromResult<IReadOnlyList<StartupEntry>>(entries);
    }

    private void ScanRegistryRunKey(
        List<StartupEntry> entries,
        RegistryHive hive,
        RegistryView view,
        string subKey,
        StartupLocationType locType,
        bool is32Bit)
    {
        var values = _registryAccessor.GetValues(hive, view, subKey);
        if (values is null)
            return;

        foreach (var (name, rawVal) in values)
        {
            if (string.IsNullOrWhiteSpace(name) || rawVal is null)
                continue;

            var cmd = rawVal.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cmd))
                continue;

            var exePath = CommandLineParser.ExtractExecutablePath(cmd);
            var publisher = GetPublisher(exePath);
            var isEnabled = IsRegistryEntryEnabled(hive, name, is32Bit);

            entries.Add(new StartupEntry(
                id: Guid.NewGuid(),
                name: name,
                command: cmd,
                executablePath: exePath,
                publisher: publisher,
                locationType: locType,
                locationPath: $"{hive}\\{subKey}",
                isEnabled: isEnabled,
                impact: EstimateImpact(exePath)));
        }
    }

    private bool IsRegistryEntryEnabled(RegistryHive hive, string valueName, bool is32Bit)
    {
        var approvedSubKey = is32Bit ? StartupApprovedRun32SubKey : StartupApprovedRunSubKey;

        try
        {
            // 1. Check HKCU first (HKCU can override HKLM for current user)
            var raw = _registryAccessor.GetValue(RegistryHive.CurrentUser, RegistryView.Default, approvedSubKey, valueName);
            if (raw is byte[] userBytes && userBytes.Length > 0)
            {
                // Even byte (0x02, 0x06) is Enabled, odd byte (0x01, 0x03, 0x07) is Disabled
                return (userBytes[0] & 1) == 0;
            }

            // 2. If not found in HKCU and entry is in HKLM, check HKLM
            if (hive == RegistryHive.LocalMachine)
            {
                var machineRaw = _registryAccessor.GetValue(RegistryHive.LocalMachine, RegistryView.Default, approvedSubKey, valueName);
                if (machineRaw is byte[] machBytes && machBytes.Length > 0)
                {
                    return (machBytes[0] & 1) == 0;
                }
            }
        }
        catch
        {
            // Default to true if unable to read StartupApproved
        }

        return true;
    }

    private static void ScanStartupFolder(
        List<StartupEntry> entries,
        string folderPath,
        StartupLocationType locType)
    {
        if (!Directory.Exists(folderPath))
            return;

        try
        {
            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                bool isEnabled = !ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileNameWithoutExtension(file);

                if (!isEnabled)
                {
                    name = Path.GetFileNameWithoutExtension(name); // Strip .disabled and original ext
                }

                entries.Add(new StartupEntry(
                    id: Guid.NewGuid(),
                    name: string.IsNullOrWhiteSpace(name) ? Path.GetFileName(file) : name,
                    command: file,
                    executablePath: file,
                    publisher: GetPublisher(file),
                    locationType: locType,
                    locationPath: file,
                    isEnabled: isEnabled,
                    impact: StartupImpact.Low));
            }
        }
        catch
        {
            // Ignore access errors on startup folders
        }
    }

    private static string? GetPublisher(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            return vi.CompanyName;
        }
        catch
        {
            return null;
        }
    }

    private static StartupImpact EstimateImpact(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return StartupImpact.Low;

        try
        {
            var fi = new FileInfo(exePath);
            if (fi.Length > 20 * 1024 * 1024) // > 20MB executable
                return StartupImpact.High;
            if (fi.Length > 5 * 1024 * 1024)
                return StartupImpact.Medium;
        }
        catch
        {
            // ignored
        }

        return StartupImpact.Low;
    }

    public async Task<OperationResult> SetEnabledAsync(
        StartupEntry entry,
        bool enable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (entry.LocationType is StartupLocationType.RegistryCurrentUser or StartupLocationType.RegistryLocalMachine)
            {
                var is32Bit = entry.LocationPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase);
                var approvedSubKey = is32Bit ? StartupApprovedRun32SubKey : StartupApprovedRunSubKey;

                var bytes = new byte[12];
                bytes[0] = enable ? (byte)0x02 : (byte)0x03;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);

                var primaryHive = entry.LocationType == StartupLocationType.RegistryCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                bool written = false;

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(primaryHive, RegistryView.Default);
                    using var subKey = baseKey.CreateSubKey(approvedSubKey, writable: true);
                    if (subKey != null)
                    {
                        subKey.SetValue(entry.Name, bytes, RegistryValueKind.Binary);
                        written = true;
                    }
                }
                catch (Exception) when (primaryHive == RegistryHive.LocalMachine)
                {
                    // Writing to HKLM failed (e.g. requires elevation)
                }

                // If unable to write to HKLM, write to HKCU as user override (Windows Task Manager behavior)
                if (!written && primaryHive == RegistryHive.LocalMachine)
                {
                    try
                    {
                        using var userBaseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                        using var userSubKey = userBaseKey.CreateSubKey(approvedSubKey, writable: true);
                        if (userSubKey != null)
                        {
                            userSubKey.SetValue(entry.Name, bytes, RegistryValueKind.Binary);
                            written = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogActionFailed(_logger, ex, "Toggle startup entry state in HKCU fallback");
                    }
                }

                return written ? OperationResult.Success() : OperationResult.Failure(ErrorCode.AccessDenied, "Unable to update startup registry state.");
            }
            else if (entry.LocationType is StartupLocationType.StartupFolderUser or StartupLocationType.StartupFolderCommon)
            {
                var currentPath = entry.LocationPath;
                if (!File.Exists(currentPath))
                    return OperationResult.Failure(ErrorCode.NotFound, "Startup file not found.");

                if (enable && currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = currentPath[..^9]; // Remove .disabled
                    File.Move(currentPath, newPath, overwrite: true);
                }
                else if (!enable && !currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = currentPath + ".disabled";
                    File.Move(currentPath, newPath, overwrite: true);
                }

                // Also update StartupApproved\StartupFolder in HKCU
                try
                {
                    var folderBytes = new byte[12];
                    folderBytes[0] = enable ? (byte)0x02 : (byte)0x03;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(folderBytes, 4);

                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                    using var subKey = baseKey.CreateSubKey(StartupApprovedFolderSubKey, writable: true);
                    subKey?.SetValue(entry.Name, folderBytes, RegistryValueKind.Binary);
                }
                catch
                {
                    // Ignore registry update failure for shortcut files
                }

                return OperationResult.Success();
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogActionFailed(_logger, ex, "Toggle startup entry state");
            return OperationResult.Failure(ErrorCode.OperationFailed, ex.Message);
        }
    }

    public async Task<OperationResult> RemoveEntryAsync(
        StartupEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (entry.LocationType is StartupLocationType.RegistryCurrentUser or StartupLocationType.RegistryLocalMachine)
            {
                var hive = entry.LocationType == StartupLocationType.RegistryCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;

                // 1. Backup value
                var txId = Guid.NewGuid();
                await _backupService.BackupItemAsync(txId, TransactionItemType.RegistryKey, entry.LocationPath, cancellationToken).ConfigureAwait(false);

                // 2. Delete value
                try
                {
                    var subKeyPath = entry.LocationPath.Contains('\\', StringComparison.Ordinal)
                        ? entry.LocationPath[(entry.LocationPath.IndexOf('\\', StringComparison.Ordinal) + 1)..]
                        : entry.LocationPath;

                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var subKey = baseKey.OpenSubKey(subKeyPath, writable: true);
                    subKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

                    // Also remove from StartupApproved if present
                    using (var approvedKey = baseKey.OpenSubKey(StartupApprovedRunSubKey, writable: true))
                    {
                        approvedKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
                    }
                    using (var approved32Key = baseKey.OpenSubKey(StartupApprovedRun32SubKey, writable: true))
                    {
                        approved32Key?.DeleteValue(entry.Name, throwOnMissingValue: false);
                    }

                    return OperationResult.Success();
                }
                catch (UnauthorizedAccessException) when (hive == RegistryHive.LocalMachine)
                {
                    var sessionResult = await _workerClient.StartSessionAsync(requestElevation: true, cancellationToken).ConfigureAwait(false);
                    if (sessionResult.IsSuccess && sessionResult.Value != null)
                    {
                        await using var session = sessionResult.Value;
                        var execResult = await session.ExecuteAsync(ElevatedCommandType.DeleteRegistryValue, entry.LocationPath, entry.Name, cancellationToken: cancellationToken).ConfigureAwait(false);
                        return execResult.IsSuccess ? OperationResult.Success() : OperationResult.Failure(ErrorCode.OperationFailed, execResult.ErrorMessage ?? "Privileged removal failed.");
                    }
                }
            }
            else if (entry.LocationType is StartupLocationType.StartupFolderUser or StartupLocationType.StartupFolderCommon)
            {
                if (File.Exists(entry.LocationPath))
                {
                    var txId = Guid.NewGuid();
                    await _backupService.BackupItemAsync(txId, TransactionItemType.File, entry.LocationPath, cancellationToken).ConfigureAwait(false);

                    try
                    {
                        File.Delete(entry.LocationPath);
                        return OperationResult.Success();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        var sessionResult = await _workerClient.StartSessionAsync(requestElevation: true, cancellationToken).ConfigureAwait(false);
                        if (sessionResult.IsSuccess && sessionResult.Value != null)
                        {
                            await using var session = sessionResult.Value;
                            var execResult = await session.ExecuteAsync(ElevatedCommandType.DeleteFile, entry.LocationPath, cancellationToken: cancellationToken).ConfigureAwait(false);
                            return execResult.IsSuccess ? OperationResult.Success() : OperationResult.Failure(ErrorCode.OperationFailed, execResult.ErrorMessage ?? "Privileged removal failed.");
                        }
                    }
                }
            }

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogActionFailed(_logger, ex, "Remove startup entry");
            return OperationResult.Failure(ErrorCode.OperationFailed, ex.Message);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Startup manager error during {Action}")]
    private static partial void LogActionFailed(ILogger logger, Exception ex, string action);
}
