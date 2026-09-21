using Microsoft.Extensions.Logging;
using Remvora.Application.Cleaning;
using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Domain.Results;
using Remvora.Core.Policies;

namespace Remvora.Windows.Cleaning;

/// <summary>
/// Production Windows implementation of temporary and junk file detection and removal.
/// </summary>
public sealed partial class WindowsJunkCleaner : IJunkCleaner
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ILogger<WindowsJunkCleaner> _logger;

    public WindowsJunkCleaner(
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<WindowsJunkCleaner> logger)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<JunkGroup>> ScanJunkAsync(CancellationToken cancellationToken = default)
    {
        var groups = new List<JunkGroup>();

        // 1. User Temp
        var userTemp = Path.GetTempPath();
        groups.Add(ScanDirectory(JunkCategory.UserTemp, "User Temporary Files", "Cached application session and runtime files in %TEMP%", userTemp));

        // 2. System Temp
        var systemTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        groups.Add(ScanDirectory(JunkCategory.SystemTemp, "Windows System Temp", "Temporary system files in %SystemRoot%\\Temp", systemTemp));

        // 3. Crash Dumps
        var crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        groups.Add(ScanDirectory(JunkCategory.CrashDumps, "Memory & Crash Dumps", "Application error dumps and diagnostic trace logs", crashDumps));

        // 4. Windows Update Download Cache
        var winUpdate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        groups.Add(ScanDirectory(JunkCategory.WindowsUpdateCache, "Windows Update Cache", "Downloaded installation payloads from Windows Update", winUpdate));

        return Task.FromResult<IReadOnlyList<JunkGroup>>(groups);
    }

    private static JunkGroup ScanDirectory(JunkCategory category, string title, string description, string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return new JunkGroup(category, title, description, 0, 0, []);
        }

        var filesList = new List<string>();
        long totalBytes = 0;

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    filesList.Add(file.FullName);
                    totalBytes += file.Length;
                }
                catch
                {
                    // Ignore inaccessible single files
                }
            }
        }
        catch
        {
            // Ignore access errors on top level directory
        }

        return new JunkGroup(category, title, description, totalBytes, filesList.Count, filesList);
    }

    public async Task<OperationResult<long>> CleanJunkAsync(
        IEnumerable<JunkCategory> selectedCategories,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedCategories);

        var groups = await ScanJunkAsync(cancellationToken).ConfigureAwait(false);
        var targetCategories = selectedCategories.ToHashSet();
        long reclaimedBytes = 0;

        foreach (var grp in groups.Where(g => targetCategories.Contains(g.Category)))
        {
            progress?.Report($"Cleaning {grp.Title}...");

            foreach (var filePath in grp.FilePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Safety check: Never delete a protected system path
                if (_protectedPathsPolicy.IsPathProtected(filePath, out _))
                    continue;

                try
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Exists)
                    {
                        var len = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        reclaimedBytes += len;
                    }
                }
                catch
                {
                    // Locked or in-use files are safely skipped without aborting
                }
            }
        }

        LogJunkCleaned(_logger, reclaimedBytes);
        return OperationResult.Success(reclaimedBytes);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Cleaned {Bytes} bytes of junk artifacts.")]
    private static partial void LogJunkCleaned(ILogger logger, long bytes);
}
