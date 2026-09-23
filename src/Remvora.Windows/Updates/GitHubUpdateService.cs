using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Remvora.Application.Updates;
using Remvora.Contracts;
using Remvora.Contracts.Updates;
using Remvora.Core.Domain.Results;

namespace Remvora.Windows.Updates;

/// <summary>
/// Production update service for checking, downloading, verifying, and staging differential releases from GitHub.
/// Dynamically falls back across GitHub Releases API, raw manifests, local distributions, and private repositories.
/// </summary>
public sealed partial class GitHubUpdateService : IUpdateService
{
    private const string DefaultManifestUrl = "https://raw.githubusercontent.com/MuhammadFrz/Remvora/master/releases/manifest.json";
    private static readonly HttpClient RedirectHttpClient = CreateRedirectHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient CreateRedirectHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Remvora-App/{GetCurrentAppVersion()} (Windows; +https://github.com/MuhammadFrz/Remvora)");
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

    /// <summary>
    /// Checks for available updates by querying remote GitHub endpoints and falling back gracefully.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersionStr = GetCurrentAppVersion();
        LogCheckingUpdates(_logger, currentVersionStr, _manifestUrl);

        try
        {
            UpdateManifest? manifest = null;

            // 1. Try GitHub Releases latest API
            try
            {
                const string releasesApiUrl = "https://api.github.com/repos/MuhammadFrz/Remvora/releases/latest";
                using var apiResponse = await SendWithRedirectHandlingAsync(releasesApiUrl, "application/vnd.github.v3+json", cancellationToken).ConfigureAwait(false);
                if (apiResponse.IsSuccessStatusCode)
                {
                    var releaseDoc = await apiResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
                    manifest = await TryParseManifestFromGitHubReleaseAsync(releaseDoc, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogRemoteManifestFetchFailed(_logger, ex);
            }

            // 2. Fallback: GitHub Releases list (gets newest release even if not marked latest)
            if (manifest == null)
            {
                try
                {
                    const string releasesListUrl = "https://api.github.com/repos/MuhammadFrz/Remvora/releases";
                    using var listResponse = await SendWithRedirectHandlingAsync(releasesListUrl, "application/vnd.github.v3+json", cancellationToken).ConfigureAwait(false);
                    if (listResponse.IsSuccessStatusCode)
                    {
                        var releaseArray = await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (releaseArray.ValueKind == JsonValueKind.Array && releaseArray.GetArrayLength() > 0)
                        {
                            var firstRelease = releaseArray[0];
                            manifest = await TryParseManifestFromGitHubReleaseAsync(firstRelease, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogRemoteManifestFetchFailed(_logger, ex);
                }
            }

            // 3. Fallback: GitHub Contents API for releases/manifest.json (works on private repos with token)
            if (manifest == null)
            {
                try
                {
                    const string contentsUrl = "https://api.github.com/repos/MuhammadFrz/Remvora/contents/releases/manifest.json";
                    using var contentsResponse = await SendWithRedirectHandlingAsync(contentsUrl, "application/vnd.github.raw+json", cancellationToken).ConfigureAwait(false);
                    if (contentsResponse.IsSuccessStatusCode)
                    {
                        manifest = await contentsResponse.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogRemoteManifestFetchFailed(_logger, ex);
                }
            }

            // 4. Fallback: Remote raw manifest on master branch
            if (manifest == null)
            {
                try
                {
                    using var rawResponse = await SendWithRedirectHandlingAsync(_manifestUrl, null, cancellationToken).ConfigureAwait(false);
                    if (rawResponse.IsSuccessStatusCode)
                    {
                        manifest = await rawResponse.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogRemoteManifestFetchFailed(_logger, ex);
                }
            }

            // 5. Fallback: Remote raw manifest on main branch
            if (manifest == null)
            {
                try
                {
                    const string mainRawUrl = "https://raw.githubusercontent.com/MuhammadFrz/Remvora/main/releases/manifest.json";
                    using var mainRawResponse = await SendWithRedirectHandlingAsync(mainRawUrl, null, cancellationToken).ConfigureAwait(false);
                    if (mainRawResponse.IsSuccessStatusCode)
                    {
                        manifest = await mainRawResponse.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogRemoteManifestFetchFailed(_logger, ex);
                }
            }

            // 6. Fallback: Local manifest file from candidate release directories
            if (manifest == null)
            {
                var localManifestPath = ResolveLocalManifestPath();
                if (!string.IsNullOrEmpty(localManifestPath) && File.Exists(localManifestPath))
                {
                    using var stream = File.OpenRead(localManifestPath);
                    manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            // 7. Fallback: Synthesize manifest from locally found release packages
            if (manifest == null)
            {
                manifest = DiscoverManifestFromLocalPackages();
            }

            if (manifest == null)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersionStr,
                    ErrorMessage = "Could not retrieve update manifest from server or local repository. If repository is private, enter your GitHub Token in Settings."
                };
            }

            // Determine current architecture package
            var arch = GetCurrentArchitecture();
            if (!manifest.Packages.TryGetValue(arch, out var packageInfo) ||
                (packageInfo.FullPackage == null && packageInfo.DeltaPackage == null))
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = currentVersionStr,
                    LatestVersion = manifest.Version,
                    ErrorMessage = $"No release package found for architecture: {arch}"
                };
            }

            var currentVer = ParseVersion(currentVersionStr);
            var latestVer = ParseVersion(manifest.Version);

            var isDeltaAvailable = false;
            UpdateArchiveInfo targetArchive = packageInfo.FullPackage ?? packageInfo.DeltaPackage!;

            if (packageInfo.DeltaPackage != null && packageInfo.FullPackage != null)
            {
                if (string.IsNullOrWhiteSpace(manifest.MinDeltaVersion) ||
                    currentVer >= ParseVersion(manifest.MinDeltaVersion))
                {
                    isDeltaAvailable = true;
                    targetArchive = packageInfo.DeltaPackage;
                }
            }

            var isNewer = latestVer > currentVer;

            if (!isNewer)
            {
                LogAppUpToDate(_logger, currentVersionStr, manifest.Version);
            }
            else
            {
                LogUpdateFound(_logger, manifest.Version, isDeltaAvailable ? "Delta" : "Full", targetArchive.SizeBytes);
            }

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isNewer,
                CurrentVersion = currentVersionStr,
                LatestVersion = manifest.Version,
                ReleaseDate = manifest.ReleaseDate,
                ReleaseNotes = manifest.ReleaseNotes,
                IsDeltaAvailable = isDeltaAvailable,
                DownloadSizeBytes = targetArchive.SizeBytes,
                DownloadUrl = targetArchive.Url,
                PackageSha256 = targetArchive.Sha256,
                AssetId = targetArchive.AssetId
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

    /// <summary>
    /// Downloads, verifies, and stages the update archive for installation.
    /// </summary>
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

            // 1. Check local package candidates across all search paths
            string? localZip = null;
            if (File.Exists(update.DownloadUrl))
            {
                localZip = update.DownloadUrl;
            }
            else
            {
                var targetFileName = Path.GetFileName(new Uri(update.DownloadUrl, UriKind.RelativeOrAbsolute).LocalPath);
                localZip = FindLocalPackage(targetFileName);
            }

            if (!string.IsNullOrEmpty(localZip) && File.Exists(localZip))
            {
                File.Copy(localZip, tempZipPath, overwrite: true);
                progress?.Report(new UpdateDownloadProgress(update.DownloadSizeBytes, update.DownloadSizeBytes));
            }
            else
            {
                // Download from HTTP
                string downloadEndpoint = update.DownloadUrl;
                string? accept = null;
                if (update.AssetId.HasValue && !string.IsNullOrWhiteSpace(ResolveGitHubToken()))
                {
                    downloadEndpoint = $"https://api.github.com/repos/MuhammadFrz/Remvora/releases/assets/{update.AssetId.Value}";
                    accept = "application/octet-stream";
                }

                using var response = await SendWithRedirectHandlingAsync(downloadEndpoint, accept, cancellationToken).ConfigureAwait(false);
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
            var expectedSha256 = update.PackageSha256;
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                var targetFileName = Path.GetFileName(new Uri(update.DownloadUrl, UriKind.RelativeOrAbsolute).LocalPath);
                expectedSha256 = FindSha256InChecksumsFile(targetFileName);
            }

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return OperationResult.Failure(ErrorCode.ValidationFailed, "Downloaded update package rejected: missing cryptographic SHA-256 checksum in release manifest or checksums file.");
            }

            LogVerifyingChecksum(_logger, expectedSha256);
            using var sha256 = SHA256.Create();
            await using (var stream = File.OpenRead(tempZipPath))
            {
                var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
                var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (!string.Equals(actualHash, expectedSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    LogChecksumMismatch(_logger, expectedSha256, actualHash);
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

    /// <summary>
    /// Spawns the launcher with update arguments and gracefully terminates this application instance.
    /// </summary>
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

    /// <summary>
    /// Dynamically resolves GitHub token from environment variables, AppData, or local .env configuration.
    /// </summary>
    public static string? ResolveGitHubToken()
    {
        // 1. Environment variables
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN")
            ?? Environment.GetEnvironmentVariable("REMVORA_GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GITHUB_PAT");

        if (!string.IsNullOrWhiteSpace(token)) return token.Trim();

        // 2. Local AppData token file
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDataToken = Path.Combine(localAppData, "Remvora", "github_token.txt");
        if (File.Exists(appDataToken))
        {
            try
            {
                var content = File.ReadAllText(appDataToken).Trim();
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }
            catch
            {
            }
        }

        // 3. User profile candidate files
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidateEnvFiles =
        [
            Path.Combine(userProfile, ".remvora", "token.txt"),
            Path.Combine(userProfile, ".remvora", "github_token.txt"),
            Path.Combine(userProfile, "Desktop", "gh", ".env"),
            Path.Combine(userProfile, ".env")
        ];

        foreach (var file in candidateEnvFiles)
        {
            if (File.Exists(file))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith('#')) continue;
                        if (trimmed.StartsWith("GITHUB_TOKEN=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("GH_TOKEN=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("REMVORA_GITHUB_TOKEN=", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = trimmed.Split('=', 2)[1].Trim(' ', '"', '\'');
                            if (!string.IsNullOrWhiteSpace(val)) return val;
                        }
                        else if (trimmed.Length > 20 && (trimmed.StartsWith("ghp_", StringComparison.Ordinal) || trimmed.StartsWith("github_pat_", StringComparison.Ordinal)))
                        {
                            return trimmed;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static async Task<HttpResponseMessage> SendWithRedirectHandlingAsync(string url, string? acceptHeader, CancellationToken cancellationToken)
    {
        var currentUrl = url;
        for (int i = 0; i < 5; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            var isGitHubHost = currentUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase);
            var token = ResolveGitHubToken();
            if (isGitHubHost && !string.IsNullOrWhiteSpace(token))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            if (!string.IsNullOrWhiteSpace(acceptHeader))
            {
                req.Headers.Accept.ParseAdd(acceptHeader);
            }

            var response = await RedirectHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location != null)
                {
                    currentUrl = location.IsAbsoluteUri ? location.AbsoluteUri : new Uri(new Uri(currentUrl), location).AbsoluteUri;
                    response.Dispose();
                    continue;
                }
            }
            return response;
        }
        throw new InvalidOperationException("Too many HTTP redirects encountered while fetching update asset.");
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

        var ver = assembly.GetName().Version;
        if (ver != null && ver.Major > 0)
        {
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }

        return "1.2.2";
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

    public static IEnumerable<string> GetCandidateReleaseDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        dirs.Add(Path.Combine(baseDir, "releases"));
        dirs.Add(baseDir);

        var parent = Directory.GetParent(baseDir)?.FullName;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(parent); i++)
        {
            dirs.Add(Path.Combine(parent, "releases"));
            dirs.Add(parent);
            parent = Directory.GetParent(parent)?.FullName;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(Path.Combine(localAppData, "Programs", "Remvora", "releases"));
        dirs.Add(Path.Combine(localAppData, "Programs", "Remvora"));
        dirs.Add(Path.Combine(localAppData, "Remvora", "releases"));
        dirs.Add(Path.Combine(localAppData, "Remvora"));

        dirs.Add(@"D:\Github\Remvora\releases");
        dirs.Add(@"C:\Github\Remvora\releases");

        var envDir = Environment.GetEnvironmentVariable("REMVORA_RELEASES_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            dirs.Add(envDir);
        }

        return dirs.Where(Directory.Exists);
    }

    private static string? ResolveLocalManifestPath()
    {
        foreach (var dir in GetCandidateReleaseDirectories())
        {
            var testPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(testPath)) return testPath;
        }

        return null;
    }

    private static string? FindLocalPackage(string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(targetFileName)) return null;

        foreach (var dir in GetCandidateReleaseDirectories())
        {
            var path = Path.Combine(dir, targetFileName);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static string? FindSha256InChecksumsFile(string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(targetFileName)) return null;

        foreach (var dir in GetCandidateReleaseDirectories())
        {
            var checksumFile = Path.Combine(dir, "checksums-sha256.txt");
            if (File.Exists(checksumFile))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(checksumFile))
                    {
                        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var hash = parts[0].Trim();
                            var file = parts[1].Trim().TrimStart('*');
                            if (string.Equals(file, targetFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                return hash;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static Dictionary<string, string> FindChecksumsMapLocally()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in GetCandidateReleaseDirectories())
        {
            var checksumFile = Path.Combine(dir, "checksums-sha256.txt");
            if (File.Exists(checksumFile))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(checksumFile))
                    {
                        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var hash = parts[0].Trim();
                            var file = parts[1].Trim().TrimStart('*');
                            map[file] = hash;
                        }
                    }
                }
                catch
                {
                }
            }
        }
        return map;
    }

    private static UpdateManifest? DiscoverManifestFromLocalPackages()
    {
        var candidateFiles = new List<string>();
        foreach (var dir in GetCandidateReleaseDirectories())
        {
            if (Directory.Exists(dir))
            {
                candidateFiles.AddRange(Directory.GetFiles(dir, "Remvora-v*.zip"));
            }
        }

        if (candidateFiles.Count == 0) return null;

        var byVersion = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in candidateFiles)
        {
            var name = Path.GetFileName(file);
            var match = Regex.Match(name, @"Remvora-v([\d\.]+)-(win-[a-z0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var ver = match.Groups[1].Value;
                if (!byVersion.TryGetValue(ver, out var list))
                {
                    list = [];
                    byVersion[ver] = list;
                }
                list.Add(file);
            }
        }

        if (byVersion.Count == 0) return null;

        var highestVerStr = byVersion.Keys.OrderByDescending(ParseVersion).First();
        var matchingFiles = byVersion[highestVerStr];
        var checksumMap = FindChecksumsMapLocally();

        var packages = new Dictionary<string, UpdatePackageInfo>(StringComparer.OrdinalIgnoreCase);
        string[] archs = ["win-x64", "win-arm64", "win-x86"];

        foreach (var arch in archs)
        {
            UpdateArchiveInfo? fullPkg = null;
            UpdateArchiveInfo? deltaPkg = null;

            foreach (var file in matchingFiles)
            {
                var name = Path.GetFileName(file);
                if (name.Contains(arch, StringComparison.OrdinalIgnoreCase))
                {
                    var size = new FileInfo(file).Length;
                    var sha = checksumMap.TryGetValue(name, out var sVal) ? sVal : string.Empty;
                    if (name.EndsWith("-delta.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        deltaPkg = new UpdateArchiveInfo { Url = file, Sha256 = sha, SizeBytes = size };
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        fullPkg = new UpdateArchiveInfo { Url = file, Sha256 = sha, SizeBytes = size };
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

        if (packages.Count == 0) return null;

        return new UpdateManifest
        {
            Version = highestVerStr,
            ReleaseDate = DateTimeOffset.UtcNow.ToString("O"),
            ReleaseNotes = "Discovered from local distribution packages.",
            MinDeltaVersion = "1.0.0",
            Packages = packages
        };
    }

    private static async Task<UpdateManifest?> TryParseManifestFromGitHubReleaseAsync(JsonElement root, CancellationToken cancellationToken)
    {
        try
        {
            if (!root.TryGetProperty("tag_name", out var tagElem)) return null;
            var tag = tagElem.GetString()?.TrimStart('v', 'V') ?? "1.0.0";
            var dateStr = root.TryGetProperty("published_at", out var pubElem) ? pubElem.GetString() ?? DateTimeOffset.UtcNow.ToString("O") : DateTimeOffset.UtcNow.ToString("O");
            var notes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? string.Empty : string.Empty;

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                var assets = assetsElem.EnumerateArray().ToList();

                // 1. If manifest.json is directly uploaded as an asset, fetch and parse it!
                var manifestAsset = assets.FirstOrDefault(a =>
                    a.TryGetProperty("name", out var n) &&
                    string.Equals(n.GetString(), "manifest.json", StringComparison.OrdinalIgnoreCase));

                if (manifestAsset.ValueKind == JsonValueKind.Object &&
                    manifestAsset.TryGetProperty("id", out var mAssetIdProp))
                {
                    try
                    {
                        var mAssetId = mAssetIdProp.GetInt64();
                        var manifestUrl = $"https://api.github.com/repos/MuhammadFrz/Remvora/releases/assets/{mAssetId}";
                        using var mResponse = await SendWithRedirectHandlingAsync(manifestUrl, "application/octet-stream", cancellationToken).ConfigureAwait(false);
                        if (mResponse.IsSuccessStatusCode)
                        {
                            var parsedManifest = await mResponse.Content.ReadFromJsonAsync<UpdateManifest>(JsonOptions, cancellationToken).ConfigureAwait(false);
                            if (parsedManifest != null) return parsedManifest;
                        }
                    }
                    catch
                    {
                    }
                }

                // 2. Fetch checksums-sha256.txt if available
                var checksumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var checksumAsset = assets.FirstOrDefault(a =>
                    a.TryGetProperty("name", out var n) &&
                    string.Equals(n.GetString(), "checksums-sha256.txt", StringComparison.OrdinalIgnoreCase));

                if (checksumAsset.ValueKind == JsonValueKind.Object &&
                    checksumAsset.TryGetProperty("id", out var csAssetIdProp))
                {
                    try
                    {
                        var csAssetId = csAssetIdProp.GetInt64();
                        var csUrl = $"https://api.github.com/repos/MuhammadFrz/Remvora/releases/assets/{csAssetId}";
                        using var csResponse = await SendWithRedirectHandlingAsync(csUrl, "application/octet-stream", cancellationToken).ConfigureAwait(false);
                        if (csResponse.IsSuccessStatusCode)
                        {
                            var csContent = await csResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            foreach (var line in csContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                            {
                                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2)
                                {
                                    var hash = parts[0].Trim();
                                    var file = parts[1].Trim().TrimStart('*');
                                    checksumMap[file] = hash;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // If remote checksum fetch was empty, try reading local checksums-sha256.txt
                if (checksumMap.Count == 0)
                {
                    var localCs = FindChecksumsMapLocally();
                    foreach (var kvp in localCs) checksumMap[kvp.Key] = kvp.Value;
                }

                var packages = new Dictionary<string, UpdatePackageInfo>(StringComparer.OrdinalIgnoreCase);
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
                        var assetId = asset.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : (long?)null;
                        var sha = checksumMap.TryGetValue(name, out var sVal) ? sVal : string.Empty;

                        if (name.Contains(arch, StringComparison.OrdinalIgnoreCase))
                        {
                            if (name.EndsWith("-delta.zip", StringComparison.OrdinalIgnoreCase))
                            {
                                deltaPkg = new UpdateArchiveInfo { Url = url, Sha256 = sha, SizeBytes = size, AssetId = assetId };
                            }
                            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                fullPkg = new UpdateArchiveInfo { Url = url, Sha256 = sha, SizeBytes = size, AssetId = assetId };
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

                if (packages.Count > 0)
                {
                    return new UpdateManifest
                    {
                        Version = tag,
                        ReleaseDate = dateStr,
                        ReleaseNotes = notes,
                        MinDeltaVersion = "1.0.0",
                        Packages = packages
                    };
                }
            }

            return null;
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
