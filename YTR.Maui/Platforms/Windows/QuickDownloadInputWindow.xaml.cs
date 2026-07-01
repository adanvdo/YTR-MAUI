using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// A small always-on-top window shown when "Quick Download" is clicked from the tray menu.
/// Provides a URL input, Download button, and auto-dismisses after 7 seconds if idle.
/// </summary>
public sealed partial class QuickDownloadInputWindow : Microsoft.UI.Xaml.Window
{
    private AppWindow? _appWindow;
    private DispatcherTimer? _autoDismissTimer;
    private bool _downloadStarted;

    /// <summary>
    /// Raised when the user submits a URL for download.
    /// </summary>
    public event Action<string>? DownloadRequested;

    public QuickDownloadInputWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        StartAutoDismissTimer();
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow is not null)
        {
            _appWindow.Resize(new SizeInt32(480, 240));

            // Position in bottom-right corner of the primary display
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var x = workArea.X + workArea.Width - 480 - 16;
            var y = workArea.Y + workArea.Height - 240 - 16;
            _appWindow.Move(new PointInt32(x, y));

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            _appWindow.Title = "YTR - Quick Download";
        }
    }

    private void StartAutoDismissTimer()
    {
        _autoDismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(7)
        };
        _autoDismissTimer.Tick += (_, _) =>
        {
            _autoDismissTimer.Stop();
            if (!_downloadStarted)
            {
                Close();
            }
        };
        _autoDismissTimer.Start();
    }

    private void ResetAutoDismissTimer()
    {
        _autoDismissTimer?.Stop();
        _autoDismissTimer?.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _autoDismissTimer?.Stop();
        Close();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitUrl();
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Reset auto-dismiss whenever the user types
        ResetAutoDismissTimer();

        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            SubmitUrl();
            e.Handled = true;
        }
    }

    private void SubmitUrl()
    {
        var url = UrlTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        _downloadStarted = true;
        _autoDismissTimer?.Stop();

        // Disable input controls
        UrlTextBox.IsEnabled = false;
        DownloadButton.IsEnabled = false;

        // Show status area
        StatusPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        StatusText.Text = "Starting download...";

        DownloadRequested?.Invoke(url);
    }

    /// <summary>
    /// Updates the status and progress bar during download.
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
    /// Shows completion and auto-closes after a short delay.
    /// </summary>
    public void Complete(string message, bool isError = false)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = isError ? 0 : 100;

            var dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            dismissTimer.Tick += (_, _) =>
            {
                dismissTimer.Stop();
                Close();
            };
            dismissTimer.Start();
        });
    }
}
