namespace Remvora.Core.Domain.Startup;

/// <summary>
/// Source location where a startup program is registered.
/// </summary>
public enum StartupLocationType
{
    RegistryCurrentUser = 1,
    RegistryLocalMachine = 2,
    StartupFolderUser = 3,
    StartupFolderCommon = 4,
    TaskScheduler = 5
}

/// <summary>
/// Estimated performance impact of the startup program during system boot.
/// </summary>
public enum StartupImpact
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// Domain model representing an item configured to launch automatically at Windows boot or user logon.
/// </summary>
public sealed record StartupEntry
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string Command { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Publisher { get; init; }
    public StartupLocationType LocationType { get; init; }
    public string LocationPath { get; init; }
    public bool IsEnabled { get; init; }
    public StartupImpact Impact { get; init; }

    public StartupEntry(
        Guid id,
        string name,
        string command,
        string? executablePath,
        string? publisher,
        StartupLocationType locationType,
        string locationPath,
        bool isEnabled = true,
        StartupImpact impact = StartupImpact.Medium)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationPath);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Name = name.Trim();
        Command = command.Trim();
        ExecutablePath = executablePath?.Trim();
        Publisher = publisher?.Trim();
        LocationType = locationType;
        LocationPath = locationPath.Trim();
        IsEnabled = isEnabled;
        Impact = impact;
    }
}
