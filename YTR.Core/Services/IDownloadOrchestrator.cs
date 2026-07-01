using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Coordinates the full download workflow: fetch formats → download → post-process → record history.
/// </summary>
public interface IDownloadOrchestrator
{
    /// <summary>
    /// Downloads with the "best" format and optional post-processing.
    /// </summary>
    Task<Result<DownloadRecord>> DownloadBestAsync(
        string url,
        StreamKind streamKind,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads a specific format pair with optional post-processing.
    /// </summary>
    Task<Result<DownloadRecord>> DownloadFormatAsync(
        string url,
        FormatPair formatPair,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads all selected items from a playlist sequentially.
    /// </summary>
    Task<Result<int>> DownloadPlaylistAsync(
        string playlistUrl,
        IReadOnlyList<PlaylistItem> selectedItems,
        StreamKind streamKind,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        IProgress<PlaylistProgress>? playlistProgress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Optional post-processing parameters for a download.
/// </summary>
public sealed record DownloadRequest
{
    public TimeSpan? SegmentStart { get; init; }
    public TimeSpan? SegmentDuration { get; init; }
    public int[]? CropValues { get; init; }
    public VideoFormat? ConvertVideo { get; init; }
    public AudioFormat? ConvertAudio { get; init; }
    public bool EmbedThumbnail { get; init; }
    public string? PlaylistFolder { get; init; }
    public string? Title { get; init; }
    public TimeSpan? MediaDuration { get; init; }
    public int MaxResolutionPixels { get; init; }
    public int MaxFileSizeMb { get; init; }
}

/// <summary>
/// Progress for playlist batch downloads.
/// </summary>
public sealed record PlaylistProgress(int Completed, int Total, string? CurrentTitle);
