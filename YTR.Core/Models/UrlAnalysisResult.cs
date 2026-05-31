using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Result of analyzing a URL to determine its platform and type.
/// </summary>
public sealed record UrlAnalysisResult
{
    public required string OriginalInput { get; init; }
    public required string NormalizedUrl { get; init; }
    public MediaPlatform Platform { get; init; }
    public YoutubeLinkType YouTubeLinkType { get; init; } = YoutubeLinkType.Invalid;
    public bool IsValid => Platform != MediaPlatform.Empty && Platform != MediaPlatform.Unknown;
    public bool IsPlaylist { get; init; }
    public string? VideoId { get; init; }
    public string? PlaylistId { get; init; }
}
