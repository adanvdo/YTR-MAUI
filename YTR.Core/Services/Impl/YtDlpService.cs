using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YTR.Core.Configuration;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Invokes yt-dlp as an external process to fetch metadata and download media.
/// </summary>
public sealed partial class YtDlpService : IYtDlpService
{
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platform;
    private readonly IOptions<DownloadOptions> _downloadOptions;
    private readonly IOptions<RestrictionOptions> _restrictionOptions;
    private readonly IOptions<ProcessingOptions> _processingOptions;
    private readonly ILogger<YtDlpService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public YtDlpService(
        IProcessRunner processRunner,
        IPlatformService platform,
        IOptions<DownloadOptions> downloadOptions,
        IOptions<RestrictionOptions> restrictionOptions,
        IOptions<ProcessingOptions> processingOptions,
        ILogger<YtDlpService> logger)
    {
        _processRunner = processRunner;
        _platform = platform;
        _downloadOptions = downloadOptions;
        _restrictionOptions = restrictionOptions;
        _processingOptions = processingOptions;
        _logger = logger;
    }

    private string YtDlpPath => _platform.GetResourcePath("yt-dlp");

    public async Task<Result<MediaMetadata>> GetMediaInfoAsync(string url, CancellationToken ct = default)
    {
        var args = $"--dump-json --no-playlist \"{url}\"";
        var result = await _processRunner.RunAsync(new ProcessRequest
        {
            Executable = YtDlpPath,
            Arguments = args
        }, ct);

        if (result.WasCancelled)
            return Result<MediaMetadata>.Failure("Operation cancelled.");

        if (!result.Success)
            return Result<MediaMetadata>.Failure($"yt-dlp failed: {result.StandardError.Trim()}");

        try
        {
            var metadata = ParseVideoJson(result.StandardOutput.Trim());
            return Result<MediaMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse yt-dlp JSON output for {Url}", url);
            return Result<MediaMetadata>.Failure($"Failed to parse metadata: {ex.Message}");
        }
    }

    public async Task<Result<MediaMetadata>> GetPlaylistInfoAsync(string url, CancellationToken ct = default)
    {
        var args = $"--flat-playlist --dump-single-json \"{url}\"";
        var result = await _processRunner.RunAsync(new ProcessRequest
        {
            Executable = YtDlpPath,
            Arguments = args
        }, ct);

        if (result.WasCancelled)
            return Result<MediaMetadata>.Failure("Operation cancelled.");

        if (!result.Success)
            return Result<MediaMetadata>.Failure($"yt-dlp failed: {result.StandardError.Trim()}");

        try
        {
            var metadata = ParsePlaylistJson(result.StandardOutput.Trim());
            return Result<MediaMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse playlist JSON for {Url}", url);
            return Result<MediaMetadata>.Failure($"Failed to parse playlist: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetFormatUrlsAsync(string url, string format, CancellationToken ct = default)
    {
        var args = $"--get-url -f \"{format}\" \"{url}\"";
        var result = await _processRunner.RunAsync(new ProcessRequest
        {
            Executable = YtDlpPath,
            Arguments = args
        }, ct);

        if (result.WasCancelled)
            return Result<IReadOnlyList<string>>.Failure("Operation cancelled.");

        if (!result.Success)
            return Result<IReadOnlyList<string>>.Failure($"yt-dlp failed: {result.StandardError.Trim()}");

        var urls = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return urls.Count > 0
            ? Result<IReadOnlyList<string>>.Success(urls)
            : Result<IReadOnlyList<string>>.Failure("No URLs returned by yt-dlp.");
    }

    public async Task<Result<string>> DownloadBestAsync(
        string url,
        StreamKind streamKind,
        int maxResolution = 0,
        int maxFileSizeMb = 0,
        string? outputPath = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        var opts = _downloadOptions.Value;
        var outputDir = outputPath ?? (streamKind == StreamKind.Audio
            ? opts.AudioDownloadPath
            : opts.VideoDownloadPath);

        var outputTemplate = opts.UseTitleAsFileName
            ? Path.Combine(outputDir, "%(title)s.%(ext)s")
            : Path.Combine(outputDir, $"{DateTime.Now:MMddyyyyHHmmss}.%(ext)s");

        string formatString;
        if (streamKind == StreamKind.Audio)
        {
            formatString = "bestaudio/best";
            if (maxFileSizeMb > 0)
                formatString = $"bestaudio[filesize<={maxFileSizeMb}M]/bestaudio/best";
        }
        else
        {
            var resFilter = maxResolution > 0 ? $"[height<={maxResolution}]" : "";
            var sizeFilter = maxFileSizeMb > 0 ? $"[filesize<={maxFileSizeMb}M]" : "";
            formatString = $"bestvideo{resFilter}{sizeFilter}+bestaudio/best{resFilter}{sizeFilter}";
        }

        var args = BuildDownloadArgs(url, formatString, outputTemplate, streamKind, embedThumbnail: streamKind == StreamKind.Audio);

        string? downloadedFile = null;
        var request = new ProcessRequest
        {
            Executable = YtDlpPath,
            Arguments = args,
            OnOutputLine = line =>
            {
                output?.Report(line);
                ParseProgressLine(line, progress);
                var filePath = ExtractFilePath(line);
                if (filePath is not null)
                    downloadedFile = filePath;
            }
        };

        var result = await _processRunner.RunAsync(request, ct);

        if (result.WasCancelled)
            return Result<string>.Failure("Download cancelled.");

        if (!result.Success)
            return Result<string>.Failure($"Download failed: {result.StandardError.Trim()}");

        // Try to find the output file from stdout if not captured via progress
        downloadedFile ??= ExtractFilePathFromOutput(result.StandardOutput);

        return downloadedFile is not null
            ? Result<string>.Success(downloadedFile)
            : Result<string>.Failure("Download completed but output file path could not be determined.");
    }

    public async Task<Result<string>> DownloadFormatAsync(
        string url,
        string formatId,
        StreamKind streamKind,
        string? outputPath = null,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        var opts = _downloadOptions.Value;
        var outputDir = outputPath ?? (streamKind == StreamKind.Audio
            ? opts.AudioDownloadPath
            : opts.VideoDownloadPath);

        var outputTemplate = opts.UseTitleAsFileName
            ? Path.Combine(outputDir, "%(title)s.%(ext)s")
            : Path.Combine(outputDir, $"{DateTime.Now:MMddyyyyHHmmss}.%(ext)s");

        var args = BuildDownloadArgs(url, formatId, outputTemplate, streamKind);

        string? downloadedFile = null;
        var request = new ProcessRequest
        {
            Executable = YtDlpPath,
            Arguments = args,
            OnOutputLine = line =>
            {
                output?.Report(line);
                ParseProgressLine(line, progress);
                var filePath = ExtractFilePath(line);
                if (filePath is not null)
                    downloadedFile = filePath;
            }
        };

        var result = await _processRunner.RunAsync(request, ct);

        if (result.WasCancelled)
            return Result<string>.Failure("Download cancelled.");

        if (!result.Success)
            return Result<string>.Failure($"Download failed: {result.StandardError.Trim()}");

        downloadedFile ??= ExtractFilePathFromOutput(result.StandardOutput);

        return downloadedFile is not null
            ? Result<string>.Success(downloadedFile)
            : Result<string>.Failure("Download completed but output file path could not be determined.");
    }

    #region Argument Building

    private string BuildDownloadArgs(string url, string format, string outputTemplate, StreamKind streamKind, bool embedThumbnail = false, VideoFormat? mergeFormat = null)
    {
        var args = new List<string>
        {
            $"-f \"{format}\"",
            $"-o \"{outputTemplate}\"",
            "--no-playlist",
            "--newline",
            "--restrict-filenames"
        };

        // Merge output format (forces container for merged video+audio)
        if (mergeFormat.HasValue && mergeFormat != VideoFormat.Unspecified && mergeFormat != VideoFormat.Gif)
        {
            args.Add($"--merge-output-format {VideoFormatToString(mergeFormat.Value)}");
        }

        // Preferred format conversion via yt-dlp (for "Download Preferred" flow)
        if (streamKind == StreamKind.Audio)
        {
            var preferred = _processingOptions.Value.PreferredAudioFormat;
            if (_processingOptions.Value.AlwaysConvertToPreferred && preferred != AudioFormat.Unspecified)
            {
                args.Add($"-x --audio-format {AudioFormatToString(preferred)}");
            }

            // Embed thumbnail for audio downloads (not supported for opus/aac)
            if (embedThumbnail && preferred != AudioFormat.Opus && preferred != AudioFormat.Aac)
            {
                args.Add("--embed-thumbnail");
                args.Add("--add-metadata");
                args.Add("--ppa \"ffmpeg:-write_id3v1 1 -id3v2_version 3\"");
                args.Add("--convert-thumbnails jpg");
            }
        }
        else if (_processingOptions.Value.AlwaysConvertToPreferred)
        {
            var preferredVideo = _processingOptions.Value.PreferredVideoFormat;
            if (preferredVideo != VideoFormat.Unspecified && preferredVideo != VideoFormat.Gif)
            {
                args.Add($"--recode-video {VideoFormatToString(preferredVideo)}");
                args.Add($"--merge-output-format {VideoFormatToString(preferredVideo)}");
            }
        }

        if (_processingOptions.Value.VerboseOutput)
            args.Add("--verbose");

        args.Add($"\"{url}\"");

        return string.Join(" ", args);
    }

    private static string AudioFormatToString(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => "mp3",
        AudioFormat.M4a => "m4a",
        AudioFormat.Aac => "aac",
        AudioFormat.Ogg => "vorbis",
        AudioFormat.Wav => "wav",
        AudioFormat.Flac => "flac",
        AudioFormat.Opus => "opus",
        AudioFormat.Vorbis => "vorbis",
        _ => "best"
    };

    private static string VideoFormatToString(VideoFormat format) => format switch
    {
        VideoFormat.Mp4 => "mp4",
        VideoFormat.Mkv => "mkv",
        VideoFormat.Webm => "webm",
        VideoFormat.Flv => "flv",
        VideoFormat.Ogg => "ogg",
        _ => "mp4"
    };

    #endregion

    #region Output Parsing

    private static void ParseProgressLine(string line, IProgress<DownloadProgress>? progress)
    {
        if (progress is null) return;

        // yt-dlp progress format: [download]  45.2% of ~50.00MiB at 5.00MiB/s ETA 00:05
        var match = ProgressRegex().Match(line);
        if (match.Success)
        {
            if (double.TryParse(match.Groups["pct"].Value, out var pct))
            {
                progress.Report(new DownloadProgress
                {
                    State = DownloadState.Downloading,
                    Progress = pct / 100.0,
                    Speed = match.Groups["speed"].Success ? match.Groups["speed"].Value : null,
                    Eta = match.Groups["eta"].Success ? match.Groups["eta"].Value : null
                });
            }
        }
        else if (line.Contains("[Merger]") || line.Contains("[ExtractAudio]") || line.Contains("[ffmpeg]"))
        {
            progress.Report(new DownloadProgress
            {
                State = DownloadState.PostProcessing,
                Progress = 1.0
            });
        }
    }

    private static string? ExtractFilePath(string line)
    {
        // [download] Destination: /path/to/file.ext
        // [Merger] Merging formats into "/path/to/file.ext"
        // [ExtractAudio] Destination: /path/to/file.ext

        var destMatch = DestinationRegex().Match(line);
        if (destMatch.Success)
            return destMatch.Groups["path"].Value.Trim();

        var mergerMatch = MergerRegex().Match(line);
        if (mergerMatch.Success)
            return mergerMatch.Groups["path"].Value.Trim().Trim('"');

        return null;
    }

    private static string? ExtractFilePathFromOutput(string output)
    {
        // Search backwards through lines for the last file path
        var lines = output.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var path = ExtractFilePath(lines[i]);
            if (path is not null && !path.Contains(".part"))
                return path;
        }
        return null;
    }

    private static MediaMetadata ParseVideoJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var formats = new List<FormatInfo>();
        if (root.TryGetProperty("formats", out var formatsEl))
        {
            foreach (var fmt in formatsEl.EnumerateArray())
            {
                var formatId = fmt.GetPropertyOrDefault("format_id", "");
                if (string.IsNullOrEmpty(formatId)) continue;

                // Skip storyboard/thumbnail formats
                if (formatId.StartsWith("sb") || formatId.StartsWith("http-"))
                    continue;

                formats.Add(new FormatInfo
                {
                    FormatId = formatId,
                    Format = fmt.GetPropertyOrDefault("format", null),
                    FormatNote = fmt.GetPropertyOrDefault("format_note", null),
                    Extension = fmt.GetPropertyOrDefault("ext", null),
                    Url = fmt.GetPropertyOrDefault("url", null),
                    Width = fmt.GetIntOrDefault("width"),
                    Height = fmt.GetIntOrDefault("height"),
                    FrameRate = fmt.GetFloatOrDefault("fps"),
                    VideoCodec = fmt.GetPropertyOrDefault("vcodec", null),
                    VideoBitrate = fmt.GetDoubleOrDefault("vbr"),
                    AudioCodec = fmt.GetPropertyOrDefault("acodec", null),
                    AudioBitrate = fmt.GetDoubleOrDefault("abr"),
                    AudioSamplingRate = fmt.GetDoubleOrDefault("asr"),
                    FileSize = fmt.GetLongOrDefault("filesize"),
                    ApproximateFileSize = fmt.GetLongOrDefault("filesize_approx"),
                    Protocol = fmt.GetPropertyOrDefault("protocol", null),
                    ContainerFormat = fmt.GetPropertyOrDefault("container", null)
                });
            }
        }

        var durationSec = root.GetDoubleOrDefault("duration");
        TimeSpan? duration = durationSec.HasValue ? TimeSpan.FromSeconds(durationSec.Value) : null;

        // Get best thumbnail
        string? thumbnailUrl = root.GetPropertyOrDefault("thumbnail", null);
        if (thumbnailUrl is null && root.TryGetProperty("thumbnails", out var thumbs))
        {
            var lastThumb = thumbs.EnumerateArray().LastOrDefault();
            if (lastThumb.ValueKind != JsonValueKind.Undefined)
                thumbnailUrl = lastThumb.GetPropertyOrDefault("url", null);
        }

        return new MediaMetadata
        {
            Id = root.GetPropertyOrDefault("id", "unknown") ?? "unknown",
            Url = root.GetPropertyOrDefault("webpage_url", null) ?? root.GetPropertyOrDefault("url", "") ?? "",
            Title = root.GetPropertyOrDefault("title", null),
            Description = root.GetPropertyOrDefault("description", null),
            Duration = duration,
            ThumbnailUrl = thumbnailUrl,
            Uploader = root.GetPropertyOrDefault("uploader", null),
            Platform = MediaPlatform.Unknown, // Caller can set this from UrlAnalyzer
            Formats = formats
        };
    }

    private static MediaMetadata ParsePlaylistJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var items = new List<PlaylistItem>();
        if (root.TryGetProperty("entries", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var id = entry.GetPropertyOrDefault("id", null);
                var url = entry.GetPropertyOrDefault("url", null);
                var title = entry.GetPropertyOrDefault("title", null);

                if (id is null || url is null) continue;
                if (title?.Equals("[Deleted video]", StringComparison.OrdinalIgnoreCase) == true) continue;
                if (title?.Equals("[Private video]", StringComparison.OrdinalIgnoreCase) == true) continue;

                var durSec = entry.GetDoubleOrDefault("duration");

                items.Add(new PlaylistItem
                {
                    Id = id,
                    Url = url,
                    Title = title,
                    Duration = durSec.HasValue ? TimeSpan.FromSeconds(durSec.Value) : null,
                    ThumbnailUrl = entry.GetPropertyOrDefault("thumbnail", null)
                });
            }
        }

        return new MediaMetadata
        {
            Id = root.GetPropertyOrDefault("id", "playlist") ?? "playlist",
            Url = root.GetPropertyOrDefault("webpage_url", "") ?? "",
            Title = root.GetPropertyOrDefault("title", null),
            Description = root.GetPropertyOrDefault("description", null),
            Platform = MediaPlatform.Unknown,
            IsPlaylist = true,
            PlaylistItems = items,
            Formats = []
        };
    }

    #endregion

    #region Regex

    [GeneratedRegex(@"\[download\]\s+(?<pct>[\d.]+)%.*?(?:at\s+(?<speed>\S+))?.*?(?:ETA\s+(?<eta>\S+))?")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"\[download\] Destination:\s*(?<path>.+)")]
    private static partial Regex DestinationRegex();

    [GeneratedRegex(@"\[Merger\] Merging formats into\s*""?(?<path>[^""]+)""?")]
    private static partial Regex MergerRegex();

    #endregion
}

/// <summary>
/// Extension methods for JsonElement to simplify parsing.
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement el, string name, string? defaultValue)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return defaultValue;
    }

    public static int? GetIntOrDefault(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                return val;
        }
        return null;
    }

    public static float? GetFloatOrDefault(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return (float)prop.GetDouble();
        }
        return null;
    }

    public static double? GetDoubleOrDefault(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDouble();
        }
        return null;
    }

    public static long? GetLongOrDefault(this JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val))
                return val;
        }
        return null;
    }
}
