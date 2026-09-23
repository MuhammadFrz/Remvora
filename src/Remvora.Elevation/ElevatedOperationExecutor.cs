using System.Diagnostics;
using Microsoft.Win32;
using Remvora.Contracts;
using Remvora.Contracts.Operations;
using Remvora.Core.CommandLine;
using Remvora.Core.Policies;

namespace Remvora.Elevation;

/// <summary>
/// Executes authorized privileged commands strictly within safety policy constraints.
/// </summary>
public sealed class ElevatedOperationExecutor
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;

    public ElevatedOperationExecutor(IProtectedPathsPolicy protectedPathsPolicy)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
    }

    public async Task<ElevatedOperationResult> ExecuteAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.CommandType switch
        {
            ElevatedCommandType.DeleteFile => ExecuteDeleteFile(request),
            ElevatedCommandType.DeleteDirectory => ExecuteDeleteDirectory(request),
            ElevatedCommandType.DeleteRegistryKey => ExecuteDeleteRegistryKey(request),
            ElevatedCommandType.DeleteRegistryValue => ExecuteDeleteRegistryValue(request),
            ElevatedCommandType.StopService => await ExecuteStopServiceAsync(request, cancellationToken).ConfigureAwait(false),
            ElevatedCommandType.DeleteService => await ExecuteDeleteServiceAsync(request, cancellationToken).ConfigureAwait(false),
            ElevatedCommandType.DeleteScheduledTask => await ExecuteDeleteScheduledTaskAsync(request, cancellationToken).ConfigureAwait(false),
            _ => new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 7, ErrorMessage: $"Unsupported command type: {request.CommandType}")
        };
    }

    private ElevatedOperationResult ExecuteDeleteFile(ElevatedOperationRequest request)
    {
        var target = request.Target;
        if (_protectedPathsPolicy.IsPathProtected(target, out var reason))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"File deletion blocked: {reason}");
        }

        if (!File.Exists(target))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true, ErrorMessage: "File already deleted.");
        }

        try
        {
            var info = new FileInfo(target);
            var size = info.Length;

            // Clear read-only if set
            if (info.IsReadOnly)
                info.IsReadOnly = false;

            File.Delete(target);
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true, BytesReclaimed: size);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete file: {ex.Message}");
        }
    }

    private ElevatedOperationResult ExecuteDeleteDirectory(ElevatedOperationRequest request)
    {
        var target = request.Target;
        if (_protectedPathsPolicy.IsPathProtected(target, out var reason))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Directory deletion blocked: {reason}");
        }

        if (!Directory.Exists(target))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true, ErrorMessage: "Directory already deleted.");
        }

        try
        {
            var dirInfo = new DirectoryInfo(target);
            long size = 0;
            try
            {
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += file.Length;
                        if (file.IsReadOnly)
                            file.IsReadOnly = false;
                    }
                    catch
                    {
                        // Ignore individual file inspect error
                    }
                }
            }
            catch
            {
                // Ignore enumeration error
            }

            Directory.Delete(target, recursive: true);
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true, BytesReclaimed: size);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete directory: {ex.Message}");
        }
    }

    private ElevatedOperationResult ExecuteDeleteRegistryKey(ElevatedOperationRequest request)
    {
        var target = request.Target;
        if (_protectedPathsPolicy.IsRegistryKeyProtected(target, out var reason))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Registry key deletion blocked: {reason}");
        }

        var parsed = ParseRegistryPath(target);
        if (parsed is null)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 18, ErrorMessage: $"Invalid registry path format: '{target}'");
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(parsed.Value.Hive, parsed.Value.View);
            baseKey.DeleteSubKeyTree(parsed.Value.SubKey, throwOnMissingSubKey: false);
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete registry key: {ex.Message}");
        }
    }

    private ElevatedOperationResult ExecuteDeleteRegistryValue(ElevatedOperationRequest request)
    {
        var targetKey = request.Target;
        var valueName = request.SecondaryTarget;

        if (string.IsNullOrWhiteSpace(valueName))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 7, ErrorMessage: "Missing value name to delete.");
        }

        if (_protectedPathsPolicy.IsRegistryKeyProtected(targetKey, out var reason))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Registry value deletion blocked: {reason}");
        }

        var parsed = ParseRegistryPath(targetKey);
        if (parsed is null)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 18, ErrorMessage: $"Invalid registry path format: '{targetKey}'");
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(parsed.Value.Hive, parsed.Value.View);
            using var subKey = baseKey.OpenSubKey(parsed.Value.SubKey, writable: true);
            if (subKey != null)
            {
                subKey.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete registry value: {ex.Message}");
        }
    }

    private static readonly HashSet<string> s_protectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend",
        "mpssvc",
        "SecurityHealthService",
        "Sense",
        "WdNisSvc",
        "EventLog",
        "RpcSs",
        "RpcEptMapper",
        "DcomLaunch",
        "PlugPlay",
        "LanmanWorkstation",
        "LanmanServer",
        "wuauserv",
        "CryptSvc",
        "TrustedInstaller",
        "sppsvc",
        "BFE",
        "SamSs",
        "LSM",
        "TermService",
        "BrokerInfrastructure",
        "DsmSvc"
    };

    private static readonly string[] s_protectedTaskPrefixes =
    [
        @"\Microsoft\Windows\",
        @"Microsoft\Windows\",
        @"\Microsoft\",
        @"Microsoft\"
    ];

    private static bool IsValidServiceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
            return false;

        return !name.Any(c => c is '"' or '/' or '\\' or ';' or '&' or '|' or '<' or '>' or '\r' or '\n');
    }

    private static bool IsValidTaskPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512)
            return false;

        return !path.Any(c => c is '"' or ';' or '&' or '|' or '<' or '>' or '\r' or '\n');
    }

    private static async Task<ElevatedOperationResult> ExecuteStopServiceAsync(ElevatedOperationRequest request, CancellationToken cancellationToken)
    {
        var serviceName = request.Target?.Trim() ?? string.Empty;
        if (!IsValidServiceName(serviceName))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: "Invalid or malformed service name.");
        }

        if (s_protectedServices.Contains(serviceName))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Service '{serviceName}' is a protected Windows core service and cannot be stopped.");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = $"stop \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to stop service: {ex.Message}");
        }
    }

    private static async Task<ElevatedOperationResult> ExecuteDeleteServiceAsync(ElevatedOperationRequest request, CancellationToken cancellationToken)
    {
        var serviceName = request.Target?.Trim() ?? string.Empty;
        if (!IsValidServiceName(serviceName))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: "Invalid or malformed service name.");
        }

        if (s_protectedServices.Contains(serviceName))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Service '{serviceName}' is a protected Windows core service and cannot be deleted.");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete service: {ex.Message}");
        }
    }

    private static async Task<ElevatedOperationResult> ExecuteDeleteScheduledTaskAsync(ElevatedOperationRequest request, CancellationToken cancellationToken)
    {
        var taskPath = request.Target?.Trim() ?? string.Empty;
        if (!IsValidTaskPath(taskPath))
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: "Invalid or malformed scheduled task path.");
        }

        foreach (var prefix in s_protectedTaskPrefixes)
        {
            if (taskPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 8, ErrorMessage: $"Scheduled task '{taskPath}' is a protected Windows system task and cannot be deleted.");
            }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{taskPath}\" /f",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }

            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: true);
        }
        catch (Exception ex)
        {
            return new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: $"Failed to delete scheduled task: {ex.Message}");
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
}
