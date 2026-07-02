using System.Text.Json;
using Microsoft.Extensions.Logging;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Checks for and downloads application updates from GitHub Releases.
/// Nothing is automatic — the user must explicitly trigger check and install.
/// </summary>
public sealed class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IPlatformService _platform;
    private readonly ILogger<AppUpdateService> _logger;

    private const string GitHubReleasesUrl = "https://api.github.com/repos/adanvdo/YTR-MAUI/releases";
    private const string InstallerAssetName = "YTR-Setup.exe";

    public AppUpdateService(
        IHttpClientFactory httpClientFactory,
        IPlatformService platform,
        ILogger<AppUpdateService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YTR/2.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _platform = platform;
        _logger = logger;
    }

    public async Task<Result<AppRelease?>> CheckForUpdateAsync(ReleaseChannel channel, CancellationToken ct = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();

            // For stable, we can use the /latest endpoint directly
            // For beta/alpha, we need to scan all releases
            var url = channel == ReleaseChannel.Stable
                ? $"{GitHubReleasesUrl}/latest"
                : $"{GitHubReleasesUrl}?per_page=20";

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return Result<AppRelease?>.Failure($"GitHub returned {(int)response.StatusCode} {response.StatusCode}.");

            var json = await response.Content.ReadAsStringAsync(ct);

            AppRelease? release = channel == ReleaseChannel.Stable
                ? ParseSingleRelease(json, channel)
                : FindLatestForChannel(json, channel);

            if (release is null)
                return Result<AppRelease?>.Success(null);

            // Only report an update if the remote version is newer
            if (release.Version <= currentVersion)
                return Result<AppRelease?>.Success(null);

            return Result<AppRelease?>.Success(release);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for app updates.");
            return Result<AppRelease?>.Failure($"Update check failed: {ex.Message}");
        }
    }

    public async Task<Result<string>> DownloadUpdateAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(release.DownloadUrl))
            return Result<string>.Failure("No download URL available for this release.");

        try
        {
            var updateDir = Path.Combine(_platform.AppDataDirectory, "Updates");
            Directory.CreateDirectory(updateDir);

            var filePath = Path.Combine(updateDir, InstallerAssetName);

            // Delete existing file if present
            if (File.Exists(filePath))
                File.Delete(filePath);

            using var response = await _httpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes);
            }

            progress?.Report(1.0);
            return Result<string>.Success(filePath);
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure("Download cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update.");
            return Result<string>.Failure($"Download failed: {ex.Message}");
        }
    }

    public async Task InstallUpdateAsync(string installerPath)
    {
        if (!File.Exists(installerPath))
            return;

        // Launch the downloaded installer and exit
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(startInfo);

        // Give the installer a moment to start, then exit
        await Task.Delay(500);
        Environment.Exit(0);
    }

    private AppRelease? ParseSingleRelease(string json, ReleaseChannel channel)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseReleaseElement(doc.RootElement, channel);
    }

    private AppRelease? FindLatestForChannel(string json, ReleaseChannel channel)
    {
        using var doc = JsonDocument.Parse(json);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var tagName = element.GetProperty("tag_name").GetString() ?? string.Empty;
            var releaseChannel = GetChannelFromTag(tagName);

            // For beta channel: accept beta and stable releases
            // For alpha channel: accept alpha, beta, and stable releases
            var matches = channel switch
            {
                ReleaseChannel.Beta => releaseChannel is ReleaseChannel.Beta or ReleaseChannel.Stable,
                ReleaseChannel.Alpha => true,
                _ => releaseChannel == ReleaseChannel.Stable
            };

            if (matches)
            {
                var release = ParseReleaseElement(element, releaseChannel);
                if (release is not null)
                    return release;
            }
        }

        return null;
    }

    private AppRelease? ParseReleaseElement(JsonElement element, ReleaseChannel channel)
    {
        var tagName = element.GetProperty("tag_name").GetString() ?? string.Empty;
        var version = ParseVersionFromTag(tagName);
        if (version is null)
            return null;

        // Find the installer asset
        string? downloadUrl = null;
        if (element.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        var publishedAt = element.TryGetProperty("published_at", out var pub)
            ? DateTime.TryParse(pub.GetString(), out var dt) ? dt : DateTime.MinValue
            : DateTime.MinValue;

        var releaseNotes = element.TryGetProperty("body", out var body)
            ? body.GetString()
            : null;

        return new AppRelease
        {
            TagName = tagName,
            Version = version,
            Channel = channel,
            ReleaseDate = publishedAt,
            DownloadUrl = downloadUrl,
            ReleaseNotes = releaseNotes
        };
    }

    private static ReleaseChannel GetChannelFromTag(string tag)
    {
        if (tag.Contains("-alpha", StringComparison.OrdinalIgnoreCase))
            return ReleaseChannel.Alpha;
        if (tag.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return ReleaseChannel.Beta;
        return ReleaseChannel.Stable;
    }

    /// <summary>
    /// Parses a version from a tag like "v1.2.3", "v1.2.3-beta.1", or "v1.0.0-alpha.2".
    /// </summary>
    private static Version? ParseVersionFromTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;

        // Strip leading 'v'
        var versionStr = tag.StartsWith('v') ? tag[1..] : tag;

        // Strip pre-release suffix (everything after first '-')
        var hyphenIndex = versionStr.IndexOf('-');
        if (hyphenIndex > 0)
            versionStr = versionStr[..hyphenIndex];

        return Version.TryParse(versionStr, out var version) ? version : null;
    }

    private static Version GetCurrentVersion()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        return asm?.GetName().Version ?? new Version(0, 0, 0);
    }
}
