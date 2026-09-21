using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Remvora.Application.Updates;
using Remvora.Contracts;
using Remvora.Contracts.Updates;
using Remvora.Core.Domain.Results;

namespace Remvora.Windows.Updates;

/// <summary>
/// Production update service for checking, downloading, verifying, and staging differential releases from GitHub.
/// </summary>
public sealed partial class GitHubUpdateService : IUpdateService
{
    private const string DefaultManifestUrl = "https://raw.githubusercontent.com/MuhammadFrz/Remvora/master/releases/manifest.json";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Remvora-App/1.1.0 (Windows; +https://github.com/MuhammadFrz/Remvora)");
        return client;
    }

    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly string _manifestUrl;

    public GitHubUpdateService(ILogger<GitHubUpdateService> logger)
        : this(logger, DefaultManifestUrl)
    {
    }

    public GitHubUpdateService(ILogger<GitHubUpdateService> logger, string manifestUrl)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? DefaultManifestUrl : manifestUrl;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersionStr = GetCurrentAppVersion();
        LogCheckingUpdates(_logger, currentVersionStr, _manifestUrl);

        try
        {
            UpdateManifest? manifest = null;

            // 1. Try remote raw manifest first
            try
            {
                using var response = await HttpClient.GetAsync(_manifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogRemoteManifestFetchFailed(_logger, ex);
            }

            // 2. Fallback to GitHub Releases API if raw manifest is not yet merged/available
            if (manifest == null)
            {
                try
                {
                    const string releasesApiUrl = "https://api.github.com/repos/MuhammadFrz/Remvora/releases/latest";
                    using var apiResponse = await HttpClient.GetAsync(releasesApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        var releaseDoc = await apiResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
                        manifest = TryParseManifestFromGitHubRelease(releaseDoc);
                    }
                }
                catch (Exception ex)
                {
                    LogRemoteManifestFetchFailed(_logger, ex);
                }
            }

            // 3. Fallback to local manifest if developing or offline
            if (manifest == null)
            {
                var localManifestPath = ResolveLocalManifestPath();
                if (File.Exists(localManifestPath))
                {
                    using var stream = File.OpenRead(localManifestPath);
                    manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            if (manifest == null)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersionStr,
                    ErrorMessage = "Could not retrieve update manifest from server or local repository."
                };
            }

            var currentVer = ParseVersion(currentVersionStr);
            var latestVer = ParseVersion(manifest.Version);

            if (latestVer <= currentVer)
            {
                LogAppUpToDate(_logger, currentVersionStr, manifest.Version);
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersionStr,
                    LatestVersion = manifest.Version,
                    ReleaseDate = manifest.ReleaseDate,
                    ReleaseNotes = manifest.ReleaseNotes
                };
            }

            // Determine current architecture package
            var arch = GetCurrentArchitecture();
            if (!manifest.Packages.TryGetValue(arch, out var packageInfo))
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersionStr,
                    LatestVersion = manifest.Version,
                    ErrorMessage = $"No release package found for architecture: {arch}"
                };
            }

            // Check if delta is eligible
            var isDeltaAvailable = false;
            UpdateArchiveInfo targetArchive = packageInfo.FullPackage;

            if (packageInfo.DeltaPackage != null)
            {
                if (string.IsNullOrWhiteSpace(manifest.MinDeltaVersion) ||
                    currentVer >= ParseVersion(manifest.MinDeltaVersion))
                {
                    isDeltaAvailable = true;
                    targetArchive = packageInfo.DeltaPackage;
                }
            }

            LogUpdateFound(_logger, manifest.Version, isDeltaAvailable ? "Delta" : "Full", targetArchive.SizeBytes);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                CurrentVersion = currentVersionStr,
                LatestVersion = manifest.Version,
                ReleaseDate = manifest.ReleaseDate,
                ReleaseNotes = manifest.ReleaseNotes,
                IsDeltaAvailable = isDeltaAvailable,
                DownloadSizeBytes = targetArchive.SizeBytes,
                DownloadUrl = targetArchive.Url,
                PackageSha256 = targetArchive.Sha256
            };
        }
        catch (Exception ex)
        {
            LogCheckUpdatesFailed(_logger, ex);
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = currentVersionStr,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OperationResult> DownloadAndStageUpdateAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return OperationResult.Failure(ErrorCode.ValidationFailed, "Download URL is invalid.");
        }

        var stagingDir = GetStagingDirectory();
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"Remvora_update_{Guid.NewGuid():N}.zip");

        try
        {
            LogStartingDownload(_logger, update.DownloadUrl, update.DownloadSizeBytes);

            // 1. Download file with progress
            if (File.Exists(update.DownloadUrl)) // Local file fallback
            {
                File.Copy(update.DownloadUrl, tempZipPath, overwrite: true);
                progress?.Report(new UpdateDownloadProgress(update.DownloadSizeBytes, update.DownloadSizeBytes));
            }
            else
            {
                using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? update.DownloadSizeBytes;
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;
                    progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes));
                }
            }

            // 2. Verify SHA-256 integrity
            if (!string.IsNullOrWhiteSpace(update.PackageSha256))
            {
                LogVerifyingChecksum(_logger, update.PackageSha256);
                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(tempZipPath);
                var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
                var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (!string.Equals(actualHash, update.PackageSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    LogChecksumMismatch(_logger, update.PackageSha256, actualHash);
                    return OperationResult.Failure(ErrorCode.ValidationFailed, "Downloaded update package checksum does not match manifest. File may be corrupted.");
                }
            }

            // 3. Unpack into staging directory
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, recursive: true);
            }
            Directory.CreateDirectory(stagingDir);

            LogExtractingUpdate(_logger, stagingDir);
            ZipFile.ExtractToDirectory(tempZipPath, stagingDir, overwriteFiles: true);

            // 4. Write pending update descriptor
            var markerFile = Path.Combine(stagingDir, "update.pending");
            await File.WriteAllTextAsync(markerFile, $"version={update.LatestVersion}\r\ndate={DateTimeOffset.UtcNow:O}", cancellationToken).ConfigureAwait(false);

            LogUpdateStaged(_logger, update.LatestVersion);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            LogDownloadOrStageFailed(_logger, ex);
            return OperationResult.Failure(ErrorCode.OperationFailed, $"Failed to download or stage update: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
            catch
            {
                // Ignored
            }
        }
    }

    public void ApplyUpdateAndRestart()
    {
        var launcherPath = ResolveLauncherPath();
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException($"Launcher binary not found at '{launcherPath}'. Cannot apply update.");
        }

        LogLaunchingUpdateAndExiting(_logger, launcherPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            Arguments = "--apply-update",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)
        };

        Process.Start(startInfo);
        Environment.Exit(0);
    }

    private static string GetCurrentAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIdx = infoVer.IndexOf('+', StringComparison.Ordinal);
            return plusIdx > 0 ? infoVer[..plusIdx] : infoVer;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private static Version ParseVersion(string version)
    {
        var clean = version.TrimStart('v', 'V');
        var dash = clean.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0) clean = clean[..dash];

        return Version.TryParse(clean, out var v) ? v : new Version(1, 0, 0);
    }

    private static string GetCurrentArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => "win-x64"
        };
    }

    private static string GetStagingDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        // If app running inside 'app' subfolder:
        var parentDir = Directory.GetParent(baseDir)?.FullName;
        if (!string.IsNullOrEmpty(parentDir) && Path.GetFileName(baseDir).Equals("app", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(parentDir, "updates", "staging");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Remvora", "updates", "staging");
    }

    private static string ResolveLauncherPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var parentDir = Directory.GetParent(baseDir)?.FullName;

        if (!string.IsNullOrEmpty(parentDir))
        {
            var parentLauncher = Path.Combine(parentDir, "Remvora.exe");
            if (File.Exists(parentLauncher)) return parentLauncher;
        }

        var directLauncher = Path.Combine(baseDir, "Remvora.exe");
        if (File.Exists(directLauncher)) return directLauncher;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installedLauncher = Path.Combine(localAppData, "Programs", "Remvora", "Remvora.exe");
        if (File.Exists(installedLauncher)) return installedLauncher;

        return Path.Combine(baseDir, "Remvora.exe");
    }

    private static string ResolveLocalManifestPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var probe = Path.Combine(baseDir, "releases", "manifest.json");
        if (File.Exists(probe)) return probe;

        var probeDirect = Path.Combine(baseDir, "manifest.json");
        if (File.Exists(probeDirect)) return probeDirect;

        var parent = Directory.GetParent(baseDir)?.FullName;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(parent); i++)
        {
            var testPath = Path.Combine(parent, "releases", "manifest.json");
            if (File.Exists(testPath)) return testPath;

            var testDirect = Path.Combine(parent, "manifest.json");
            if (File.Exists(testDirect)) return testDirect;

            parent = Directory.GetParent(parent)?.FullName;
        }

        return probe;
    }

    private static UpdateManifest? TryParseManifestFromGitHubRelease(System.Text.Json.JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("tag_name", out var tagElem)) return null;
            var tag = tagElem.GetString()?.TrimStart('v', 'V') ?? "1.0.0";
            var dateStr = root.TryGetProperty("published_at", out var pubElem) ? pubElem.GetString() ?? DateTimeOffset.UtcNow.ToString("O") : DateTimeOffset.UtcNow.ToString("O");
            var notes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? string.Empty : string.Empty;

            var packages = new Dictionary<string, UpdatePackageInfo>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var assets = assetsElem.EnumerateArray().ToList();
                string[] archs = ["win-x64", "win-arm64", "win-x86"];

                foreach (var arch in archs)
                {
                    UpdateArchiveInfo? fullPkg = null;
                    UpdateArchiveInfo? deltaPkg = null;

                    foreach (var asset in assets)
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;

                        if (name.Contains(arch, StringComparison.OrdinalIgnoreCase))
                        {
                            if (name.EndsWith("-delta.zip", StringComparison.OrdinalIgnoreCase))
                            {
                                deltaPkg = new UpdateArchiveInfo { Url = url, Sha256 = string.Empty, SizeBytes = size };
                            }
                            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                fullPkg = new UpdateArchiveInfo { Url = url, Sha256 = string.Empty, SizeBytes = size };
                            }
                        }
                    }

                    if (fullPkg != null)
                    {
                        packages[arch] = new UpdatePackageInfo
                        {
                            FullPackage = fullPkg,
                            DeltaPackage = deltaPkg
                        };
                    }
                }
            }

            return new UpdateManifest
            {
                Version = tag,
                ReleaseDate = dateStr,
                ReleaseNotes = notes,
                MinDeltaVersion = "1.0.0",
                Packages = packages
            };
        }
        catch
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Checking for updates. Current version: {CurrentVersion}, Manifest URL: {ManifestUrl}")]
    private static partial void LogCheckingUpdates(ILogger logger, string currentVersion, string manifestUrl);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to fetch remote update manifest")]
    private static partial void LogRemoteManifestFetchFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Application is up to date (Current: {CurrentVersion}, Latest: {LatestVersion})")]
    private static partial void LogAppUpToDate(ILogger logger, string currentVersion, string latestVersion);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Update found: {LatestVersion} ({PackageType}, {SizeBytes} bytes)")]
    private static partial void LogUpdateFound(ILogger logger, string latestVersion, string packageType, long sizeBytes);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Update check failed")]
    private static partial void LogCheckUpdatesFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Starting update package download: {DownloadUrl} ({SizeBytes} bytes)")]
    private static partial void LogStartingDownload(ILogger logger, string downloadUrl, long sizeBytes);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Verifying package cryptographic checksum (Expected: {ExpectedSha256})")]
    private static partial void LogVerifyingChecksum(ILogger logger, string expectedSha256);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Package checksum mismatch! Expected: {Expected}, Actual: {Actual}")]
    private static partial void LogChecksumMismatch(ILogger logger, string expected, string actual);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Extracting update archive to staging: {StagingDir}")]
    private static partial void LogExtractingUpdate(ILogger logger, string stagingDir);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Update {Version} staged successfully")]
    private static partial void LogUpdateStaged(ILogger logger, string version);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Failed to download or stage update")]
    private static partial void LogDownloadOrStageFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Spawning launcher for atomic update application: {LauncherPath}")]
    private static partial void LogLaunchingUpdateAndExiting(ILogger logger, string launcherPath);
}
