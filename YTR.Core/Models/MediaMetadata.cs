using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Metadata about a media item (video/audio) fetched from a platform.
/// </summary>
public sealed record MediaMetadata
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// URL of a thumbnail whose aspect ratio matches the video's aspect ratio.
    /// May be a data URI if the thumbnail was cropped locally.
    /// </summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// The original remote thumbnail URL before any local cropping.
    /// Used for downloading the full-resolution image for album art embedding.
    /// </summary>
    public string? OriginalThumbnailUrl { get; init; }

    /// <summary>
    /// True if the ThumbnailUrl has been verified to match the video's aspect ratio.
    /// When false, the thumbnail may need to be cropped before use in the visual crop tool.
    /// </summary>
    public bool ThumbnailAspectRatioMatched { get; init; }

    /// <summary>
    /// All available thumbnails for this media item.
    /// </summary>
    public IReadOnlyList<ThumbnailInfo> Thumbnails { get; init; } = [];

    public string? Uploader { get; init; }
    public DateTime? UploadDate { get; init; }
    public MediaPlatform Platform { get; init; }
    public IReadOnlyList<FormatInfo> Formats { get; init; } = [];
    public bool IsPlaylist { get; init; }
    public IReadOnlyList<PlaylistItem>? PlaylistItems { get; init; }
}
