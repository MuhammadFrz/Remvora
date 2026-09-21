namespace Remvora.Core.Domain.Applications;

/// <summary>
/// Source record providing provenance and raw attributes of discovered application metadata.
/// </summary>
public sealed record DiscoverySource(
    DiscoverySourceType SourceType,
    string SourceIdentifier,
    DateTimeOffset DiscoveredAt,
    IReadOnlyDictionary<string, string>? RawMetadata = null);

/// <summary>
/// Information required to execute an application's uninstaller.
/// </summary>
public sealed record UninstallInfo(
    string? UninstallString,
    string? QuietUninstallString = null,
    string? ModifyPath = null,
    bool IsMsi = false,
    bool RequiresElevation = false)
{
    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString) || IsMsi;
    public bool CanQuietUninstall => !string.IsNullOrWhiteSpace(QuietUninstallString) || IsMsi;
}
