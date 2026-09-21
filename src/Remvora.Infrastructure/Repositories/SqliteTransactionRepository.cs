using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using Remvora.Application.Transactions;
using Remvora.Contracts;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;
using Remvora.Infrastructure.Database;

namespace Remvora.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed persistence repository for operation transactions and journal items.
/// </summary>
public sealed partial class SqliteTransactionRepository : ITransactionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteTransactionRepository> _logger;

    public SqliteTransactionRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SqliteTransactionRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveTransactionAsync(OperationTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var connection = _connectionFactory.CreateConnection();

        const string insertTransactionSql = """
            INSERT INTO transactions (
                id, plan_id, application_id, operation_type, phase, started_at,
                completed_at, restore_point_sequence, journal_path, summary_notes
            ) VALUES (
                @Id, @PlanId, @ApplicationId, @OperationType, @Phase, @StartedAt,
                @CompletedAt, @RestorePointSequence, @JournalPath, @SummaryNotes
            );
            """;

        var transEntity = new
        {
            Id = transaction.Id.ToString(),
            PlanId = transaction.PlanId.ToString(),
            ApplicationId = transaction.ApplicationId.ToString(),
            OperationType = transaction.OperationType,
            Phase = (int)transaction.Phase,
            StartedAt = transaction.StartedAt.ToString("O"),
            CompletedAt = transaction.CompletedAt?.ToString("O"),
            RestorePointSequence = transaction.RestorePointSequenceNumber,
            JournalPath = transaction.JournalPath,
            SummaryNotes = transaction.SummaryNotes
        };

        var cmdTrans = new CommandDefinition(insertTransactionSql, transEntity, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(cmdTrans).ConfigureAwait(false);

        if (transaction.Items.Count > 0)
        {
            const string insertItemSql = """
                INSERT INTO transaction_items (
                    id, transaction_id, item_type, target_location, original_state,
                    backup_path, result, is_reversible, error_message
                ) VALUES (
                    @Id, @TransactionId, @ItemType, @TargetLocation, @OriginalState,
                    @BackupPath, @Result, @IsReversible, @ErrorMessage
                );
                """;

            var itemsEntities = transaction.Items.Select(item => new
            {
                Id = item.Id.ToString(),
                TransactionId = transaction.Id.ToString(),
                ItemType = (int)item.ItemType,
                TargetLocation = item.TargetLocation,
                OriginalState = item.OriginalState,
                BackupPath = item.BackupPath,
                Result = (int)item.Result,
                IsReversible = item.IsReversible ? 1 : 0,
                ErrorMessage = item.ErrorMessage
            }).ToList();

            var cmdItems = new CommandDefinition(insertItemSql, itemsEntities, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(cmdItems).ConfigureAwait(false);
        }
    }

    public async Task<OperationTransaction?> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string selectTransSql = """
            SELECT
                id AS Id,
                plan_id AS PlanId,
                application_id AS ApplicationId,
                operation_type AS OperationType,
                phase AS Phase,
                started_at AS StartedAt,
                completed_at AS CompletedAt,
                restore_point_sequence AS RestorePointSequence,
                journal_path AS JournalPath,
                summary_notes AS SummaryNotes
            FROM transactions
            WHERE id = @Id;
            """;

        var cmdTrans = new CommandDefinition(selectTransSql, new { Id = transactionId.ToString() }, cancellationToken: cancellationToken);
        var transRow = await connection.QuerySingleOrDefaultAsync<TransactionEntity>(cmdTrans).ConfigureAwait(false);
        if (transRow is null)
            return null;

        const string selectItemsSql = """
            SELECT
                id AS Id,
                transaction_id AS TransactionId,
                item_type AS ItemType,
                target_location AS TargetLocation,
                original_state AS OriginalState,
                backup_path AS BackupPath,
                result AS Result,
                is_reversible AS IsReversible,
                error_message AS ErrorMessage
            FROM transaction_items
            WHERE transaction_id = @TransactionId;
            """;

        var cmdItems = new CommandDefinition(selectItemsSql, new { TransactionId = transactionId.ToString() }, cancellationToken: cancellationToken);
        var itemRows = await connection.QueryAsync<TransactionItemEntity>(cmdItems).ConfigureAwait(false);

        var items = itemRows.Select(i => new TransactionItem(
            id: Guid.Parse(i.Id),
            transactionId: Guid.Parse(i.TransactionId),
            itemType: (TransactionItemType)i.ItemType,
            targetLocation: i.TargetLocation,
            originalState: i.OriginalState,
            backupPath: i.BackupPath,
            result: (TransactionItemResult)i.Result,
            isReversible: i.IsReversible == 1,
            errorMessage: i.ErrorMessage
        )).ToList();

        return new OperationTransaction(
            id: Guid.Parse(transRow.Id),
            planId: Guid.Parse(transRow.PlanId),
            applicationId: Guid.Parse(transRow.ApplicationId),
            operationType: transRow.OperationType,
            phase: (TransactionPhase)transRow.Phase,
            startedAt: DateTimeOffset.Parse(transRow.StartedAt, CultureInfo.InvariantCulture),
            completedAt: transRow.CompletedAt != null ? DateTimeOffset.Parse(transRow.CompletedAt, CultureInfo.InvariantCulture) : null,
            restorePointSequenceNumber: transRow.RestorePointSequence,
            journalPath: transRow.JournalPath,
            items: items,
            summaryNotes: transRow.SummaryNotes);
    }

    public async Task<IReadOnlyList<OperationTransaction>> GetTransactionsAsync(
        Guid? applicationId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql;
        object parameters;

        if (applicationId.HasValue)
        {
            sql = "SELECT id, plan_id, application_id, operation_type, phase, started_at, completed_at, restore_point_sequence, journal_path, summary_notes FROM transactions WHERE application_id = @ApplicationId ORDER BY started_at DESC LIMIT @Limit;";
            parameters = new { ApplicationId = applicationId.Value.ToString(), Limit = limit };
        }
        else
        {
            sql = "SELECT id, plan_id, application_id, operation_type, phase, started_at, completed_at, restore_point_sequence, journal_path, summary_notes FROM transactions ORDER BY started_at DESC LIMIT @Limit;";
            parameters = new { Limit = limit };
        }

        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<TransactionEntity>(cmd).ConfigureAwait(false);

        return rows.Select(r => new OperationTransaction(
            id: Guid.Parse(r.Id),
            planId: Guid.Parse(r.PlanId),
            applicationId: Guid.Parse(r.ApplicationId),
            operationType: r.OperationType,
            phase: (TransactionPhase)r.Phase,
            startedAt: DateTimeOffset.Parse(r.StartedAt, CultureInfo.InvariantCulture),
            completedAt: r.CompletedAt != null ? DateTimeOffset.Parse(r.CompletedAt, CultureInfo.InvariantCulture) : null,
            restorePointSequenceNumber: r.RestorePointSequence,
            journalPath: r.JournalPath,
            items: [],
            summaryNotes: r.SummaryNotes
        )).ToList();
    }

    public async Task UpdatePhaseAsync(
        Guid transactionId,
        TransactionPhase phase,
        DateTimeOffset? completedAt = null,
        string? summaryNotes = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            UPDATE transactions
            SET phase = @Phase, completed_at = @CompletedAt, summary_notes = COALESCE(@SummaryNotes, summary_notes)
            WHERE id = @Id;
            """;

        var parameters = new
        {
            Id = transactionId.ToString(),
            Phase = (int)phase,
            CompletedAt = completedAt?.ToString("O"),
            SummaryNotes = summaryNotes
        };

        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(cmd).ConfigureAwait(false);
    }

    public async Task AddTransactionItemAsync(TransactionItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        using var connection = _connectionFactory.CreateConnection();

        const string insertItemSql = """
            INSERT INTO transaction_items (
                id, transaction_id, item_type, target_location, original_state,
                backup_path, result, is_reversible, error_message
            ) VALUES (
                @Id, @TransactionId, @ItemType, @TargetLocation, @OriginalState,
                @BackupPath, @Result, @IsReversible, @ErrorMessage
            );
            """;

        var itemEntity = new
        {
            Id = item.Id.ToString(),
            TransactionId = item.TransactionId.ToString(),
            ItemType = (int)item.ItemType,
            TargetLocation = item.TargetLocation,
            OriginalState = item.OriginalState,
            BackupPath = item.BackupPath,
            Result = (int)item.Result,
            IsReversible = item.IsReversible ? 1 : 0,
            ErrorMessage = item.ErrorMessage
        };

        var cmd = new CommandDefinition(insertItemSql, itemEntity, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(cmd).ConfigureAwait(false);
    }

    public async Task<OperationResult> DeleteTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tx = await GetTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false);
            if (tx == null)
                return OperationResult.Failure(ErrorCode.NotFound, "Transaction not found.");

            // 1. Delete backed up files from disk
            foreach (var item in tx.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.BackupPath) && File.Exists(item.BackupPath))
                {
                    try { File.Delete(item.BackupPath); } catch { }
                }
            }

            // Delete journal directory if exists
            if (!string.IsNullOrWhiteSpace(tx.JournalPath) && Directory.Exists(tx.JournalPath))
            {
                try { Directory.Delete(tx.JournalPath, recursive: true); } catch { }
            }

            using var connection = _connectionFactory.CreateConnection();
            var transIdStr = transactionId.ToString();

            const string deleteItemsSql = "DELETE FROM transaction_items WHERE transaction_id = @Id;";
            await connection.ExecuteAsync(new CommandDefinition(deleteItemsSql, new { Id = transIdStr }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            const string deleteTransSql = "DELETE FROM transactions WHERE id = @Id;";
            await connection.ExecuteAsync(new CommandDefinition(deleteTransSql, new { Id = transIdStr }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogDeleteTransactionFailed(_logger, ex, transactionId);
            return OperationResult.Failure(ErrorCode.OperationFailed, ex.Message);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Failed to delete transaction {TransactionId}")]
    private static partial void LogDeleteTransactionFailed(ILogger logger, Exception ex, Guid transactionId);

    private sealed class TransactionEntity
    {
        public required string Id { get; init; }
        public required string PlanId { get; init; }
        public required string ApplicationId { get; init; }
        public required string OperationType { get; init; }
        public required int Phase { get; init; }
        public required string StartedAt { get; init; }
        public string? CompletedAt { get; init; }
        public long? RestorePointSequence { get; init; }
        public string? JournalPath { get; init; }
        public string? SummaryNotes { get; init; }
    }

    private sealed class TransactionItemEntity
    {
        public required string Id { get; init; }
        public required string TransactionId { get; init; }
        public required int ItemType { get; init; }
        public required string TargetLocation { get; init; }
        public string? OriginalState { get; init; }
        public string? BackupPath { get; init; }
        public required int Result { get; init; }
        public required int IsReversible { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
