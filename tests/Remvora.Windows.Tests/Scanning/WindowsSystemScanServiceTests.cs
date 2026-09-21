using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Stats;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Scanning;
using Remvora.Core.Domain.Stats;
using Remvora.Core.Policies;
using Remvora.Windows.Scanning;

namespace Remvora.Windows.Tests.Scanning;

public sealed class WindowsSystemScanServiceTests : IDisposable
{
    private readonly IApplicationRepository _appRepository;
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly FakeCleaningStatsRepository _statsRepository;
    private readonly WindowsSystemScanService _scanService;
    private readonly string _testRoot;

    public WindowsSystemScanServiceTests()
    {
        _appRepository = new FakeAppRepository();
        _protectedPathsPolicy = new ProtectedPathsPolicy();
        _statsRepository = new FakeCleaningStatsRepository();
        _scanService = new WindowsSystemScanService(
            _appRepository,
            _protectedPathsPolicy,
            _statsRepository,
            NullLogger<WindowsSystemScanService>.Instance);

        _testRoot = Path.Combine(Path.GetTempPath(), "remvora_scan_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task ScanSystemAsync_ReturnsExpectedCategoryGroups()
    {
        var groups = await _scanService.ScanSystemAsync();

        groups.Should().NotBeNull();
        groups.Should().HaveCount(4);

        groups.Should().Contain(g => g.Category == ScanCategory.WindowsRedundant);
        groups.Should().Contain(g => g.Category == ScanCategory.AppCache);
        groups.Should().Contain(g => g.Category == ScanCategory.OrphanedLeftovers);
        groups.Should().Contain(g => g.Category == ScanCategory.AppIssues);
    }

    [Fact]
    public async Task CleanSelectedItemsAsync_RemovesValidFiles_And_RecordsStats()
    {
        var dummyFile = Path.Combine(_testRoot, "test_cache.tmp");
        await File.WriteAllBytesAsync(dummyFile, new byte[1024]);

        var item = new ScanItem
        {
            Category = ScanCategory.AppCache,
            Title = "Test Cache File",
            Description = "Unit test dummy file",
            TargetPath = dummyFile,
            SizeBytes = 1024,
            IsSelected = true
        };

        var result = await _scanService.CleanSelectedItemsAsync([item]);

        result.ItemsRemoved.Should().Be(1);
        result.BytesReclaimed.Should().Be(1024);
        File.Exists(dummyFile).Should().BeFalse();

        _statsRepository.RecordedEvents.Should().ContainSingle();
        var recorded = _statsRepository.RecordedEvents[0];
        recorded.BytesSaved.Should().Be(1024);
        recorded.ItemsCount.Should().Be(1);
    }

    [Fact]
    public async Task CleanSelectedItemsAsync_RejectsProtectedPaths()
    {
        var protectedSystemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "critical_system.dll");

        var item = new ScanItem
        {
            Category = ScanCategory.WindowsRedundant,
            Title = "Fake System Item",
            Description = "Must never be deleted",
            TargetPath = protectedSystemPath,
            SizeBytes = 5000,
            IsSelected = true
        };

        var result = await _scanService.CleanSelectedItemsAsync([item]);

        result.ItemsRemoved.Should().Be(0);
        result.Errors.Should().Contain(e => e.Contains("Protected path skipped"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch { }
    }

    private sealed class FakeCleaningStatsRepository : ICleaningStatsRepository
    {
        public List<CleaningStatEvent> RecordedEvents { get; } = [];

        public Task RecordEventAsync(CleaningStatEvent statEvent, CancellationToken cancellationToken = default)
        {
            RecordedEvents.Add(statEvent);
            return Task.CompletedTask;
        }

        public Task<LifetimeStats> GetLifetimeStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LifetimeStats(0, 0, 0, 0, null));

        public Task<IReadOnlyList<CleaningStatEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CleaningStatEvent>>(RecordedEvents);
    }

    private sealed class FakeAppRepository : IApplicationRepository
    {
        public Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationRecord>>([]);

        public Task<ApplicationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApplicationRecord?>(null);

        public Task SaveAsync(IEnumerable<ApplicationRecord> applications, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
