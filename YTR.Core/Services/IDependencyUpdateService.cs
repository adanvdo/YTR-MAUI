namespace YTR.Core.Services;

/// <summary>
/// Manages updates for external dependencies (yt-dlp, FFmpeg).
/// </summary>
public interface IDependencyUpdateService
{
    Task<Result<string?>> GetLatestYtDlpVersionAsync(CancellationToken ct = default);
    Task<Result<string?>> GetLatestFfmpegVersionAsync(CancellationToken ct = default);
    Task<Result> UpdateYtDlpAsync(IProgress<double>? progress = null, CancellationToken ct = default);
    Task<Result> UpdateFfmpegAsync(IProgress<double>? progress = null, CancellationToken ct = default);
}
