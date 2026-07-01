using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// A small always-on-top progress window shown during quick downloads.
/// Displays URL, status, and a progress bar. Auto-dismisses on completion.
/// </summary>
public sealed partial class QuickDownloadProgressWindow : Microsoft.UI.Xaml.Window
{
    private AppWindow? _appWindow;
    private DispatcherTimer? _dismissTimer;

    public QuickDownloadProgressWindow()
    {
        InitializeComponent();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        // Get the AppWindow to configure size, position, and always-on-top
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow is not null)
        {
            // Set a compact size
            _appWindow.Resize(new SizeInt32(460, 200));

            // Position in bottom-right corner of the primary display
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var x = workArea.X + workArea.Width - 460 - 16;
            var y = workArea.Y + workArea.Height - 200 - 16;
            _appWindow.Move(new PointInt32(x, y));

            // Set always on top via presenter
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            // Set title
            _appWindow.Title = "YTR - Quick Download";
        }
    }

    /// <summary>
    /// Sets the URL being downloaded.
    /// </summary>
    public void SetUrl(string url)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UrlText.Text = url;
        });
    }

    /// <summary>
    /// Updates the status text and progress bar.
    /// </summary>
    public void UpdateProgress(string status, double progressPercent, bool isIndeterminate = false)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = status;
            ProgressBar.IsIndeterminate = isIndeterminate;
            if (!isIndeterminate)
            {
                ProgressBar.Value = progressPercent * 100;
            }
        });
    }

    /// <summary>
    /// Shows a completion message and auto-closes after a short delay.
    /// </summary>
    public void Complete(string message, bool isError = false)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = isError ? 0 : 100;

            // Auto-dismiss after 2.5 seconds
            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _dismissTimer.Tick += (_, _) =>
            {
                _dismissTimer.Stop();
                Close();
            };
            _dismissTimer.Start();
        });
    }
}
