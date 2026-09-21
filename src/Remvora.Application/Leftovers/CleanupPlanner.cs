using Microsoft.Extensions.Logging;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Application.Leftovers;

/// <summary>
/// Result of validating a planned cleanup execution.
/// </summary>
public sealed record PlanValidationResult(
    bool IsValid,
    IReadOnlyList<CleanupCandidate> ValidCandidates,
    IReadOnlyList<string> BlockedReasons)
{
    public static PlanValidationResult Success(IReadOnlyList<CleanupCandidate> validCandidates)
        => new(true, validCandidates, Array.Empty<string>());

    public static PlanValidationResult Failure(IReadOnlyList<CleanupCandidate> validCandidates, IReadOnlyList<string> blockedReasons)
        => new(false, validCandidates, blockedReasons);
}

/// <summary>
/// Service responsible for constructing, organizing, and validating deterministic cleanup plans.
/// </summary>
public interface ICleanupPlanner
{
    CleanupPlan CreatePlan(ApplicationRecord application, IReadOnlyList<CleanupCandidate> candidates);

    PlanValidationResult ValidatePlan(CleanupPlan plan, IEnumerable<Guid> selectedCandidateIds);
}

/// <summary>
/// Production cleanup planner enforcing system protection invariants and risk-weighted candidate selection.
/// </summary>
public sealed partial class CleanupPlanner : ICleanupPlanner
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ILogger<CleanupPlanner> _logger;

    public CleanupPlanner(
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<CleanupPlanner> logger)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CleanupPlan CreatePlan(ApplicationRecord application, IReadOnlyList<CleanupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(candidates);

        LogCreatingPlan(_logger, candidates.Count, application.DisplayName);

        var sanitizedCandidates = new List<CleanupCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var isProtected = candidate.IsProtected;
            if (!isProtected)
            {
                if (candidate.Kind is CandidateKind.File or CandidateKind.Directory or CandidateKind.Shortcut)
                {
                    isProtected = _protectedPathsPolicy.IsPathProtected(candidate.Target, out _);
                }
                else if (candidate.Kind is CandidateKind.RegistryKey or CandidateKind.RegistryValue)
                {
                    isProtected = _protectedPathsPolicy.IsRegistryKeyProtected(candidate.Target, out _);
                }
            }

            var defaultSelected = !isProtected && candidate.DefaultSelected;
            var risk = isProtected ? RiskLevel.SystemProtected : candidate.Risk;

            sanitizedCandidates.Add(candidate with
            {
                IsProtected = isProtected,
                DefaultSelected = defaultSelected,
                Risk = risk
            });
        }

        var plan = new CleanupPlan(
            id: Guid.NewGuid(),
            applicationId: application.Id,
            createdAt: DateTimeOffset.UtcNow,
            candidates: sanitizedCandidates);

        LogPlanCreated(_logger, plan.Id, plan.SelectedCandidates.Count, plan.TotalSelectedSizeBytes);
        return plan;
    }

    public PlanValidationResult ValidatePlan(CleanupPlan plan, IEnumerable<Guid> selectedCandidateIds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedCandidateIds);

        var selectedIdSet = selectedCandidateIds.ToHashSet();
        var selectedCandidates = plan.Candidates.Where(c => selectedIdSet.Contains(c.Id)).ToList();

        var validCandidates = new List<CleanupCandidate>();
        var blockedReasons = new List<string>();

        foreach (var candidate in selectedCandidates)
        {
            // Protection check
            if (candidate.IsProtected)
            {
                blockedReasons.Add($"Candidate '{candidate.Target}' is system-protected and cannot be selected.");
                continue;
            }

            if (candidate.Kind is CandidateKind.File or CandidateKind.Directory or CandidateKind.Shortcut)
            {
                if (_protectedPathsPolicy.IsPathProtected(candidate.Target, out var reason))
                {
                    blockedReasons.Add($"Path '{candidate.Target}' is protected: {reason}");
                    continue;
                }
            }
            else if (candidate.Kind is CandidateKind.RegistryKey or CandidateKind.RegistryValue)
            {
                if (_protectedPathsPolicy.IsRegistryKeyProtected(candidate.Target, out var reason))
                {
                    blockedReasons.Add($"Registry key '{candidate.Target}' is protected: {reason}");
                    continue;
                }
            }

            validCandidates.Add(candidate);
        }

        if (blockedReasons.Count > 0)
        {
            LogValidationBlocked(_logger, blockedReasons.Count);
            return PlanValidationResult.Failure(validCandidates, blockedReasons);
        }

        return PlanValidationResult.Success(validCandidates);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Creating cleanup plan from {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogCreatingPlan(ILogger logger, int candidateCount, string appName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Created cleanup plan #{PlanId} with {SelectedCount} selected candidates ({SizeBytes} bytes)")]
    private static partial void LogPlanCreated(ILogger logger, Guid planId, int selectedCount, long sizeBytes);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Plan validation blocked {BlockedCount} candidate(s) due to safety policy violation")]
    private static partial void LogValidationBlocked(ILogger logger, int blockedCount);
}
