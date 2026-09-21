using Microsoft.Extensions.Logging;
using Remvora.Application.Cleaning;
using Remvora.Application.Stats;
using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Stats;
using Remvora.Core.Policies;

namespace Remvora.Windows.Cleaning;

/// <summary>
/// Production Windows implementation of temporary and junk file detection and removal.
/// Uses resilient enumeration and safe deletion routines that never abort on locked or inaccessible files.
/// </summary>
public sealed partial class WindowsJunkCleaner : IJunkCleaner
{
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ICleaningStatsRepository? _statsRepository;
    private readonly ILogger<WindowsJunkCleaner> _logger;

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };

    public WindowsJunkCleaner(
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<WindowsJunkCleaner> logger)
        : this(protectedPathsPolicy, null, logger)
    {
    }

    public WindowsJunkCleaner(
        IProtectedPathsPolicy protectedPathsPolicy,
        ICleaningStatsRepository? statsRepository,
        ILogger<WindowsJunkCleaner> logger)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _statsRepository = statsRepository;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<JunkGroup>> ScanJunkAsync(
        IProgress<JunkScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scanTargets = GetScanTargets();
        var groups = new List<JunkGroup>();
        int totalFoundItems = 0;
        long totalFoundBytes = 0;

        for (int i = 0; i < scanTargets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = scanTargets[i];

            double pct = (double)i / scanTargets.Count * 100;
            progress?.Report(new JunkScanProgress(target.Title, totalFoundItems, totalFoundBytes, pct, target.FolderPath));

            var group = ScanTargetDirectory(target);
            groups.Add(group);

            totalFoundItems += group.ItemCount;
            totalFoundBytes += group.TotalSizeBytes;
        }

        progress?.Report(new JunkScanProgress("Scan Complete", totalFoundItems, totalFoundBytes, 100));
        LogScanCompleted(_logger, totalFoundItems, totalFoundBytes);

        return Task.FromResult<IReadOnlyList<JunkGroup>>(groups);
    }

    private static List<JunkTargetSpec> GetScanTargets()
    {
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var targets = new List<JunkTargetSpec>
        {
            // 1. User Temp
            new(JunkCategory.UserTemp, "User Temporary Files", "Cached application session and runtime files in %TEMP%", Path.GetTempPath()),

            // 2. Windows System Temp
            new(JunkCategory.SystemTemp, "Windows System Temp", "Temporary system files in %SystemRoot%\\Temp", Path.Combine(windir, "Temp")),

            // 3. Crash & Minidumps
            new(JunkCategory.CrashDumps, "Memory & Crash Dumps", "Application error dumps and diagnostic trace logs", Path.Combine(localApp, "CrashDumps")),
            new(JunkCategory.CrashDumps, "Windows Minidumps", "Kernel diagnostic minidumps", Path.Combine(windir, "Minidump")),

            // 4. Windows Error Reporting
            new(JunkCategory.ErrorReporting, "WER Report Queue", "Pending crash reports queued for submission", Path.Combine(localApp, "Microsoft", "Windows", "WER", "ReportQueue")),
            new(JunkCategory.ErrorReporting, "WER Report Archive", "Archived crash reports and error diagnostics", Path.Combine(localApp, "Microsoft", "Windows", "WER", "ReportArchive")),

            // 5. Windows Update Download Cache
            new(JunkCategory.WindowsUpdateCache, "Windows Update Cache", "Downloaded installation payloads from Windows Update", Path.Combine(windir, "SoftwareDistribution", "Download")),

            // 6. DirectX Shader Caches
            new(JunkCategory.DirectXCache, "DirectX Shader Cache", "Precompiled GPU shader bytecode", Path.Combine(localApp, "D3DSCache")),
            new(JunkCategory.DirectXCache, "NVIDIA Shader Cache", "Cached graphics pipelines", Path.Combine(localApp, "NVIDIA", "DXCache")),
            new(JunkCategory.DirectXCache, "AMD Shader Cache", "Cached GPU shader pipelines", Path.Combine(localApp, "AMD", "DxCache")),

            // 7. Thumbnail & Icon Caches
            new(JunkCategory.ThumbnailCache, "Explorer Thumbnail Cache", "Cached preview thumbnails for images and documents", Path.Combine(localApp, "Microsoft", "Windows", "Explorer")),

            // 8. Delivery Optimization
            new(JunkCategory.DeliveryOptimization, "Delivery Optimization Cache", "Shared update distribution cache", Path.Combine(windir, "SoftwareDistribution", "DeliveryOptimization")),

            // 9. Web Browser & App Caches
            new(JunkCategory.BrowserCache, "Google Chrome Cache", "Web page, image, and HTTP cache", Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data")),
            new(JunkCategory.BrowserCache, "Google Chrome Code Cache", "V8 compiled JavaScript cache", Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "Code Cache")),
            new(JunkCategory.BrowserCache, "Microsoft Edge Cache", "Web resources and cached images", Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data")),
            new(JunkCategory.BrowserCache, "Microsoft Edge Code Cache", "Compiled script and GPU cache", Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "Code Cache")),
            new(JunkCategory.BrowserCache, "Discord Cache", "Media, emoji, and voice client cache", Path.Combine(appData, "discord", "Cache", "Cache_Data")),
            new(JunkCategory.BrowserCache, "Discord Code Cache", "Electron runtime cached bytecode", Path.Combine(appData, "discord", "Code Cache")),
            new(JunkCategory.BrowserCache, "Spotify Cache", "Cached streaming tracks and offline audio", Path.Combine(localApp, "Spotify", "Storage"))
        };

        return targets;
    }

    private static JunkGroup ScanTargetDirectory(JunkTargetSpec spec)
    {
        if (!Directory.Exists(spec.FolderPath))
        {
            return new JunkGroup(spec.Category, spec.Title, spec.Description, 0, 0, []);
        }

        var filesList = new List<string>();
        long totalBytes = 0;

        try
        {
            var dirInfo = new DirectoryInfo(spec.FolderPath);
            foreach (var file in dirInfo.EnumerateFiles("*", SafeEnumOptions))
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
            // Inaccessible root folder
        }

        return new JunkGroup(spec.Category, spec.Title, spec.Description, totalBytes, filesList.Count, filesList);
    }

    public async Task<OperationResult<long>> CleanJunkAsync(
        IEnumerable<JunkCategory> selectedCategories,
        IProgress<JunkCleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedCategories);

        var groups = await ScanJunkAsync(null, cancellationToken).ConfigureAwait(false);
        var targetCategories = selectedCategories.ToHashSet();

        var selectedGroups = groups.Where(g => targetCategories.Contains(g.Category)).ToList();
        var allFiles = selectedGroups.SelectMany(g => g.FilePaths.Select(f => (Category: g.Title, Path: f))).ToList();
        int totalFiles = allFiles.Count;

        long reclaimedBytes = 0;
        int cleanedCount = 0;

        for (int i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = allFiles[i];

            double pct = totalFiles > 0 ? (double)(i + 1) / totalFiles * 100 : 100;
            progress?.Report(new JunkCleanProgress(
                item.Category,
                Path.GetFileName(item.Path),
                cleanedCount,
                totalFiles,
                reclaimedBytes,
                pct));

            // Safety check: Never delete a protected system path
            if (_protectedPathsPolicy.IsPathProtected(item.Path, out _))
                continue;

            try
            {
                var fi = new FileInfo(item.Path);
                if (fi.Exists)
                {
                    long len = fi.Length;
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fi.Attributes = FileAttributes.Normal;
                    }

                    fi.Delete();
                    reclaimedBytes += len;
                    cleanedCount++;
                }
            }
            catch
            {
                // Locked or in-use files are skipped without aborting
            }
        }

        // Clean empty child directories within scanned folders (preserving the root folder itself)
        foreach (var target in GetScanTargets().Where(t => targetCategories.Contains(t.Category)))
        {
            if (Directory.Exists(target.FolderPath))
            {
                CleanEmptySubdirectories(target.FolderPath);
            }
        }

        // Record in lifetime statistics if repository is provided
        if (_statsRepository != null && (cleanedCount > 0 || reclaimedBytes > 0))
        {
            try
            {
                await _statsRepository.RecordEventAsync(new CleaningStatEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    CleaningCategory.SystemJunk,
                    cleanedCount,
                    reclaimedBytes,
                    $"Cleaned {cleanedCount} junk files across {selectedGroups.Count} categories."), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Non-critical metric failure
            }
        }

        LogJunkCleaned(_logger, reclaimedBytes, cleanedCount);
        return OperationResult.Success(reclaimedBytes);
    }

    private static void CleanEmptySubdirectories(string rootFolder)
    {
        try
        {
            var dir = new DirectoryInfo(rootFolder);
            foreach (var sub in dir.EnumerateDirectories("*", SafeEnumOptions))
            {
                try
                {
                    if (sub.Exists && !sub.EnumerateFileSystemInfos().Any())
                    {
                        sub.Delete(false);
                    }
                }
                catch
                {
                    // Locked or in-use subfolders skipped
                }
            }
        }
        catch
        {
            // Ignore access errors on root folder
        }
    }

    private sealed record JunkTargetSpec(JunkCategory Category, string Title, string Description, string FolderPath);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Junk scan completed. Found {Items} items ({Bytes} bytes).")]
    private static partial void LogScanCompleted(ILogger logger, int items, long bytes);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Cleaned {Bytes} bytes across {Count} junk files.")]
    private static partial void LogJunkCleaned(ILogger logger, long bytes, int count);
}
