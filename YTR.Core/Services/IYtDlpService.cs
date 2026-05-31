using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Abstraction over yt-dlp process invocation.
/// </summary>
public interface IYtDlpService
{
    /// <summary>
    /// Fetches metadata and available formats for a URL.
    /// </summary>
    Task<Result<MediaMetadata>> GetMediaInfoAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Fetches playlist metadata (flat, no format details per item).
    /// </summary>
    Task<Result<MediaMetadata>> GetPlaylistInfoAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Gets direct stream URLs for a given format string.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetFormatUrlsAsync(string url, string format, CancellationToken ct = default);

    /// <summary>
    /// Downloads using "best" format selection with optional restrictions.
    /// </summary>
    Task<Result<string>> DownloadBestAsync(
        string url,
        StreamKind streamKind,
        int maxResolution = 0,
        int maxFileSizeMb = 0,
        string? outputPath = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a specific format by format ID.
    /// </summary>
    Task<Result<string>> DownloadFormatAsync(
        string url,
        string formatId,
        StreamKind streamKind,
        string? outputPath = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default);
}
