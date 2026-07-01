using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YTR.Core.Enums;

namespace YTR.Core.Services.Impl;

/// <summary>
/// FFmpeg-based media processing: segment extraction, crop, convert, GIF, concatenation.
/// Uses hardware acceleration when available (NVENC/QSV/AMF) and fast presets for software encoding.
/// </summary>
public sealed partial class FfmpegMediaProcessor : IMediaProcessor
{
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platform;
    private readonly IHardwareEncoderService _hwEncoder;
    private readonly ILogger<FfmpegMediaProcessor> _logger;

    public FfmpegMediaProcessor(
        IProcessRunner processRunner,
        IPlatformService platform,
        IHardwareEncoderService hwEncoder,
        ILogger<FfmpegMediaProcessor> logger)
    {
        _processRunner = processRunner;
        _platform = platform;
        _hwEncoder = hwEncoder;
        _logger = logger;
    }

    private string FfmpegPath => _platform.GetResourcePath("ffmpeg");

    public async Task<Result<string>> ExtractSegmentAsync(
        string inputPath,
        TimeSpan start,
        TimeSpan duration,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // Segment extraction with stream copy. Use -avoid_negative_ts to fix timestamp issues
        // when seeking to non-keyframe positions. For MP4 output, add -movflags +faststart.
        var ext = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        var movFlags = (ext is "mp4" or "m4a" or "m4v" or "mov") ? "-movflags +faststart" : "";
        var args = $"-y -ss {FormatTime(start)} -i \"{inputPath}\" -t {FormatTime(duration)} -c copy -avoid_negative_ts make_zero {movFlags} \"{outputPath}\"";
        return await RunFfmpegAsync(args, duration, progress, ct, outputPath);
    }

    public async Task<Result<string>> CropAsync(
        string inputPath,
        int x, int y, int width, int height,
        string outputPath,
        TimeSpan? totalDuration = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await _hwEncoder.InitializeAsync(ct);

        var ext = Path.GetExtension(outputPath).TrimStart('.');
        var targetFormat = ExtensionToVideoFormat(ext);
        var vEncoder = GetVideoEncoder(targetFormat);
        var encoderArgs = GetEncoderArgs(targetFormat);

        var args = $"-y -i \"{inputPath}\" -filter:v \"crop={width}:{height}:{x}:{y}\" -c:v {vEncoder} {encoderArgs} -c:a copy \"{outputPath}\"";
        return await RunFfmpegAsync(args, totalDuration, progress, ct, outputPath);
    }

    public async Task<Result<string>> ConvertAsync(
        string inputPath,
        VideoFormat videoFormat,
        AudioFormat audioFormat,
        string outputPath,
        TimeSpan? totalDuration = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await _hwEncoder.InitializeAsync(ct);

        var codecArgs = BuildCodecArgs(videoFormat, audioFormat);
        var args = $"-y -i \"{inputPath}\" {codecArgs} \"{outputPath}\"";
        return await RunFfmpegAsync(args, totalDuration, progress, ct, outputPath);
    }

    public async Task<Result<string>> ConvertToGifAsync(
        string inputPath,
        string outputPath,
        TimeSpan? start = null,
        TimeSpan? duration = null,
        int maxSize = 600,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var inputArgs = string.Empty;
        if (start.HasValue)
            inputArgs += $"-ss {FormatTime(start.Value)} ";
        if (duration.HasValue)
            inputArgs += $"-t {FormatTime(duration.Value)} ";

        // Two-pass GIF generation for quality
        var filterComplex = $"fps=15,scale={maxSize}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse";
        var args = $"-y {inputArgs}-i \"{inputPath}\" -vf \"{filterComplex}\" -loop 0 \"{outputPath}\"";

        return await RunFfmpegAsync(args, duration, progress, ct, outputPath);
    }

    public async Task<Result<string>> ConcatenateAsync(
        string firstPath,
        string secondPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // Create a temporary concat file
        var concatFile = Path.Combine(Path.GetTempPath(), $"ytr_concat_{Guid.NewGuid():N}.txt");
        try
        {
            var content = $"file '{firstPath.Replace("'", "'\\''")}'\nfile '{secondPath.Replace("'", "'\\''")}'";
            await File.WriteAllTextAsync(concatFile, content, ct);

            var args = $"-y -f concat -safe 0 -i \"{concatFile}\" -c copy \"{outputPath}\"";
            return await RunFfmpegAsync(args, null, progress, ct, outputPath);
        }
        finally
        {
            try { File.Delete(concatFile); } catch { /* best effort cleanup */ }
        }
    }

    public async Task<Result<string>> ConvertFromUrlsAsync(
        string videoUrl,
        string? audioUrl,
        TimeSpan? start,
        TimeSpan? duration,
        int[]? cropMargins,
        VideoFormat videoFormat,
        AudioFormat audioFormat,
        string outputPath,
        TimeSpan? totalDuration = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await _hwEncoder.InitializeAsync(ct);

        var inputArgs = new List<string>();
        var mapArgs = new List<string>();
        var filterArgs = new List<string>();
        var codecArgs = new List<string>();
        var outputArgs = new List<string>();

        // Seek before each input for fast seeking (same as original app)
        if (start.HasValue)
            inputArgs.Add($"-ss {FormatTime(start.Value)}");

        // Video input
        inputArgs.Add($"-i \"{videoUrl}\"");

        // Audio input (separate stream)
        if (!string.IsNullOrEmpty(audioUrl))
        {
            if (start.HasValue)
                inputArgs.Add($"-ss {FormatTime(start.Value)}");
            inputArgs.Add($"-i \"{audioUrl}\"");
            // Explicit stream mapping (like original app: -map 0:0 -map 1:0)
            mapArgs.Add("-map 0:0");
            mapArgs.Add("-map 1:0");
        }

        // Duration limit — placed as output arg (after inputs), not input arg
        if (duration.HasValue)
            outputArgs.Add($"-t {FormatTime(duration.Value)}");

        // Crop filter
        if (cropMargins is { Length: 4 })
        {
            var x = cropMargins[2]; // left
            var y = cropMargins[0]; // top
            filterArgs.Add($"crop=iw-{cropMargins[2]}-{cropMargins[3]}:ih-{cropMargins[0]}-{cropMargins[1]}:{x}:{y}");
        }

        // Determine if we need explicit codec specification
        bool needsReencode = cropMargins is { Length: 4 }
            || videoFormat != VideoFormat.Unspecified
            || audioFormat != AudioFormat.Unspecified;

        if (videoFormat == VideoFormat.Gif)
        {
            filterArgs.Clear();
            filterArgs.Add("fps=15,scale=600:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
            codecArgs.Add("-loop 0");
        }
        else if (needsReencode)
        {
            var targetFormat = videoFormat != VideoFormat.Unspecified ? videoFormat : VideoFormat.Mp4;
            // Use software encoders for stream URLs — hardware encoders are unreliable with HTTP inputs
            var vEncoder = targetFormat switch
            {
                VideoFormat.Mp4 or VideoFormat.Mkv or VideoFormat.Flv => "libx264",
                VideoFormat.Webm => "libvpx-vp9",
                VideoFormat.Ogg => "libtheora -qscale:v 3",
                _ => "libx264"
            };
            var encoderExtraArgs = targetFormat switch
            {
                VideoFormat.Webm => "-deadline good -cpu-used 4 -b:v 0 -crf 30",
                VideoFormat.Ogg => "",
                _ => "-preset veryfast"
            };
            codecArgs.Add($"-c:v {vEncoder} {encoderExtraArgs}");

            if (audioFormat != AudioFormat.Unspecified)
            {
                var aCodec = Models.CodecMap.GetAudioCodecForFormat(audioFormat);
                codecArgs.Add($"-c:a {aCodec.Encoder}");
            }
            else
            {
                var aCodec = Models.CodecMap.GetBestAudioCodec(targetFormat);
                if (aCodec is not null)
                    codecArgs.Add($"-c:a {aCodec.Encoder}");
            }
        }
        // else: no codec args — let ffmpeg choose defaults for the output container
        // This matches the original app behavior for segment-only downloads

        // Build final args
        var vf = filterArgs.Count > 0 ? $"-vf \"{string.Join(",", filterArgs)}\"" : "";
        var map = mapArgs.Count > 0 ? string.Join(" ", mapArgs) : "";
        var codec = codecArgs.Count > 0 ? string.Join(" ", codecArgs) : "";
        var outOpts = outputArgs.Count > 0 ? string.Join(" ", outputArgs) : "";
        var allArgs = $"-y {string.Join(" ", inputArgs)} {map} {outOpts} {vf} {codec} \"{outputPath}\"";

        return await RunFfmpegAsync(allArgs, totalDuration ?? duration, progress, ct, outputPath);
    }

    #region Helpers

    /// <summary>
    /// Single-pass post-processing: combines segment extraction, crop, and format conversion
    /// into one ffmpeg command. Avoids intermediate files and redundant decode/encode cycles.
    /// </summary>
    public async Task<Result<string>> PostProcessSinglePassAsync(
        string inputPath,
        string outputPath,
        TimeSpan? segmentStart,
        TimeSpan? segmentDuration,
        int[]? cropValues,
        VideoFormat videoFormat,
        AudioFormat audioFormat,
        TimeSpan? totalDuration = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await _hwEncoder.InitializeAsync(ct);

        var inputArgs = new List<string>();
        var filterArgs = new List<string>();

        // Input seeking (before -i for fast seek)
        if (segmentStart.HasValue)
            inputArgs.Add($"-ss {FormatTime(segmentStart.Value)}");

        inputArgs.Add($"-i \"{inputPath}\"");

        // Duration limit
        if (segmentDuration.HasValue)
            inputArgs.Add($"-t {FormatTime(segmentDuration.Value)}");

        // Crop filter
        if (cropValues is { Length: 4 } margins)
        {
            var x = margins[2]; // left
            var y = margins[0]; // top
            filterArgs.Add($"crop=iw-{margins[2]}-{margins[3]}:ih-{margins[0]}-{margins[1]}:{x}:{y}");
        }

        // Determine if we need video re-encoding
        bool needsVideoReencode = cropValues is { Length: 4 }
            || (videoFormat != VideoFormat.Unspecified && videoFormat != VideoFormat.Gif);

        // Build codec args
        var codecArgs = new List<string>();

        if (videoFormat == VideoFormat.Gif)
        {
            // GIF mode — replace all filters with gif pipeline
            filterArgs.Clear();
            filterArgs.Add("fps=15,scale=600:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
            codecArgs.Add("-loop 0");
        }
        else if (needsVideoReencode)
        {
            var targetFormat = videoFormat != VideoFormat.Unspecified ? videoFormat : VideoFormat.Mp4;
            var vEncoder = GetVideoEncoder(targetFormat);
            var encoderArgs = GetEncoderArgs(targetFormat);
            codecArgs.Add($"-c:v {vEncoder} {encoderArgs}");

            if (audioFormat != AudioFormat.Unspecified)
            {
                var aCodec = Models.CodecMap.GetAudioCodecForFormat(audioFormat);
                codecArgs.Add($"-c:a {aCodec.Encoder}");
            }
            else
            {
                codecArgs.Add("-c:a copy");
            }
        }
        else if (audioFormat != AudioFormat.Unspecified)
        {
            // Audio-only conversion, video stream copy
            codecArgs.Add("-c:v copy");
            var aCodec = Models.CodecMap.GetAudioCodecForFormat(audioFormat);
            codecArgs.Add($"-c:a {aCodec.Encoder}");
        }
        else
        {
            // Segment only — stream copy
            codecArgs.Add("-c copy");
            codecArgs.Add("-avoid_negative_ts make_zero");
        }

        var vf = filterArgs.Count > 0 ? $"-vf \"{string.Join(",", filterArgs)}\"" : "";

        // For MP4 output, add faststart for better playback compatibility
        var outputExt = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        var movFlags = (outputExt is "mp4" or "m4a" or "m4v" or "mov") ? "-movflags +faststart" : "";

        var args = $"-y {string.Join(" ", inputArgs)} {vf} {string.Join(" ", codecArgs)} {movFlags} \"{outputPath}\"";

        // For progress, use segment duration if available, else total duration
        var progressDuration = segmentDuration ?? totalDuration;

        return await RunFfmpegAsync(args, progressDuration, progress, ct, outputPath);
    }

    /// <summary>
    /// Gets the best video encoder for the given format, using hardware acceleration when available.
    /// </summary>
    private string GetVideoEncoder(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => _hwEncoder.H264Encoder,
        VideoFormat.Mkv => _hwEncoder.H264Encoder,
        VideoFormat.Webm => "libvpx-vp9", // No HW acceleration for VP9 encoding in most cases
        VideoFormat.Flv => _hwEncoder.H264Encoder,
        VideoFormat.Ogg => "libtheora -qscale:v 3",
        _ => _hwEncoder.H264Encoder
    };

    /// <summary>
    /// Gets extra encoder arguments (preset, quality, etc.) appropriate for the encoder type.
    /// </summary>
    private string GetEncoderArgs(VideoFormat format) => format switch
    {
        // VP9 and Theora don't use the hw encoder args
        VideoFormat.Webm => "-deadline good -cpu-used 4 -b:v 0 -crf 30",
        VideoFormat.Ogg => "",
        _ => _hwEncoder.HwEncoderExtraArgs
    };

    private async Task<Result<string>> RunFfmpegAsync(
        string args,
        TimeSpan? totalDuration,
        IProgress<double>? progress,
        CancellationToken ct,
        string outputPath)
    {
        // Use -progress - -nostats to get structured progress on stdout
        // (-progress pipe:1 doesn't work reliably on Windows)
        var fullArgs = progress is not null && totalDuration.HasValue
            ? args.Replace("-y ", "-y -progress - -nostats ")
            : args;

        var request = new Models.ProcessRequest
        {
            Executable = FfmpegPath,
            Arguments = fullArgs,
            OnOutputLine = line =>
            {
                if (progress is null || !totalDuration.HasValue) return;
                // -progress - outputs: out_time=00:00:04.000000 (or out_time=00:00:04.00)
                var timeMatch = FfmpegProgressTimeRegex().Match(line);
                if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups["time"].Value, out var current))
                {
                    var pct = current.TotalSeconds / totalDuration.Value.TotalSeconds;
                    progress.Report(Math.Clamp(pct, 0, 1));
                }
            },
            OnErrorLine = line =>
            {
                if (progress is null || !totalDuration.HasValue) return;
                // Fallback: parse stderr time= output (when -nostats is not used)
                var timeMatch = FfmpegTimeRegex().Match(line);
                if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups["time"].Value, out var current))
                {
                    var pct = current.TotalSeconds / totalDuration.Value.TotalSeconds;
                    progress.Report(Math.Clamp(pct, 0, 1));
                }
            }
        };

        var result = await _processRunner.RunAsync(request, ct);

        if (result.WasCancelled)
            return Result<string>.Failure("Processing cancelled.");

        if (File.Exists(outputPath))
        {
            progress?.Report(1.0);
            return Result<string>.Success(outputPath);
        }

        return Result<string>.Failure($"FFmpeg failed: {result.StandardError.Trim()}");
    }

    private string BuildCodecArgs(VideoFormat videoFormat, AudioFormat audioFormat)
    {
        var parts = new List<string>();

        if (videoFormat != VideoFormat.Unspecified && videoFormat != VideoFormat.Gif)
        {
            var vEncoder = GetVideoEncoder(videoFormat);
            var encoderArgs = GetEncoderArgs(videoFormat);
            parts.Add($"-c:v {vEncoder} {encoderArgs}");
        }

        if (audioFormat != AudioFormat.Unspecified)
        {
            var aCodec = Models.CodecMap.GetAudioCodecForFormat(audioFormat);
            parts.Add($"-c:a {aCodec.Encoder}");
        }
        else if (videoFormat != VideoFormat.Unspecified)
        {
            var aCodec = Models.CodecMap.GetBestAudioCodec(videoFormat);
            if (aCodec is not null)
                parts.Add($"-c:a {aCodec.Encoder}");
            else
                parts.Add("-c:a copy");
        }

        return string.Join(" ", parts);
    }

    private static string FormatTime(TimeSpan ts) => ts.ToString(@"hh\:mm\:ss\.fff");

    private static VideoFormat ExtensionToVideoFormat(string ext) => ext.ToLowerInvariant() switch
    {
        "mp4" => VideoFormat.Mp4,
        "mkv" => VideoFormat.Mkv,
        "webm" => VideoFormat.Webm,
        "flv" => VideoFormat.Flv,
        "ogg" => VideoFormat.Ogg,
        _ => VideoFormat.Mp4
    };

    [GeneratedRegex(@"time=(?<time>[\d:.]+)")]
    private static partial Regex FfmpegTimeRegex();

    [GeneratedRegex(@"out_time=\s*(?<time>[\d:.]+)")]
    private static partial Regex FfmpegProgressTimeRegex();

    #endregion
}
