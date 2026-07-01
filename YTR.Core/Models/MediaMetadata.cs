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
    /// Null if no matching thumbnail is available (visual crop tool should not be shown).
    /// </summary>
    public string? ThumbnailUrl { get; init; }

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
