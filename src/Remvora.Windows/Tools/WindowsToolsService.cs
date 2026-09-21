using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Tools;
using Remvora.Core.Domain.Results;

namespace Remvora.Windows.Tools;

/// <summary>
/// Implements curated Windows administrative and diagnostic utility discovery and execution.
/// </summary>
public sealed partial class WindowsToolsService : IWindowsToolsService
{
    private readonly ILogger<WindowsToolsService> _logger;

    private static readonly IReadOnlyList<WindowsToolItem> Tools =
    [
        // System Administration
        new WindowsToolItem(
            "regedit",
            "Registry Editor",
            "View, search, and edit Windows system registry configuration keys and values.",
            ToolCategory.SystemAdministration,
            "regedit.exe",
            null,
            true,
            "\uE74C"),

        new WindowsToolItem(
            "services",
            "Services Manager",
            "Start, stop, and configure Windows background service processes and startup modes.",
            ToolCategory.SystemAdministration,
            "services.msc",
            null,
            true,
            "\uE9F9"),

        new WindowsToolItem(
            "taskschd",
            "Task Scheduler",
            "Automate system tasks, monitor trigger states, and inspect background maintenance tasks.",
            ToolCategory.SystemAdministration,
            "taskschd.msc",
            null,
            true,
            "\uE823"),

        new WindowsToolItem(
            "gpedit",
            "Group Policy Editor",
            "Configure user permissions, system security policies, and Windows behaviors.",
            ToolCategory.SystemAdministration,
            "gpedit.msc",
            null,
            true,
            "\uE72D"),

        new WindowsToolItem(
            "eventvwr",
            "Event Viewer",
            "Monitor and troubleshoot Windows application, system, and security diagnostic event logs.",
            ToolCategory.SystemAdministration,
            "eventvwr.msc",
            null,
            true,
            "\uE7C3"),

        new WindowsToolItem(
            "msinfo32",
            "System Information",
            "Comprehensive hardware summary, loaded drivers, and software environment diagnostic report.",
            ToolCategory.SystemAdministration,
            "msinfo32.exe",
            null,
            false,
            "\uE946"),

        // Hardware & Diagnostics
        new WindowsToolItem(
            "devmgmt",
            "Device Manager",
            "Inspect, enable, disable, and update hardware devices, peripherals, and driver status.",
            ToolCategory.HardwareDiagnostics,
            "devmgmt.msc",
            null,
            true,
            "\uE7F4"),

        new WindowsToolItem(
            "resmon",
            "Resource Monitor",
            "Real-time inspection of CPU threads, working memory, disk file handles, and network connections.",
            ToolCategory.HardwareDiagnostics,
            "resmon.exe",
            null,
            true,
            "\uE9D9"),

        new WindowsToolItem(
            "dxdiag",
            "DirectX Diagnostic Tool",
            "Diagnose DirectX graphics acceleration, audio hardware, and display subsystem driver info.",
            ToolCategory.HardwareDiagnostics,
            "dxdiag.exe",
            null,
            false,
            "\uE790"),

        new WindowsToolItem(
            "perfmon",
            "Performance Monitor",
            "In-depth system performance metrics, counter logs, and reliability historical monitoring.",
            ToolCategory.HardwareDiagnostics,
            "perfmon.msc",
            null,
            true,
            "\uE9D2"),

        // Storage & Maintenance
        new WindowsToolItem(
            "diskmgmt",
            "Disk Management",
            "Partition, format, resize, and configure storage drives, dynamic volumes, and drive letters.",
            ToolCategory.StorageMaintenance,
            "diskmgmt.msc",
            null,
            true,
            "\uEDA2"),

        new WindowsToolItem(
            "cleanmgr",
            "Disk Cleanup",
            "Built-in Windows cleanup utility for clearing temporary files, logs, and downloaded updates.",
            ToolCategory.StorageMaintenance,
            "cleanmgr.exe",
            null,
            false,
            "\uE74D"),

        new WindowsToolItem(
            "dfrgui",
            "Drive Optimizer (Defrag)",
            "Run TRIM on SSD volumes and defragment fragmented spinning hard disks.",
            ToolCategory.StorageMaintenance,
            "dfrgui.exe",
            null,
            true,
            "\uE74E"),

        // Network & Terminal
        new WindowsToolItem(
            "ncpa",
            "Network Connections",
            "Configure physical and virtual network adapters, IPv4/IPv6 addresses, and DNS servers.",
            ToolCategory.NetworkTerminal,
            "ncpa.cpl",
            null,
            false,
            "\uE774"),

        new WindowsToolItem(
            "powershell",
            "PowerShell Terminal",
            "Launch the Windows PowerShell command line terminal environment.",
            ToolCategory.NetworkTerminal,
            "powershell.exe",
            null,
            false,
            "\uE756"),

        new WindowsToolItem(
            "cmd",
            "Command Prompt",
            "Launch the classic Windows Command Prompt terminal.",
            ToolCategory.NetworkTerminal,
            "cmd.exe",
            null,
            false,
            "\uE756")
    ];

    public WindowsToolsService(ILogger<WindowsToolsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<WindowsToolItem> GetAllTools() => Tools;

    public OperationResult<bool> LaunchTool(WindowsToolItem tool, bool runAsAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(tool);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool.Executable,
                Arguments = tool.Arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = (runAsAdmin || tool.RequiresElevation) ? "runas" : string.Empty
            };

            LogLaunchingTool(_logger, tool.Id, tool.Executable, runAsAdmin);
            Process.Start(psi);
            return OperationResult.Success(true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED by user at UAC prompt
        {
            LogLaunchCancelled(_logger, tool.Id);
            return OperationResult.Failure<bool>(ErrorCode.Cancelled, "Elevation prompt was cancelled by the user.");
        }
        catch (Exception ex)
        {
            LogLaunchFailed(_logger, ex, tool.Id, tool.Executable);
            return OperationResult.Failure<bool>(ErrorCode.ExecutionFailed, $"Failed to launch {tool.Name}: {ex.Message}");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Launching tool {ToolId} ({Executable}) with runAsAdmin={RunAsAdmin}")]
    private static partial void LogLaunchingTool(ILogger logger, string toolId, string executable, bool runAsAdmin);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Tool launch {ToolId} cancelled by user at UAC elevation prompt")]
    private static partial void LogLaunchCancelled(ILogger logger, string toolId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to launch Windows tool {ToolId} ({Executable})")]
    private static partial void LogLaunchFailed(ILogger logger, Exception ex, string toolId, string executable);
}
