namespace YTR.Core.Models;

/// <summary>
/// Validates and converts crop coordinates from user input (top/bottom/left/right margins)
/// to FFmpeg crop filter parameters (x, y, width, height).
/// </summary>
public static class CropHelper
{
    /// <summary>
    /// Converts margin-based crop values [top, bottom, left, right] to FFmpeg crop filter values [x, y, width, height].
    /// Returns null if the crop is invalid.
    /// </summary>
    public static CropResult? ConvertCrop(int[] margins, int videoWidth, int videoHeight)
    {
        if (margins is not { Length: 4 } || videoWidth <= 0 || videoHeight <= 0)
            return null;

        int top = margins[0];
        int bottom = margins[1];
        int left = margins[2];
        int right = margins[3];

        int x = left;
        int y = top;
        int width = videoWidth - left - right;
        int height = videoHeight - top - bottom;

        // Validate resulting dimensions
        if (width < 50 || height < 50)
            return null;
        if (x < 0 || y < 0 || x >= videoWidth || y >= videoHeight)
            return null;
        if (width > videoWidth || height > videoHeight)
            return null;

        return new CropResult(x, y, width, height);
    }

    /// <summary>
    /// Clamps crop margins so the resulting video is at least 50px in each dimension.
    /// </summary>
    public static int[] ClampMargins(int[] margins, int videoWidth, int videoHeight)
    {
        if (margins is not { Length: 4 })
            return [0, 0, 0, 0];

        int top = Math.Max(0, margins[0]);
        int bottom = Math.Max(0, margins[1]);
        int left = Math.Max(0, margins[2]);
        int right = Math.Max(0, margins[3]);

        // Ensure at least 50px remain vertically
        while (videoHeight - top - bottom < 50 && (top > 0 || bottom > 0))
        {
            if (top > bottom) top--;
            else bottom--;
        }

        // Ensure at least 50px remain horizontally
        while (videoWidth - left - right < 50 && (left > 0 || right > 0))
        {
            if (left > right) left--;
            else right--;
        }

        return [top, bottom, left, right];
    }
}

public sealed record CropResult(int X, int Y, int Width, int Height);
