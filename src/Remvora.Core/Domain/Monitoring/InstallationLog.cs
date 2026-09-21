namespace Remvora.Core.Domain.Monitoring;

/// <summary>
/// Status of an installation monitoring session.
/// </summary>
public enum MonitorSessionStatus
{
    Active = 1,
    Completed = 2,
    Aborted = 3
}

/// <summary>
/// Represents a captured change item during an installation monitoring session.
/// </summary>
public enum MonitoredChangeType
{
    FileCreated,
    FileModified,
    RegistryKeyCreated,
    RegistryValueModified,
    ServiceCreated,
    TaskCreated,
    StartupEntryCreated
}

/// <summary>
/// Structured record of an installation monitoring session capturing all system changes made by an installer.
/// </summary>
public sealed record InstallationSession(
    Guid Id,
    string SessionName,
    string? InstallerPath,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    MonitorSessionStatus Status,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> CreatedRegistryKeys,
    IReadOnlyList<string> ModifiedRegistryValues,
    IReadOnlyList<string> CreatedServices,
    IReadOnlyList<string> CreatedTasks,
    IReadOnlyList<string> CreatedStartupEntries
)
{
    public int TotalChangesCount => CreatedFiles.Count +
                                    ModifiedFiles.Count +
                                    CreatedRegistryKeys.Count +
                                    ModifiedRegistryValues.Count +
                                    CreatedServices.Count +
                                    CreatedTasks.Count +
                                    CreatedStartupEntries.Count;

    public string FormattedDate => StartedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
