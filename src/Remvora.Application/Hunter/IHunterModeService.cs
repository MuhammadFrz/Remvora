using Remvora.Core.Domain.Results;

namespace Remvora.Application.Hunter;

/// <summary>
/// Service for acquiring and resolving application targets from windows, processes, or files.
/// </summary>
public interface IHunterModeService
{
    /// <summary>
    /// Resolves an application target from screen coordinates (x, y).
    /// </summary>
    Task<HunterTargetResult?> ResolveTargetFromPointAsync(int screenX, int screenY, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an application target from an executable or shortcut (.lnk) file path.
    /// </summary>
    Task<HunterTargetResult?> ResolveTargetFromPathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an application target from a running process ID.
    /// </summary>
    Task<HunterTargetResult?> ResolveTargetFromProcessIdAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates visible top-level desktop windows and their process targets for selection.
    /// </summary>
    Task<IReadOnlyList<HunterTargetResult>> GetActiveWindowTargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely terminates the specified target process. Protected system processes are rejected.
    /// </summary>
    OperationResult TerminateTargetProcess(int processId);
}
