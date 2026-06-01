namespace YTR.Core.Services;

/// <summary>
/// Probes media files/URLs to get stream information (dimensions, duration, codecs).
/// </summary>
public interface IMediaProbeService
{
    Task<Result<MediaProbeResult>> ProbeAsync(string pathOrUrl, CancellationToken ct = default);
}

/// <summary>
/// Result of probing a media file/URL with ffprobe.
/// </summary>
public sealed record MediaProbeResult
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Framerate { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public string? PixelFormat { get; init; }
}
