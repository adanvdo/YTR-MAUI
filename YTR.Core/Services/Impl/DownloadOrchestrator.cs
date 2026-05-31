using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YTR.Core.Configuration;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Coordinates the full download workflow: download → post-process → record history.
/// </summary>
public sealed class DownloadOrchestrator : IDownloadOrchestrator
{
    private readonly IYtDlpService _ytDlp;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly IHistoryService _history;
    private readonly IUrlAnalyzer _urlAnalyzer;
    private readonly IOptions<DownloadOptions> _downloadOptions;
    private readonly IOptions<RestrictionOptions> _restrictionOptions;
    private readonly ILogger<DownloadOrchestrator> _logger;

    public DownloadOrchestrator(
        IYtDlpService ytDlp,
        IMediaProcessor mediaProcessor,
        IHistoryService history,
        IUrlAnalyzer urlAnalyzer,
        IOptions<DownloadOptions> downloadOptions,
        IOptions<RestrictionOptions> restrictionOptions,
        ILogger<DownloadOrchestrator> logger)
    {
        _ytDlp = ytDlp;
        _mediaProcessor = mediaProcessor;
        _history = history;
        _urlAnalyzer = urlAnalyzer;
        _downloadOptions = downloadOptions;
        _restrictionOptions = restrictionOptions;
        _logger = logger;
    }

    public async Task<Result<DownloadRecord>> DownloadBestAsync(
        string url,
        StreamKind streamKind,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        var restrictions = _restrictionOptions.Value;
        var outputDir = GetOutputDirectory(streamKind, request?.PlaylistFolder);

        EnsureDirectoryExists(outputDir);

        output?.Report("Starting download...");
        progress?.Report(new DownloadProgress { State = DownloadState.PreProcessing });

        // Download
        var downloadResult = await _ytDlp.DownloadBestAsync(
            url,
            streamKind,
            maxResolution: restrictions.MaxResolutionPixels,
            maxFileSizeMb: restrictions.MaxFileSizeMb,
            outputPath: outputDir,
            progress: progress,
            output: output,
            ct: ct);

        if (downloadResult.IsFailure)
            return Result<DownloadRecord>.Failure(downloadResult.Error!);

        var filePath = downloadResult.Value!;

        // Post-process if needed
        filePath = await PostProcessAsync(filePath, request, streamKind, progress, output, ct);

        // Record in history
        var analysis = _urlAnalyzer.Analyze(url);
        var record = new DownloadRecord
        {
            Url = url,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Platform = analysis.Platform,
            StreamKind = streamKind,
            DownloadedAt = DateTime.UtcNow,
            FilePath = filePath,
            Format = "best",
            InSubFolder = request?.PlaylistFolder is not null,
            PlaylistTitle = request?.PlaylistFolder,
            SegmentStart = request?.SegmentStart,
            SegmentDuration = request?.SegmentDuration,
            CropValues = request?.CropValues is not null ? string.Join(",", request.CropValues) : null,
            VideoConversion = request?.ConvertVideo,
            AudioConversion = request?.ConvertAudio,
            MaxResolution = restrictions.MaxResolution != Resolution.Any ? restrictions.MaxResolution : null,
            MaxFileSizeMb = restrictions.MaxFileSizeMb > 0 ? restrictions.MaxFileSizeMb : null
        };

        await _history.RecordAsync(record, ct);

        progress?.Report(new DownloadProgress { State = DownloadState.Success, Progress = 1.0, Data = filePath });
        output?.Report($"Download complete: {filePath}");

        return Result<DownloadRecord>.Success(record);
    }

    public async Task<Result<DownloadRecord>> DownloadFormatAsync(
        string url,
        FormatPair formatPair,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        if (!formatPair.IsValid)
            return Result<DownloadRecord>.Failure("Invalid format pair.");

        var streamKind = formatPair.StreamKind;
        var outputDir = GetOutputDirectory(streamKind, request?.PlaylistFolder);

        EnsureDirectoryExists(outputDir);

        output?.Report("Starting format download...");
        progress?.Report(new DownloadProgress { State = DownloadState.PreProcessing });

        var downloadResult = await _ytDlp.DownloadFormatAsync(
            url,
            formatPair.FormatId,
            streamKind,
            outputPath: outputDir,
            progress: progress,
            output: output,
            ct: ct);

        if (downloadResult.IsFailure)
            return Result<DownloadRecord>.Failure(downloadResult.Error!);

        var filePath = downloadResult.Value!;

        // Post-process if needed
        filePath = await PostProcessAsync(filePath, request, streamKind, progress, output, ct);

        // Record in history
        var analysis = _urlAnalyzer.Analyze(url);
        var record = new DownloadRecord
        {
            Url = url,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Platform = analysis.Platform,
            StreamKind = streamKind,
            DownloadedAt = DateTime.UtcNow,
            FilePath = filePath,
            Format = formatPair.DisplayText,
            InSubFolder = request?.PlaylistFolder is not null,
            PlaylistTitle = request?.PlaylistFolder,
            SegmentStart = request?.SegmentStart,
            SegmentDuration = request?.SegmentDuration,
            CropValues = request?.CropValues is not null ? string.Join(",", request.CropValues) : null,
            VideoConversion = request?.ConvertVideo,
            AudioConversion = request?.ConvertAudio
        };

        await _history.RecordAsync(record, ct);

        progress?.Report(new DownloadProgress { State = DownloadState.Success, Progress = 1.0, Data = filePath });
        output?.Report($"Download complete: {filePath}");

        return Result<DownloadRecord>.Success(record);
    }

    #region Post-Processing

    private async Task<string> PostProcessAsync(
        string filePath,
        DownloadRequest? request,
        StreamKind streamKind,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? output,
        CancellationToken ct)
    {
        if (request is null) return filePath;

        var needsProcessing = request.SegmentStart.HasValue
                              || request.SegmentDuration.HasValue
                              || request.CropValues is not null
                              || request.ConvertVideo.HasValue
                              || request.ConvertAudio.HasValue;

        if (!needsProcessing) return filePath;

        progress?.Report(new DownloadProgress { State = DownloadState.PostProcessing });
        output?.Report("Post-processing...");

        var currentPath = filePath;

        // Segment extraction
        if (request.SegmentStart.HasValue && request.SegmentDuration.HasValue)
        {
            var segOutput = GenerateProcessedPath(currentPath, "_seg");
            output?.Report("Extracting segment...");
            var segResult = await _mediaProcessor.ExtractSegmentAsync(
                currentPath, request.SegmentStart.Value, request.SegmentDuration.Value,
                segOutput, ct: ct);

            if (segResult.IsSuccess)
            {
                TryDeleteTemp(currentPath, filePath);
                currentPath = segResult.Value!;
            }
            else
            {
                _logger.LogWarning("Segment extraction failed: {Error}", segResult.Error);
            }
        }

        // Crop
        if (request.CropValues is { Length: 4 } crops)
        {
            var cropOutput = GenerateProcessedPath(currentPath, "_crop");
            output?.Report("Applying crop...");
            var cropResult = await _mediaProcessor.CropAsync(
                currentPath, crops[0], crops[1], crops[2], crops[3],
                cropOutput, ct: ct);

            if (cropResult.IsSuccess)
            {
                TryDeleteTemp(currentPath, filePath);
                currentPath = cropResult.Value!;
            }
            else
            {
                _logger.LogWarning("Crop failed: {Error}", cropResult.Error);
            }
        }

        // Format conversion
        if (request.ConvertVideo == VideoFormat.Gif)
        {
            var gifOutput = Path.ChangeExtension(GenerateProcessedPath(currentPath, "_gif"), ".gif");
            output?.Report("Converting to GIF...");
            var gifResult = await _mediaProcessor.ConvertToGifAsync(
                currentPath, gifOutput,
                start: request.SegmentStart,
                duration: request.SegmentDuration,
                ct: ct);

            if (gifResult.IsSuccess)
            {
                TryDeleteTemp(currentPath, filePath);
                currentPath = gifResult.Value!;
            }
        }
        else if (request.ConvertVideo.HasValue && request.ConvertVideo != VideoFormat.Unspecified)
        {
            var ext = VideoFormatToExtension(request.ConvertVideo.Value);
            var convOutput = Path.ChangeExtension(GenerateProcessedPath(currentPath, "_conv"), ext);
            output?.Report($"Converting to {request.ConvertVideo.Value}...");
            var convResult = await _mediaProcessor.ConvertAsync(
                currentPath, request.ConvertVideo.Value, request.ConvertAudio ?? AudioFormat.Unspecified,
                convOutput, ct: ct);

            if (convResult.IsSuccess)
            {
                TryDeleteTemp(currentPath, filePath);
                currentPath = convResult.Value!;
            }
        }
        else if (request.ConvertAudio.HasValue && request.ConvertAudio != AudioFormat.Unspecified)
        {
            var ext = AudioFormatToExtension(request.ConvertAudio.Value);
            var convOutput = Path.ChangeExtension(GenerateProcessedPath(currentPath, "_conv"), ext);
            output?.Report($"Converting to {request.ConvertAudio.Value}...");
            var convResult = await _mediaProcessor.ConvertAsync(
                currentPath, VideoFormat.Unspecified, request.ConvertAudio.Value,
                convOutput, ct: ct);

            if (convResult.IsSuccess)
            {
                TryDeleteTemp(currentPath, filePath);
                currentPath = convResult.Value!;
            }
        }

        return currentPath;
    }

    #endregion

    #region Helpers

    private string GetOutputDirectory(StreamKind streamKind, string? playlistFolder)
    {
        var opts = _downloadOptions.Value;
        var baseDir = streamKind == StreamKind.Audio ? opts.AudioDownloadPath : opts.VideoDownloadPath;

        if (!string.IsNullOrEmpty(playlistFolder) && opts.CreateFolderForPlaylists)
            return Path.Combine(baseDir, playlistFolder);

        return baseDir;
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static string GenerateProcessedPath(string originalPath, string suffix)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        return Path.Combine(dir, $"{name}{suffix}{ext}");
    }

    private static void TryDeleteTemp(string tempPath, string originalPath)
    {
        // Don't delete the original download — only intermediate processing files
        if (tempPath == originalPath) return;
        try { File.Delete(tempPath); } catch { /* best effort */ }
    }

    private static string VideoFormatToExtension(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => ".mp4",
        VideoFormat.Mkv => ".mkv",
        VideoFormat.Webm => ".webm",
        VideoFormat.Flv => ".flv",
        VideoFormat.Ogg => ".ogg",
        VideoFormat.Gif => ".gif",
        _ => ".mp4"
    };

    private static string AudioFormatToExtension(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => ".mp3",
        AudioFormat.M4a => ".m4a",
        AudioFormat.Aac => ".aac",
        AudioFormat.Ogg => ".ogg",
        AudioFormat.Wav => ".wav",
        AudioFormat.Flac => ".flac",
        AudioFormat.Opus => ".opus",
        AudioFormat.Vorbis => ".ogg",
        _ => ".mp3"
    };

    #endregion
}
