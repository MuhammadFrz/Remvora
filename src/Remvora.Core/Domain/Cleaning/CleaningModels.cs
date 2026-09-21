namespace Remvora.Core.Domain.Cleaning;

/// <summary>
/// Categorization of system junk and temporary cache artifacts.
/// </summary>
public enum JunkCategory
{
    UserTemp = 1,
    SystemTemp = 2,
    WindowsUpdateCache = 3,
    CrashDumps = 4,
    ThumbnailCache = 5,
    RecycleBin = 6
}

/// <summary>
/// Aggregated group of temporary or junk files identified during system scan.
/// </summary>
public sealed record JunkGroup
{
    public JunkCategory Category { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public long TotalSizeBytes { get; init; }
    public int ItemCount { get; init; }
    public IReadOnlyList<string> FilePaths { get; init; }
    public bool DefaultSelected { get; init; }

    public JunkGroup(
        JunkCategory category,
        string title,
        string description,
        long totalSizeBytes,
        int itemCount,
        IReadOnlyList<string>? filePaths = null,
        bool defaultSelected = true)
    {
        Category = category;
        Title = title;
        Description = description;
        TotalSizeBytes = totalSizeBytes;
        ItemCount = itemCount;
        FilePaths = filePaths ?? [];
        DefaultSelected = defaultSelected;
    }
}

/// <summary>
/// Privacy trace or history entry identified for cleanup.
/// </summary>
public sealed record PrivacyItem
{
    public string Key { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public int TracesCount { get; init; }
    public bool DefaultSelected { get; init; }

    public PrivacyItem(
        string key,
        string title,
        string description,
        int tracesCount,
        bool defaultSelected = true)
    {
        Key = key;
        Title = title;
        Description = description;
        TracesCount = tracesCount;
        DefaultSelected = defaultSelected;
    }
}

/// <summary>
/// Military-grade and standard wiping algorithms for secure file destruction.
/// </summary>
public enum ShredderAlgorithm
{
    /// <summary>
    /// Quick single pass writing zeroes (0x00).
    /// </summary>
    ZeroFill = 1,

    /// <summary>
    /// Single pass writing cryptographically secure pseudo-random bytes.
    /// </summary>
    Pseudorandom = 2,

    /// <summary>
    /// US DoD 5220.22-M 3-pass erasure: zeroes -> ones (0xFF) -> pseudorandom.
    /// </summary>
    Dod522022M = 3,

    /// <summary>
    /// NIST 800-88 Clear standard (pseudorandom pass followed by zero-fill pass).
    /// </summary>
    Nist80088 = 4
}

/// <summary>
/// Progress reported during multi-pass file shredding.
/// </summary>
public sealed record ShredProgress(
    string CurrentFile,
    int CurrentFileIndex,
    int TotalFiles,
    int CurrentPass,
    int TotalPasses,
    int PercentComplete);
