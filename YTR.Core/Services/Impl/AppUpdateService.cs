using System.Text.Json;
using Microsoft.Extensions.Logging;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Checks for and downloads application updates from the jamgalactic.com API.
/// Nothing is automatic — the user must explicitly trigger check and install.
/// </summary>
public sealed class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IPlatformService _platform;
    private readonly ILogger<AppUpdateService> _logger;

    private const string ApiBaseUrl = "https://www.jamgalactic.com/api";

    public AppUpdateService(
        IHttpClientFactory httpClientFactory,
        IPlatformService platform,
        ILogger<AppUpdateService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YTR/2.0");
        _platform = platform;
        _logger = logger;
    }

    public async Task<Result<AppRelease?>> CheckForUpdateAsync(ReleaseChannel channel, CancellationToken ct = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/getlatest?channel={(int)channel}";
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return Result<AppRelease?>.Failure($"Server returned {response.StatusCode}.");

            var content = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(content) || content == "Unknown")
                return Result<AppRelease?>.Success(null); // No update available

            var release = JsonSerializer.Deserialize<ApiReleaseResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release is null)
                return Result<AppRelease?>.Success(null);

            return Result<AppRelease?>.Success(new AppRelease
            {
                ReleaseId = release.ReleaseID,
                Channel = (ReleaseChannel)release.Channel,
                Version = new Version(release.Major, release.Minor, release.Build, release.Revision),
                ReleaseDate = release.ReleaseDate,
                Active = release.Active,
                DownloadUrl = release.x64Url ?? release.x86Url,
                ManualInstallRequired = release.ManualInstallRequired
            });
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

            var fileName = Path.GetFileName(new Uri(release.DownloadUrl).LocalPath);
            var filePath = Path.Combine(updateDir, fileName);

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

    public async Task<Result> InstallUpdateAsync(string packagePath, CancellationToken ct = default)
    {
        if (!File.Exists(packagePath))
            return Result.Failure("Update package not found.");

        try
        {
            // Launch the updater executable and exit the current app
            var updaterPath = Path.Combine(AppContext.BaseDirectory, "YTR_Updater.exe");
            if (!File.Exists(updaterPath))
            {
                // Fallback: just open the package location for manual install
                await _platform.OpenFolderAsync(Path.GetDirectoryName(packagePath)!);
                return Result.Success();
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = $"\"{packagePath}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch updater.");
            return Result.Failure($"Install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Matches the JSON shape returned by the jamgalactic.com API.
    /// </summary>
    private sealed class ApiReleaseResponse
    {
        public Guid ReleaseID { get; set; }
        public int Channel { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Build { get; set; }
        public int Revision { get; set; }
        public DateTime ReleaseDate { get; set; }
        public bool Active { get; set; }
        public string? x86Url { get; set; }
        public string? x64Url { get; set; }
        public bool ManualInstallRequired { get; set; }
    }
}
