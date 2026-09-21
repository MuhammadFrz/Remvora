using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Domain.Applications;
using Remvora.Infrastructure.Database;
using Remvora.Infrastructure.Repositories;

namespace Remvora.Infrastructure.Tests.Repositories;

public sealed class SqliteApplicationRepositoryTests
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteApplicationRepository _repository;

    public SqliteApplicationRepositoryTests()
    {
        // Use unique in-memory database per test class instance
        var dbName = "test_" + Guid.NewGuid().ToString("N") + ".db";
        var tempPath = Path.Combine(Path.GetTempPath(), dbName);

        _connectionFactory = new SqliteConnectionFactory(tempPath);
        var migrator = new DatabaseMigrator(_connectionFactory);
        migrator.Migrate();

        _repository = new SqliteApplicationRepository(_connectionFactory, NullLogger<SqliteApplicationRepository>.Instance);
    }

    [Fact]
    public async Task SaveAsync_And_GetAllAsync_PersistsAndRetrievesApplicationRecords()
    {
        var id = Guid.NewGuid();
        var identity = new ApplicationIdentity("Studio App", "Studio Corp", "3.0", @"C:\Studio", "{12345678-1234-1234-1234-123456789012}");
        var uninstall = new UninstallInfo(@"C:\Studio\unins000.exe", @"C:\Studio\unins000.exe /SILENT", null, true, true);
        var source = new DiscoverySource(DiscoverySourceType.Registry64, "HKLM\\...\\Studio", DateTimeOffset.UtcNow);

        var app = new ApplicationRecord(
            id,
            "Studio App",
            "Studio Corp",
            "3.0",
            DateTimeOffset.UtcNow,
            @"C:\Studio",
            50_000_000L,
            null,
            InstallationScope.PerMachine,
            ArchitectureType.X64,
            InstallerType.InnoSetup,
            identity,
            uninstall,
            [source]);

        await _repository.SaveAsync([app]);

        var all = await _repository.GetAllAsync();
        all.Should().HaveCount(1);

        var loaded = all[0];
        loaded.Id.Should().Be(id);
        loaded.DisplayName.Should().Be("Studio App");
        loaded.Publisher.Should().Be("Studio Corp");
        loaded.Identity.ProductCode.Should().Be("{12345678-1234-1234-1234-123456789012}");
        loaded.Uninstall.CanUninstall.Should().BeTrue();
        loaded.Uninstall.CanQuietUninstall.Should().BeTrue();
        loaded.Uninstall.IsMsi.Should().BeTrue();
        loaded.DiscoverySources.Should().HaveCount(1);
        loaded.DiscoverySources[0].SourceType.Should().Be(DiscoverySourceType.Registry64);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var id = Guid.NewGuid();
        var app = new ApplicationRecord(
            id,
            "Temp App",
            null,
            null,
            null,
            null,
            null,
            null,
            InstallationScope.PerUser,
            ArchitectureType.Neutral,
            InstallerType.Unknown,
            new ApplicationIdentity("Temp App"),
            new UninstallInfo("uninstall.exe"));

        await _repository.SaveAsync([app]);
        var before = await _repository.GetByIdAsync(id);
        before.Should().NotBeNull();

        await _repository.DeleteAsync(id);
        var after = await _repository.GetByIdAsync(id);
        after.Should().BeNull();
    }
}
