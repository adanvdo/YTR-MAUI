using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// A paired video + audio format selection for download.
/// </summary>
public sealed record FormatPair
{
    public FormatInfo? VideoFormat { get; init; }
    public FormatInfo? AudioFormat { get; init; }

    public StreamKind StreamKind
    {
        get
        {
            if (VideoFormat is not null && AudioFormat is not null)
                return StreamKind.AudioAndVideo;
            if (VideoFormat is not null)
                return StreamKind.Video;
            if (AudioFormat is not null)
                return StreamKind.Audio;
            return StreamKind.Unknown;
        }
    }

    public string FormatId
    {
        get
        {
            var ids = new List<string>(2);
            if (AudioFormat is not null) ids.Add(AudioFormat.FormatId);
            if (VideoFormat is not null) ids.Add(VideoFormat.FormatId);
            return string.Join("+", ids);
        }
    }

    public string? Extension => VideoFormat?.Extension ?? AudioFormat?.Extension;

    public TimeSpan? Duration => VideoFormat?.Duration ?? AudioFormat?.Duration;

    public int? Width => VideoFormat?.Width;
    public int? Height => VideoFormat?.Height;

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(2);
            if (VideoFormat?.Format is not null) parts.Add(VideoFormat.Format);
            if (AudioFormat?.Format is not null) parts.Add(AudioFormat.Format);
            return string.Join(" + ", parts);
        }
    }

    public bool IsValid => VideoFormat is not null || (AudioFormat is not null && !string.IsNullOrEmpty(AudioFormat.Extension));
}
