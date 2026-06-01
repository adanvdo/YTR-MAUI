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
    public bool EnableHotkeys { get; set; }
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyKey { get; set; } = "D";
}
