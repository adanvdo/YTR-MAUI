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
    private readonly IAudioTaggingService _audioTagging;
    private readonly ILogger<DownloadOrchestrator> _logger;

    public DownloadOrchestrator(
        IYtDlpService ytDlp,
        IMediaProcessor mediaProcessor,
        IMediaProbeService probe,
        IHistoryService history,
        IUrlAnalyzer urlAnalyzer,
        ISettingsService settings,
        IAudioTaggingService audioTagging,
        ILogger<DownloadOrchestrator> logger)
    {
        _ytDlp = ytDlp;
        _mediaProcessor = mediaProcessor;
        _probe = probe;
        _history = history;
        _urlAnalyzer = urlAnalyzer;
        _settings = settings;
        _audioTagging = audioTagging;
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
        if (request is not null && NeedsExternalProcessing(request))
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
        filePath = await ApplyAudioTaggingAsync(filePath, streamKind, request, output, ct);

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

        // Resolve codec compatibility: if the audio stream isn't compatible with the
        // target container, try to find a compatible audio format from the available streams
        // so yt-dlp can merge natively without ffmpeg transcoding.
        formatPair = ResolveCompatibleFormats(formatPair, request?.AvailableFormats, output);

        var streamKind = formatPair.StreamKind;
        var outputDir = GetOutputDirectory(streamKind, request?.PlaylistFolder);
        EnsureDirectoryExists(outputDir);

        output?.Report("Starting format download...");
        progress?.Report(new DownloadProgress { State = DownloadState.PreProcessing });

        // Strip conversion options that already match the selected format (avoids unnecessary re-encoding)
        var effectiveRequest = request is not null ? StripRedundantConversion(request, formatPair) : request;

        // If post-processing is needed, try stream-based processing first.
        if (effectiveRequest is not null && NeedsExternalProcessing(effectiveRequest))
        {
            var streamResult = await ProcessFromStreamAsync(url, formatPair, streamKind, effectiveRequest, progress, output, ct);
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
        filePath = await PostProcessAsync(filePath, effectiveRequest, streamKind, progress, output, ct);
        filePath = await ApplyAudioTaggingAsync(filePath, streamKind, effectiveRequest, output, ct);

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

        // Determine format string based on stream kind
        string formatString;
        if (formatPair is not null)
        {
            formatString = formatPair.FormatId;
        }
        else if (streamKind == StreamKind.Audio)
        {
            var maxSize = request.MaxFileSizeMb > 0 ? $"[filesize<={request.MaxFileSizeMb}M]" : "";
            formatString = $"bestaudio{maxSize}";
        }
        else
        {
            var maxRes = request.MaxResolutionPixels > 0 ? $"[height<={request.MaxResolutionPixels}]" : "";
            var maxSize = request.MaxFileSizeMb > 0 ? $"[filesize<={request.MaxFileSizeMb}M]" : "";
            formatString = $"bestvideo{maxRes}{maxSize}+bestaudio/best{maxRes}{maxSize}";
        }

        var urlsResult = await _ytDlp.GetFormatUrlsAsync(url, formatString, ct);
        if (urlsResult.IsFailure || urlsResult.Value!.Count == 0)
            return Result<DownloadRecord>.Failure(urlsResult.Error ?? "No stream URLs resolved.");

        var streamUrls = urlsResult.Value!;
        string videoUrl;
        string? audioUrl;

        if (streamKind == StreamKind.Audio)
        {
            // Audio-only: the single URL is the audio stream
            videoUrl = string.Empty;
            audioUrl = streamUrls[0];
        }
        else
        {
            videoUrl = streamUrls[0];
            audioUrl = streamUrls.Count > 1 ? streamUrls[1] : null;
        }

        // Build FFmpeg args for direct stream processing
        output?.Report("Downloading and processing...");
        progress?.Report(new DownloadProgress { State = DownloadState.Downloading, Progress = 0 });

        var opts = _settings.Download;
        var outputDir = GetOutputDirectory(streamKind, request.PlaylistFolder);
        EnsureDirectoryExists(outputDir);

        var fileName = opts.UseTitleAsFileName && !string.IsNullOrWhiteSpace(request.Title)
            ? SanitizeFileName(request.Title)
            : $"{DateTime.Now:MMddyyyyHHmmss}";
        string ext;
        if (streamKind == StreamKind.Audio)
        {
            // Audio-only: use audio format extension
            var targetAudioFormat = request.ConvertAudio ?? AudioFormat.Mp3;
            ext = AudioFormatToExtension(targetAudioFormat);
        }
        else
        {
            var targetFormat = request.ConvertVideo ?? (formatPair?.VideoFormat is not null
                ? CodecMap.GetBestContainerForCodec(formatPair.VideoFormat.VideoCodec)
                : VideoFormat.Mp4);
            ext = targetFormat == VideoFormat.Gif ? ".gif" : VideoFormatToExtension(targetFormat);
        }
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
        filePath = await ApplyAudioTaggingAsync(filePath, streamKind, request, output, ct);
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
        // If only start is provided (no duration), calculate duration from file length
        // If only duration is provided (no start), treat as trimming from beginning
        var effectiveSegmentStart = request.SegmentStart;
        var effectiveSegmentDuration = request.SegmentDuration;
        if (effectiveSegmentStart.HasValue && !effectiveSegmentDuration.HasValue && fileDuration.HasValue)
        {
            effectiveSegmentDuration = fileDuration.Value - effectiveSegmentStart.Value;
            if (effectiveSegmentDuration.Value < TimeSpan.FromSeconds(1))
                effectiveSegmentDuration = TimeSpan.FromSeconds(1);
        }

        bool hasSegment = effectiveSegmentStart.HasValue || effectiveSegmentDuration.HasValue;
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
            bool audioAlreadyMatches = targetAudioExt is null || fileExt == targetAudioExt
                || (targetAudioExt == "m4a" && fileExt == "mp4")
                || (targetAudioExt == "aac" && fileExt is "mp4" or "m4a");

            if (videoAlreadyMatches && audioAlreadyMatches)
            {
                hasConvert = false;
                _logger.LogInformation("Skipping conversion: file is already in target format ({Ext})", fileExt);
            }
        }

        // If only segmenting (no crop, no convert), use fast stream-copy extraction
        if (hasSegment && !hasCrop && !hasConvert)
        {
            var segStart = effectiveSegmentStart ?? TimeSpan.Zero;
            var segDur = effectiveSegmentDuration ?? (fileDuration.HasValue ? fileDuration.Value - segStart : (TimeSpan?)null);

            if (segDur.HasValue)
            {
                var segOutput = GenerateProcessedPath(filePath, "_seg");
                output?.Report("Extracting segment (stream copy)...");
                var segResult = await _mediaProcessor.ExtractSegmentAsync(
                    filePath, segStart, segDur.Value,
                    segOutput, progress: ffmpegProgress, ct: ct);

                if (segResult.IsSuccess)
                {
                    TryDeleteFile(filePath);
                    return segResult.Value!;
                }

                _logger.LogWarning("Segment extraction failed: {Error}", segResult.Error);
                return filePath;
            }
        }

        // If nothing left to process after skipping redundant conversions, return as-is
        if (!hasSegment && !hasCrop && !hasConvert)
        {
            _logger.LogInformation("No processing needed after redundancy check, returning file as-is");
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
            segmentStart: hasSegment ? effectiveSegmentStart : null,
            segmentDuration: hasSegment ? effectiveSegmentDuration : null,
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

    /// <summary>
    /// Applies audio tagging (metadata embedding, album art, filename rename) if the settings and file format allow.
    /// </summary>
    private async Task<string> ApplyAudioTaggingAsync(
        string filePath,
        StreamKind streamKind,
        DownloadRequest? request,
        IProgress<string>? output,
        CancellationToken ct)
    {
        // Only tag audio downloads
        if (streamKind != StreamKind.Audio)
            return filePath;

        // Only tag if the file format supports it
        if (!_audioTagging.SupportsTagging(filePath))
            return filePath;

        var prefs = _settings.AudioPreferences;
        bool shouldEmbedMeta = prefs.EmbedMetadata;
        bool shouldEmbedArt = prefs.UseVideoThumbnailAsAlbumArt;
        bool shouldRename = prefs.UseArtistTrackFilename;

        if (!shouldEmbedMeta && !shouldEmbedArt && !shouldRename)
            return filePath;

        output?.Report("Tagging audio file...");

        var tagRequest = new AudioTagRequest
        {
            FilePath = filePath,
            Title = request?.Title,
            Artist = request?.Uploader,
            ThumbnailUrl = shouldEmbedArt ? request?.ThumbnailUrl : null,
            VideoAspectRatio = request?.VideoAspectRatio,
            EmbedThumbnail = shouldEmbedArt && !string.IsNullOrWhiteSpace(request?.ThumbnailUrl),
            EmbedMetadata = shouldEmbedMeta,
            UseArtistTrackFilename = shouldRename
        };

        try
        {
            var result = await _audioTagging.TagAsync(tagRequest, ct);
            if (result != filePath)
                output?.Report($"Renamed to: {Path.GetFileName(result)}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio tagging failed for {Path}", filePath);
            return filePath;
        }
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
        request.ConvertVideo == VideoFormat.Gif;

    /// <summary>
    /// Returns true if the request needs processing via ffmpeg that can't be handled by yt-dlp natively.
    /// yt-dlp handles --merge-output-format and --recode-video for format conversion,
    /// so pure format conversion (without segment/crop) should not trigger ffmpeg processing.
    /// </summary>
    private static bool NeedsExternalProcessing(DownloadRequest request) =>
        request.SegmentStart.HasValue || request.SegmentDuration.HasValue ||
        request.CropValues is not null ||
        request.ConvertVideo == VideoFormat.Gif;

    /// <summary>
    /// Removes conversion options from the request when the selected formats already match
    /// the target format, preventing unnecessary re-encoding.
    /// </summary>
    private static DownloadRequest StripRedundantConversion(DownloadRequest request, FormatPair formatPair)
    {
        var convertVideo = request.ConvertVideo;
        var convertAudio = request.ConvertAudio;
        bool changed = false;

        // Check if video conversion matches the selected video format's extension
        if (convertVideo.HasValue && convertVideo != VideoFormat.Unspecified && formatPair.VideoFormat is not null)
        {
            var targetExt = VideoFormatToExtension(convertVideo.Value).TrimStart('.');
            var sourceExt = formatPair.VideoFormat.Extension?.TrimStart('.').ToLowerInvariant();
            if (string.Equals(targetExt, sourceExt, StringComparison.OrdinalIgnoreCase))
            {
                convertVideo = null;
                changed = true;
            }
        }

        // Check if audio conversion matches the selected audio format's extension
        if (convertAudio.HasValue && convertAudio != AudioFormat.Unspecified && formatPair.AudioFormat is not null)
        {
            var targetExt = AudioFormatToExtension(convertAudio.Value).TrimStart('.');
            var sourceExt = formatPair.AudioFormat.Extension?.TrimStart('.').ToLowerInvariant();
            if (string.Equals(targetExt, sourceExt, StringComparison.OrdinalIgnoreCase))
            {
                convertAudio = null;
                changed = true;
            }
        }

        // For video+audio merge: if converting to mp4 and both streams are mp4/m4a compatible, skip
        if (convertVideo.HasValue && convertVideo == VideoFormat.Mp4
            && formatPair.VideoFormat is not null && formatPair.AudioFormat is not null)
        {
            var videoExt = formatPair.VideoFormat.Extension?.TrimStart('.').ToLowerInvariant();
            var audioExt = formatPair.AudioFormat.Extension?.TrimStart('.').ToLowerInvariant();
            // mp4 container natively holds h264/h265 video + aac/m4a audio
            if (videoExt is "mp4" && audioExt is "m4a" or "mp4" or "aac")
            {
                convertVideo = null;
                changed = true;
            }
        }

        return changed ? request with { ConvertVideo = convertVideo, ConvertAudio = convertAudio } : request;
    }

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

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        // Also restrict filenames similar to yt-dlp's --restrict-filenames
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^\w\-.]", "_");
        return sanitized.Length > 200 ? sanitized[..200] : sanitized;
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

    #region Format Compatibility Resolution

    /// <summary>
    /// Checks if the selected format pair has codec compatibility issues and attempts to
    /// swap the incompatible stream with a compatible alternative from the available formats.
    /// This avoids ffmpeg transcoding by using a natively-compatible stream that yt-dlp can merge.
    /// </summary>
    private FormatPair ResolveCompatibleFormats(
        FormatPair formatPair,
        IReadOnlyList<FormatInfo>? availableFormats,
        IProgress<string>? output)
    {
        // Only relevant for video+audio pairs where we're merging two streams
        if (formatPair.VideoFormat is null || formatPair.AudioFormat is null)
            return formatPair;

        // Determine the target container from the video stream
        var videoCodecName = formatPair.VideoFormat.VideoCodec;
        var targetContainer = CodecMap.GetBestContainerForCodec(videoCodecName);

        // Check if the selected audio codec is compatible with that container
        var audioCodecName = formatPair.AudioFormat.AudioCodec;
        var audioCodecId = CodecMap.ParseAudioCodecName(audioCodecName);

        if (audioCodecId != AudioCodecId.None)
        {
            var audioCodec = GetFfmpegCodecById(audioCodecId);
            if (audioCodec is not null && CodecMap.IsAudioCodecCompatible(audioCodec, targetContainer))
                return formatPair; // Already compatible, no swap needed
        }
        else
        {
            // Unknown codec — assume it might be compatible, don't force a swap
            return formatPair;
        }

        // Audio is incompatible. Try to find a better audio stream from available formats.
        if (availableFormats is null || availableFormats.Count == 0)
        {
            _logger.LogDebug("Audio codec '{AudioCodec}' is incompatible with {Container}, but no available formats to swap from",
                audioCodecName, targetContainer);
            return formatPair;
        }

        // Get the compatible audio codecs for the target container
        var compatibleAudioCodecs = CodecMap.GetAudioCodecs(targetContainer);

        // Find the best compatible audio stream: highest bitrate audio-only format
        // whose codec matches one of the container's compatible codecs
        var compatibleAudio = availableFormats
            .Where(f => f.StreamKind == StreamKind.Audio)
            .Where(f =>
            {
                var id = CodecMap.ParseAudioCodecName(f.AudioCodec);
                if (id == AudioCodecId.None) return false;
                var codec = GetFfmpegCodecById(id);
                return codec is not null && compatibleAudioCodecs.Contains(codec);
            })
            .OrderByDescending(f => f.AudioBitrate ?? 0)
            .FirstOrDefault();

        if (compatibleAudio is not null)
        {
            _logger.LogInformation(
                "Swapped incompatible audio '{OldCodec}' (format {OldId}) → '{NewCodec}' (format {NewId}) for {Container} compatibility",
                audioCodecName, formatPair.AudioFormat.FormatId,
                compatibleAudio.AudioCodec, compatibleAudio.FormatId, targetContainer);
            output?.Report($"Auto-selected compatible audio: {compatibleAudio.AudioCodec} ({compatibleAudio.FormatId})");

            return formatPair with { AudioFormat = compatibleAudio };
        }

        _logger.LogDebug("No compatible audio format found in available streams for {Container}, ffmpeg will transcode",
            targetContainer);
        return formatPair;
    }

    private static FfmpegCodec? GetFfmpegCodecById(AudioCodecId id) => id switch
    {
        AudioCodecId.Aac => CodecMap.Aac,
        AudioCodecId.Mp3 => CodecMap.Mp3,
        AudioCodecId.Opus => CodecMap.Opus,
        AudioCodecId.Vorbis => CodecMap.Vorbis,
        AudioCodecId.Flac => CodecMap.Flac,
        AudioCodecId.Wav => CodecMap.Wav,
        _ => null
    };

    #endregion
}
