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
        var targetFormat = Models.CodecMap.GetBestContainerForCodec(null); // Determine from extension
        var ext = Path.GetExtension(outputPath).TrimStart('.');
        var vCodec = Models.CodecMap.GetBestVideoCodec(ExtensionToVideoFormat(ext));
        var args = $"-y -i \"{inputPath}\" -filter:v \"crop={width}:{height}:{x}:{y}\" -c:v {vCodec.Encoder} -c:a copy \"{outputPath}\"";
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
        var codecArgs = BuildCodecArgsFromMap(videoFormat, audioFormat);
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
        var inputArgs = new List<string>();
        var filterArgs = new List<string>();
        var codecArgs = new List<string>();

        // Seek before input for fast seeking
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
        }

        // Duration limit
        if (duration.HasValue)
            inputArgs.Add($"-t {FormatTime(duration.Value)}");

        // Crop filter
        if (cropMargins is { Length: 4 })
        {
            // Margins are [top, bottom, left, right] — need to probe for dimensions
            // For stream processing, we pass the crop filter and let FFmpeg handle it
            // The caller should have validated via CropHelper already
            var x = cropMargins[2]; // left
            var y = cropMargins[0]; // top
            // We can't know exact width/height without probing the URL, so use iw/ih expressions
            filterArgs.Add($"crop=iw-{cropMargins[2]}-{cropMargins[3]}:ih-{cropMargins[0]}-{cropMargins[1]}:{x}:{y}");
        }

        // Video codec
        if (videoFormat == VideoFormat.Gif)
        {
            // GIF conversion filter
            filterArgs.Clear();
            filterArgs.Add("fps=15,scale=600:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
            codecArgs.Add("-loop 0");
        }
        else if (videoFormat != VideoFormat.Unspecified)
        {
            var vCodec = Models.CodecMap.GetBestVideoCodec(videoFormat);
            codecArgs.Add($"-c:v {vCodec.Encoder}");
            var aCodec = Models.CodecMap.GetBestAudioCodec(videoFormat);
            if (aCodec is not null)
                codecArgs.Add($"-c:a {aCodec.Encoder}");
        }
        else
        {
            codecArgs.Add("-c:v libx264");
            codecArgs.Add("-c:a aac");
        }

        // Audio format override
        if (audioFormat != AudioFormat.Unspecified && videoFormat != VideoFormat.Gif)
        {
            var aCodec = Models.CodecMap.GetAudioCodecForFormat(audioFormat);
            // Remove any existing -c:a
            codecArgs.RemoveAll(a => a.StartsWith("-c:a"));
            codecArgs.Add($"-c:a {aCodec.Encoder}");
        }

        // Build final args
        var vf = filterArgs.Count > 0 ? $"-vf \"{string.Join(",", filterArgs)}\"" : "";
        var allArgs = $"-y {string.Join(" ", inputArgs)} {vf} {string.Join(" ", codecArgs)} \"{outputPath}\"";

        return await RunFfmpegAsync(allArgs, totalDuration ?? duration, progress, ct, outputPath);
    }

    #region Helpers

    private async Task<Result<string>> RunFfmpegAsync(
        string args,
        TimeSpan? totalDuration,
        IProgress<double>? progress,
        CancellationToken ct,
        string outputPath)
    {
        // Add -progress pipe:1 to get progress output on stdout with proper newlines
        // Format: key=value pairs including out_time=HH:MM:SS.ffffff
        var fullArgs = progress is not null && totalDuration.HasValue
            ? $"-progress pipe:1 {args}"
            : args;

        var request = new Models.ProcessRequest
        {
            Executable = FfmpegPath,
            Arguments = fullArgs,
            OnOutputLine = line =>
            {
                if (progress is null || !totalDuration.HasValue) return;
                // -progress pipe:1 outputs: out_time=00:00:04.000000
                var timeMatch = FfmpegProgressTimeRegex().Match(line);
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

    private static string BuildCodecArgsFromMap(VideoFormat videoFormat, AudioFormat audioFormat)
    {
        var parts = new List<string>();

        if (videoFormat != VideoFormat.Unspecified && videoFormat != VideoFormat.Gif)
        {
            var vCodec = Models.CodecMap.GetBestVideoCodec(videoFormat);
            parts.Add($"-c:v {vCodec.Encoder}");
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

    [GeneratedRegex(@"^out_time=(?<time>[\d:.]+)")]
    private static partial Regex FfmpegProgressTimeRegex();

    #endregion
}
