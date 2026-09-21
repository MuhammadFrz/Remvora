using Dapper;
using Microsoft.Extensions.Logging;
using Remvora.Application.Auditing;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Results;
using Remvora.Infrastructure.Database;

namespace Remvora.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed persistence repository for structured audit events.
/// </summary>
public sealed class SqliteAuditLogRepository : IAuditLogRepository
{
    private const string SelectColumnsSql = """
        SELECT
            id AS Id,
            timestamp AS Timestamp,
            category AS Category,
            severity AS Severity,
            action AS Action,
            target AS Target,
            application_id AS ApplicationId,
            transaction_id AS TransactionId,
            result AS Result,
            error_code AS ErrorCode,
            details AS Details
        FROM audit_events
        """;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteAuditLogRepository> _logger;

    public SqliteAuditLogRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SqliteAuditLogRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        const string sql = """
            INSERT INTO audit_events (
                id, timestamp, category, severity, action, target,
                application_id, transaction_id, result, error_code, details
            ) VALUES (
                @Id, @Timestamp, @Category, @Severity, @Action, @Target,
                @ApplicationId, @TransactionId, @Result, @ErrorCode, @Details
            );
            """;

        using var connection = _connectionFactory.CreateConnection();
        var entity = new AuditEventEntity
        {
            Id = auditEvent.Id.ToString(),
            Timestamp = auditEvent.Timestamp.ToString("O"),
            Category = (int)auditEvent.Category,
            Severity = (int)auditEvent.Severity,
            Action = auditEvent.Action,
            Target = auditEvent.Target,
            ApplicationId = auditEvent.ApplicationId?.ToString(),
            TransactionId = auditEvent.TransactionId?.ToString(),
            Result = auditEvent.Result,
            ErrorCode = (int)auditEvent.ErrorCode,
            Details = auditEvent.Details
        };

        var command = new CommandDefinition(sql, entity, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(
        Guid? applicationId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql;
        object parameters;

        if (applicationId.HasValue)
        {
            sql = $"{SelectColumnsSql} WHERE application_id = @ApplicationId ORDER BY timestamp DESC LIMIT @Limit";
            parameters = new { ApplicationId = applicationId.Value.ToString(), Limit = limit };
        }
        else
        {
            sql = $"{SelectColumnsSql} ORDER BY timestamp DESC LIMIT @Limit";
            parameters = new { Limit = limit };
        }

        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<AuditEventEntity>(command).ConfigureAwait(false);

        return rows.Select(r => new AuditEvent(
            Id: Guid.Parse(r.Id),
            Timestamp: DateTimeOffset.Parse(r.Timestamp, System.Globalization.CultureInfo.InvariantCulture),
            Category: (AuditCategory)r.Category,
            Severity: (AuditSeverity)r.Severity,
            Action: r.Action,
            Target: r.Target,
            ApplicationId: r.ApplicationId != null ? Guid.Parse(r.ApplicationId) : null,
            TransactionId: r.TransactionId != null ? Guid.Parse(r.TransactionId) : null,
            Result: r.Result,
            ErrorCode: (ErrorCode)r.ErrorCode,
            Details: r.Details
        )).ToList();
    }

    private sealed class AuditEventEntity
    {
        public required string Id { get; init; }
        public required string Timestamp { get; init; }
        public required int Category { get; init; }
        public required int Severity { get; init; }
        public required string Action { get; init; }
        public string? Target { get; init; }
        public string? ApplicationId { get; init; }
        public string? TransactionId { get; init; }
        public string? Result { get; init; }
        public required int ErrorCode { get; init; }
        public string? Details { get; init; }
    }
}
