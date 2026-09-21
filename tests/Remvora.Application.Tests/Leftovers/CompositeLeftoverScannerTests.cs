using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;

namespace Remvora.Application.Tests.Leftovers;

public sealed class CompositeLeftoverScannerTests
{
    private sealed class FakeSubScanner : ISubLeftoverScanner
    {
        public CandidateKind SupportedKind { get; init; }
        public List<CleanupCandidate> CandidatesToReturn { get; init; } = [];
        public int ScanCount { get; private set; }

        public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
            ApplicationRecord application,
            ScanOptions options,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return Task.FromResult<IReadOnlyList<CleanupCandidate>>(CandidatesToReturn);
        }
    }

    private static ApplicationRecord CreateTestApp()
    {
        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: "Sample App",
            publisher: "Sample Publisher",
            displayVersion: "1.0",
            installDate: null,
            installLocation: @"C:\Program Files\SampleApp",
            estimatedSizeBytes: 1024,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.InnoSetup,
            identity: new ApplicationIdentity("Sample App", "Sample Publisher", "1.0", @"C:\Program Files\SampleApp"),
            uninstall: new UninstallInfo(@"C:\Program Files\SampleApp\unins000.exe"));
    }

    private static CleanupCandidate CreateCandidate(CandidateKind kind, string target)
    {
        return new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: Guid.NewGuid(),
            kind: kind,
            target: target,
            parentTarget: null,
            evidenceReasons: ["Test reason"],
            confidence: CandidateConfidence.High,
            confidenceScore: 80,
            risk: RiskLevel.Low,
            defaultSelected: true,
            isProtected: false);
    }

    [Fact]
    public async Task ScanLeftoversAsync_InvokesAllEnabledSubScanners()
    {
        var fileScanner = new FakeSubScanner
        {
            SupportedKind = CandidateKind.Directory,
            CandidatesToReturn = [CreateCandidate(CandidateKind.Directory, @"C:\AppData\SampleApp")]
        };
        var regScanner = new FakeSubScanner
        {
            SupportedKind = CandidateKind.RegistryKey,
            CandidatesToReturn = [CreateCandidate(CandidateKind.RegistryKey, @"HKCU\Software\SampleApp")]
        };

        var composite = new CompositeLeftoverScanner(
            [fileScanner, regScanner],
            NullLogger<CompositeLeftoverScanner>.Instance);

        var app = CreateTestApp();
        var results = await composite.ScanLeftoversAsync(app, ScanOptions.Default);

        results.Should().HaveCount(2);
        fileScanner.ScanCount.Should().Be(1);
        regScanner.ScanCount.Should().Be(1);
    }

    [Fact]
    public async Task ScanLeftoversAsync_SkipsDisabledSubScanners()
    {
        var fileScanner = new FakeSubScanner { SupportedKind = CandidateKind.Directory };
        var regScanner = new FakeSubScanner { SupportedKind = CandidateKind.RegistryKey };

        var composite = new CompositeLeftoverScanner(
            [fileScanner, regScanner],
            NullLogger<CompositeLeftoverScanner>.Instance);

        var app = CreateTestApp();
        var options = new ScanOptions(IncludeFiles: true, IncludeRegistry: false);

        await composite.ScanLeftoversAsync(app, options);

        fileScanner.ScanCount.Should().Be(1);
        regScanner.ScanCount.Should().Be(0);
    }

    [Fact]
    public async Task ScanLeftoversAsync_DeduplicatesCandidatesAcrossScanners()
    {
        var candidate1 = CreateCandidate(CandidateKind.Directory, @"C:\AppData\SampleApp");
        var candidate2 = CreateCandidate(CandidateKind.Directory, @"C:\AppData\SampleApp"); // Duplicate path

        var scanner1 = new FakeSubScanner
        {
            SupportedKind = CandidateKind.Directory,
            CandidatesToReturn = [candidate1]
        };
        var scanner2 = new FakeSubScanner
        {
            SupportedKind = CandidateKind.Directory,
            CandidatesToReturn = [candidate2]
        };

        var composite = new CompositeLeftoverScanner(
            [scanner1, scanner2],
            NullLogger<CompositeLeftoverScanner>.Instance);

        var app = CreateTestApp();
        var results = await composite.ScanLeftoversAsync(app, ScanOptions.Default);

        results.Should().HaveCount(1);
    }
}
