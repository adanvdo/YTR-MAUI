using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Downloads thumbnails and crops them to a target aspect ratio using SkiaSharp.
/// Returns cropped images as data URIs for direct use in Blazor img elements.
/// </summary>
public sealed class ThumbnailService : IThumbnailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(IHttpClientFactory httpClientFactory, ILogger<ThumbnailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> GetCroppedThumbnailAsync(string thumbnailUrl, double targetAspectRatio, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl) || targetAspectRatio <= 0)
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var imageBytes = await client.GetByteArrayAsync(thumbnailUrl, ct);

            if (imageBytes.Length == 0)
            {
                _logger.LogWarning("Downloaded empty thumbnail from {Url}", thumbnailUrl);
                return null;
            }

            var croppedBytes = CropToAspectRatio(imageBytes, targetAspectRatio);
            if (croppedBytes is null)
                return null;

            var base64 = Convert.ToBase64String(croppedBytes);
            return $"data:image/jpeg;base64,{base64}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download/crop thumbnail from {Url}", thumbnailUrl);
            return null;
        }
    }

    /// <summary>
    /// Crops the image bytes to the target aspect ratio, centering the crop.
    /// Returns JPEG-encoded bytes, or null if decoding fails.
    /// </summary>
    private byte[]? CropToAspectRatio(byte[] imageBytes, double targetAspectRatio)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
        {
            _logger.LogWarning("Failed to decode thumbnail image");
            return null;
        }

        var currentRatio = (double)bitmap.Width / bitmap.Height;

        // If the aspect ratio already matches (within tolerance), just re-encode
        if (Math.Abs(currentRatio - targetAspectRatio) < 0.02)
        {
            using var img = SKImage.FromBitmap(bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }

        SKRectI cropRect;
        if (currentRatio > targetAspectRatio)
        {
            // Thumbnail is wider than target — crop sides
            var newWidth = (int)(bitmap.Height * targetAspectRatio);
            var x = (bitmap.Width - newWidth) / 2;
            cropRect = new SKRectI(x, 0, x + newWidth, bitmap.Height);
        }
        else
        {
            // Thumbnail is taller than target — crop top/bottom
            var newHeight = (int)(bitmap.Width / targetAspectRatio);
            var y = (bitmap.Height - newHeight) / 2;
            cropRect = new SKRectI(0, y, bitmap.Width, y + newHeight);
        }

        using var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
        if (!bitmap.ExtractSubset(cropped, cropRect))
        {
            _logger.LogWarning("Failed to extract subset from thumbnail");
            return null;
        }

        using var image = SKImage.FromBitmap(cropped);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }
}
