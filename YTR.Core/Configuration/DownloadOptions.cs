namespace YTR.Core.Configuration;

/// <summary>
/// Settings related to download paths and filename behavior.
/// </summary>
public sealed class DownloadOptions
{
    public string VideoDownloadPath { get; set; } = string.Empty;
    public string AudioDownloadPath { get; set; } = string.Empty;
    public bool UseTitleAsFileName { get; set; }
    public bool CreateFolderForPlaylists { get; set; } = true;
    public bool AutoOpenDownloadLocation { get; set; } = true;
    public bool EnableHotkeys { get; set; }
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyKey { get; set; } = "D";
}
