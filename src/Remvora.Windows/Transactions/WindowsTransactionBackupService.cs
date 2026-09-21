using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Transactions;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Transactions;

/// <summary>
/// Windows implementation of transactional file and registry backups for uninstallation rollback.
/// </summary>
public sealed partial class WindowsTransactionBackupService : ITransactionBackupService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly IRegistryAccessor _registryAccessor;
    private readonly ILogger<WindowsTransactionBackupService> _logger;
    private readonly string _backupRoot;

    public WindowsTransactionBackupService(
        IRegistryAccessor registryAccessor,
        ILogger<WindowsTransactionBackupService> logger,
        string? customBackupRoot = null)
    {
        _registryAccessor = registryAccessor ?? throw new ArgumentNullException(nameof(registryAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!string.IsNullOrWhiteSpace(customBackupRoot))
        {
            _backupRoot = customBackupRoot;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _backupRoot = Path.Combine(localAppData, "Remvora", "Backups");
        }
    }

    public async Task<OperationResult<string>> BackupItemAsync(
        Guid transactionId,
        TransactionItemType itemType,
        string targetLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocation);

        var transDir = Path.Combine(_backupRoot, transactionId.ToString("N"));

        try
        {
            if (itemType is TransactionItemType.File)
            {
                if (!File.Exists(targetLocation))
                    return OperationResult.Failure<string>(ErrorCode.NotFound, $"Target file does not exist: {targetLocation}");

                var filesDir = Path.Combine(transDir, "Files");
                Directory.CreateDirectory(filesDir);

                var backupFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(targetLocation)}";
                var backupPath = Path.Combine(filesDir, backupFileName);

                File.Copy(targetLocation, backupPath, overwrite: true);
                LogFileBackedUp(_logger, targetLocation, backupPath);

                return OperationResult.Success(backupPath);
            }

            if (itemType is TransactionItemType.RegistryKey)
            {
                var parsed = ParseRegistryPath(targetLocation);
                if (parsed is null)
                    return OperationResult.Failure<string>(ErrorCode.InvalidRegistryKey, $"Invalid registry path: {targetLocation}");

                var regDir = Path.Combine(transDir, "Registry");
                Directory.CreateDirectory(regDir);

                var values = _registryAccessor.GetValues(parsed.Value.Hive, parsed.Value.View, parsed.Value.SubKey);
                var snapshot = new RegistrySnapshot
                {
                    Hive = parsed.Value.Hive.ToString(),
                    View = parsed.Value.View.ToString(),
                    SubKey = parsed.Value.SubKey,
                    Values = values?.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? string.Empty) ?? new Dictionary<string, string>()
                };

                var backupFileName = $"reg_{Guid.NewGuid():N}.json";
                var backupPath = Path.Combine(regDir, backupFileName);

                var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
                await File.WriteAllTextAsync(backupPath, json, cancellationToken).ConfigureAwait(false);

                LogRegistryBackedUp(_logger, targetLocation, backupPath);
                return OperationResult.Success(backupPath);
            }

            // Services and tasks do not currently support direct binary rollback
            return OperationResult.Failure<string>(ErrorCode.OperationFailed, $"Backup not supported for item type: {itemType}");
        }
        catch (Exception ex)
        {
            LogBackupFailed(_logger, ex, targetLocation);
            return OperationResult.Failure<string>(ErrorCode.BackupFailed, $"Failed to backup item: {ex.Message}");
        }
    }

    public async Task<OperationResult> RestoreItemAsync(
        TransactionItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.BackupPath) || !File.Exists(item.BackupPath))
        {
            return OperationResult.Failure(ErrorCode.NotFound, $"Backup file not found: {item.BackupPath}");
        }

        try
        {
            if (item.ItemType is TransactionItemType.File)
            {
                var targetDir = Path.GetDirectoryName(item.TargetLocation);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.Copy(item.BackupPath, item.TargetLocation, overwrite: true);
                LogFileRestored(_logger, item.BackupPath, item.TargetLocation);
                return OperationResult.Success();
            }

            if (item.ItemType is TransactionItemType.RegistryKey)
            {
                var json = await File.ReadAllTextAsync(item.BackupPath, cancellationToken).ConfigureAwait(false);
                var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(json, s_jsonOptions);
                if (snapshot is null)
                    return OperationResult.Failure(ErrorCode.OperationFailed, "Failed to deserialize registry snapshot.");

                var hive = Enum.Parse<RegistryHive>(snapshot.Hive);
                var view = Enum.Parse<RegistryView>(snapshot.View);

                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.CreateSubKey(snapshot.SubKey);

                foreach (var (valueName, val) in snapshot.Values)
                {
                    subKey.SetValue(valueName, val, RegistryValueKind.String);
                }

                LogRegistryRestored(_logger, snapshot.SubKey);
                return OperationResult.Success();
            }

            return OperationResult.Failure(ErrorCode.OperationFailed, $"Restore not supported for item type: {item.ItemType}");
        }
        catch (Exception ex)
        {
            LogRestoreFailed(_logger, ex, item.TargetLocation);
            return OperationResult.Failure(ErrorCode.OperationFailed, $"Failed to restore item: {ex.Message}");
        }
    }

    private static (RegistryHive Hive, RegistryView View, string SubKey)? ParseRegistryPath(string rawPath)
    {
        var span = rawPath.AsSpan().Trim();
        RegistryHive hive;
        var view = RegistryView.Default;
        string subKey;

        if (span.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = span["HKLM\\".Length..].ToString();
        }
        else if (span.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = span["HKEY_LOCAL_MACHINE\\".Length..].ToString();
        }
        else if (span.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = span["HKCU\\".Length..].ToString();
        }
        else if (span.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = span["HKEY_CURRENT_USER\\".Length..].ToString();
        }
        else
        {
            return null;
        }

        if (subKey.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
        {
            view = RegistryView.Registry32;
        }

        return (hive, view, subKey);
    }

    private sealed class RegistrySnapshot
    {
        public required string Hive { get; init; }
        public required string View { get; init; }
        public required string SubKey { get; init; }
        public required Dictionary<string, string> Values { get; init; }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Backed up file '{Target}' to '{Backup}'")]
    private static partial void LogFileBackedUp(ILogger logger, string target, string backup);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Backed up registry key '{Target}' to '{Backup}'")]
    private static partial void LogRegistryBackedUp(ILogger logger, string target, string backup);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to backup item '{Target}'")]
    private static partial void LogBackupFailed(ILogger logger, Exception ex, string target);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Restored file '{Backup}' to '{Target}'")]
    private static partial void LogFileRestored(ILogger logger, string backup, string target);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Restored registry key '{SubKey}' from backup")]
    private static partial void LogRegistryRestored(ILogger logger, string subKey);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to restore item '{Target}'")]
    private static partial void LogRestoreFailed(ILogger logger, Exception ex, string target);
}
