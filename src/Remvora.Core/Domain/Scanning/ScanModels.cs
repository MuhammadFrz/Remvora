namespace Remvora.Core.Domain.Scanning;

/// <summary>
/// Primary classification categories for the deep system scanner.
/// </summary>
public enum ScanCategory
{
    WindowsRedundant = 0,
    AppCache = 1,
    OrphanedLeftovers = 2,
    AppIssues = 3
}

/// <summary>
/// An individual item discovered during a system scan.
/// </summary>
public sealed record ScanItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ScanCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string TargetPath { get; init; }
    public long SizeBytes { get; init; }
    public bool IsSelected { get; set; } = true;
    public bool IsRemovable { get; init; } = true;
    public string? ExtraInfo { get; init; }

    public string FormattedSize => FormatBytes(SizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// A category group of discovered scan items.
/// </summary>
public sealed record ScanGroup
{
    public ScanCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<ScanItem> Items { get; init; } = [];

    public long TotalSizeBytes => Items.Sum(i => i.SizeBytes);
    public int TotalCount => Items.Count;
    public string FormattedTotalSize => ScanItem.FormatBytes(TotalSizeBytes);
}

/// <summary>
/// Progress reporting during an active system scan.
/// </summary>
public sealed record ScanProgressReport(
    string CurrentStep,
    string CurrentTarget,
    int ItemsFound,
    long BytesFound,
    int PercentComplete);

/// <summary>
/// Result of a cleanup pass on selected scan items.
/// </summary>
public sealed record ScanCleanupResult(
    int ItemsRemoved,
    long BytesReclaimed,
    IReadOnlyList<string> Errors);
