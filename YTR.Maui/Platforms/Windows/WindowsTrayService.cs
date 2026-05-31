using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// System tray icon using Win32 Shell_NotifyIcon.
/// Provides context menu with Show, Quick Download, and Exit options.
/// </summary>
public sealed class WindowsTrayService : ITrayService, IDisposable
{
    private readonly ILogger<WindowsTrayService> _logger;
    private bool _initialized;
    private nint _windowHandle;
    private Thread? _messageThread;
    private volatile bool _running;

    private const int WM_USER_TRAY = 0x0400 + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;
    private const int IDM_SHOW = 1001;
    private const int IDM_QUICKDL = 1002;
    private const int IDM_EXIT = 1003;

    public event Action? ShowRequested;
    public event Action? QuickDownloadRequested;
    public event Action? ExitRequested;

    public WindowsTrayService(ILogger<WindowsTrayService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _running = true;
        _messageThread = new Thread(TrayMessageLoop)
        {
            IsBackground = true,
            Name = "TrayMessageLoop"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        _logger.LogInformation("System tray service started.");
    }

    public void ShowNotification(string title, string message)
    {
        if (_windowHandle == 0) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NIF_INFO,
            szInfoTitle = title.Length > 63 ? title[..63] : title,
            szInfo = message.Length > 255 ? message[..255] : message,
            dwInfoFlags = NIIF_INFO
        };
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void Dispose()
    {
        _running = false;
        if (_windowHandle != 0)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            PostMessage(_windowHandle, 0x0012 /* WM_QUIT */, 0, 0);
        }
    }

    private void TrayMessageLoop()
    {
        // Create message-only window
        _windowHandle = CreateWindowEx(0, "STATIC", "YTR_TrayWindow", 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);
        if (_windowHandle == 0)
        {
            _logger.LogWarning("Failed to create tray message window.");
            return;
        }

        // Add tray icon
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_USER_TRAY,
            hIcon = LoadIcon(0, new nint(32512)), // IDI_APPLICATION
            szTip = "YTR - Media Downloader"
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);

        // Message pump
        while (_running && GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_USER_TRAY)
            {
                var lParam = (int)msg.lParam;
                if (lParam == WM_LBUTTONDBLCLK)
                {
                    ShowRequested?.Invoke();
                }
                else if (lParam == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
            }
            else if (msg.message == WM_COMMAND)
            {
                var id = (int)(msg.wParam & 0xFFFF);
                switch (id)
                {
                    case IDM_SHOW: ShowRequested?.Invoke(); break;
                    case IDM_QUICKDL: QuickDownloadRequested?.Invoke(); break;
                    case IDM_EXIT: ExitRequested?.Invoke(); break;
                }
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IDM_SHOW, "Show YTR");
        AppendMenu(menu, 0, IDM_QUICKDL, "Quick Download");
        AppendMenu(menu, 0x0800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0, IDM_EXIT, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_windowHandle);
        TrackPopupMenu(menu, 0, pt.x, pt.y, 0, _windowHandle, nint.Zero);
        DestroyMenu(menu);
    }

    #region P/Invoke

    private const int NIM_ADD = 0x00;
    private const int NIM_MODIFY = 0x01;
    private const int NIM_DELETE = 0x02;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;
    private const int NIIF_INFO = 0x01;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

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

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(int hInstance, nint lpIconName);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, int uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(nint hMenu, int uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    #endregion
}
