namespace YTR.Core.Services;

/// <summary>
/// Manages the system tray icon and context menu (Windows-only).
/// </summary>
public interface ITrayService
{
    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shows a balloon/toast notification from the tray.
    /// </summary>
    void ShowNotification(string title, string message);

    /// <summary>
    /// Fired when the user clicks "Show" from the tray context menu.
    /// </summary>
    event Action? ShowRequested;

    /// <summary>
    /// Fired when the user clicks "Quick Download" from the tray context menu.
    /// </summary>
    event Action? QuickDownloadRequested;

    /// <summary>
    /// Fired when the user clicks "Exit" from the tray context menu.
    /// </summary>
    event Action? ExitRequested;
}
