using Microsoft.Extensions.Logging;
using YTR.Core.Configuration;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Coordinates the full download workflow: download → post-process → record history.
/// Supports single downloads, format-specific downloads, playlist batch, and stream-based processing.
/// </summary>
public sealed class DownloadOrchestrator : IDownloadOrchestrator
{
    private readonly IYtDlpService _ytDlp;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly IMediaProbeService _probe;
    private readonly IHistoryService _history;
    private readonly IUrlAnalyzer _urlAnalyzer;
    private readonly ISettingsService _settings;
    private readonly ILogger<DownloadOrchestrator> _logger;

    public DownloadOrchestrator(
        IYtDlpService ytDlp,
        IMediaProcessor mediaProcessor,
        IMediaProbeService probe,
        IHistoryService history,
        IUrlAnalyzer urlAnalyzer,
        ISettingsService settings,
        ILogger<DownloadOrchestrator> logger)
    {
        _ytDlp = ytDlp;
        _mediaProcessor = mediaProcessor;
        _probe = probe;
        _history = history;
        _urlAnalyzer = urlAnalyzer;
        _settings = settings;
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
        var restrictions = _settings.Restrictions;
        var outputDir = GetOutputDirectory(streamKind, request?.PlaylistFolder);
        EnsureDirectoryExists(outputDir);

        output?.Report("Starting download...");
        progress?.Report(new DownloadProgress { State = DownloadState.PreProcessing });

        // If post-processing is needed and we have format URLs, process directly from stream
        if (request is not null && HasPostProcessing(request) && streamKind != StreamKind.Audio)
        {
            var streamResult = await ProcessFromStreamAsync(url, null, streamKind, request, progress, output, ct);
            if (streamResult.IsSuccess)
                return streamResult;
            // Fall through to download-then-process if stream processing fails
            _logger.LogWarning("Stream processing failed, falling back to download-then-process: {Error}", streamResult.Error);
        }

        var downloadResult = await _ytDlp.DownloadBestAsync(
            url, streamKind,
            maxResolution: restrictions.MaxResolutionPixels,
            maxFileSizeMb: restrictions.MaxFileSizeMb,
            outputPath: outputDir,
            progress: progress,
            output: output,
            ct: ct);

        if (downloadResult.IsFailure)
            return Result<DownloadRecord>.Failure(downloadResult.Error!);

        var filePath = downloadResult.Value!;
        filePath = await PostProcessAsync(filePath, request, streamKind, progress, output, ct);

        var record = BuildRecord(url, filePath, streamKind, "best", request, restrictions);
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

        // If post-processing is needed, try stream-based processing first
        if (request is not null && HasPostProcessing(request) && streamKind != StreamKind.Audio)
        {
            var streamResult = await ProcessFromStreamAsync(url, formatPair, streamKind, request, progress, output, ct);
            if (streamResult.IsSuccess)
                return streamResult;
            _logger.LogWarning("Stream processing failed, falling back: {Error}", streamResult.Error);
        }

        var downloadResult = await _ytDlp.DownloadFormatAsync(
            url, formatPair.FormatId, streamKind,
            outputPath: outputDir,
            progress: progress,
            output: output,
            ct: ct);

        if (downloadResult.IsFailure)
            return Result<DownloadRecord>.Failure(downloadResult.Error!);

        var filePath = downloadResult.Value!;
        filePath = await PostProcessAsync(filePath, request, streamKind, progress, output, ct);

        var record = BuildRecord(url, filePath, streamKind, formatPair.DisplayText, request);
        await _history.RecordAsync(record, ct);

        progress?.Report(new DownloadProgress { State = DownloadState.Success, Progress = 1.0, Data = filePath });
        output?.Report($"Download complete: {filePath}");
        return Result<DownloadRecord>.Success(record);
    }

    public async Task<Result<int>> DownloadPlaylistAsync(
        string playlistUrl,
        IReadOnlyList<PlaylistItem> selectedItems,
        StreamKind streamKind,
        DownloadRequest? request = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        IProgress<PlaylistProgress>? playlistProgress = null,
        CancellationToken ct = default)
    {
        if (selectedItems.Count == 0)
            return Result<int>.Failure("No items selected.");

        var analysis = _urlAnalyzer.Analyze(playlistUrl);
        var playlistTitle = request?.PlaylistFolder ?? "Playlist";
        var outputDir = GetOutputDirectory(streamKind, playlistTitle);
        EnsureDirectoryExists(outputDir);

        int completed = 0;
        string? lastFilePath = null;

        for (int i = 0; i < selectedItems.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = selectedItems[i];
            output?.Report($"Downloading {i + 1}/{selectedItems.Count}: {item.Title ?? item.Url}");
            playlistProgress?.Report(new PlaylistProgress(i, selectedItems.Count, item.Title));

            var itemRequest = request is not null ? request with { PlaylistFolder = playlistTitle } : new DownloadRequest { PlaylistFolder = playlistTitle };

            var downloadResult = await _ytDlp.DownloadBestAsync(
                item.Url, streamKind,
                maxResolution: _settings.Restrictions.MaxResolutionPixels,
                maxFileSizeMb: _settings.Restrictions.MaxFileSizeMb,
                outputPath: outputDir,
                progress: progress,
                output: output,
                ct: ct);

            if (downloadResult.IsFailure)
            {
                _logger.LogWarning("Playlist item failed: {Url} - {Error}", item.Url, downloadResult.Error);
                continue;
            }

            lastFilePath = downloadResult.Value!;

            var record = new DownloadRecord
            {
                Url = item.Url,
                Title = item.Title ?? Path.GetFileName(lastFilePath),
                Platform = analysis.Platform,
                StreamKind = streamKind,
                DownloadedAt = DateTime.UtcNow,
                FilePath = lastFilePath,
                Format = "best",
                InSubFolder = true,
                PlaylistTitle = playlistTitle,
                PlaylistUrl = playlistUrl
            };
            await _history.RecordAsync(record, ct);
            completed++;
        }

        playlistProgress?.Report(new PlaylistProgress(completed, selectedItems.Count, null));
        output?.Report($"Playlist complete: {completed}/{selectedItems.Count} items downloaded.");

        return Result<int>.Success(completed);
    }

    #region Stream-Based Processing (Item 11)

    /// <summary>
    /// Resolves format URLs and passes them directly to FFmpeg for processing,
    /// avoiding a full download before post-processing.
    /// </summary>
    private async Task<Result<DownloadRecord>> ProcessFromStreamAsync(
        string url,
        FormatPair? formatPair,
        StreamKind streamKind,
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? output,
        CancellationToken ct)
    {
        output?.Report("Resolving stream URLs...");

        // Determine format string
        var formatString = formatPair?.FormatId ?? $"bestvideo+bestaudio/best";
        var urlsResult = await _ytDlp.GetFormatUrlsAsync(url, formatString, ct);
        if (urlsResult.IsFailure || urlsResult.Value!.Count == 0)
            return Result<DownloadRecord>.Failure(urlsResult.Error ?? "No stream URLs resolved.");

        var streamUrls = urlsResult.Value!;
        var videoUrl = streamUrls[0];
        var audioUrl = streamUrls.Count > 1 ? streamUrls[1] : null;

        // Build FFmpeg args for direct stream processing
        output?.Report("Processing from stream...");
        progress?.Report(new DownloadProgress { State = DownloadState.Downloading });

        var opts = _settings.Download;
        var outputDir = GetOutputDirectory(streamKind, request.PlaylistFolder);
        EnsureDirectoryExists(outputDir);

        var fileName = $"{DateTime.Now:MMddyyyyHHmmss}";
        var targetFormat = request.ConvertVideo ?? (formatPair?.VideoFormat is not null
            ? CodecMap.GetBestContainerForCodec(formatPair.VideoFormat.VideoCodec)
            : VideoFormat.Mp4);
        var ext = targetFormat == VideoFormat.Gif ? ".gif" : VideoFormatToExtension(targetFormat);
        var outputPath = Path.Combine(outputDir, fileName + ext);

        // For progress calculation, ffmpeg needs to know the total expected output duration.
        // Use segment duration if set, otherwise fall back to format pair duration or media duration from metadata.
        var segmentDuration = request.SegmentDuration;
        var totalDurationForProgress = segmentDuration ?? formatPair?.Duration ?? request.MediaDuration;

        IProgress<double>? ffmpegProgress = progress is not null
            ? new DirectProgress<double>(pct => progress.Report(new DownloadProgress
            {
                State = DownloadState.Downloading,
                Progress = pct
            }))
            : null;

        // Use the media processor's ConvertFromUrlsAsync with progress
        var result = await _mediaProcessor.ConvertFromUrlsAsync(
            videoUrl, audioUrl, request.SegmentStart, segmentDuration,
            request.CropValues, request.ConvertVideo ?? VideoFormat.Unspecified,
            request.ConvertAudio ?? AudioFormat.Unspecified, outputPath,
            totalDuration: totalDurationForProgress, progress: ffmpegProgress, ct: ct);

        if (result.IsFailure)
            return Result<DownloadRecord>.Failure(result.Error!);

        var filePath = result.Value!;
        var record = BuildRecord(url, filePath, streamKind, formatPair?.DisplayText ?? "best", request);
        await _history.RecordAsync(record, ct);

        progress?.Report(new DownloadProgress { State = DownloadState.Success, Progress = 1.0, Data = filePath });
        return Result<DownloadRecord>.Success(record);
    }

    private static string BuildStreamProcessingArgs(string videoUrl, string? audioUrl, DownloadRequest request, VideoFormat targetFormat, string outputPath)
    {
        // This is a placeholder — actual args are built by FfmpegMediaProcessor.ConvertFromUrlsAsync
        return string.Empty;
    }

    #endregion

    #region Post-Processing

    private async Task<string> PostProcessAsync(
        string filePath,
        DownloadRequest? request,
        StreamKind streamKind,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? output,
        CancellationToken ct)
    {
        if (request is null || !HasPostProcessing(request)) return filePath;

        progress?.Report(new DownloadProgress { State = DownloadState.PostProcessing });
        output?.Report("Post-processing...");

        // Bridge IProgress<double> from ffmpeg to IProgress<DownloadProgress> for the UI
        IProgress<double>? ffmpegProgress = progress is not null
            ? new DirectProgress<double>(pct => progress.Report(new DownloadProgress
            {
                State = DownloadState.PostProcessing,
                Progress = pct
            }))
            : null;

        var tempFiles = new List<string>();
        var currentPath = filePath;

        try
        {
            // Segment extraction
            if (request.SegmentStart.HasValue && request.SegmentDuration.HasValue)
            {
                var segOutput = GenerateProcessedPath(currentPath, "_seg");
                output?.Report("Extracting segment...");
                var segResult = await _mediaProcessor.ExtractSegmentAsync(
                    currentPath, request.SegmentStart.Value, request.SegmentDuration.Value,
                    segOutput, progress: ffmpegProgress, ct: ct);

                if (segResult.IsSuccess)
                {
                    if (currentPath != filePath) tempFiles.Add(currentPath);
                    currentPath = segResult.Value!;
                }
                else
                {
                    _logger.LogWarning("Segment extraction failed: {Error}", segResult.Error);
                }
            }

            // Crop — probe first to validate dimensions
            if (request.CropValues is { Length: 4 } cropMargins)
            {
                var probeResult = await _probe.ProbeAsync(currentPath, ct);
                if (probeResult.IsSuccess && probeResult.Value!.Width.HasValue && probeResult.Value.Height.HasValue)
                {
                    var crop = CropHelper.ConvertCrop(cropMargins, probeResult.Value.Width.Value, probeResult.Value.Height.Value);
                    if (crop is not null)
                    {
                        var cropOutput = GenerateProcessedPath(currentPath, "_crop");
                        output?.Report("Applying crop...");
                        var cropResult = await _mediaProcessor.CropAsync(
                            currentPath, crop.X, crop.Y, crop.Width, crop.Height,
                            cropOutput, progress: ffmpegProgress, ct: ct);

                        if (cropResult.IsSuccess)
                        {
                            if (currentPath != filePath) tempFiles.Add(currentPath);
                            currentPath = cropResult.Value!;
                        }
                        else
                        {
                            _logger.LogWarning("Crop failed: {Error}", cropResult.Error);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Crop coordinates exceed video dimensions ({W}x{H})", probeResult.Value.Width, probeResult.Value.Height);
                    }
                }
            }

            // Format conversion
            if (request.ConvertVideo == VideoFormat.Gif)
            {
                var gifOutput = Path.ChangeExtension(GenerateProcessedPath(currentPath, "_gif"), ".gif");
                output?.Report("Converting to GIF...");
                var gifResult = await _mediaProcessor.ConvertToGifAsync(
                    currentPath, gifOutput, progress: ffmpegProgress, ct: ct);

                if (gifResult.IsSuccess)
                {
                    if (currentPath != filePath) tempFiles.Add(currentPath);
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
                    convOutput, progress: ffmpegProgress, ct: ct);

                if (convResult.IsSuccess)
                {
                    if (currentPath != filePath) tempFiles.Add(currentPath);
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
                    convOutput, progress: ffmpegProgress, ct: ct);

                if (convResult.IsSuccess)
                {
                    if (currentPath != filePath) tempFiles.Add(currentPath);
                    currentPath = convResult.Value!;
                }
            }

            return currentPath;
        }
        finally
        {
            // Clean up all intermediate temp files
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); }
                catch { /* best effort */ }
            }
        }
    }

    #endregion

    #region Helpers

    private static bool HasPostProcessing(DownloadRequest request) =>
        request.SegmentStart.HasValue || request.SegmentDuration.HasValue ||
        request.CropValues is not null ||
        (request.ConvertVideo.HasValue && request.ConvertVideo != VideoFormat.Unspecified) ||
        (request.ConvertAudio.HasValue && request.ConvertAudio != AudioFormat.Unspecified);

    private DownloadRecord BuildRecord(string url, string filePath, StreamKind streamKind, string format, DownloadRequest? request, RestrictionOptions? restrictions = null)
    {
        var analysis = _urlAnalyzer.Analyze(url);
        restrictions ??= _settings.Restrictions;
        return new DownloadRecord
        {
            Url = url,
            Title = request?.Title ?? Path.GetFileNameWithoutExtension(filePath),
            Platform = analysis.Platform,
            StreamKind = streamKind,
            DownloadedAt = DateTime.UtcNow,
            FilePath = filePath,
            Format = format,
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
    }

    private string GetOutputDirectory(StreamKind streamKind, string? playlistFolder)
    {
        var opts = _settings.Download;
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
