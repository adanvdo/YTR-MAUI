using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Codec-to-container compatibility maps. Determines which video/audio codecs
/// are valid for each container format, and provides best-default selections.
/// Ported from the old SystemCodecMaps static class.
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
        VideoFormat.Mp4 => [Opus, Vorbis, Mp3, Aac],
        VideoFormat.Mkv => [Opus, Vorbis, Mp3, Aac],
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
    /// Gets the best default audio codec for a container.
    /// </summary>
    public static FfmpegCodec GetBestAudioCodec(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => Mp3,
        VideoFormat.Mkv => Aac,
        VideoFormat.Webm => Vorbis,
        VideoFormat.Flv => Mp3,
        VideoFormat.Ogg => Vorbis,
        VideoFormat.Gif => null!,
        _ => Mp3
    };

    /// <summary>
    /// Gets the audio codec encoder string for an audio format.
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
}

/// <summary>
/// Represents an FFmpeg encoder with its command-line string.
/// </summary>
public sealed record FfmpegCodec(string Encoder, VideoCodecId VideoId = VideoCodecId.None, AudioCodecId AudioId = AudioCodecId.None)
{
    public FfmpegCodec(string encoder, VideoCodecId videoId) : this(encoder, videoId, AudioCodecId.None) { }
    public FfmpegCodec(string encoder, AudioCodecId audioId) : this(encoder, VideoCodecId.None, audioId) { }
}

public enum VideoCodecId
{
    None, H264, H265, Vp9, Av1, Mpeg4, Theora
}

public enum AudioCodecId
{
    None, Aac, Mp3, Opus, Vorbis, Flac, Wav
}
