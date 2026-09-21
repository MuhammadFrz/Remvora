namespace Remvora.Contracts.Updates;

/// <summary>
/// Cryptographic and semantic manifest describing an application release distribution.
/// </summary>
public sealed record UpdateManifest
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseDate { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string? MinDeltaVersion { get; init; }
    public Dictionary<string, UpdatePackageInfo> Packages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FileManifestEntry> Files { get; init; } = [];
}

/// <summary>
/// Architecture-specific full and delta archive URLs and checksums.
/// </summary>
public sealed record UpdatePackageInfo
{
    public UpdateArchiveInfo? FullPackage { get; init; }
    public UpdateArchiveInfo? DeltaPackage { get; init; }
    public UpdateArchiveInfo? InstallerExe { get; init; }
}

/// <summary>
/// Download location, cryptographic checksum, and size of an update archive.
/// </summary>
public sealed record UpdateArchiveInfo
{
    public string Url { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}

/// <summary>
/// Manifest entry for an individual application file.
/// </summary>
public sealed record FileManifestEntry
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
}

/// <summary>
/// Result of an update check against the remote repository manifest.
/// </summary>
public sealed record UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public required string CurrentVersion { get; init; }
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseDate { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public bool IsDeltaAvailable { get; init; }
    public long DownloadSizeBytes { get; init; }
    public string DownloadUrl { get; init; } = string.Empty;
    public string PackageSha256 { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Progress reporting for an active update download.
/// </summary>
public sealed record UpdateDownloadProgress
{
    public long BytesReceived { get; init; }
    public long TotalBytesToReceive { get; init; }
    public double PercentComplete { get; init; }

    public UpdateDownloadProgress(long bytesReceived, long totalBytesToReceive)
    {
        BytesReceived = bytesReceived;
        TotalBytesToReceive = totalBytesToReceive;
        PercentComplete = totalBytesToReceive > 0
            ? Math.Round((double)bytesReceived / totalBytesToReceive * 100, 1)
            : 0;
    }
}
