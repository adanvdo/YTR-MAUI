namespace YTR.Core.Services;

/// <summary>
/// Downloads and processes thumbnails, including cropping to match a target aspect ratio.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Downloads a thumbnail from the given URL and crops it to match the target aspect ratio.
    /// Returns a data URI (data:image/jpeg;base64,...) suitable for use in img src attributes,
    /// or null if the operation fails.
    /// </summary>
    Task<string?> GetCroppedThumbnailAsync(string thumbnailUrl, double targetAspectRatio, CancellationToken ct = default);
}
