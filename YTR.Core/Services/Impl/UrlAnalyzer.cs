using System.Text.RegularExpressions;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Analyzes URLs to determine their source platform and extract identifiers.
/// </summary>
public sealed partial class UrlAnalyzer : IUrlAnalyzer
{
    public UrlAnalysisResult Analyze(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Empty(input);

        var trimmed = input.Trim();

        // Check if it's a bare YouTube video ID (11 chars) or playlist ID (34 chars)
        if (BareIdRegex().IsMatch(trimmed))
        {
            if (trimmed.Length == 11)
                return YouTubeVideo(input, trimmed, $"https://www.youtube.com/watch?v={trimmed}");
            if (trimmed.Length == 34)
                return YouTubePlaylist(input, trimmed, $"https://www.youtube.com/playlist?list={trimmed}");
        }

        // YouTube
        if (IsYouTube(trimmed))
            return AnalyzeYouTube(input, trimmed);

        // Reddit
        if (trimmed.StartsWith("https://reddit.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://www.reddit.com", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.Reddit);

        // Twitter/X
        if (trimmed.StartsWith("https://twitter.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://www.twitter.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://x.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://www.x.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://m.twitter.com", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.Twitter);

        // Twitch
        if (trimmed.StartsWith("https://www.twitch.tv", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://twitch.tv", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://m.twitch.tv", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.Twitch);

        // Vimeo
        if (trimmed.StartsWith("https://vimeo.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://www.vimeo.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://player.vimeo.com", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.Vimeo);

        // Instagram
        if (trimmed.StartsWith("https://instagram.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://www.instagram.com", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.Instagram);

        // TikTok
        if (trimmed.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("musical.ly", StringComparison.OrdinalIgnoreCase))
            return Platform(input, trimmed, MediaPlatform.TikTok);

        return new UrlAnalysisResult
        {
            OriginalInput = input,
            NormalizedUrl = trimmed,
            Platform = MediaPlatform.Unknown
        };
    }

    private static bool IsYouTube(string url) =>
        url.StartsWith("https://www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://youtube.com", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://youtu.be", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://m.youtube.com", StringComparison.OrdinalIgnoreCase);

    private static UrlAnalysisResult AnalyzeYouTube(string original, string url)
    {
        // youtu.be short links
        if (url.StartsWith("https://youtu.be/", StringComparison.OrdinalIgnoreCase))
        {
            var id = url[17..].Split('?', '&', '/')[0];
            if (id.Length == 11)
                return YouTubeVideo(original, id, $"https://www.youtube.com/watch?v={id}");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Platform(original, url, MediaPlatform.YouTube);

        var segments = uri.Segments.Select(s => s.TrimEnd('/')).ToArray();

        // Shorts
        if (segments.Any(s => s.Equals("shorts", StringComparison.OrdinalIgnoreCase)))
        {
            var shortsIdx = Array.FindIndex(segments, s => s.Equals("shorts", StringComparison.OrdinalIgnoreCase));
            if (shortsIdx + 1 < segments.Length && segments[shortsIdx + 1].Length == 11)
            {
                var id = segments[shortsIdx + 1];
                return YouTubeVideo(original, id, $"https://www.youtube.com/watch?v={id}", YoutubeLinkType.Short);
            }
        }

        // Playlist page
        if (segments.Any(s => s.Equals("playlist", StringComparison.OrdinalIgnoreCase)))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var listId = query["list"];
            if (!string.IsNullOrEmpty(listId))
                return YouTubePlaylist(original, listId, $"https://www.youtube.com/playlist?list={listId}");
        }

        // Watch page
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var videoId = qs["v"];
        if (!string.IsNullOrEmpty(videoId) && videoId.Length == 11)
        {
            var listId = qs["list"];
            if (!string.IsNullOrEmpty(listId))
            {
                // Video within a playlist context
                return new UrlAnalysisResult
                {
                    OriginalInput = original,
                    NormalizedUrl = $"https://www.youtube.com/watch?v={videoId}&list={listId}",
                    Platform = MediaPlatform.YouTube,
                    YouTubeLinkType = YoutubeLinkType.Video,
                    VideoId = videoId,
                    PlaylistId = listId,
                    IsPlaylist = false
                };
            }
            return YouTubeVideo(original, videoId, $"https://www.youtube.com/watch?v={videoId}");
        }

        return Platform(original, url, MediaPlatform.YouTube);
    }

    private static UrlAnalysisResult Empty(string input) => new()
    {
        OriginalInput = input,
        NormalizedUrl = string.Empty,
        Platform = MediaPlatform.Empty
    };

    private static UrlAnalysisResult Platform(string original, string url, MediaPlatform platform) => new()
    {
        OriginalInput = original,
        NormalizedUrl = url,
        Platform = platform
    };

    private static UrlAnalysisResult YouTubeVideo(string original, string videoId, string normalized, YoutubeLinkType type = YoutubeLinkType.Video) => new()
    {
        OriginalInput = original,
        NormalizedUrl = normalized,
        Platform = MediaPlatform.YouTube,
        YouTubeLinkType = type,
        VideoId = videoId
    };

    private static UrlAnalysisResult YouTubePlaylist(string original, string playlistId, string normalized) => new()
    {
        OriginalInput = original,
        NormalizedUrl = normalized,
        Platform = MediaPlatform.YouTube,
        YouTubeLinkType = YoutubeLinkType.Playlist,
        PlaylistId = playlistId,
        IsPlaylist = true
    };

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]+$")]
    private static partial Regex BareIdRegex();
}
