using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Checks for and installs updates to yt-dlp and FFmpeg.
/// Nothing is automatic — the user must click buttons to check and update.
/// 
/// When the install directory is write-protected (e.g. Program Files), the service
/// downloads to a temp location and then uses <see cref="IElevationService"/> to
/// copy files with admin privileges (triggering a UAC prompt).
/// </summary>
public sealed class DependencyUpdateService : IDependencyUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IPlatformService _platform;
    private readonly ISettingsService _settings;
    private readonly IElevationService? _elevation;
    private readonly ILogger<DependencyUpdateService> _logger;

    private const string YtDlpTagsUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/tags";
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FfmpegVersionUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z.ver";
    private const string FfmpegDownloadUrl = "https://github.com/GyanD/codexffmpeg/releases/latest/download/ffmpeg-{0}-essentials_build.zip";

    private string latestFfmpegVersion = string.Empty;

    public DependencyUpdateService(
        IHttpClientFactory httpClientFactory,
        IPlatformService platform,
        ISettingsService settings,
        ILogger<DependencyUpdateService> logger,
        IElevationService? elevation = null)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YTR/2.0");
        _platform = platform;
        _settings = settings;
        _logger = logger;
        _elevation = elevation;
    }

    public async Task<Result<string?>> GetLatestYtDlpVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(YtDlpTagsUrl, ct);
            if (!response.IsSuccessStatusCode)
                return Result<string?>.Failure($"GitHub API returned {response.StatusCode}.");

            var content = await response.Content.ReadAsStringAsync(ct);
            var tags = JsonSerializer.Deserialize<List<GithubTag>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var latest = tags?.FirstOrDefault()?.Name;
            return Result<string?>.Success(latest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check yt-dlp version.");
            return Result<string?>.Failure($"Version check failed: {ex.Message}");
        }
    }

    public async Task<Result<string?>> GetLatestFfmpegVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(FfmpegVersionUrl, ct);
            if (!response.IsSuccessStatusCode)
                return Result<string?>.Failure($"FFmpeg version check returned {response.StatusCode}.");

            var version = (await response.Content.ReadAsStringAsync(ct)).Trim();
            this.latestFfmpegVersion = version;
            return Result<string?>.Success(version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check FFmpeg version.");
            return Result<string?>.Failure($"Version check failed: {ex.Message}");
        }
    }

    public async Task<Result> UpdateYtDlpAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var targetPath = _platform.GetResourcePath("yt-dlp");
            var tempDir = Path.Combine(_platform.AppDataDirectory, "Temp");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, "yt-dlp.exe");

            // Download to temp (always user-writable)
            using var response = await _httpClient.GetAsync(YtDlpDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                        progress?.Report((double)totalRead / totalBytes * 0.9);
                }
            }

            progress?.Report(0.92);

            // Move to target — elevate if needed
            var moveResult = await MoveFileWithElevationAsync(tempPath, targetPath, ct);
            if (moveResult.IsFailure)
                return moveResult;

            // Update stored version
            var versionResult = await GetLatestYtDlpVersionAsync(ct);
            if (versionResult.IsSuccess && versionResult.Value is not null)
            {
                _settings.Updates.YtDlpLocalVersion = versionResult.Value;
                await _settings.SaveAsync(ct);
            }

            progress?.Report(1.0);
            _logger.LogInformation("yt-dlp updated successfully.");
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Update cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update yt-dlp.");
            return Result.Failure($"yt-dlp update failed: {ex.Message}");
        }
    }

    public async Task<Result> UpdateFfmpegAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(this.latestFfmpegVersion))
            {
                await GetLatestFfmpegVersionAsync(ct);
            }

            var tempDir = Path.Combine(_platform.AppDataDirectory, "Temp");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, "ffmpeg-release-essentials.zip");

            // Download zip to temp (always user-writable)
            using var response = await _httpClient.GetAsync(string.Format(FfmpegDownloadUrl, latestFfmpegVersion), HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                        progress?.Report((double)totalRead / totalBytes * 0.75); // 75% for download
                }
            }

            // Extract ffmpeg.exe and ffprobe.exe to temp directory
            progress?.Report(0.80);
            var ffmpegTempPath = Path.Combine(tempDir, "ffmpeg.exe");
            var ffprobeTempPath = Path.Combine(tempDir, "ffprobe.exe");

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var ffmpegEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.Contains("bin", StringComparison.OrdinalIgnoreCase));

                var ffprobeEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.Contains("bin", StringComparison.OrdinalIgnoreCase));

                if (ffmpegEntry is not null)
                {
                    if (File.Exists(ffmpegTempPath)) File.Delete(ffmpegTempPath);
                    ffmpegEntry.ExtractToFile(ffmpegTempPath);
                }

                if (ffprobeEntry is not null)
                {
                    if (File.Exists(ffprobeTempPath)) File.Delete(ffprobeTempPath);
                    ffprobeEntry.ExtractToFile(ffprobeTempPath);
                }
            }

            progress?.Report(0.85);

            // Move to target locations — elevate if needed
            var ffmpegTarget = _platform.GetResourcePath("ffmpeg");
            var ffprobeTarget = _platform.GetResourcePath("ffprobe");

            var operations = new List<(string Source, string Destination)>();
            if (File.Exists(ffmpegTempPath))
                operations.Add((ffmpegTempPath, ffmpegTarget));
            if (File.Exists(ffprobeTempPath))
                operations.Add((ffprobeTempPath, ffprobeTarget));

            var moveResult = await MoveFilesWithElevationAsync(operations, ct);
            if (moveResult.IsFailure)
                return moveResult;

            progress?.Report(0.95);

            // Cleanup zip
            try { File.Delete(zipPath); } catch { /* best effort */ }

            // Update stored version
            var versionResult = await GetLatestFfmpegVersionAsync(ct);
            if (versionResult.IsSuccess && versionResult.Value is not null)
            {
                _settings.Updates.FfmpegLocalVersion = versionResult.Value;
                await _settings.SaveAsync(ct);
            }

            progress?.Report(1.0);
            _logger.LogInformation("FFmpeg updated successfully.");
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Update cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update FFmpeg.");
            return Result.Failure($"FFmpeg update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a single file to the target path, using elevation if the target is write-protected.
    /// </summary>
    private async Task<Result> MoveFileWithElevationAsync(string source, string destination, CancellationToken ct)
    {
        return await MoveFilesWithElevationAsync([(source, destination)], ct);
    }

    /// <summary>
    /// Moves files to their target paths, using elevation if any target is write-protected.
    /// </summary>
    private async Task<Result> MoveFilesWithElevationAsync(IReadOnlyList<(string Source, string Destination)> operations, CancellationToken ct)
    {
        if (operations.Count == 0)
            return Result.Success();

        // Check if we need elevation by testing write access to the first target's directory
        var firstTargetDir = Path.GetDirectoryName(operations[0].Destination);
        var needsElevation = _elevation is not null
            && !string.IsNullOrEmpty(firstTargetDir)
            && !_elevation.CanWriteTo(firstTargetDir);

        if (!needsElevation)
        {
            // Try direct file operations
            try
            {
                foreach (var (source, destination) in operations)
                {
                    var destDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    if (File.Exists(destination))
                        File.Delete(destination);
                    File.Move(source, destination);
                }
                return Result.Success();
            }
            catch (UnauthorizedAccessException) when (_elevation is not null)
            {
                // Fall through to elevated path
                _logger.LogWarning("Direct file write failed due to permissions, attempting elevated copy.");
            }
        }

        // Need elevation
        if (_elevation is null)
        {
            return Result.Failure(
                "Cannot write to the install directory. " +
                "Try running YTR as administrator, or reinstall to a non-protected location.");
        }

        _logger.LogInformation("Requesting elevated permissions to update dependencies.");
        return await _elevation.ElevatedCopyAsync(operations, ct);
    }

    private sealed class GithubTag
    {
        public string? Name { get; set; }
    }
}
