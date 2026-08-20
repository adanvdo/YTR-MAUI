using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// A small always-on-top progress window shown during quick downloads.
/// Displays URL, status, and a progress bar. Auto-dismisses on success,
/// stays open with action buttons on error.
/// </summary>
public sealed partial class QuickDownloadProgressWindow : Microsoft.UI.Xaml.Window
{
    private AppWindow? _appWindow;
    private DispatcherTimer? _dismissTimer;
    private string? _fullErrorMessage;

    /// <summary>
    /// Raised when the user clicks "View Details" on a failed download.
    /// The string parameter contains the full error message.
    /// </summary>
    public event Action<string>? ViewDetailsRequested;

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
            _appWindow.Resize(new SizeInt32(460, 220));

            // Position in bottom-right corner of the primary display
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var x = workArea.X + workArea.Width - 460 - 16;
            var y = workArea.Y + workArea.Height - 220 - 16;
            _appWindow.Move(new PointInt32(x, y));

            // Set always on top via presenter
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            // Set title and icon
            _appWindow.Title = "YTR - Quick Download";
            SetWindowIcon();
        }
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon", "appicon.ico");
        if (File.Exists(iconPath) && _appWindow is not null)
        {
            _appWindow.SetIcon(iconPath);
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
    /// Shows a completion message. On success, auto-closes after a short delay.
    /// On error, stays open with action buttons.
    /// </summary>
    public void Complete(string message, bool isError = false)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = isError ? 0 : 100;

            if (isError)
            {
                // Store full error and show action buttons — do NOT auto-close
                _fullErrorMessage = message;
                ProgressBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                ErrorActions.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                // Resize taller to fit buttons
                _appWindow?.Resize(new SizeInt32(460, 260));
            }
            else
            {
                // Auto-dismiss after 2.5 seconds on success
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
            }
        });
    }

    private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewDetailsRequested?.Invoke(_fullErrorMessage ?? "Unknown error");
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _dismissTimer?.Stop();
        Close();
    }
}
