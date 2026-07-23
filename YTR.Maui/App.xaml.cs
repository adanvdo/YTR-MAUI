using System.Runtime.InteropServices;
using YTR.Core.Services;

namespace YTR.Maui;

public partial class App : Application
{
    private readonly AppStartup _startup;
    private ISettingsService? _settings;
    private Window? _mainWindow;

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(int hInstance, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x0010;
    private const uint WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0;
    private const nint ICON_BIG = 1;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GWL_WNDPROC = -4;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_CLOSE = 0x0010;
    private const nint SC_MINIMIZE = 0xF020;

    private nint _originalWndProc;
    private WndProcDelegate? _wndProcDelegate;
    private nint _hwnd;
    private bool _isExiting;
    private bool _windowStateSaved;
    private bool _hasMinimizedOnce;
    private ITrayService? _tray;
#endif

    public App(AppStartup startup)
    {
        InitializeComponent();
        _startup = startup;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _mainWindow = new Window(new MainPage()) { Title = string.Empty };

#if WINDOWS
        InitializeWindowsPlatformFeatures(_mainWindow);
#endif

        return _mainWindow;
    }

#if WINDOWS
    private void InitializeWindowsPlatformFeatures(Window window)
    {
        var services = Handler?.MauiContext?.Services;
        if (services is null) return;

        _settings = services.GetService<ISettingsService>();

        // Single instance check
        var guard = services.GetService<Platforms.Windows.SingleInstanceGuard>();
        if (guard is not null && !guard.TryAcquire())
        {
            System.Diagnostics.Debug.WriteLine("Another instance is already running. Exiting.");
            Current?.Quit();
            return;
        }

        // Run async initialization (DB, migration, settings load) then register hotkey
        Task.Run(async () =>
        {
            try
            {
                await _startup.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup failed: {ex.Message}");
                return;
            }

            // Register hotkey after settings are loaded
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var hotkey = services.GetService<IHotkeyService>();
                if (_settings?.Download.EnableHotkeys == true)
                {
                    hotkey?.Register();
                }

                // Quick download handler
                var quickDl = services.GetService<Platforms.Windows.QuickDownloadHandler>();
                quickDl?.Initialize();
            });
        });

        // Restore window state (item 22)
        if (_settings is not null)
        {
            var ws = _settings.WindowState;
            window.Width = ws.Width > 0 ? ws.Width : 1188;
            window.Height = ws.Height > 0 ? ws.Height : 850;
            if (ws.X >= 0 && ws.Y >= 0)
            {
                window.X = ws.X;
                window.Y = ws.Y;
            }
        }
        else
        {
            window.Width = 1188;
            window.Height = 850;
        }
        window.MinimumWidth = 800;
        window.MinimumHeight = 850;

        // System tray (item 24 — uses app icon via Shell_NotifyIcon)
        var tray = services.GetService<ITrayService>();
        tray?.Initialize();
        _tray = tray;

        if (tray is not null)
        {
            tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() => RestoreWindow(window));
            tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(() => ExitApplication());
        }

        // Minimize to tray (item 25) — subclass the window to intercept minimize and close
        window.Created += (_, _) =>
        {
            var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow is not null)
            {
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

                // Set the window icon (taskbar + title bar)
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon", "appicon.ico");
                if (File.Exists(iconPath))
                {
                    var hIconSmall = LoadImage(0, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    var hIconBig = LoadImage(0, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    if (hIconSmall != 0) SendMessage(_hwnd, WM_SETICON, ICON_SMALL, hIconSmall);
                    if (hIconBig != 0) SendMessage(_hwnd, WM_SETICON, ICON_BIG, hIconBig);
                }

                _wndProcDelegate = new WndProcDelegate(WndProc);
                _originalWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC,
                    Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }
        };
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        // Intercept close (X button) → save state and exit
        if (msg == WM_CLOSE)
        {
            if (!_isExiting)
            {
                ExitApplication();
                return 0;
            }
            // When _isExiting is true, let WM_CLOSE pass through to actually destroy the window
        }

        // Intercept minimize → hide window to tray (but not when exiting)
        if (msg == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_MINIMIZE && !_isExiting)
        {
            ShowWindow(hWnd, SW_HIDE);

            if (!_hasMinimizedOnce)
            {
                _hasMinimizedOnce = true;
                var message = "YTR is still running in the system tray. Double-click the tray icon to re-open.";
                if (_settings?.Download.EnableHotkeys == true)
                {
                    var mods = _settings.Download.HotkeyModifiers;
                    var key = _settings.Download.HotkeyKey;
                    message += $"\nPress {mods}+{key} to quick download from any selected URL.";
                }
                _tray?.ShowNotification("YTR Minimized to Tray", message);
            }

            return 0;
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Performs a clean exit: saves state, disposes services, then terminates the process.
    /// </summary>
    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        // Save window state once
        SaveWindowState(_mainWindow!);

        // Kill any active child processes (ffmpeg, yt-dlp, node, etc.)
        var services = Handler?.MauiContext?.Services;
        if (services is not null)
        {
            services.GetService<IProcessRunner>()?.KillAll();
            (services.GetService<ITrayService>() as IDisposable)?.Dispose();
            (services.GetService<IHotkeyService>() as IDisposable)?.Dispose();
        }

        // Force the process to exit — avoids hangs from WinUI/WebView2 shutdown issues
        Environment.Exit(0);
    }

    private void RestoreWindow(Window window)
    {
        if (_hwnd != 0)
        {
            ShowWindow(_hwnd, SW_SHOW);
            ShowWindow(_hwnd, SW_RESTORE);
            SetForegroundWindow(_hwnd);
        }
    }

    private void SaveWindowState(Window window)
    {
        if (_settings is null || _windowStateSaved) return;
        _windowStateSaved = true;

        try
        {
            _settings.WindowState.X = (int)window.X;
            _settings.WindowState.Y = (int)window.Y;
            _settings.WindowState.Width = (int)window.Width;
            _settings.WindowState.Height = (int)window.Height;
            _settings.SaveAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Don't let save failures prevent shutdown
        }
    }
#endif
}
