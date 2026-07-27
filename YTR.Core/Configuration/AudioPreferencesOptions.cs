namespace YTR.Core.Configuration;

/// <summary>
/// Settings for audio download metadata, tagging, and thumbnail embedding.
/// </summary>
public sealed class AudioPreferencesOptions
{
    /// <summary>
    /// Embed available metadata in the audio file (artist, track name, etc).
    /// </summary>
    public bool EmbedMetadata { get; set; } = true;

    /// <summary>
    /// Save audio downloads using the available artist name and track name as the filename.
    /// </summary>
    public bool UseArtistTrackFilename { get; set; }

    /// <summary>
    /// When downloading audio from a video stream, use the video thumbnail as the embedded album art.
    /// </summary>
    public bool UseVideoThumbnailAsAlbumArt { get; set; } = true;
}
