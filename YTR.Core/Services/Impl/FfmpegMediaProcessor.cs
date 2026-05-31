using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YTR.Core.Enums;

namespace YTR.Core.Services.Impl;

/// <summary>
/// FFmpeg-based media processing: segment extraction, crop, convert, GIF, concatenation.
/// </summary>
public sealed partial class FfmpegMediaProcessor : IMediaProcessor
{
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platform;
    private readonly ILogger<FfmpegMediaProcessor> _logger;

    public FfmpegMediaProcessor(
        IProcessRunner processRunner,
        IPlatformService platform,
        ILogger<FfmpegMediaProcessor> logger)
    {
        _processRunner = processRunner;
        _platform = platform;
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
        var args = $"-y -ss {FormatTime(start)} -i \"{inputPath}\" -t {FormatTime(duration)} -c copy \"{outputPath}\"";
        return await RunFfmpegAsync(args, duration, progress, ct, outputPath);
    }

    public async Task<Result<string>> CropAsync(
        string inputPath,
        int x, int y, int width, int height,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var codec = GetBestVideoCodecForExtension(Path.GetExtension(outputPath));
        var args = $"-y -i \"{inputPath}\" -filter:v \"crop={width}:{height}:{x}:{y}\" -c:v {codec} -c:a copy \"{outputPath}\"";
        return await RunFfmpegAsync(args, null, progress, ct, outputPath);
    }

    public async Task<Result<string>> ConvertAsync(
        string inputPath,
        VideoFormat videoFormat,
        AudioFormat audioFormat,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var codecArgs = BuildCodecArgs(videoFormat, audioFormat);
        var args = $"-y -i \"{inputPath}\" {codecArgs} \"{outputPath}\"";
        return await RunFfmpegAsync(args, null, progress, ct, outputPath);
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

    #region Helpers

    private async Task<Result<string>> RunFfmpegAsync(
        string args,
        TimeSpan? totalDuration,
        IProgress<double>? progress,
        CancellationToken ct,
        string outputPath)
    {
        var request = new Models.ProcessRequest
        {
            Executable = FfmpegPath,
            Arguments = args,
            OnOutputLine = line =>
            {
                if (progress is null || !totalDuration.HasValue) return;
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

        // FFmpeg writes progress to stderr, so check if output file exists
        if (File.Exists(outputPath))
        {
            progress?.Report(1.0);
            return Result<string>.Success(outputPath);
        }

        return Result<string>.Failure($"FFmpeg failed: {result.StandardError.Trim()}");
    }

    private static string FormatTime(TimeSpan ts) => ts.ToString(@"hh\:mm\:ss\.fff");

    private static string BuildCodecArgs(VideoFormat videoFormat, AudioFormat audioFormat)
    {
        var parts = new List<string>();

        if (videoFormat != VideoFormat.Unspecified && videoFormat != VideoFormat.Gif)
        {
            var vCodec = videoFormat switch
            {
                VideoFormat.Mp4 => "libx264",
                VideoFormat.Mkv => "libx264",
                VideoFormat.Webm => "libvpx-vp9",
                VideoFormat.Flv => "libx264",
                VideoFormat.Ogg => "libtheora",
                _ => "copy"
            };
            parts.Add($"-c:v {vCodec}");
        }

        if (audioFormat != AudioFormat.Unspecified)
        {
            var aCodec = audioFormat switch
            {
                AudioFormat.Mp3 => "libmp3lame",
                AudioFormat.Aac => "aac",
                AudioFormat.Flac => "flac",
                AudioFormat.Opus => "libopus",
                AudioFormat.Vorbis => "libvorbis",
                AudioFormat.Wav => "pcm_s16le",
                AudioFormat.M4a => "aac",
                AudioFormat.Ogg => "libvorbis",
                _ => "copy"
            };
            parts.Add($"-c:a {aCodec}");
        }
        else if (videoFormat != VideoFormat.Unspecified)
        {
            parts.Add("-c:a copy");
        }

        return string.Join(" ", parts);
    }

    private static string GetBestVideoCodecForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" => "libx264",
        ".mkv" => "libx264",
        ".webm" => "libvpx-vp9",
        ".flv" => "libx264",
        ".ogg" => "libtheora",
        _ => "libx264"
    };

    [GeneratedRegex(@"time=(?<time>[\d:.]+)")]
    private static partial Regex FfmpegTimeRegex();

    #endregion
}
