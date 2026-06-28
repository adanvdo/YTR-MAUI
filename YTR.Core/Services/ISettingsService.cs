using YTR.Core.Configuration;

namespace YTR.Core.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
public interface ISettingsService
{
    DownloadOptions Download { get; }
    RestrictionOptions Restrictions { get; }
    ProcessingOptions Processing { get; }
    AppearanceOptions Appearance { get; }
    UpdateOptions Updates { get; }
    HistoryOptions History { get; }
    WindowStateOptions WindowState { get; }

    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates current settings. Returns null if valid, or an error message.
    /// </summary>
    string? Validate();

    /// <summary>
    /// Raised when the dark mode setting changes so other components (e.g. MainLayout) can re-render.
    /// </summary>
    event Action? DarkModeChanged;

    /// <summary>
    /// Call this to notify subscribers that dark mode was toggled.
    /// </summary>
    void NotifyDarkModeChanged();
}
