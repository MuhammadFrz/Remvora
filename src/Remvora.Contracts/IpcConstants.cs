namespace Remvora.Contracts;

/// <summary>
/// Constants governing named-pipe IPC communication between the unelevated UI and the elevated worker.
/// </summary>
public static class IpcConstants
{
    public const string PipePrefix = "Remvora.Worker.";
    public const int CurrentProtocolVersion = 1;
    public const int DefaultOperationTimeoutMs = 60_000;
    public const int HandshakeTimeoutMs = 10_000;
}

/// <summary>
/// Explicit typed operations allowed by the elevated worker.
/// Arbitrary shell commands are strictly prohibited.
/// </summary>
public enum ElevatedCommandType
{
    None = 0,
    DeleteFile = 1,
    DeleteDirectory = 2,
    DeleteRegistryKey = 3,
    DeleteRegistryValue = 4,
    StopService = 5,
    DeleteService = 6,
    DeleteScheduledTask = 7,
    CreateRestorePoint = 8,
    ExecuteVendorUninstall = 9
}
