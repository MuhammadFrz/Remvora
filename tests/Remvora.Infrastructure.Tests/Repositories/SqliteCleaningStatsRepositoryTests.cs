using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Domain.Stats;
using Remvora.Infrastructure.Database;
using Remvora.Infrastructure.Repositories;

namespace Remvora.Infrastructure.Tests.Repositories;

public sealed class SqliteCleaningStatsRepositoryTests
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteCleaningStatsRepository _repository;

    public SqliteCleaningStatsRepositoryTests()
    {
        var dbName = "test_stats_" + Guid.NewGuid().ToString("N") + ".db";
        var tempPath = Path.Combine(Path.GetTempPath(), dbName);

        _connectionFactory = new SqliteConnectionFactory(tempPath);
        var migrator = new DatabaseMigrator(_connectionFactory);
        migrator.Migrate();

        _repository = new SqliteCleaningStatsRepository(_connectionFactory, NullLogger<SqliteCleaningStatsRepository>.Instance);
    }

    [Fact]
    public async Task RecordEventAsync_And_GetLifetimeStatsAsync_AggregatesAccurately()
    {
        // 1. Single App Uninstall (50 MB, 1 item)
        await _repository.RecordEventAsync(new CleaningStatEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-3),
            CleaningCategory.AppUninstall,
            1,
            50_000_000L,
            "Uninstalled App A"));

        // 2. Batch Uninstall (120 MB, 3 items)
        await _repository.RecordEventAsync(new CleaningStatEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-2),
            CleaningCategory.BatchUninstall,
            3,
            120_000_000L,
            "Batch Uninstallation of 3 apps"));

        // 3. Leftover Remnants (10 MB, 15 items)
        await _repository.RecordEventAsync(new CleaningStatEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-1),
            CleaningCategory.LeftoverRemnants,
            15,
            10_000_000L,
            "Removed 15 remnant keys and folders"));

        // 4. Deep System Scan (300 MB, 40 items)
        await _repository.RecordEventAsync(new CleaningStatEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            CleaningCategory.SystemScan,
            40,
            300_000_000L,
            "Cleaned caches and redundant files"));

        var stats = await _repository.GetLifetimeStatsAsync();

        stats.TotalBytesSaved.Should().Be(480_000_000L);
        stats.TotalAppsUninstalled.Should().Be(4); // 1 + 3
        stats.TotalLeftoversCleaned.Should().Be(15);
        stats.TotalJunkAndCacheFilesPurged.Should().Be(40);
        stats.LastCleanedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRecentEventsAsync_ReturnsOrderedHistory()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await _repository.RecordEventAsync(new CleaningStatEvent(
            id1,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            CleaningCategory.SystemJunk,
            5,
            10_000L,
            "Cleaned Temp"));

        await _repository.RecordEventAsync(new CleaningStatEvent(
            id2,
            DateTimeOffset.UtcNow,
            CleaningCategory.AppCache,
            10,
            20_000L,
            "Cleaned Browser Cache"));

        var recent = await _repository.GetRecentEventsAsync(10);

        recent.Should().HaveCount(2);
        recent[0].Id.Should().Be(id2);
        recent[1].Id.Should().Be(id1);
    }
}
