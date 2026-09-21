namespace Remvora.Application.RestorePoint;

/// <summary>
/// Result metadata of a created Windows System Restore Point.
/// </summary>
public sealed record RestorePointResult(
    long SequenceNumber,
    string Description,
    DateTimeOffset CreatedAt);
