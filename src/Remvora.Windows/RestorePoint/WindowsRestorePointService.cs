using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Remvora.Application.RestorePoint;
using Remvora.Core.Domain.Results;

namespace Remvora.Windows.RestorePoint;

/// <summary>
/// Windows implementation of System Restore integration using SrClient.dll.
/// </summary>
public sealed partial class WindowsRestorePointService : IRestorePointService
{
    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const int END_SYSTEM_CHANGE = 101;
    private const int APPLICATION_UNINSTALL = 1;
    private const int ERROR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct RESTOREPOINTINFOW
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        public fixed char szDescription[256];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [LibraryImport("srclient.dll", EntryPoint = "SRSetRestorePointW", SetLastError = true)]
    private static unsafe partial int SRSetRestorePoint(
        RESTOREPOINTINFOW* pRestorePtSpec,
        STATEMGRSTATUS* pSMgrStatus);

    private readonly ILogger<WindowsRestorePointService> _logger;

    public WindowsRestorePointService(ILogger<WindowsRestorePointService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> IsRestorePointSupportedAsync(CancellationToken cancellationToken = default)
    {
        // Try loading srclient.dll
        if (!NativeLibrary.TryLoad("srclient.dll", out var handle))
        {
            return Task.FromResult(false);
        }

        var hasExport = NativeLibrary.TryGetExport(handle, "SRSetRestorePointW", out _);
        NativeLibrary.Free(handle);
        return Task.FromResult(hasExport);
    }

    public async Task<OperationResult<RestorePointResult>> CreateRestorePointAsync(
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            description = "Remvora Uninstall";

        var isSupported = await IsRestorePointSupportedAsync(cancellationToken).ConfigureAwait(false);
        if (!isSupported)
        {
            return OperationResult.Failure<RestorePointResult>(
                ErrorCode.SystemRestoreUnavailable,
                "Windows System Restore is not supported or not installed on this edition of Windows.");
        }

        return await Task.Run(() =>
        {
            try
            {
                unsafe
                {
                    // 1. Begin system change
                    var beginInfo = new RESTOREPOINTINFOW
                    {
                        dwEventType = BEGIN_SYSTEM_CHANGE,
                        dwRestorePtType = APPLICATION_UNINSTALL,
                        llSequenceNumber = 0
                    };

                    var descSpan = description.AsSpan();
                    var maxLen = Math.Min(descSpan.Length, 255);
                    for (var i = 0; i < maxLen; i++)
                    {
                        beginInfo.szDescription[i] = descSpan[i];
                    }
                    beginInfo.szDescription[maxLen] = '\0';

                    var beginStatus = new STATEMGRSTATUS();
                    var result = SRSetRestorePoint(&beginInfo, &beginStatus);

                    if (result == 0 || beginStatus.nStatus != ERROR_SUCCESS)
                    {
                        var win32Error = beginStatus.nStatus != 0 ? beginStatus.nStatus : Marshal.GetLastWin32Error();
                        var win32Msg = new Win32Exception(win32Error).Message;
                        LogRestorePointFailed(_logger, win32Error, win32Msg);

                        return OperationResult.Failure<RestorePointResult>(
                            ErrorCode.SystemRestoreFailed,
                            $"Failed to create system restore point (Win32 {win32Error}: {win32Msg}).");
                    }

                    var seq = beginStatus.llSequenceNumber;

                    // 2. End system change to finalize the restore point
                    var endInfo = new RESTOREPOINTINFOW
                    {
                        dwEventType = END_SYSTEM_CHANGE,
                        dwRestorePtType = APPLICATION_UNINSTALL,
                        llSequenceNumber = seq
                    };

                    var endStatus = new STATEMGRSTATUS();
                    SRSetRestorePoint(&endInfo, &endStatus);

                    LogRestorePointCreated(_logger, seq, description);

                    return OperationResult.Success(
                        new RestorePointResult(seq, description, DateTimeOffset.UtcNow));
                }
            }
            catch (DllNotFoundException)
            {
                return OperationResult.Failure<RestorePointResult>(
                    ErrorCode.SystemRestoreUnavailable,
                    "srclient.dll was not found on this system.");
            }
            catch (Exception ex)
            {
                LogRestorePointUnexpectedException(_logger, ex);
                return OperationResult.Failure<RestorePointResult>(
                    ErrorCode.SystemRestoreFailed,
                    $"Unexpected error creating restore point: {ex.Message}");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "System Restore failed with error {ErrorCode}: {Message}")]
    private static partial void LogRestorePointFailed(ILogger logger, int errorCode, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Successfully created System Restore Point #{SequenceNumber}: {Description}")]
    private static partial void LogRestorePointCreated(ILogger logger, long sequenceNumber, string description);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Unexpected error attempting to create restore point.")]
    private static partial void LogRestorePointUnexpectedException(ILogger logger, Exception ex);
}
