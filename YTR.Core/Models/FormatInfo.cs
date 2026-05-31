using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Represents a single media format/stream as reported by yt-dlp.
/// </summary>
public sealed record FormatInfo
{
    public required string FormatId { get; init; }
    public string? Format { get; init; }
    public string? FormatNote { get; init; }
    public string? Extension { get; init; }
    public string? Url { get; init; }

    // Video properties
    public int? Width { get; init; }
    public int? Height { get; init; }
    public float? FrameRate { get; init; }
    public string? VideoCodec { get; init; }
    public double? VideoBitrate { get; init; }

    // Audio properties
    public string? AudioCodec { get; init; }
    public double? AudioBitrate { get; init; }
    public double? AudioSamplingRate { get; init; }

    // Metadata
    public long? FileSize { get; init; }
    public long? ApproximateFileSize { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? Protocol { get; init; }
    public string? ContainerFormat { get; init; }

    public StreamKind StreamKind
    {
        get
        {
            bool hasVideo = !string.IsNullOrEmpty(VideoCodec) && VideoCodec != "none";
            bool hasAudio = !string.IsNullOrEmpty(AudioCodec) && AudioCodec != "none";

            return (hasVideo, hasAudio) switch
            {
                (true, true) => StreamKind.AudioAndVideo,
                (true, false) => StreamKind.Video,
                (false, true) => StreamKind.Audio,
                _ => StreamKind.Unknown
            };
        }
    }
}
