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
#endif

    public App(AppStartup startup)
    {
        InitializeComponent();
        _startup = startup;

        Task.Run(async () =>
        {
            try { await _startup.InitializeAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Startup failed: {ex.Message}"); }
        });
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

        // Restore window state (item 22)
        if (_settings is not null)
        {
            var ws = _settings.WindowState;
            window.Width = ws.Width > 0 ? ws.Width : 1188;
            window.Height = ws.Height > 0 ? ws.Height : 800;
            if (ws.X >= 0 && ws.Y >= 0)
            {
                window.X = ws.X;
                window.Y = ws.Y;
            }
        }
        else
        {
            window.Width = 1188;
            window.Height = 800;
        }
        window.MinimumWidth = 800;
        window.MinimumHeight = 600;

        // System tray (item 24 — uses app icon via Shell_NotifyIcon)
        var tray = services.GetService<ITrayService>();
        tray?.Initialize();

        if (tray is not null)
        {
            tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() => RestoreWindow(window));
            tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(() =>
            {
                SaveWindowState(window);
                Current?.Quit();
            });
        }

        // Global hotkey (item 23 — reads from settings)
        var hotkey = services.GetService<IHotkeyService>();
        hotkey?.Register();

        // Quick download handler
        var quickDl = services.GetService<Platforms.Windows.QuickDownloadHandler>();
        quickDl?.Initialize();

        // Minimize to tray (item 25)
        window.Destroying += (_, _) => SaveWindowState(window);
    }

    private void RestoreWindow(Window window)
    {
        var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
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
