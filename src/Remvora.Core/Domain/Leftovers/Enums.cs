namespace Remvora.Core.Domain.Leftovers;

/// <summary>
/// Type of system leftover candidate.
/// </summary>
public enum CandidateKind
{
    File,
    Directory,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    StartupEntry,
    Shortcut
}

/// <summary>
/// Evidence-based confidence classification for a leftover candidate.
/// </summary>
public enum CandidateConfidence
{
    /// <summary>
    /// Ambiguous match; never selected automatically.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Probable match with plausible evidence; conservative default selection.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Strong, verifiable evidence linking item directly to target application.
    /// </summary>
    High = 3
}

/// <summary>
/// Evaluated risk level of modifying or deleting the target resource.
/// </summary>
public enum RiskLevel
{
    Safe = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    SystemProtected = 4
}
