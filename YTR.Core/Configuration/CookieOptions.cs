using YTR.Core.Enums;

namespace YTR.Core.Configuration;

/// <summary>
/// Settings for yt-dlp cookie handling (--cookies-from-browser / --cookies).
/// </summary>
public sealed class CookieOptions
{
    public CookieMode Mode { get; set; } = CookieMode.Disabled;
    public CookieBrowser Browser { get; set; } = CookieBrowser.Chrome;
    public string CookieFilePath { get; set; } = string.Empty;
}

