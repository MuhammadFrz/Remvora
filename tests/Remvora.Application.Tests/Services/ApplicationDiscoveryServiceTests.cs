using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Services;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Policies;

namespace Remvora.Application.Tests.Services;

public sealed class ApplicationDiscoveryServiceTests
{
    private sealed class StubDiscoverySource : IApplicationDiscoverySource
    {
        public DiscoverySourceType SourceType { get; }
        public string DisplayName => SourceType.ToString();
        public List<ApplicationRecord> Applications { get; } = [];

        public StubDiscoverySource(DiscoverySourceType sourceType)
        {
            SourceType = sourceType;
        }

        public async IAsyncEnumerable<ApplicationRecord> DiscoverAsync(
            DiscoveryFilter filter,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var app in Applications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return app;
                await Task.Yield();
            }
        }
    }

    private static ApplicationRecord CreateSample(
        string name,
        string publisher,
        string? productCode = null,
        DiscoverySourceType source = DiscoverySourceType.Registry64)
    {
        var identity = new ApplicationIdentity(name, publisher, "1.0", @"C:\App", productCode);
        var uninstall = new UninstallInfo(@"C:\App\uninstall.exe");
        var discoverySource = new DiscoverySource(source, name, DateTimeOffset.UtcNow);

        return new ApplicationRecord(
            Guid.NewGuid(),
            name,
            publisher,
            "1.0",
            null,
            @"C:\App",
            null,
            null,
            InstallationScope.PerMachine,
            ArchitectureType.X64,
            productCode != null ? InstallerType.Msi : InstallerType.InnoSetup,
            identity,
            uninstall,
            [discoverySource]);
    }

    [Fact]
    public async Task DiscoverAllAsync_MergesDuplicatesAcrossMultipleSources()
    {
        const string productCode = "{11111111-2222-3333-4444-555555555555}";

        // Registry source finds the app
        var registrySource = new StubDiscoverySource(DiscoverySourceType.Registry64);
        registrySource.Applications.Add(CreateSample("Visual Editor", "ToolCorp", productCode, DiscoverySourceType.Registry64));

        // MSI source finds the same app by ProductCode
        var msiSource = new StubDiscoverySource(DiscoverySourceType.MsiDatabase);
        msiSource.Applications.Add(CreateSample("Visual Editor", "ToolCorp", productCode, DiscoverySourceType.MsiDatabase));

        // Standalone independent app
        var packageSource = new StubDiscoverySource(DiscoverySourceType.AppxPackage);
        packageSource.Applications.Add(CreateSample("Photo Tool", "MediaCorp", null, DiscoverySourceType.AppxPackage));

        var service = new ApplicationDiscoveryService(
            [registrySource, msiSource, packageSource],
            new DeduplicationPolicy(),
            NullLogger<ApplicationDiscoveryService>.Instance);

        var progressReports = new List<DiscoveryProgress>();
        var progress = new Progress<DiscoveryProgress>(p => progressReports.Add(p));

        var results = await service.DiscoverAllAsync(new DiscoveryFilter(), progress, CancellationToken.None);

        // 3 raw discovered -> 2 canonical applications after deduplication
        results.Should().HaveCount(2);

        var editor = results.Single(a => a.DisplayName == "Visual Editor");
        // Merged discovery sources: contains both Registry64 and MsiDatabase!
        editor.DiscoverySources.Should().HaveCount(2);
        editor.DiscoverySources.Select(s => s.SourceType)
            .Should().Contain([DiscoverySourceType.Registry64, DiscoverySourceType.MsiDatabase]);
    }

    [Fact]
    public async Task DiscoverAllAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var source = new StubDiscoverySource(DiscoverySourceType.Registry64);
        source.Applications.Add(CreateSample("Sample App", "Publisher"));

        var service = new ApplicationDiscoveryService(
            [source],
            new DeduplicationPolicy(),
            NullLogger<ApplicationDiscoveryService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.DiscoverAllAsync(new DiscoveryFilter(), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
