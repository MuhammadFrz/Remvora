using Remvora.Core.Domain.Leftovers;

namespace Remvora.Core.Policies;

/// <summary>
/// Policy governing evidence-based scoring and confidence classification for leftover candidates.
/// </summary>
public interface ICandidateScoringPolicy
{
    (CandidateConfidence Confidence, int Score, RiskLevel Risk, bool DefaultSelected, bool IsProtected) Evaluate(
        CandidateKind kind,
        string target,
        IReadOnlyList<string> evidenceReasons,
        bool isInstallMonitorMatch,
        bool isExactInstallDirectoryMatch,
        bool isExactServiceOrTaskUnderInstallDir,
        bool isVendorAndAppDirMatch,
        bool isAppSpecificRegistryPath,
        bool isAppSpecificAppDataFolder,
        bool isShortcutTargetMatch,
        bool isSharedRuntimeOrCache,
        bool isMicrosoftOrSystemComponent);
}

/// <summary>
/// Production deterministic scoring policy implementing the plan's exact weights and rules.
/// </summary>
public sealed class CandidateScoringPolicy : ICandidateScoringPolicy
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;

    public CandidateScoringPolicy(IProtectedPathsPolicy protectedPathsPolicy)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
    }

    public (CandidateConfidence Confidence, int Score, RiskLevel Risk, bool DefaultSelected, bool IsProtected) Evaluate(
        CandidateKind kind,
        string target,
        IReadOnlyList<string> evidenceReasons,
        bool isInstallMonitorMatch,
        bool isExactInstallDirectoryMatch,
        bool isExactServiceOrTaskUnderInstallDir,
        bool isVendorAndAppDirMatch,
        bool isAppSpecificRegistryPath,
        bool isAppSpecificAppDataFolder,
        bool isShortcutTargetMatch,
        bool isSharedRuntimeOrCache,
        bool isMicrosoftOrSystemComponent)
    {
        // 1. Check protection first
        bool isProtected = false;
        string protectReason = string.Empty;

        if (kind is CandidateKind.File or CandidateKind.Directory or CandidateKind.Shortcut)
        {
            isProtected = _protectedPathsPolicy.IsPathProtected(target, out protectReason);
        }
        else if (kind is CandidateKind.RegistryKey or CandidateKind.RegistryValue)
        {
            isProtected = _protectedPathsPolicy.IsRegistryKeyProtected(target, out protectReason);
        }

        if (isProtected)
        {
            return (CandidateConfidence.Low, 0, RiskLevel.SystemProtected, false, true);
        }

        // 2. Compute evidence score based on documented weights
        int score = 0;

        if (isInstallMonitorMatch) score += 100;
        if (isExactInstallDirectoryMatch) score += 90;
        if (isExactServiceOrTaskUnderInstallDir) score += 80;
        if (isVendorAndAppDirMatch) score += 75;
        if (isAppSpecificRegistryPath) score += 50;
        if (isAppSpecificAppDataFolder) score += 40;
        if (isShortcutTargetMatch) score += 30;

        // Negative penalties
        if (isSharedRuntimeOrCache) score -= 60;
        if (isMicrosoftOrSystemComponent) score -= 50;

        score = Math.Clamp(score, 0, 100);

        // 3. Determine Confidence, Risk, and DefaultSelected
        CandidateConfidence confidence;
        RiskLevel risk;
        bool defaultSelected;

        if (score >= 75)
        {
            confidence = CandidateConfidence.High;
            risk = RiskLevel.Low;
            defaultSelected = true;
        }
        else if (score >= 40)
        {
            confidence = CandidateConfidence.Medium;
            risk = RiskLevel.Moderate;
            defaultSelected = false; // Conservative: medium evidence is visible but not selected by default
        }
        else
        {
            confidence = CandidateConfidence.Low;
            risk = RiskLevel.High;
            defaultSelected = false; // Never auto-select low confidence
        }

        return (confidence, score, risk, defaultSelected, false);
    }
}
