using YTR.Core.Enums;

namespace YTR.Core.Configuration;

/// <summary>
/// Settings for UI appearance and layout.
/// </summary>
public sealed class AppearanceOptions
{
    public bool DarkMode { get; set; } = true;
    public FormatMode FormatMode { get; set; } = FormatMode.Custom;
    public SegmentMode SegmentMode { get; set; } = SegmentMode.EndTime;
}
