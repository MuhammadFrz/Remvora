using Remvora.Core.Domain.Results;

namespace Remvora.Core.Domain.Leftovers;

/// <summary>
/// Deterministic cleanup plan representing candidates prepared for user review and execution.
/// </summary>
public sealed record CleanupPlan
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<CleanupCandidate> Candidates { get; init; }
    public string PolicyVersion { get; init; }

    public IReadOnlyList<CleanupCandidate> SelectedCandidates =>
        Candidates.Where(c => c.DefaultSelected && !c.IsProtected).ToList();

    public long TotalSelectedSizeBytes =>
        SelectedCandidates.Sum(c => c.SizeBytes ?? 0L);

    public bool RequiresElevation =>
        SelectedCandidates.Any(c => c.Kind is CandidateKind.Service or CandidateKind.ScheduledTask
            || (c.Target.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
                || c.Target.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                || c.Target.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)));

    public CleanupPlan(
        Guid id,
        Guid applicationId,
        DateTimeOffset createdAt,
        IReadOnlyList<CleanupCandidate> candidates,
        string policyVersion = "1.0")
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ApplicationId = applicationId;
        CreatedAt = createdAt;
        Candidates = candidates ?? [];
        PolicyVersion = policyVersion;
    }
}

/// <summary>
/// Result summary of an executed cleanup plan.
/// </summary>
public sealed record CleanupResult(
    Guid PlanId,
    Guid ApplicationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int TotalItems,
    int SucceededItems,
    int FailedItems,
    long ReclaimedSizeBytes,
    bool RebootRequired,
    IReadOnlyList<OperationError> Errors)
{
    public bool IsSuccess => FailedItems == 0 && Errors.Count == 0;
    public bool IsPartialSuccess => SucceededItems > 0 && (FailedItems > 0 || Errors.Count > 0);
}
