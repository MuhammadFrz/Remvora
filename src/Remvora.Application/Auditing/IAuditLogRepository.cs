using Remvora.Core.Domain.Auditing;

namespace Remvora.Application.Auditing;

/// <summary>
/// Repository abstraction for persisting and querying structured audit events.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Persists a new audit log event.
    /// </summary>
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent audit log events, optionally filtered by application ID.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        Guid? applicationId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
