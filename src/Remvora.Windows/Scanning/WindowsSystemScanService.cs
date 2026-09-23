using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Scanning;
using Remvora.Application.Stats;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Scanning;
using Remvora.Core.Domain.Stats;
using Remvora.Core.Policies;

namespace Remvora.Windows.Scanning;

/// <summary>
/// Deep system scanner inspecting Windows redundant files, app caches, orphaned leftover remnants, and broken app issues.
/// </summary>
public sealed partial class WindowsSystemScanService : ISystemScanService
{
    private readonly IApplicationRepository _appRepository;
    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ICleaningStatsRepository _statsRepository;
    private readonly ILogger<WindowsSystemScanService> _logger;

    private static readonly string[] ProtectedAppFolderNames =
    [
        "Microsoft", "Windows", "Common Files", "Internet Explorer", "Packages",
        "WindowsApps", "SystemApps", "Remvora", "dotnet", "Git", "NVIDIA", "Intel", "AMD"
    ];

    public WindowsSystemScanService(
        IApplicationRepository appRepository,
        IProtectedPathsPolicy protectedPathsPolicy,
        ICleaningStatsRepository statsRepository,
        ILogger<WindowsSystemScanService> logger)
    {
        _appRepository = appRepository ?? throw new ArgumentNullException(nameof(appRepository));
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _statsRepository = statsRepository ?? throw new ArgumentNullException(nameof(statsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ScanGroup>> ScanSystemAsync(
        IEnumerable<ScanCategory>? categoriesToScan = null,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LogScanStarted(_logger);
        var groups = new List<ScanGroup>();

        var catList = categoriesToScan?.ToList();
        var shouldScanAll = catList == null || catList.Count == 0;
        var selectedCats = shouldScanAll ? null : catList!.ToHashSet();

        int totalItems = 0;
        long totalBytes = 0;

        // 1. Redundant Windows Files
        if (shouldScanAll || selectedCats!.Contains(ScanCategory.WindowsRedundant))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressReport("Scanning Redundant Windows Files", "Temporary folders and error dumps", totalItems, totalBytes, 10));
            var redundantItems = await Task.Run(ScanWindowsRedundantFiles, cancellationToken).ConfigureAwait(false);
            totalItems += redundantItems.Count;
            totalBytes += redundantItems.Sum(i => i.SizeBytes);
            groups.Add(new ScanGroup
            {
                Category = ScanCategory.WindowsRedundant,
                Title = "Redundant System Clutter",
                Description = "Temporary files, memory error dumps, Windows Update payloads, and crash logs safe for removal.",
                Items = redundantItems
            });
        }

        // 2. Application & Browser Caches
        if (shouldScanAll || selectedCats!.Contains(ScanCategory.AppCache))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressReport("Scanning Application Caches", "Browser and client caches", totalItems, totalBytes, 35));
            var cacheItems = await Task.Run(ScanApplicationCaches, cancellationToken).ConfigureAwait(false);
            totalItems += cacheItems.Count;
            totalBytes += cacheItems.Sum(i => i.SizeBytes);
            groups.Add(new ScanGroup
            {
                Category = ScanCategory.AppCache,
                Title = "Application & Browser Caches",
                Description = "Discardable shader, GPU, web, and streaming media caches (Chrome, Edge, Discord, Spotify, etc.).",
                Items = cacheItems
            });
        }

        // 3. Orphaned Leftovers from Prior Uninstalls
        if (shouldScanAll || selectedCats!.Contains(ScanCategory.OrphanedLeftovers))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressReport("Scanning Orphaned Remnants", "Analyzing %AppData% & ProgramData", totalItems, totalBytes, 65));
            var leftoverItems = await ScanOrphanedLeftoversAsync(cancellationToken).ConfigureAwait(false);
            totalItems += leftoverItems.Count;
            totalBytes += leftoverItems.Sum(i => i.SizeBytes);
            groups.Add(new ScanGroup
            {
                Category = ScanCategory.OrphanedLeftovers,
                Title = "Orphaned App Remnants",
                Description = "Folders and broken shortcuts lingering from software that was previously uninstalled.",
                Items = leftoverItems
            });
        }

        // 4. Broken App Issues & Damaged Registrations
        if (shouldScanAll || selectedCats!.Contains(ScanCategory.AppIssues))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressReport("Analyzing Application Health", "Checking registry and broken paths", totalItems, totalBytes, 90));
            var issueItems = await Task.Run(ScanAppIssues, cancellationToken).ConfigureAwait(false);
            totalItems += issueItems.Count;
            totalBytes += issueItems.Sum(i => i.SizeBytes);
            groups.Add(new ScanGroup
            {
                Category = ScanCategory.AppIssues,
                Title = "Damaged App Registrations",
                Description = "Orphaned registry uninstall entries and broken shortcuts pointing to deleted executables.",
                Items = issueItems
            });
        }

        progress?.Report(new ScanProgressReport("Scan Complete", "Ready for review", totalItems, totalBytes, 100));
        LogScanCompleted(_logger, totalItems, totalBytes);

        return groups;
    }

    public async Task<ScanCleanupResult> CleanSelectedItemsAsync(
        IEnumerable<ScanItem> items,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var itemList = items.Where(i => i.IsSelected).ToList();
        var errors = new List<string>();

        int removed = 0;
        long reclaimedBytes = 0;

        LogCleanStarted(_logger, itemList.Count);

        return await Task.Run(async () =>
        {
            for (int i = 0; i < itemList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = itemList[i];

                progress?.Report(new ScanProgressReport(
                    $"Purging ({i + 1}/{itemList.Count})",
                    item.Title,
                    removed,
                    reclaimedBytes,
                    (int)((double)(i + 1) / itemList.Count * 100)));

                try
                {
                    if (item.Category == ScanCategory.AppIssues && item.TargetPath.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_protectedPathsPolicy.IsRegistryKeyProtected(item.TargetPath, out var regReason))
                        {
                            errors.Add($"Protected registry key skipped: {item.TargetPath} ({regReason})");
                            continue;
                        }

                        // Registry issue
                        CleanRegistryEntry(item.TargetPath);
                        removed++;
                        continue;
                    }

                    if (_protectedPathsPolicy.IsPathProtected(item.TargetPath, out var reason))
                    {
                        errors.Add($"Protected path skipped: {item.TargetPath} ({reason})");
                        continue;
                    }

                    if (File.Exists(item.TargetPath))
                    {
                        var size = new FileInfo(item.TargetPath).Length;
                        File.Delete(item.TargetPath);
                        removed++;
                        reclaimedBytes += size;
                    }
                    else if (Directory.Exists(item.TargetPath))
                    {
                        bool shouldDeleteFolderItself = item.Category == ScanCategory.OrphanedLeftovers;
                        var (subRemoved, subBytes) = CleanDirectoryContents(item.TargetPath, shouldDeleteFolderItself, _protectedPathsPolicy);
                        if (subRemoved > 0 || subBytes > 0)
                        {
                            removed += subRemoved;
                            reclaimedBytes += subBytes;
                        }
                        else if (shouldDeleteFolderItself)
                        {
                            try
                            {
                                Directory.Delete(item.TargetPath, recursive: true);
                                removed++;
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogCleanItemFailed(_logger, ex, item.TargetPath);
                    errors.Add($"{Path.GetFileName(item.TargetPath)}: {ex.Message}");
                }
            }

            // Record lifetime statistics
            if (removed > 0 || reclaimedBytes > 0)
            {
                await _statsRepository.RecordEventAsync(new CleaningStatEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    CleaningCategory.SystemScan,
                    removed,
                    reclaimedBytes,
                    $"Deep System Scan cleaned {removed} items across categories."), cancellationToken).ConfigureAwait(false);
            }

            LogCleanFinished(_logger, removed, reclaimedBytes, errors.Count);
            return new ScanCleanupResult(removed, reclaimedBytes, errors);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static List<ScanItem> ScanWindowsRedundantFiles()
    {
        var list = new List<ScanItem>();

        // 1. User Temp
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "User Temporary Files", "Cached session and scratch files in %TEMP%", Path.GetTempPath());

        // 2. Windows Temp
        var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "Windows System Temp", "Operating system temporary data in %WINDIR%\\Temp", winTemp);

        // 3. Crash Dumps
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var crashDumps = Path.Combine(localApp, "CrashDumps");
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "Crash & Memory Dumps", "Application crash logs and diagnostic minidumps", crashDumps);

        // 4. Windows Error Reporting
        var werQueue = Path.Combine(localApp, "Microsoft", "Windows", "WER", "ReportQueue");
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "WER Error Queues", "Pending Windows Error Reporting submissions", werQueue);

        var werArchive = Path.Combine(localApp, "Microsoft", "Windows", "WER", "ReportArchive");
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "WER Report Archive", "Historical crash diagnostics archived by Windows", werArchive);

        // 5. Windows Update Cache
        var winUpdate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        AddDirectoryScanItem(list, ScanCategory.WindowsRedundant, "Windows Update Download Cache", "Leftover downloaded patches from Windows Update", winUpdate);

        return list;
    }

    private static List<ScanItem> ScanApplicationCaches()
    {
        var list = new List<ScanItem>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Chrome
        var chromeCache = Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Google Chrome Cache", "Web page, image, and HTTP cache", chromeCache);

        var chromeCode = Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Google Chrome Code Cache", "V8 compiled JavaScript and WebAssembly cache", chromeCode);

        // Edge
        var edgeCache = Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Microsoft Edge Cache", "Web resources and cached images", edgeCache);

        var edgeCode = Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Microsoft Edge Code Cache", "Compiled script and GPU cache", edgeCode);

        // Discord
        var discordCache = Path.Combine(roaming, "discord", "Cache", "Cache_Data");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Discord Cache", "Media, emoji, and voice client cache", discordCache);

        var discordCode = Path.Combine(roaming, "discord", "Code Cache");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Discord Code Cache", "Electron runtime cached bytecode", discordCode);

        // Spotify
        var spotifyData = Path.Combine(local, "Spotify", "Data");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Spotify Local Cache", "Downloaded stream cache and album art", spotifyData);

        var spotifyStorage = Path.Combine(local, "Spotify", "Storage");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Spotify Storage Cache", "Temporary stream buffer files", spotifyStorage);

        // VS Code
        var vscodeCache = Path.Combine(roaming, "Code", "Cache", "Cache_Data");
        AddDirectoryScanItem(list, ScanCategory.AppCache, "Visual Studio Code Cache", "Editor session data and extension webview cache", vscodeCache);

        return list;
    }

    private async Task<List<ScanItem>> ScanOrphanedLeftoversAsync(CancellationToken cancellationToken)
    {
        var list = new List<ScanItem>();

        try
        {
            // Get all installed app names for correlation
            var installedApps = await _appRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var installedNames = installedApps.Select(a => a.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            ScanDirectoryForOrphans(list, roaming, installedNames, "Roaming AppData");
            ScanDirectoryForOrphans(list, local, installedNames, "Local AppData");
            ScanDirectoryForOrphans(list, programData, installedNames, "ProgramData");

            // Check Desktop shortcuts for dead targets
            ScanBrokenShortcuts(list, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            ScanBrokenShortcuts(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        }
        catch (Exception ex)
        {
            LogScanOrphansFailed(_logger, ex);
        }

        return list;
    }

    private static void ScanDirectoryForOrphans(List<ScanItem> list, string rootFolder, HashSet<string> installedNames, string locationName)
    {
        if (!Directory.Exists(rootFolder)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootFolder))
            {
                var folderName = Path.GetFileName(dir);
                if (IsProtectedFolderName(folderName)) continue;

                // Check if any installed app contains this folder name or vice versa
                bool hasMatch = installedNames.Any(name =>
                    name.Contains(folderName, StringComparison.OrdinalIgnoreCase) ||
                    folderName.Contains(name, StringComparison.OrdinalIgnoreCase));

                if (!hasMatch)
                {
                    // Check if modified over 14 days ago to avoid scanning newly unpacked portables
                    var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                    if (DateTime.UtcNow - lastWrite > TimeSpan.FromDays(14))
                    {
                        long size = CalculateDirectorySize(dir);
                        if (size > 512) // Only non-empty
                        {
                            list.Add(new ScanItem
                            {
                                Category = ScanCategory.OrphanedLeftovers,
                                Title = $"Orphaned: {folderName}",
                                Description = $"No corresponding installed application found in {locationName}.",
                                TargetPath = dir,
                                SizeBytes = size,
                                ExtraInfo = $"Last modified: {lastWrite:yyyy-MM-dd}"
                            });
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore restricted directories
        }
    }

    private static void ScanBrokenShortcuts(List<ScanItem> list, string folder)
    {
        if (!Directory.Exists(folder)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                // In Windows, if shortcut target is invalid or missing, flag it
                // Minimal heuristic: read .lnk contents or flag dead shortcuts
                var fi = new FileInfo(file);
                if (fi.Length < 1024 * 1024)
                {
                    // Verify if target executable exists
                    var targetPath = ResolveShortcutTarget(file);
                    if (!string.IsNullOrWhiteSpace(targetPath) && !File.Exists(targetPath) && !Directory.Exists(targetPath))
                    {
                        list.Add(new ScanItem
                        {
                            Category = ScanCategory.OrphanedLeftovers,
                            Title = $"Dead Shortcut: {Path.GetFileNameWithoutExtension(file)}",
                            Description = $"Points to missing target: '{targetPath}'",
                            TargetPath = file,
                            SizeBytes = fi.Length
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignored
        }
    }

    private static List<ScanItem> ScanAppIssues()
    {
        var list = new List<ScanItem>();

        // Check HKLM & HKCU Uninstall registry keys for orphaned entries
        ScanUninstallKey(list, Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU");
        ScanUninstallKey(list, Microsoft.Win32.Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM");
        ScanUninstallKey(list, Microsoft.Win32.Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM (32-bit)");

        return list;
    }

    private static void ScanUninstallKey(List<ScanItem> list, RegistryKey root, string subKeyPath, string hiveName)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key == null) return;

            foreach (var name in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(name);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    var installLoc = subKey.GetValue("InstallLocation") as string;
                    var uninstallStr = subKey.GetValue("UninstallString") as string;

                    // If both InstallLocation and UninstallString point to non-existent folders/files
                    bool installLocMissing = !string.IsNullOrWhiteSpace(installLoc) && !Directory.Exists(installLoc);
                    bool uninstallStrMissing = false;

                    if (!string.IsNullOrWhiteSpace(uninstallStr) && !uninstallStr.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
                    {
                        var exePath = ExtractExePath(uninstallStr);
                        if (!string.IsNullOrWhiteSpace(exePath) && !File.Exists(exePath))
                        {
                            uninstallStrMissing = true;
                        }
                    }

                    if (installLocMissing && uninstallStrMissing)
                    {
                        list.Add(new ScanItem
                        {
                            Category = ScanCategory.AppIssues,
                            Title = $"Broken Registration: {displayName}",
                            Description = $"Install path '{installLoc}' and uninstaller executable no longer exist on disk.",
                            TargetPath = $@"{hiveName}\{subKeyPath}\{name}",
                            SizeBytes = 0,
                            IsRemovable = true
                        });
                    }
                }
                catch
                {
                    // Ignored
                }
            }
        }
        catch
        {
            // Ignored
        }
    }

    private static void CleanRegistryEntry(string fullRegistryPath)
    {
        var slash = fullRegistryPath.IndexOf('\\', StringComparison.Ordinal);
        if (slash <= 0) return;

        var hiveStr = fullRegistryPath[..slash];
        var keyPath = fullRegistryPath[(slash + 1)..];

        var lastSlash = keyPath.LastIndexOf('\\');
        if (lastSlash <= 0) return;

        var parentKey = keyPath[..lastSlash];
        var subKeyName = keyPath[(lastSlash + 1)..];

        var root = hiveStr.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
            ? Microsoft.Win32.Registry.CurrentUser
            : Microsoft.Win32.Registry.LocalMachine;

        using var parent = root.OpenSubKey(parentKey, writable: true);
        parent?.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
    }

    private static void AddDirectoryScanItem(List<ScanItem> list, ScanCategory category, string title, string description, string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        long size = CalculateDirectorySize(folderPath);
        if (size > 0)
        {
            list.Add(new ScanItem
            {
                Category = category,
                Title = title,
                Description = description,
                TargetPath = folderPath,
                SizeBytes = size
            });
        }
    }

    private static long CalculateDirectorySize(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return 0;
        try
        {
            var dir = new DirectoryInfo(folderPath);
            long bytes = 0;
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { bytes += file.Length; } catch { }
            }
            return bytes;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsProtectedFolderName(string name)
    {
        return ProtectedAppFolderNames.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        try
        {
            // Reading basic shortcut target by inspecting header
            byte[] bytes = File.ReadAllBytes(shortcutPath);
            if (bytes.Length < 76 || bytes[0] != 0x4C) return null;

            // Simple heuristic to extract ASCII path if present in shortcut
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            var match = System.Text.RegularExpressions.Regex.Match(text, @"[A-Za-z]:\\[A-Za-z0-9_\-\\\s\.]+\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractExePath(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('\"'))
        {
            var endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1) return trimmed[1..endQuote];
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static (int Removed, long ReclaimedBytes) CleanDirectoryContents(
        string folderPath,
        bool deleteFolderItself,
        IProtectedPathsPolicy protectedPathsPolicy)
    {
        int removed = 0;
        long reclaimedBytes = 0;

        if (!Directory.Exists(folderPath))
            return (0, 0);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            var dirInfo = new DirectoryInfo(folderPath);

            // 1. Clean child files safely
            foreach (var file in dirInfo.EnumerateFiles("*", options))
            {
                if (protectedPathsPolicy.IsPathProtected(file.FullName, out _))
                    continue;

                try
                {
                    long len = file.Length;
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        file.Attributes = FileAttributes.Normal;
                    }

                    file.Delete();
                    removed++;
                    reclaimedBytes += len;
                }
                catch
                {
                    // In-use or locked files skipped
                }
            }

            // 2. Clean empty child directories
            try
            {
                foreach (var sub in dirInfo.EnumerateDirectories("*", options))
                {
                    try
                    {
                        if (sub.Exists && !sub.EnumerateFileSystemInfos().Any())
                        {
                            sub.Delete(false);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 3. If requested and safe, delete the root folder itself
            if (deleteFolderItself)
            {
                try
                {
                    if (!dirInfo.EnumerateFileSystemInfos().Any())
                    {
                        dirInfo.Delete(false);
                    }
                }
                catch { }
            }
        }
        catch { }

        return (removed, reclaimedBytes);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Beginning deep system scan across all categories")]
    private static partial void LogScanStarted(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Deep system scan completed. Found {ItemsCount} items, total {TotalBytes} bytes")]
    private static partial void LogScanCompleted(ILogger logger, int itemsCount, long totalBytes);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Starting cleanup of {Count} selected scan items")]
    private static partial void LogCleanStarted(ILogger logger, int count);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to clean scan item '{Path}'")]
    private static partial void LogCleanItemFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Cleanup pass finished: {Removed} items removed, {Bytes} bytes reclaimed, {Errors} errors")]
    private static partial void LogCleanFinished(ILogger logger, int removed, long bytes, int errors);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Error during orphaned leftovers inspection")]
    private static partial void LogScanOrphansFailed(ILogger logger, Exception ex);
}
