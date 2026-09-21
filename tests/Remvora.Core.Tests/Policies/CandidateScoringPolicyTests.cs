using FluentAssertions;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Core.Tests.Policies;

public sealed class CandidateScoringPolicyTests
{
    private readonly ProtectedPathsPolicy _pathsPolicy = new(@"C:\Program Files\Remvora");
    private readonly CandidateScoringPolicy _scoringPolicy;

    public CandidateScoringPolicyTests()
    {
        _scoringPolicy = new CandidateScoringPolicy(_pathsPolicy);
    }

    [Fact]
    public void Evaluate_WhenTargetIsSystemProtected_ReturnsSystemProtectedRiskAndDeselected()
    {
        var result = _scoringPolicy.Evaluate(
            CandidateKind.File,
            @"C:\Windows\System32\important.dll",
            ["Found during scan"],
            isInstallMonitorMatch: true, // Even if monitor claimed it, system protection outranks!
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: false,
            isAppSpecificRegistryPath: false,
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: false);

        result.IsProtected.Should().BeTrue();
        result.Risk.Should().Be(RiskLevel.SystemProtected);
        result.DefaultSelected.Should().BeFalse();
        result.Confidence.Should().Be(CandidateConfidence.Low);
    }

    [Fact]
    public void Evaluate_WithStrongEvidence_AssignsHighConfidenceAndDefaultSelected()
    {
        var result = _scoringPolicy.Evaluate(
            CandidateKind.Directory,
            @"C:\Program Files\VendorApp",
            ["Exact install directory"],
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: true,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: true,
            isAppSpecificRegistryPath: false,
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: false);

        result.IsProtected.Should().BeFalse();
        result.Confidence.Should().Be(CandidateConfidence.High);
        result.Score.Should().BeGreaterThanOrEqualTo(75);
        result.Risk.Should().Be(RiskLevel.Low);
        result.DefaultSelected.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithModerateEvidence_AssignsMediumConfidenceAndDeselected()
    {
        var result = _scoringPolicy.Evaluate(
            CandidateKind.RegistryKey,
            @"HKCU\Software\VendorApp",
            ["App registry key"],
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: false,
            isAppSpecificRegistryPath: true, // +50
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: false);

        result.IsProtected.Should().BeFalse();
        result.Confidence.Should().Be(CandidateConfidence.Medium);
        result.Score.Should().Be(50);
        result.Risk.Should().Be(RiskLevel.Moderate);
        result.DefaultSelected.Should().BeFalse(); // Medium is conservative
    }

    [Fact]
    public void Evaluate_WithSharedRuntimePenalty_LowersConfidence()
    {
        var result = _scoringPolicy.Evaluate(
            CandidateKind.Directory,
            @"C:\Program Files\Common Files\VendorShared",
            ["Shared folder"],
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: false,
            isAppSpecificRegistryPath: false,
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: true, // -60 penalty
            isMicrosoftOrSystemComponent: false);

        result.Confidence.Should().Be(CandidateConfidence.Low);
        result.Score.Should().Be(0);
        result.DefaultSelected.Should().BeFalse();
    }
}
