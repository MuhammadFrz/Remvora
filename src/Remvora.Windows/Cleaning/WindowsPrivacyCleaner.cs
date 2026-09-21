using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Cleaning;
using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Domain.Results;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Cleaning;

/// <summary>
/// Windows implementation for discovering and wiping user activity traces and MRUs.
/// </summary>
public sealed partial class WindowsPrivacyCleaner : IPrivacyCleaner
{
    private const string RunMruPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private const string TypedPathsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";

    private readonly IRegistryAccessor _registryAccessor;
    private readonly ILogger<WindowsPrivacyCleaner> _logger;

    public WindowsPrivacyCleaner(
        IRegistryAccessor registryAccessor,
        ILogger<WindowsPrivacyCleaner> logger)
    {
        _registryAccessor = registryAccessor ?? throw new ArgumentNullException(nameof(registryAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<PrivacyItem>> ScanPrivacyTracesAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<PrivacyItem>();

        // 1. Recent Documents
        var recentFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");
        int recentCount = 0;
        if (Directory.Exists(recentFolder))
        {
            try { recentCount = Directory.GetFiles(recentFolder).Length; } catch { }
        }
        items.Add(new PrivacyItem("recent_docs", "Recent Files & Documents", "History of opened documents and folders in Windows Explorer", recentCount));

        // 2. Run Dialog History
        var runValues = _registryAccessor.GetValues(RegistryHive.CurrentUser, RegistryView.Default, RunMruPath);
        int runCount = runValues?.Count ?? 0;
        items.Add(new PrivacyItem("run_mru", "Windows Run Dialog History", "Commands and paths entered into the Windows Run prompt (Win+R)", runCount));

        // 3. Explorer Typed Paths
        var typedValues = _registryAccessor.GetValues(RegistryHive.CurrentUser, RegistryView.Default, TypedPathsSubKey);
        int typedCount = typedValues?.Count ?? 0;
        items.Add(new PrivacyItem("typed_paths", "Explorer Address Bar History", "Directories and locations typed into the File Explorer address bar", typedCount));

        return Task.FromResult<IReadOnlyList<PrivacyItem>>(items);
    }

    public Task<OperationResult<int>> CleanPrivacyTracesAsync(
        IEnumerable<string> selectedTraceKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedTraceKeys);

        var keys = selectedTraceKeys.ToHashSet();
        int tracesRemoved = 0;

        // 1. Recent docs
        if (keys.Contains("recent_docs"))
        {
            var recentFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");
            if (Directory.Exists(recentFolder))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(recentFolder))
                    {
                        try
                        {
                            File.Delete(file);
                            tracesRemoved++;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // 2. Run MRU
        if (keys.Contains("run_mru"))
        {
            tracesRemoved += ClearRegistryValues(RegistryHive.CurrentUser, RunMruPath);
        }

        // 3. Typed paths
        if (keys.Contains("typed_paths"))
        {
            tracesRemoved += ClearRegistryValues(RegistryHive.CurrentUser, TypedPathsSubKey);
        }

        LogPrivacyCleaned(_logger, tracesRemoved);
        return Task.FromResult(OperationResult.Success(tracesRemoved));
    }

    private static int ClearRegistryValues(RegistryHive hive, string subKey)
    {
        int count = 0;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key != null)
            {
                foreach (var valName in key.GetValueNames())
                {
                    key.DeleteValue(valName, throwOnMissingValue: false);
                    count++;
                }
            }
        }
        catch { }

        return count;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Cleared {Count} privacy traces.")]
    private static partial void LogPrivacyCleaned(ILogger logger, int count);
}
