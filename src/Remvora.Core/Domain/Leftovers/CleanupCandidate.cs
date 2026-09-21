namespace Remvora.Core.Domain.Leftovers;

/// <summary>
/// An identified leftover candidate discovered during post-uninstall or advanced analysis.
/// </summary>
public sealed record CleanupCandidate
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public CandidateKind Kind { get; init; }
    public string Target { get; init; }
    public string? ParentTarget { get; init; }
    public IReadOnlyList<string> EvidenceReasons { get; init; }
    public CandidateConfidence Confidence { get; init; }
    public int ConfidenceScore { get; init; }
    public RiskLevel Risk { get; init; }
    public bool DefaultSelected { get; init; }
    public bool IsProtected { get; init; }
    public long? SizeBytes { get; init; }
    public string? Notes { get; init; }

    public CleanupCandidate(
        Guid id,
        Guid applicationId,
        CandidateKind kind,
        string target,
        string? parentTarget,
        IReadOnlyList<string> evidenceReasons,
        CandidateConfidence confidence,
        int confidenceScore,
        RiskLevel risk,
        bool defaultSelected,
        bool isProtected,
        long? sizeBytes = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApplicationId = applicationId;
        Kind = kind;
        Target = target.Trim();
        ParentTarget = parentTarget?.Trim();
        EvidenceReasons = evidenceReasons ?? [];
        Confidence = confidence;
        ConfidenceScore = Math.Clamp(confidenceScore, 0, 100);
        Risk = risk;
        DefaultSelected = !isProtected && defaultSelected;
        IsProtected = isProtected;
        SizeBytes = sizeBytes;
        Notes = notes;
    }
}
