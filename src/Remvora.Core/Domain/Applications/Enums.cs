namespace Remvora.Core.Domain.Applications;

/// <summary>
/// Indicates the installation scope of an application.
/// </summary>
public enum InstallationScope
{
    PerUser,
    PerMachine,
    SystemProtected,
    Unknown
}

/// <summary>
/// Known installer technology or packaging framework.
/// </summary>
public enum InstallerType
{
    Unknown = 0,
    Msi = 1,
    InnoSetup = 2,
    Nsis = 3,
    InstallShield = 4,
    WiXBurn = 5,
    StorePackage = 6,
    CustomExecutable = 7
}

/// <summary>
/// Processor architecture targeted by an application.
/// </summary>
public enum ArchitectureType
{
    Neutral = 0,
    X86 = 1,
    X64 = 2,
    Arm64 = 3
}

/// <summary>
/// Source where an application record was discovered.
/// </summary>
public enum DiscoverySourceType
{
    Registry64,
    Registry32,
    RegistryUser,
    MsiDatabase,
    AppxPackage,
    MonitoredInstall
}

/// <summary>
/// Running status of application processes.
/// </summary>
public enum RunningStatus
{
    NotRunning,
    Running,
    Suspended,
    Unknown
}
