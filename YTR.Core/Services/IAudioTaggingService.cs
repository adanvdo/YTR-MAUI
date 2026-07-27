namespace YTR.Core.Services;

/// <summary>
/// Embeds metadata (artist, title, album art) into audio files using ID3/tag formats.
/// </summary>
public interface IAudioTaggingService
{
    /// <summary>
    /// Applies metadata tags and optional thumbnail to an audio file.
    /// Returns the final file path (may differ from input if renamed).
    /// </summary>
    Task<string> TagAsync(AudioTagRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the given file extension supports embedded metadata.
    /// </summary>
    bool SupportsTagging(string filePath);
}

/// <summary>
/// Describes the metadata to embed into an audio file.
/// </summary>
public sealed record AudioTagRequest
{
    /// <summary>Path to the downloaded audio file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Track title (typically the video title or music track name).</summary>
    public string? Title { get; init; }

    /// <summary>Artist name (typically the uploader or resolved from metadata search).</summary>
    public string? Artist { get; init; }

    /// <summary>Album name (resolved from metadata search).</summary>
    public string? Album { get; init; }

    /// <summary>Release year (resolved from metadata search).</summary>
    public int? Year { get; init; }

    /// <summary>URL to a thumbnail image to embed as album art.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>The video's aspect ratio (width/height) for cropping the thumbnail to match.</summary>
    public double? VideoAspectRatio { get; init; }

    /// <summary>Whether to embed the thumbnail as album art.</summary>
    public bool EmbedThumbnail { get; init; }

    /// <summary>Whether to embed metadata tags (title, artist, etc).</summary>
    public bool EmbedMetadata { get; init; }

    /// <summary>Whether to rename the file to [Artist - Title] format.</summary>
    public bool UseArtistTrackFilename { get; init; }
}
