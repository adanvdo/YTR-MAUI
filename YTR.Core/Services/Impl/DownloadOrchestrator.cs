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

        // Determine effective limits (from request overrides or global settings)
        var maxRes = request?.MaxResolutionPixels ?? (restrictions.EnforceRestrictions ? restrictions.MaxResolutionPixels : 0);
        var maxSize = request?.MaxFileSizeMb ?? (restrictions.EnforceRestrictions ? restrictions.MaxFileSizeMb : 0);

        // If limits are set, fetch formats and pick the best one within constraints
        if (maxRes > 0 || maxSize > 0)
        {
            output?.Report("Fetching formats to apply limits...");
            var infoResult = await _ytDlp.GetMediaInfoAsync(url, ct);
            if (infoResult.IsSuccess && infoResult.Value?.Formats is { Count: > 0 } formats)
            {
                var bestPair = SelectBestFormatWithinLimits(formats, streamKind, maxRes, maxSize);
                if (bestPair is not null && bestPair.IsValid)
                {
                    output?.Report($"Selected format: {bestPair.DisplayText}");
                    return await DownloadFormatAsync(url, bestPair, request, progress, output, ct);
                }
                // If no format found within limits, fall through to yt-dlp's built-in filtering
                _logger.LogWarning("No format found within limits (res={MaxRes}, size={MaxSize}MB), using yt-dlp filtering", maxRes, maxSize);
            }
        }

        // If post-processing is needed and we have format URLs, process directly from stream.
        if (request is not null && HasPostProcessing(request) && streamKind != StreamKind.Audio)
        {
            var streamResult = await ProcessFromStreamAsync(url, null, streamKind, request, progress, output, ct);
            if (streamResult.IsSuccess)
                return streamResult;
            _logger.LogWarning("Stream processing failed, falling back to download-then-process: {Error}", streamResult.Error);
        }

        var downloadResult = await _ytDlp.DownloadBestAsync(
            url, streamKind,
            maxResolution: maxRes,
            maxFileSizeMb: maxSize,
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

        // If post-processing is needed, try stream-based processing first.
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
        output?.Report("Downloading and processing...");
        progress?.Report(new DownloadProgress { State = DownloadState.Downloading, Progress = 0 });

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

        // Probe the file to get its duration for progress reporting
        TimeSpan? fileDuration = null;
        var probeResult = await _probe.ProbeAsync(filePath, ct);
        if (probeResult.IsSuccess)
            fileDuration = probeResult.Value!.Duration;

        // Validate crop values if present
        int[]? validatedCrop = null;
        if (request.CropValues is { Length: 4 } cropMargins && probeResult.IsSuccess
            && probeResult.Value!.Width.HasValue && probeResult.Value.Height.HasValue)
        {
            var crop = CropHelper.ConvertCrop(cropMargins, probeResult.Value.Width.Value, probeResult.Value.Height.Value);
            if (crop is not null)
            {
                validatedCrop = cropMargins;
            }
            else
            {
                _logger.LogWarning("Crop coordinates exceed video dimensions ({W}x{H}), skipping crop",
                    probeResult.Value.Width, probeResult.Value.Height);
            }
        }

        // GIF is special — use the dedicated method
        if (request.ConvertVideo == VideoFormat.Gif)
        {
            var gifOutput = Path.ChangeExtension(GenerateProcessedPath(filePath, "_gif"), ".gif");
            output?.Report("Converting to GIF...");
            var gifResult = await _mediaProcessor.ConvertToGifAsync(
                filePath, gifOutput,
                start: request.SegmentStart,
                duration: request.SegmentDuration,
                progress: ffmpegProgress, ct: ct);

            if (gifResult.IsSuccess)
            {
                TryDeleteFile(filePath);
                return gifResult.Value!;
            }

            _logger.LogWarning("GIF conversion failed: {Error}", gifResult.Error);
            return filePath;
        }

        // Determine if we can use segment-only (stream copy) path
        bool hasSegment = request.SegmentStart.HasValue && request.SegmentDuration.HasValue;
        bool hasCrop = validatedCrop is not null;
        bool hasConvert = (request.ConvertVideo.HasValue && request.ConvertVideo != VideoFormat.Unspecified)
                       || (request.ConvertAudio.HasValue && request.ConvertAudio != AudioFormat.Unspecified);

        // Skip unnecessary conversion: if the file is already in the target format, don't re-encode
        if (hasConvert && !hasCrop)
        {
            var fileExt = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            var targetVideoExt = request.ConvertVideo.HasValue && request.ConvertVideo != VideoFormat.Unspecified
                ? VideoFormatToExtension(request.ConvertVideo.Value).TrimStart('.') : null;
            var targetAudioExt = request.ConvertAudio.HasValue && request.ConvertAudio != AudioFormat.Unspecified
                ? AudioFormatToExtension(request.ConvertAudio.Value).TrimStart('.') : null;

            bool videoAlreadyMatches = targetVideoExt is null || fileExt == targetVideoExt;
            bool audioAlreadyMatches = targetAudioExt is null; // Can't easily check audio codec from extension alone

            if (videoAlreadyMatches && audioAlreadyMatches)
            {
                hasConvert = false;
                _logger.LogInformation("Skipping conversion: file is already in target format ({Ext})", fileExt);
            }
        }

        // If only segmenting (no crop, no convert), use fast stream-copy extraction
        if (hasSegment && !hasCrop && !hasConvert)
        {
            var segOutput = GenerateProcessedPath(filePath, "_seg");
            output?.Report("Extracting segment (stream copy)...");
            var segResult = await _mediaProcessor.ExtractSegmentAsync(
                filePath, request.SegmentStart!.Value, request.SegmentDuration!.Value,
                segOutput, progress: ffmpegProgress, ct: ct);

            if (segResult.IsSuccess)
            {
                TryDeleteFile(filePath);
                return segResult.Value!;
            }

            _logger.LogWarning("Segment extraction failed: {Error}", segResult.Error);
            return filePath;
        }

        // Single-pass: combine segment + crop + convert into one ffmpeg call
        var videoFormat = request.ConvertVideo ?? VideoFormat.Unspecified;
        var audioFormat = request.ConvertAudio ?? AudioFormat.Unspecified;

        // Determine output extension
        string ext;
        if (videoFormat != VideoFormat.Unspecified)
            ext = VideoFormatToExtension(videoFormat);
        else if (audioFormat != AudioFormat.Unspecified && streamKind == StreamKind.Audio)
            ext = AudioFormatToExtension(audioFormat);
        else
            ext = Path.GetExtension(filePath);

        var outputPath = Path.ChangeExtension(GenerateProcessedPath(filePath, "_proc"), ext);

        output?.Report("Processing (single pass)...");
        var result = await _mediaProcessor.PostProcessSinglePassAsync(
            filePath, outputPath,
            segmentStart: hasSegment ? request.SegmentStart : null,
            segmentDuration: hasSegment ? request.SegmentDuration : null,
            cropValues: validatedCrop,
            videoFormat: videoFormat,
            audioFormat: audioFormat,
            totalDuration: fileDuration,
            progress: ffmpegProgress,
            ct: ct);

        if (result.IsSuccess)
        {
            TryDeleteFile(filePath);
            return result.Value!;
        }

        _logger.LogWarning("Single-pass post-processing failed: {Error}. File returned as-is.", result.Error);
        return filePath;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* best effort cleanup */ }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Selects the best video+audio format pair that fits within the given resolution and size limits.
    /// </summary>
    private static FormatPair? SelectBestFormatWithinLimits(
        IReadOnlyList<FormatInfo> formats,
        StreamKind streamKind,
        int maxResolution,
        int maxFileSizeMb)
    {
        long maxSizeBytes = maxFileSizeMb > 0 ? (long)maxFileSizeMb * 1024 * 1024 : long.MaxValue;

        if (streamKind == StreamKind.Audio)
        {
            // Pick best audio format within size limit
            var bestAudio = formats
                .Where(f => f.StreamKind == StreamKind.Audio)
                .Where(f => maxFileSizeMb <= 0 || (f.FileSize ?? f.ApproximateFileSize ?? 0) <= maxSizeBytes || (f.FileSize ?? f.ApproximateFileSize) is null)
                .OrderByDescending(f => f.AudioBitrate ?? 0)
                .FirstOrDefault();

            return bestAudio is not null ? new FormatPair { AudioFormat = bestAudio } : null;
        }

        // Video formats within resolution limit
        var videoFormats = formats
            .Where(f => f.StreamKind is StreamKind.Video or StreamKind.AudioAndVideo)
            .Where(f => maxResolution <= 0 || (f.Height ?? 0) <= maxResolution)
            .Where(f => maxFileSizeMb <= 0 || (f.FileSize ?? f.ApproximateFileSize ?? 0) <= maxSizeBytes || (f.FileSize ?? f.ApproximateFileSize) is null)
            .OrderByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.VideoBitrate ?? 0)
            .ToList();

        if (videoFormats.Count == 0) return null;

        var bestVideo = videoFormats[0];

        // If the best video format already has audio, use it as-is
        if (bestVideo.StreamKind == StreamKind.AudioAndVideo)
            return new FormatPair { VideoFormat = bestVideo };

        // Otherwise pair with the best audio format
        var bestAudioForPair = formats
            .Where(f => f.StreamKind == StreamKind.Audio)
            .OrderByDescending(f => f.AudioBitrate ?? 0)
            .FirstOrDefault();

        return new FormatPair { VideoFormat = bestVideo, AudioFormat = bestAudioForPair };
    }

    private static bool HasPostProcessing(DownloadRequest request) =>
        request.SegmentStart.HasValue || request.SegmentDuration.HasValue ||
        request.CropValues is not null ||
        (request.ConvertVideo.HasValue && request.ConvertVideo != VideoFormat.Unspecified) ||
        (request.ConvertAudio.HasValue && request.ConvertAudio != AudioFormat.Unspecified);

    /// <summary>
    /// Returns true if the request requires re-encoding (crop or format conversion).
    /// Segment-only requests don't need re-encoding and are better handled by download + local stream-copy.
    /// </summary>
    private static bool NeedsReencoding(DownloadRequest request) =>
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
