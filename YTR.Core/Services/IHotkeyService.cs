namespace YTR.Core.Services;

/// <summary>
/// Registers and manages global hotkeys for quick-download functionality.
/// </summary>
public interface IHotkeyService
{
    /// <summary>
    /// Registers the configured hotkey. Returns true if successful.
    /// </summary>
    bool Register();

    /// <summary>
    /// Registers a hotkey with the specified modifiers and key. Returns true if successful.
    /// </summary>
    bool Register(string modifiers, string key);

    /// <summary>
    /// Unregisters the hotkey.
    /// </summary>
    void Unregister();

    /// <summary>
    /// Fired when the registered hotkey is pressed.
    /// </summary>
    event Action? HotkeyPressed;
}
