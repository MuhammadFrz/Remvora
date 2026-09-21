using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Application.Tests.Leftovers;

public sealed class CleanupPlannerTests
{
    private static ApplicationRecord CreateTestApp()
    {
        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: "Test App",
            publisher: "Test Pub",
            displayVersion: "1.0",
            installDate: null,
            installLocation: @"C:\Program Files\TestApp",
            estimatedSizeBytes: 1024,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.InnoSetup,
            identity: new ApplicationIdentity("Test App", "Test Pub", "1.0", @"C:\Program Files\TestApp"),
            uninstall: new UninstallInfo(@"C:\Program Files\TestApp\unins000.exe"));
    }

    [Fact]
    public void CreatePlan_SanitizesProtectedPathsAndDisablesDefaultSelection()
    {
        var planner = new CleanupPlanner(ProtectedPathsPolicy.Default, NullLogger<CleanupPlanner>.Instance);
        var app = CreateTestApp();

        var safeCandidate = new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: app.Id,
            kind: CandidateKind.Directory,
            target: @"C:\Program Files\TestApp",
            parentTarget: null,
            evidenceReasons: ["Install dir"],
            confidence: CandidateConfidence.High,
            confidenceScore: 90,
            risk: RiskLevel.Low,
            defaultSelected: true,
            isProtected: false);

        var protectedCandidate = new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: app.Id,
            kind: CandidateKind.Directory,
            target: @"C:\Windows\System32",
            parentTarget: null,
            evidenceReasons: ["System root test"],
            confidence: CandidateConfidence.Low,
            confidenceScore: 10,
            risk: RiskLevel.Low,
            defaultSelected: true, // Should be overridden to false
            isProtected: false);

        var plan = planner.CreatePlan(app, [safeCandidate, protectedCandidate]);

        plan.Candidates.Should().HaveCount(2);

        var protectedInPlan = plan.Candidates.First(c => c.Target.Contains("System32"));
        protectedInPlan.IsProtected.Should().BeTrue();
        protectedInPlan.DefaultSelected.Should().BeFalse();
        protectedInPlan.Risk.Should().Be(RiskLevel.SystemProtected);

        var safeInPlan = plan.Candidates.First(c => c.Target.Contains("TestApp"));
        safeInPlan.IsProtected.Should().BeFalse();
        safeInPlan.DefaultSelected.Should().BeTrue();
    }

    [Fact]
    public void ValidatePlan_WhenProtectedCandidateSelected_BlocksExecution()
    {
        var planner = new CleanupPlanner(ProtectedPathsPolicy.Default, NullLogger<CleanupPlanner>.Instance);
        var app = CreateTestApp();

        var protectedId = Guid.NewGuid();
        var protectedCandidate = new CleanupCandidate(
            id: protectedId,
            applicationId: app.Id,
            kind: CandidateKind.Directory,
            target: @"C:\Windows",
            parentTarget: null,
            evidenceReasons: ["System root"],
            confidence: CandidateConfidence.Low,
            confidenceScore: 0,
            risk: RiskLevel.SystemProtected,
            defaultSelected: false,
            isProtected: true);

        var plan = new CleanupPlan(Guid.NewGuid(), app.Id, DateTimeOffset.UtcNow, [protectedCandidate]);

        var validation = planner.ValidatePlan(plan, [protectedId]);

        validation.IsValid.Should().BeFalse();
        validation.BlockedReasons.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidatePlan_WhenSafeCandidatesSelected_Succeeds()
    {
        var planner = new CleanupPlanner(ProtectedPathsPolicy.Default, NullLogger<CleanupPlanner>.Instance);
        var app = CreateTestApp();

        var safeId = Guid.NewGuid();
        var safeCandidate = new CleanupCandidate(
            id: safeId,
            applicationId: app.Id,
            kind: CandidateKind.Directory,
            target: @"C:\Program Files\TestApp",
            parentTarget: null,
            evidenceReasons: ["Install dir"],
            confidence: CandidateConfidence.High,
            confidenceScore: 90,
            risk: RiskLevel.Low,
            defaultSelected: true,
            isProtected: false);

        var plan = new CleanupPlan(Guid.NewGuid(), app.Id, DateTimeOffset.UtcNow, [safeCandidate]);

        var validation = planner.ValidatePlan(plan, [safeId]);

        validation.IsValid.Should().BeTrue();
        validation.ValidCandidates.Should().HaveCount(1);
        validation.BlockedReasons.Should().BeEmpty();
    }
}
