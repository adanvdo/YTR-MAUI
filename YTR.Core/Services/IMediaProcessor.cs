using YTR.Core.Enums;

namespace YTR.Core.Services;

/// <summary>
/// Abstraction over FFmpeg media processing operations.
/// </summary>
public interface IMediaProcessor
{
    /// <summary>
    /// Extracts a segment from a media file.
    /// </summary>
    Task<Result<string>> ExtractSegmentAsync(
        string inputPath,
        TimeSpan start,
        TimeSpan duration,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Crops a video to the specified dimensions.
    /// </summary>
    Task<Result<string>> CropAsync(
        string inputPath,
        int x, int y, int width, int height,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Converts media to a different format.
    /// </summary>
    Task<Result<string>> ConvertAsync(
        string inputPath,
        VideoFormat videoFormat,
        AudioFormat audioFormat,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Converts a video to an animated GIF.
    /// </summary>
    Task<Result<string>> ConvertToGifAsync(
        string inputPath,
        string outputPath,
        TimeSpan? start = null,
        TimeSpan? duration = null,
        int maxSize = 600,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Concatenates two media files.
    /// </summary>
    Task<Result<string>> ConcatenateAsync(
        string firstPath,
        string secondPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Processes media directly from stream URLs without downloading first.
    /// Combines segment extraction, crop, and format conversion in a single FFmpeg pass.
    /// </summary>
    Task<Result<string>> ConvertFromUrlsAsync(
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
        CancellationToken ct = default);
}
