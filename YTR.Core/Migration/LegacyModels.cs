using System.Text.Json.Serialization;

namespace YTR.Core.Migration;

/// <summary>
/// DTOs matching the old YT-RED JSON schema for deserialization during migration.
/// These are internal and only used by the migration service.
/// </summary>

internal sealed class LegacyAppSettings
{
    [JsonPropertyName("General")]
    public LegacyGeneralSettings? General { get; set; }

    [JsonPropertyName("Layout")]
    public LegacyLayoutSettings? Layout { get; set; }

    [JsonPropertyName("Advanced")]
    public LegacyAdvancedSettings? Advanced { get; set; }
}

internal sealed class LegacyGeneralSettings
{
    [JsonPropertyName("enforce_restrictions")]
    public bool EnforceRestrictions { get; set; }

    [JsonPropertyName("best_max_res")]
    public int MaxResolutionBest { get; set; } // enum int: 0=SD, 1=HD720p, 2=HD1080p, 3=UHD2160p, 4=ANY

    [JsonPropertyName("best_max_size")]
    public int MaxFilesizeBest { get; set; }

    [JsonPropertyName("history_enabled")]
    public bool EnableDownloadHistory { get; set; } = true;

    [JsonPropertyName("history_age")]
    public int HistoryAge { get; set; } = 30;

    [JsonPropertyName("auto_open")]
    public bool AutomaticallyOpenDownloadLocation { get; set; } = true;

    [JsonPropertyName("use_title_filename")]
    public bool UseTitleAsFileName { get; set; }

    [JsonPropertyName("video_dl_path")]
    public string? VideoDownloadPath { get; set; }

    [JsonPropertyName("audio_dl_path")]
    public string? AudioDownloadPath { get; set; }

    [JsonPropertyName("playlist_folders")]
    public bool CreateFolderForPlaylists { get; set; } = true;

    [JsonPropertyName("yt-dlp_local_version")]
    public string? YtdlpLocalVersion { get; set; }

    [JsonPropertyName("ffmpeg_local_version")]
    public string? FfmpegLocalVersion { get; set; }
}

internal sealed class LegacyLayoutSettings
{
    [JsonPropertyName("format_mode")]
    public int FormatMode { get; set; } // 0=Preset, 1=Custom

    [JsonPropertyName("segment_control_mode")]
    public int SegmentControlMode { get; set; } // 0=Duration, 1=EndTime
}

internal sealed class LegacyAdvancedSettings
{
    [JsonPropertyName("release_channel")]
    public int Channel { get; set; } // 0=Stable, 1=Beta, 2=Alpha

    [JsonPropertyName("fetch_metadata")]
    public bool GetMissingMetadata { get; set; } = true;

    [JsonPropertyName("always_convert")]
    public bool AlwaysConvertToPreferredFormat { get; set; } = true;

    [JsonPropertyName("preferred_video_format")]
    public int PreferredVideoFormat { get; set; } // old enum: 0=MP4, 1=WEBM, 2=FLV, 3=MKV, 4=OGG, 5=UNSPECIFIED, 6=GIF

    [JsonPropertyName("preferred_audio_format")]
    public int PreferredAudioFormat { get; set; } // old enum: 0=MP3, 1=M4A, 2=AAC, 3=OGG, 4=WAV, 5=FLAC, 6=OPUS, 7=VORBIS, 8=UNSPECIFIED

    [JsonPropertyName("verbose_output")]
    public bool VerboseOutput { get; set; }
}

internal sealed class LegacyDownloadLog
{
    [JsonPropertyName("id")]
    public Guid? DownloadID { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("in_subfolder")]
    public bool InSubFolder { get; set; }

    [JsonPropertyName("playlist_title")]
    public string? PlaylistTitle { get; set; }

    [JsonPropertyName("playlist_url")]
    public string? PlaylistUrl { get; set; }

    [JsonPropertyName("dl_type")]
    public int DownloadType { get; set; } // 0=YouTube, 1=Reddit, 2=Twitter, 3=Vimeo, 4=Instagram, 5=Twitch, 6=Playlist, 7=Unknown, 8=Empty, 9=TikTok

    [JsonPropertyName("type")]
    public int StreamType { get; set; } // 0=Video, 1=Audio, 2=AudioAndVideo, 3=File, 4=Unknown

    [JsonPropertyName("downloaded")]
    public DateTime Downloaded { get; set; }

    [JsonPropertyName("location")]
    public string? DownloadLocation { get; set; }

    [JsonPropertyName("time_logged")]
    public DateTime TimeLogged { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("start")]
    public TimeSpan? Start { get; set; }

    [JsonPropertyName("duration")]
    public TimeSpan? Duration { get; set; }

    [JsonPropertyName("crops")]
    public int[]? Crops { get; set; }

    [JsonPropertyName("video_conversion")]
    public int? VideoConversionFormat { get; set; }

    [JsonPropertyName("audio_conversion")]
    public int? AudioConversionFormat { get; set; }

    [JsonPropertyName("max_resolution")]
    public int? MaxResolution { get; set; }

    [JsonPropertyName("max_filesize")]
    public int? MaxFileSize { get; set; }

    [JsonPropertyName("segment_mode")]
    public int SegmentMode { get; set; }
}
