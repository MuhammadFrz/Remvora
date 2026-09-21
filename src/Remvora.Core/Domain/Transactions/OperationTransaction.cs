namespace Remvora.Core.Domain.Transactions;

/// <summary>
/// Detailed record of a single action within a destructive operation transaction.
/// </summary>
public sealed record TransactionItem
{
    public Guid Id { get; init; }
    public Guid TransactionId { get; init; }
    public TransactionItemType ItemType { get; init; }
    public string TargetLocation { get; init; }
    public string? OriginalState { get; init; }
    public string? BackupPath { get; init; }
    public TransactionItemResult Result { get; init; }
    public bool IsReversible { get; init; }
    public string? ErrorMessage { get; init; }

    public TransactionItem(
        Guid id,
        Guid transactionId,
        TransactionItemType itemType,
        string targetLocation,
        string? originalState = null,
        string? backupPath = null,
        TransactionItemResult result = TransactionItemResult.Pending,
        bool isReversible = false,
        string? errorMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocation);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TransactionId = transactionId;
        ItemType = itemType;
        TargetLocation = targetLocation.Trim();
        OriginalState = originalState;
        BackupPath = backupPath;
        Result = result;
        IsReversible = isReversible;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Auditable transaction encompassing an entire destructive execution workflow.
/// </summary>
public sealed record OperationTransaction
{
    public Guid Id { get; init; }
    public Guid PlanId { get; init; }
    public Guid ApplicationId { get; init; }
    public string OperationType { get; init; }
    public TransactionPhase Phase { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? RestorePointSequenceNumber { get; init; }
    public string? JournalPath { get; init; }
    public IReadOnlyList<TransactionItem> Items { get; init; }
    public string? SummaryNotes { get; init; }

    public bool IsFullyReversible => Items.Count > 0 && Items.All(i => i.IsReversible);
    public bool IsPartiallyReversible => Items.Any(i => i.IsReversible) && !IsFullyReversible;

    public OperationTransaction(
        Guid id,
        Guid planId,
        Guid applicationId,
        string operationType,
        TransactionPhase phase,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null,
        long? restorePointSequenceNumber = null,
        string? journalPath = null,
        IReadOnlyList<TransactionItem>? items = null,
        string? summaryNotes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PlanId = planId;
        ApplicationId = applicationId;
        OperationType = operationType;
        Phase = phase;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        RestorePointSequenceNumber = restorePointSequenceNumber;
        JournalPath = journalPath;
        Items = items ?? [];
        SummaryNotes = summaryNotes;
    }
}
