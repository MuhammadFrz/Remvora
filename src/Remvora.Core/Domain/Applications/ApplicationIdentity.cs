namespace Remvora.Core.Domain.Applications;

/// <summary>
/// Multi-factor application identity used for deduplication, correlation, and tracking.
/// </summary>
public sealed record ApplicationIdentity
{
    public string? ProductCode { get; init; }
    public string? PackageFamilyName { get; init; }
    public string? RegistryKeyPath { get; init; }
    public string NormalizedName { get; init; }
    public string NormalizedPublisher { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }

    public ApplicationIdentity(
        string displayName,
        string? publisher = null,
        string? version = null,
        string? installLocation = null,
        string? productCode = null,
        string? packageFamilyName = null,
        string? registryKeyPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        NormalizedName = NormalizeString(displayName);
        NormalizedPublisher = NormalizeString(publisher ?? string.Empty);
        Version = version?.Trim();
        InstallLocation = NormalizePath(installLocation);
        ProductCode = productCode?.Trim().ToUpperInvariant();
        PackageFamilyName = packageFamilyName?.Trim();
        RegistryKeyPath = registryKeyPath?.Trim();
    }

    public static string NormalizeString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
    }

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var cleaned = path.Trim().Trim('\"');
        return cleaned.TrimEnd('\\', '/');
    }
}
