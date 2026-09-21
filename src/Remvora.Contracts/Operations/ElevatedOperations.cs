namespace Remvora.Contracts.Operations;

/// <summary>
/// Strongly-typed operation request sent to the privileged worker.
/// </summary>
public sealed record ElevatedOperationRequest
{
    public Guid CorrelationId { get; init; }
    public string SessionToken { get; init; }
    public ElevatedCommandType CommandType { get; init; }
    public string Target { get; init; }
    public string? SecondaryTarget { get; init; }
    public bool BackupRequested { get; init; }
    public string? ExpectedOriginalState { get; init; }

    public ElevatedOperationRequest(
        Guid correlationId,
        string sessionToken,
        ElevatedCommandType commandType,
        string target,
        string? secondaryTarget = null,
        bool backupRequested = false,
        string? expectedOriginalState = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        CorrelationId = correlationId == Guid.Empty ? Guid.NewGuid() : correlationId;
        SessionToken = sessionToken;
        CommandType = commandType;
        Target = target.Trim();
        SecondaryTarget = secondaryTarget?.Trim();
        BackupRequested = backupRequested;
        ExpectedOriginalState = expectedOriginalState;
    }
}

/// <summary>
/// Progress update streamed during long-running privileged operations.
/// </summary>
public sealed record ElevatedOperationProgress(
    Guid CorrelationId,
    int PercentComplete,
    string CurrentItem,
    string StatusMessage);

/// <summary>
/// Result returned upon completion of an elevated operation.
/// </summary>
public sealed record ElevatedOperationResult(
    Guid CorrelationId,
    bool IsSuccess,
    int ErrorCode = 0,
    string? ErrorMessage = null,
    string? BackupPath = null,
    long BytesReclaimed = 0,
    bool RebootRequired = false);
