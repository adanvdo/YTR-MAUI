using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Codec-to-container compatibility maps. Determines which video/audio codecs
/// are valid for each container format, provides best-default selections, and
/// resolves incompatible format combinations for ffmpeg command building.
/// </summary>
public static class CodecMap
{
    // Video codecs
    public static readonly FfmpegCodec H264 = new("libx264", VideoCodecId.H264);
    public static readonly FfmpegCodec H265 = new("libx265", VideoCodecId.H265);
    public static readonly FfmpegCodec Vp9 = new("libvpx-vp9", VideoCodecId.Vp9);
    public static readonly FfmpegCodec Av1 = new("libaom-av1", VideoCodecId.Av1);
    public static readonly FfmpegCodec Mpeg4 = new("mpeg4", VideoCodecId.Mpeg4);
    public static readonly FfmpegCodec Theora = new("libtheora -qscale:v 3", VideoCodecId.Theora);

    // Audio codecs
    public static readonly FfmpegCodec Aac = new("aac", AudioCodecId.Aac);
    public static readonly FfmpegCodec Mp3 = new("libmp3lame", AudioCodecId.Mp3);
    public static readonly FfmpegCodec Opus = new("libopus", AudioCodecId.Opus);
    public static readonly FfmpegCodec Vorbis = new("libvorbis -qscale:a 3", AudioCodecId.Vorbis);
    public static readonly FfmpegCodec Flac = new("flac", AudioCodecId.Flac);
    public static readonly FfmpegCodec Wav = new("pcm_s16le", AudioCodecId.Wav);

    /// <summary>
    /// Gets the compatible video codecs for a container format.
    /// </summary>
    public static IReadOnlyList<FfmpegCodec> GetVideoCodecs(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => [Av1, Mpeg4, H264, H265, Vp9],
        VideoFormat.Mkv => [Mpeg4, H264, H265, Vp9],
        VideoFormat.Webm => [Vp9],
        VideoFormat.Flv => [Mpeg4, H264],
        VideoFormat.Ogg => [Theora],
        _ => [H264]
    };

    /// <summary>
    /// Gets the compatible audio codecs for a container format.
    /// </summary>
    public static IReadOnlyList<FfmpegCodec> GetAudioCodecs(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => [Aac, Mp3, Opus, Vorbis],
        VideoFormat.Mkv => [Aac, Mp3, Opus, Vorbis, Flac],
        VideoFormat.Webm => [Vorbis, Opus],
        VideoFormat.Flv => [Mp3],
        VideoFormat.Ogg => [Vorbis, Opus],
        _ => [Mp3]
    };

    /// <summary>
    /// Gets the best default video codec for a container.
    /// </summary>
    public static FfmpegCodec GetBestVideoCodec(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => H264,
        VideoFormat.Mkv => H264,
        VideoFormat.Webm => Vp9,
        VideoFormat.Flv => H264,
        VideoFormat.Ogg => Theora,
        _ => H264
    };

    /// <summary>
    /// Gets the best default audio codec for a container. Returns null for GIF.
    /// </summary>
    public static FfmpegCodec? GetBestAudioCodec(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => Aac,
        VideoFormat.Mkv => Aac,
        VideoFormat.Webm => Opus,
        VideoFormat.Flv => Mp3,
        VideoFormat.Ogg => Vorbis,
        VideoFormat.Gif => null,
        _ => Mp3
    };

    /// <summary>
    /// Gets the audio codec encoder for a given audio output format.
    /// </summary>
    public static FfmpegCodec GetAudioCodecForFormat(AudioFormat format) => format switch
    {
        AudioFormat.Aac => Aac,
        AudioFormat.Mp3 => Mp3,
        AudioFormat.Opus => Opus,
        AudioFormat.Vorbis => Vorbis,
        AudioFormat.Ogg => Vorbis,
        AudioFormat.Flac => Flac,
        AudioFormat.Wav => Wav,
        AudioFormat.M4a => Aac,
        _ => Mp3
    };

    /// <summary>
    /// Determines the best container format for a given video codec name from yt-dlp/ffprobe.
    /// </summary>
    public static VideoFormat GetBestContainerForCodec(string? codecName) => codecName?.ToLowerInvariant() switch
    {
        "h264" or "avc1" or "avc" => VideoFormat.Mp4,
        "h265" or "hevc" or "hev1" => VideoFormat.Mp4,
        "av1" or "av01" => VideoFormat.Mp4,
        "vp9" or "vp09" => VideoFormat.Webm,
        "vp8" => VideoFormat.Webm,
        "theora" => VideoFormat.Ogg,
        "mpeg4" or "mp4v" => VideoFormat.Mp4,
        _ => VideoFormat.Mp4
    };

    /// <summary>
    /// Checks if a video codec is compatible with a container format.
    /// </summary>
    public static bool IsVideoCodecCompatible(FfmpegCodec codec, VideoFormat format) =>
        GetVideoCodecs(format).Contains(codec);

    /// <summary>
    /// Checks if an audio codec is compatible with a container format.
    /// </summary>
    public static bool IsAudioCodecCompatible(FfmpegCodec codec, VideoFormat format) =>
        GetAudioCodecs(format).Contains(codec);

    /// <summary>
    /// Checks if an audio format (from user selection) is compatible with the target video container.
    /// </summary>
    public static bool IsAudioFormatCompatible(AudioFormat audioFormat, VideoFormat videoFormat)
    {
        if (videoFormat == VideoFormat.Gif || videoFormat == VideoFormat.Unspecified)
            return true;
        var codec = GetAudioCodecForFormat(audioFormat);
        return IsAudioCodecCompatible(codec, videoFormat);
    }

    /// <summary>
    /// Maps a yt-dlp/ffprobe audio codec name to an AudioCodecId.
    /// </summary>
    public static AudioCodecId ParseAudioCodecName(string? codecName) => codecName?.ToLowerInvariant() switch
    {
        "aac" or "mp4a" or "mp4a.40.2" => AudioCodecId.Aac,
        "mp3" or "mp3float" => AudioCodecId.Mp3,
        "opus" => AudioCodecId.Opus,
        "vorbis" => AudioCodecId.Vorbis,
        "flac" => AudioCodecId.Flac,
        "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" => AudioCodecId.Wav,
        _ => AudioCodecId.None
    };

    /// <summary>
    /// Maps a yt-dlp/ffprobe video codec name to a VideoCodecId.
    /// </summary>
    public static VideoCodecId ParseVideoCodecName(string? codecName) => codecName?.ToLowerInvariant() switch
    {
        "h264" or "avc1" or "avc" => VideoCodecId.H264,
        "h265" or "hevc" or "hev1" => VideoCodecId.H265,
        "vp9" or "vp09" => VideoCodecId.Vp9,
        "vp8" => VideoCodecId.Vp9, // treat vp8 as vp9 for container selection
        "av1" or "av01" => VideoCodecId.Av1,
        "mpeg4" or "mp4v" => VideoCodecId.Mpeg4,
        "theora" => VideoCodecId.Theora,
        _ => VideoCodecId.None
    };

    /// <summary>
    /// Resolves a potentially incompatible audio+video format combination into a valid one.
    /// Returns the corrected audio codec to use, or null if the audio can be stream-copied as-is.
    /// 
    /// Example: user wants webm video + m4a audio → m4a (AAC) is incompatible with webm,
    /// so this returns Opus (the best compatible audio codec for webm).
    /// </summary>
    /// <param name="videoFormat">Target video container format.</param>
    /// <param name="audioFormat">Requested audio format from user selection.</param>
    /// <param name="sourceAudioCodec">Codec name of the source audio stream (from yt-dlp metadata), or null if unknown.</param>
    /// <returns>
    /// The FfmpegCodec to encode audio with, or null if the source audio can be copied directly.
    /// </returns>
    public static FfmpegCodec? ResolveAudioCodec(VideoFormat videoFormat, AudioFormat audioFormat, string? sourceAudioCodec = null)
    {
        if (videoFormat == VideoFormat.Gif)
            return null; // GIF has no audio

        // If user explicitly requested an audio format, check compatibility
        if (audioFormat != AudioFormat.Unspecified)
        {
            var requestedCodec = GetAudioCodecForFormat(audioFormat);
            if (IsAudioCodecCompatible(requestedCodec, videoFormat))
                return requestedCodec; // User's choice is compatible, use it

            // User's choice is incompatible — pick the best compatible codec
            return GetBestAudioCodec(videoFormat);
        }

        // No explicit audio format requested — check if source audio is compatible
        if (sourceAudioCodec is not null)
        {
            var sourceId = ParseAudioCodecName(sourceAudioCodec);
            if (sourceId != AudioCodecId.None)
            {
                var sourceCodec = GetCodecById(sourceId);
                if (sourceCodec is not null && IsAudioCodecCompatible(sourceCodec, videoFormat))
                    return null; // Source is compatible, can stream-copy
            }

            // Source audio is incompatible with target container — transcode to best compatible
            return GetBestAudioCodec(videoFormat);
        }

        // Unknown source, no user preference — let the container's best default handle it
        return null;
    }

    /// <summary>
    /// Resolves a potentially incompatible video codec for a given container.
    /// Returns the corrected video codec to use, or null if the source video can be stream-copied.
    /// </summary>
    /// <param name="videoFormat">Target video container format.</param>
    /// <param name="sourceVideoCodec">Codec name of the source video stream (from yt-dlp metadata), or null if unknown.</param>
    /// <returns>
    /// The FfmpegCodec to encode video with, or null if the source video can be copied directly.
    /// </returns>
    public static FfmpegCodec? ResolveVideoCodec(VideoFormat videoFormat, string? sourceVideoCodec = null)
    {
        if (videoFormat is VideoFormat.Unspecified or VideoFormat.Gif)
            return null;

        if (sourceVideoCodec is not null)
        {
            var sourceId = ParseVideoCodecName(sourceVideoCodec);
            if (sourceId != VideoCodecId.None)
            {
                var sourceCodec = GetCodecById(sourceId);
                if (sourceCodec is not null && IsVideoCodecCompatible(sourceCodec, videoFormat))
                    return null; // Source is compatible, can stream-copy
            }

            // Source video is incompatible — re-encode with best codec for container
            return GetBestVideoCodec(videoFormat);
        }

        // Unknown source — default to re-encoding with the best codec
        return GetBestVideoCodec(videoFormat);
    }

    /// <summary>
    /// Given source stream codecs and a target container, determines whether transcoding is needed
    /// and returns the full codec resolution for both streams.
    /// </summary>
    public static CodecResolution ResolveForContainer(
        VideoFormat targetFormat,
        string? sourceVideoCodec,
        string? sourceAudioCodec,
        AudioFormat requestedAudioFormat = AudioFormat.Unspecified)
    {
        var videoCodec = ResolveVideoCodec(targetFormat, sourceVideoCodec);
        var audioCodec = ResolveAudioCodec(targetFormat, requestedAudioFormat, sourceAudioCodec);

        return new CodecResolution
        {
            VideoEncoder = videoCodec,
            AudioEncoder = audioCodec,
            CanCopyVideo = videoCodec is null,
            CanCopyAudio = audioCodec is null,
            NeedsTranscode = videoCodec is not null || audioCodec is not null
        };
    }

    /// <summary>
    /// Gets an FfmpegCodec instance by its AudioCodecId.
    /// </summary>
    private static FfmpegCodec? GetCodecById(AudioCodecId id) => id switch
    {
        AudioCodecId.Aac => Aac,
        AudioCodecId.Mp3 => Mp3,
        AudioCodecId.Opus => Opus,
        AudioCodecId.Vorbis => Vorbis,
        AudioCodecId.Flac => Flac,
        AudioCodecId.Wav => Wav,
        _ => null
    };

    /// <summary>
    /// Gets an FfmpegCodec instance by its VideoCodecId.
    /// </summary>
    private static FfmpegCodec? GetCodecById(VideoCodecId id) => id switch
    {
        VideoCodecId.H264 => H264,
        VideoCodecId.H265 => H265,
        VideoCodecId.Vp9 => Vp9,
        VideoCodecId.Av1 => Av1,
        VideoCodecId.Mpeg4 => Mpeg4,
        VideoCodecId.Theora => Theora,
        _ => null
    };
}

/// <summary>
/// Represents an FFmpeg encoder with its command-line string and codec identity.
/// </summary>
public sealed record FfmpegCodec(string Encoder, VideoCodecId VideoId = VideoCodecId.None, AudioCodecId AudioId = AudioCodecId.None)
{
    public FfmpegCodec(string encoder, VideoCodecId videoId) : this(encoder, videoId, AudioCodecId.None) { }
    public FfmpegCodec(string encoder, AudioCodecId audioId) : this(encoder, VideoCodecId.None, audioId) { }

    public bool IsVideo => VideoId != VideoCodecId.None;
    public bool IsAudio => AudioId != AudioCodecId.None;
}

/// <summary>
/// Result of resolving codecs for a target container format.
/// Tells callers whether they can stream-copy or need to transcode each stream.
/// </summary>
public sealed record CodecResolution
{
    /// <summary>Video encoder to use, or null if video can be stream-copied (-c:v copy).</summary>
    public FfmpegCodec? VideoEncoder { get; init; }

    /// <summary>Audio encoder to use, or null if audio can be stream-copied (-c:a copy).</summary>
    public FfmpegCodec? AudioEncoder { get; init; }

    /// <summary>True if the source video codec is compatible with the target container.</summary>
    public bool CanCopyVideo { get; init; }

    /// <summary>True if the source audio codec is compatible with the target container.</summary>
    public bool CanCopyAudio { get; init; }

    /// <summary>True if any stream needs transcoding (not just remuxing).</summary>
    public bool NeedsTranscode { get; init; }
}

public enum VideoCodecId
{
    None, H264, H265, Vp9, Av1, Mpeg4, Theora
}

public enum AudioCodecId
{
    None, Aac, Mp3, Opus, Vorbis, Flac, Wav
}
