using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Registers a global hotkey using Win32 RegisterHotKey/UnregisterHotKey.
/// Uses a hidden message-only window to receive WM_HOTKEY messages.
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    // Modifier flags
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly ILogger<WindowsHotkeyService> _logger;
    private readonly ISettingsService _settings;
    private nint _windowHandle;
    private bool _registered;
    private Thread? _messageThread;
    private volatile bool _running;

    public event Action? HotkeyPressed;

    public WindowsHotkeyService(ILogger<WindowsHotkeyService> logger, ISettingsService settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public bool Register()
    {
        var modifiers = _settings.Download.HotkeyModifiers;
        var key = _settings.Download.HotkeyKey;
        return Register(modifiers, key);
    }

    public bool Register(string modifiers, string key)
    {
        if (_registered) Unregister();

        var modFlag = ParseModifiers(modifiers) | MOD_NOREPEAT;
        var vk = ParseVirtualKey(key);

        if (vk == 0)
        {
            _logger.LogWarning("Invalid hotkey key: {Key}", key);
            return false;
        }

        _running = true;
        var capturedMod = modFlag;
        var capturedVk = vk;

        _messageThread = new Thread(() => MessageLoop(capturedMod, capturedVk))
        {
            IsBackground = true,
            Name = "HotkeyMessageLoop"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        // Give the message loop time to create the window and register
        Thread.Sleep(100);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;

        _running = false;
        if (_windowHandle != 0)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            PostMessage(_windowHandle, 0x0012 /* WM_QUIT */, 0, 0);
        }
        _registered = false;
        _messageThread = null;
    }

    private void MessageLoop(uint modifiers, uint vk)
    {
        // Create a message-only window
        _windowHandle = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, new nint(-3) /* HWND_MESSAGE */, 0, 0, 0);

        if (_windowHandle == 0)
        {
            _logger.LogWarning("Failed to create hotkey message window.");
            return;
        }

        _registered = RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
        if (!_registered)
        {
            _logger.LogWarning("Failed to register hotkey. It may be in use by another application.");
            return;
        }

        _logger.LogInformation("Global hotkey registered.");

        // Message pump
        while (_running && GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY && msg.wParam == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    /// <summary>
    /// Parses a modifier string like "Ctrl+Shift" or "Alt+Ctrl" into Win32 modifier flags.
    /// </summary>
    private static uint ParseModifiers(string modifiers)
    {
        uint flags = 0;
        if (string.IsNullOrWhiteSpace(modifiers)) return flags;

        var parts = modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    flags |= MOD_CTRL;
                    break;
                case "SHIFT":
                    flags |= MOD_SHIFT;
                    break;
                case "ALT":
                    flags |= MOD_ALT;
                    break;
                case "WIN":
                case "WINDOWS":
                    flags |= MOD_WIN;
                    break;
            }
        }
        return flags;
    }

    /// <summary>
    /// Parses a single key string (e.g. "D", "F1", "0") into a Win32 virtual key code.
    /// </summary>
    private static uint ParseVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;

        key = key.Trim().ToUpperInvariant();

        // Single letter A-Z
        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            return (uint)key[0]; // VK_A through VK_Z

        // Single digit 0-9
        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            return (uint)key[0]; // VK_0 through VK_9

        // Function keys F1-F24
        if (key.StartsWith('F') && int.TryParse(key[1..], out int fNum) && fNum >= 1 && fNum <= 24)
            return (uint)(0x6F + fNum); // VK_F1 = 0x70

        return 0;
    }

    public void Dispose()
    {
        Unregister();
    }

    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, int menu, int instance, int param);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, nint hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, int wParam, int lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion
}
