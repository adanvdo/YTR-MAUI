using System.Runtime.InteropServices;
using YTR.Core.Services;

namespace YTR.Maui;

public partial class App : Application
{
    private readonly AppStartup _startup;

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
#endif

    public App(AppStartup startup)
    {
        InitializeComponent();
        _startup = startup;

        // Run async startup (DB init, migration, settings load)
        Task.Run(async () =>
        {
            try
            {
                await _startup.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup failed: {ex.Message}");
            }
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "YTR" };

#if WINDOWS
        InitializeWindowsPlatformFeatures(window);
#endif

        return window;
    }

#if WINDOWS
    private void InitializeWindowsPlatformFeatures(Window window)
    {
        // Single instance check
        var guard = Handler?.MauiContext?.Services.GetService<Platforms.Windows.SingleInstanceGuard>();
        if (guard is not null && !guard.TryAcquire())
        {
            // Another instance is running — exit
            System.Diagnostics.Debug.WriteLine("Another instance is already running. Exiting.");
            Current?.Quit();
            return;
        }

        // System tray
        var tray = Handler?.MauiContext?.Services.GetService<ITrayService>();
        tray?.Initialize();

        if (tray is not null)
        {
            tray.ShowRequested += () => MainThread.BeginInvokeOnMainThread(() =>
            {
                // Bring the window to front
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow is not null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                    SetForegroundWindow(hwnd);
                }
            });

            tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(() =>
            {
                Current?.Quit();
            });
        }

        // Global hotkey
        var hotkey = Handler?.MauiContext?.Services.GetService<IHotkeyService>();
        hotkey?.Register();

        // Quick download handler
        var quickDl = Handler?.MauiContext?.Services.GetService<Platforms.Windows.QuickDownloadHandler>();
        quickDl?.Initialize();

        // Window size persistence
        window.Width = 1200;
        window.Height = 800;
        window.MinimumWidth = 800;
        window.MinimumHeight = 600;
    }
#endif
}
