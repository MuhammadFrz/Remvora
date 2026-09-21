using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using Remvora.Application.Stats;
using Remvora.Core.Domain.Stats;
using Remvora.Infrastructure.Database;

namespace Remvora.Infrastructure.Repositories;

/// <summary>
/// SQLite-backed persistence repository for tracking and aggregating cleanup statistics.
/// </summary>
public sealed partial class SqliteCleaningStatsRepository : ICleaningStatsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteCleaningStatsRepository> _logger;

    public SqliteCleaningStatsRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SqliteCleaningStatsRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordEventAsync(CleaningStatEvent statEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statEvent);

        const string sql = """
            INSERT INTO cleaning_stats (
                id, timestamp, category, items_count, bytes_saved, details
            ) VALUES (
                @Id, @Timestamp, @Category, @ItemsCount, @BytesSaved, @Details
            );
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var entity = new
            {
                Id = statEvent.Id.ToString(),
                Timestamp = statEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                Category = (int)statEvent.Category,
                ItemsCount = statEvent.ItemsCount,
                BytesSaved = statEvent.BytesSaved,
                Details = statEvent.Details
            };

            var cmd = new CommandDefinition(sql, entity, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRecordEventFailed(_logger, ex);
        }
    }

    public async Task<LifetimeStats> GetLifetimeStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                COALESCE(SUM(bytes_saved), 0) AS TotalBytesSaved,
                COALESCE(SUM(CASE WHEN category = 0 OR category = 1 THEN items_count ELSE 0 END), 0) AS TotalAppsUninstalled,
                COALESCE(SUM(CASE WHEN category = 4 THEN items_count ELSE 0 END), 0) AS TotalLeftoversCleaned,
                COALESCE(SUM(CASE WHEN category = 2 OR category = 3 OR category = 6 THEN items_count ELSE 0 END), 0) AS TotalJunkAndCacheFilesPurged,
                MAX(timestamp) AS LastCleanedAt
            FROM cleaning_stats;
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var row = await connection.QueryFirstOrDefaultAsync<StatsRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (row != null)
            {
                DateTimeOffset? lastCleaned = null;
                if (!string.IsNullOrWhiteSpace(row.LastCleanedAt) &&
                    DateTimeOffset.TryParse(row.LastCleanedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    lastCleaned = parsed;
                }

                return new LifetimeStats(
                    row.TotalBytesSaved,
                    (int)row.TotalAppsUninstalled,
                    (int)row.TotalLeftoversCleaned,
                    (int)row.TotalJunkAndCacheFilesPurged,
                    lastCleaned);
            }
        }
        catch (Exception ex)
        {
            LogQueryStatsFailed(_logger, ex);
        }

        return new LifetimeStats(0, 0, 0, 0, null);
    }

    public async Task<IReadOnlyList<CleaningStatEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, timestamp, category, items_count AS itemsCount, bytes_saved AS bytesSaved, details
            FROM cleaning_stats
            ORDER BY timestamp DESC
            LIMIT @Limit;
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = await connection.QueryAsync<EventRow>(
                new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            var list = new List<CleaningStatEvent>();
            foreach (var r in rows)
            {
                if (Guid.TryParse(r.Id, out var id) &&
                    DateTimeOffset.TryParse(r.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                {
                    list.Add(new CleaningStatEvent(
                        id,
                        ts,
                        (CleaningCategory)r.Category,
                        r.ItemsCount,
                        r.BytesSaved,
                        r.Details));
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            LogQueryRecentEventsFailed(_logger, ex);
            return [];
        }
    }

    private sealed class StatsRow
    {
        public long TotalBytesSaved { get; set; }
        public long TotalAppsUninstalled { get; set; }
        public long TotalLeftoversCleaned { get; set; }
        public long TotalJunkAndCacheFilesPurged { get; set; }
        public string? LastCleanedAt { get; set; }
    }

    private sealed class EventRow
    {
        public string Id { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public int Category { get; set; }
        public int ItemsCount { get; set; }
        public long BytesSaved { get; set; }
        public string? Details { get; set; }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Failed to record cleaning stat event")]
    private static partial void LogRecordEventFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to query lifetime cleaning stats")]
    private static partial void LogQueryStatsFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to query recent cleaning stat events")]
    private static partial void LogQueryRecentEventsFailed(ILogger logger, Exception ex);
}
