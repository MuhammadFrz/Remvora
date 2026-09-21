using Remvora.Core.Domain.Results;

namespace Remvora.Application.Tools;

/// <summary>
/// Service providing access to built-in Windows administrative and diagnostic utilities.
/// </summary>
public interface IWindowsToolsService
{
    /// <summary>
    /// Gets the list of curated Windows administrative, diagnostic, and maintenance tools.
    /// </summary>
    IReadOnlyList<WindowsToolItem> GetAllTools();

    /// <summary>
    /// Launches the specified Windows tool process.
    /// </summary>
    /// <param name="tool">The tool to launch.</param>
    /// <param name="runAsAdmin">Whether to prompt for elevation (runas).</param>
    /// <returns>Result indicating whether the tool was successfully launched.</returns>
    OperationResult<bool> LaunchTool(WindowsToolItem tool, bool runAsAdmin = false);
}
