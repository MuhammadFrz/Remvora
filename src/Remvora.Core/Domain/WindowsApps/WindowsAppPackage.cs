namespace Remvora.Core.Domain.WindowsApps;

/// <summary>
/// Bloatware classification for pre-installed or sponsored Store apps.
/// </summary>
public enum BloatwareCategory
{
    None = 0,
    Sponsored = 1,
    Entertainment = 2,
    TelemetryOrDiagnostic = 3,
    Redundant = 4
}

/// <summary>
/// Domain model representing a modern packaged application (MSIX/Appx/UWP or provisioned package).
/// </summary>
public sealed record WindowsAppPackage
{
    public string PackageFullName { get; init; }
    public string PackageFamilyName { get; init; }
    public string DisplayName { get; init; }
    public string Publisher { get; init; }
    public string Version { get; init; }
    public string? InstallLocation { get; init; }
    public bool IsProvisioned { get; init; }
    public bool IsFramework { get; init; }
    public bool IsSystemProtected { get; init; }
    public BloatwareCategory BloatwareType { get; init; }

    public bool IsBloatware => BloatwareType != BloatwareCategory.None;

    public WindowsAppPackage(
        string packageFullName,
        string packageFamilyName,
        string displayName,
        string publisher,
        string version,
        string? installLocation = null,
        bool isProvisioned = false,
        bool isFramework = false,
        bool isSystemProtected = false,
        BloatwareCategory bloatwareType = BloatwareCategory.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFamilyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        PackageFullName = packageFullName.Trim();
        PackageFamilyName = packageFamilyName.Trim();
        DisplayName = displayName.Trim();
        Publisher = publisher?.Trim() ?? string.Empty;
        Version = version?.Trim() ?? "1.0.0.0";
        InstallLocation = installLocation?.Trim();
        IsProvisioned = isProvisioned;
        IsFramework = isFramework;
        IsSystemProtected = isSystemProtected;
        BloatwareType = bloatwareType;
    }
}
