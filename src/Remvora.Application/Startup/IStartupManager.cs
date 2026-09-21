using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Startup;

namespace Remvora.Application.Startup;

/// <summary>
/// Service responsible for discovering, inspecting, enabling/disabling, and safely removing Windows startup entries.
/// </summary>
public interface IStartupManager
{
    /// <summary>
    /// Enumerates all configured startup entries across Registry (HKCU/HKLM), Startup folders, and Task Scheduler.
    /// </summary>
    Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables an auto-start entry without deleting it.
    /// </summary>
    Task<OperationResult> SetEnabledAsync(StartupEntry entry, bool enable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes a startup entry, backing it up to the transaction recovery store first.
    /// </summary>
    Task<OperationResult> RemoveEntryAsync(StartupEntry entry, CancellationToken cancellationToken = default);
}
