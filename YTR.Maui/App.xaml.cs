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

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GWL_WNDPROC = -4;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_CLOSE = 0x0010;
    private const nint SC_MINIMIZE = 0xF020;

    private nint _originalWndProc;
    private WndProcDelegate? _wndProcDelegate;
    private nint _hwnd;
    private bool _isExiting;
#endif

    public App(AppStartup startup)
    {
        InitializeComponent();
        _startup = startup;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _mainWindow = new Window(new MainPage()) { Title = "YTR" };

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

        if (tray is not null)
        {
            tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() => RestoreWindow(window));
            tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(() =>
            {
                _isExiting = true;
                SaveWindowState(window);
                // Ensure window is visible so MAUI can close it properly
                if (_hwnd != 0)
                    ShowWindow(_hwnd, SW_SHOW);
                Current?.Quit();
            });
        }

        // Minimize to tray (item 25) — subclass the window to intercept minimize and close
        window.Created += (_, _) =>
        {
            var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow is not null)
            {
                _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                _wndProcDelegate = new WndProcDelegate(WndProc);
                _originalWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC,
                    Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }
        };

        window.Destroying += (_, _) => SaveWindowState(window);
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        // Intercept minimize → hide window to tray (but not when exiting)
        if (msg == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_MINIMIZE && !_isExiting)
        {
            ShowWindow(hWnd, SW_HIDE);
            return 0;
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
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
        if (_settings is null) return;
        _settings.WindowState.X = (int)window.X;
        _settings.WindowState.Y = (int)window.Y;
        _settings.WindowState.Width = (int)window.Width;
        _settings.WindowState.Height = (int)window.Height;
        // Save synchronously since we're in a closing handler
        _settings.SaveAsync().GetAwaiter().GetResult();
    }
#endif
}
