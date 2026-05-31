using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// A persisted record of a completed download.
/// </summary>
public sealed class DownloadRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public MediaPlatform Platform { get; set; }
    public StreamKind StreamKind { get; set; }
    public DateTime DownloadedAt { get; set; }
    public required string FilePath { get; set; }
    public string? Format { get; set; }

    // Playlist context
    public bool InSubFolder { get; set; }
    public string? PlaylistTitle { get; set; }
    public string? PlaylistUrl { get; set; }

    // Post-processing settings used
    public TimeSpan? SegmentStart { get; set; }
    public TimeSpan? SegmentDuration { get; set; }
    public string? CropValues { get; set; }
    public VideoFormat? VideoConversion { get; set; }
    public AudioFormat? AudioConversion { get; set; }
    public Resolution? MaxResolution { get; set; }
    public int? MaxFileSizeMb { get; set; }
    public SegmentMode SegmentMode { get; set; }
}
