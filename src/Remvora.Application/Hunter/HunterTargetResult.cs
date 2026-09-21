using Remvora.Core.Domain.Applications;

namespace Remvora.Application.Hunter;

/// <summary>
/// Represents an identified target acquired through Hunter Mode.
/// </summary>
public sealed record HunterTargetResult(
    int? ProcessId,
    string ProcessName,
    string ExecutablePath,
    string? WindowTitle,
    ApplicationRecord? MatchedApplication,
    string Confidence,
    bool IsSystemProtected = false
);
