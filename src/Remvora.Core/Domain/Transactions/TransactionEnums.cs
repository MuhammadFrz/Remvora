namespace Remvora.Core.Domain.Transactions;

/// <summary>
/// Lifecycle phase of an uninstallation or cleanup transaction.
/// </summary>
public enum TransactionPhase
{
    Preparing = 0,
    Previewed = 1,
    Authorized = 2,
    Applying = 3,
    Verifying = 4,
    Completed = 5,
    PartialFailure = 6,
    RolledBack = 7,
    RollbackPartial = 8,
    Failed = 9
}

/// <summary>
/// Category of resource altered within a transaction.
/// </summary>
public enum TransactionItemType
{
    File,
    Directory,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    StartupEntry
}

/// <summary>
/// Execution status of an individual transaction item.
/// </summary>
public enum TransactionItemResult
{
    Pending,
    Succeeded,
    Failed,
    Skipped,
    Restored
}
