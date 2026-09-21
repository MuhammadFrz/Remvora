using Remvora.Core.Domain.Results;

namespace Remvora.Core.Domain.Auditing;

/// <summary>
/// Domain category of an audit log entry.
/// </summary>
public enum AuditCategory
{
    Discovery,
    Scan,
    PlanCreation,
    UserSelection,
    Elevation,
    UninstallSubprocess,
    FileModification,
    RegistryModification,
    ServiceModification,
    TaskModification,
    TransactionState,
    Rollback,
    SecurityWarning,
    Error
}

/// <summary>
/// Importance or severity level of an audit event.
/// </summary>
public enum AuditSeverity
{
    Verbose = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// Structured audit record capturing an operation event for diagnosis and user-facing activity logs.
/// </summary>
public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    AuditCategory Category,
    AuditSeverity Severity,
    string Action,
    string? Target = null,
    Guid? ApplicationId = null,
    Guid? TransactionId = null,
    string? Result = null,
    ErrorCode ErrorCode = ErrorCode.None,
    string? Details = null);
