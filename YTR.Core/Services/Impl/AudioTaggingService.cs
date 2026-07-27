using Microsoft.Extensions.Logging;
using SkiaSharp;
using TagLib;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Embeds metadata and album art into audio files using TagLibSharp.
/// Supports MP3, M4A, FLAC, OGG, and other formats that TagLib can handle.
/// </summary>
public sealed class AudioTaggingService : IAudioTaggingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AudioTaggingService> _logger;

    // Extensions that TagLib can reliably write metadata to
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".wma", ".aac"
    };

    public AudioTaggingService(IHttpClientFactory httpClientFactory, ILogger<AudioTaggingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool SupportsTagging(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public async Task<string> TagAsync(AudioTagRequest request, CancellationToken ct = default)
    {
        if (!System.IO.File.Exists(request.FilePath))
        {
            _logger.LogWarning("Audio file not found for tagging: {Path}", request.FilePath);
            return request.FilePath;
        }

        if (!SupportsTagging(request.FilePath))
        {
            _logger.LogDebug("File format does not support tagging: {Path}", request.FilePath);
            return request.FilePath;
        }

        var currentPath = request.FilePath;

        try
        {
            using var tagFile = TagLib.File.Create(currentPath);

            // Embed metadata tags
            if (request.EmbedMetadata)
            {
                if (!string.IsNullOrWhiteSpace(request.Title))
                    tagFile.Tag.Title = request.Title.Trim();

                if (!string.IsNullOrWhiteSpace(request.Artist))
                {
                    tagFile.Tag.Performers = [request.Artist.Trim()];
                    tagFile.Tag.AlbumArtists = [request.Artist.Trim()];
                }

                if (!string.IsNullOrWhiteSpace(request.Album))
                    tagFile.Tag.Album = request.Album.Trim();

                if (request.Year.HasValue && request.Year.Value > 0)
                    tagFile.Tag.Year = (uint)request.Year.Value;
            }

            // Embed thumbnail as album art
            if (request.EmbedThumbnail && !string.IsNullOrWhiteSpace(request.ThumbnailUrl))
            {
                var imageBytes = await DownloadThumbnailAsync(request.ThumbnailUrl, ct);
                if (imageBytes is not null && imageBytes.Length > 0)
                {
                    // Crop to video aspect ratio if needed
                    if (request.VideoAspectRatio.HasValue && request.VideoAspectRatio.Value > 0)
                    {
                        imageBytes = CropToAspectRatio(imageBytes, request.VideoAspectRatio.Value) ?? imageBytes;
                    }

                    // Write to temp file (some TagLib backends need this)
                    var tempImagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
                    try
                    {
                        await System.IO.File.WriteAllBytesAsync(tempImagePath, imageBytes, ct);

                        var cover = new TagLib.Id3v2.AttachmentFrame
                        {
                            Type = PictureType.FrontCover,
                            Description = "Cover",
                            MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg,
                            Data = new ByteVector(imageBytes),
                            TextEncoding = TagLib.StringType.UTF16
                        };
                        tagFile.Tag.Pictures = new IPicture[] { cover };
                        _logger.LogDebug("Embedded album art ({Size} bytes) into {Path}", imageBytes.Length, currentPath);
                    }
                    finally
                    {
                        if (System.IO.File.Exists(tempImagePath))
                        {
                            try { System.IO.File.Delete(tempImagePath); }
                            catch { /* best effort cleanup */ }
                        }
                    }
                }
            }

            tagFile.Save();
            _logger.LogInformation("Tags written to {Path}", currentPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write tags to {Path}", currentPath);
            return currentPath;
        }

        // Rename file to [Artist - Title] format if requested
        if (request.UseArtistTrackFilename
            && !string.IsNullOrWhiteSpace(request.Artist)
            && !string.IsNullOrWhiteSpace(request.Title))
        {
            currentPath = RenameToArtistTrack(currentPath, request.Artist.Trim(), request.Title.Trim());
        }

        return currentPath;
    }

    private async Task<byte[]?> DownloadThumbnailAsync(string url, CancellationToken ct)
    {
        try
        {
            // Handle data URIs (from cropped thumbnails) directly
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = url.IndexOf(',');
                if (commaIdx < 0) return null;
                var base64 = url[(commaIdx + 1)..];
                return Convert.FromBase64String(base64);
            }

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download thumbnail from {Url}: {Status}", url, response.StatusCode);
                return null;
            }

            var data = await response.Content.ReadAsByteArrayAsync(ct);
            if (data.Length == 0)
            {
                _logger.LogWarning("Thumbnail download returned empty content from {Url}", url);
                return null;
            }

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error downloading thumbnail from {Url}", url);
            return null;
        }
    }

    private static string RenameToArtistTrack(string filePath, string artist, string title)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var ext = Path.GetExtension(filePath);

        // Sanitize the filename
        var newName = SanitizeFileName($"{artist} - {title}");
        var newPath = Path.Combine(dir, $"{newName}{ext}");

        // Avoid overwriting existing files
        if (string.Equals(filePath, newPath, StringComparison.OrdinalIgnoreCase))
            return filePath;

        if (System.IO.File.Exists(newPath))
        {
            // Append a numeric suffix to avoid collision
            var counter = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{newName} ({counter}){ext}");
                counter++;
            } while (System.IO.File.Exists(candidate));
            newPath = candidate;
        }

        System.IO.File.Move(filePath, newPath);
        return newPath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        }
        return new string(sanitized).Trim();
    }

    /// <summary>
    /// Crops the image to match the target aspect ratio, centering the crop.
    /// Returns JPEG-encoded bytes, or null if decoding fails.
    /// </summary>
    private byte[]? CropToAspectRatio(byte[] imageBytes, double targetAspectRatio)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null) return null;

        var currentRatio = (double)bitmap.Width / bitmap.Height;

        // If already matching, just re-encode as JPEG
        if (Math.Abs(currentRatio - targetAspectRatio) < 0.02)
        {
            using var img = SKImage.FromBitmap(bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }

        SKRectI cropRect;
        if (currentRatio > targetAspectRatio)
        {
            // Wider than target — crop sides
            var newWidth = (int)(bitmap.Height * targetAspectRatio);
            var x = (bitmap.Width - newWidth) / 2;
            cropRect = new SKRectI(x, 0, x + newWidth, bitmap.Height);
        }
        else
        {
            // Taller than target — crop top/bottom
            var newHeight = (int)(bitmap.Width / targetAspectRatio);
            var y = (bitmap.Height - newHeight) / 2;
            cropRect = new SKRectI(0, y, bitmap.Width, y + newHeight);
        }

        using var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
        if (!bitmap.ExtractSubset(cropped, cropRect)) return null;

        using var image = SKImage.FromBitmap(cropped);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }
}
