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

    // Modifiers
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;

    // Default: Ctrl+Shift+D
    private const uint DEFAULT_MODIFIERS = MOD_CTRL | MOD_SHIFT | MOD_NOREPEAT;
    private const uint DEFAULT_VK = 0x44; // 'D'

    private readonly ILogger<WindowsHotkeyService> _logger;
    private nint _windowHandle;
    private bool _registered;
    private Thread? _messageThread;
    private volatile bool _running;

    public event Action? HotkeyPressed;

    public WindowsHotkeyService(ILogger<WindowsHotkeyService> logger)
    {
        _logger = logger;
    }

    public bool Register()
    {
        if (_registered) return true;

        _running = true;
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "HotkeyMessageLoop"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        // Give the message loop time to create the window
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
    }

    private void MessageLoop()
    {
        // Create a message-only window
        _windowHandle = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, new nint(-3) /* HWND_MESSAGE */, 0, 0, 0);

        if (_windowHandle == 0)
        {
            _logger.LogWarning("Failed to create hotkey message window.");
            return;
        }

        _registered = RegisterHotKey(_windowHandle, HOTKEY_ID, DEFAULT_MODIFIERS, DEFAULT_VK);
        if (!_registered)
        {
            _logger.LogWarning("Failed to register hotkey (Ctrl+Shift+D). It may be in use by another application.");
            return;
        }

        _logger.LogInformation("Global hotkey registered: Ctrl+Shift+D");

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
