using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using YTR.Core.Enums;
using YTR.Core.Models;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Handles the quick-download workflow triggered by the global hotkey:
/// 1. Save current clipboard contents
/// 2. Simulate Ctrl+C to copy user's selection
/// 3. Read the URL from clipboard
/// 4. Restore original clipboard
/// 5. Analyze URL and download
/// </summary>
public sealed class QuickDownloadHandler
{
    private readonly IHotkeyService _hotkey;
    private readonly ITrayService _tray;
    private readonly IUrlAnalyzer _urlAnalyzer;
    private readonly IDownloadOrchestrator _orchestrator;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILogger<QuickDownloadHandler> _logger;

    public QuickDownloadHandler(
        IHotkeyService hotkey,
        ITrayService tray,
        IUrlAnalyzer urlAnalyzer,
        IDownloadOrchestrator orchestrator,
        INotificationService notifications,
        ISettingsService settings,
        ILogger<QuickDownloadHandler> logger)
    {
        _hotkey = hotkey;
        _tray = tray;
        _urlAnalyzer = urlAnalyzer;
        _orchestrator = orchestrator;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Wires up the hotkey and tray quick-download events.
    /// </summary>
    public void Initialize()
    {
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        _tray.QuickDownloadRequested += OnTrayQuickDownloadRequested;
    }

    /// <summary>
    /// Shows a URL input window when Quick Download is clicked from the tray menu.
    /// </summary>
    private void OnTrayQuickDownloadRequested()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var inputWindow = new QuickDownloadInputWindow();
            inputWindow.DownloadRequested += url => _ = ExecuteDownloadAsync(url, inputWindow);
            inputWindow.Activate();
        });
    }

    /// <summary>
    /// Executes the download for a given URL, reporting progress to the input window.
    /// </summary>
    private async Task ExecuteDownloadAsync(string url, QuickDownloadInputWindow inputWindow)
    {
        try
        {
            var analysis = _urlAnalyzer.Analyze(url);
            if (!analysis.IsValid)
            {
                inputWindow.Complete($"✗ Not a supported URL", isError: true);
                return;
            }

            _logger.LogInformation("Quick download (tray) triggered for {Url}", analysis.NormalizedUrl);
            inputWindow.UpdateProgress($"Downloading from {analysis.Platform}...", 0, isIndeterminate: true);

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                var status = p.State switch
                {
                    DownloadState.PreProcessing => "Preparing...",
                    DownloadState.Downloading => p.Speed is not null
                        ? $"Downloading... {p.Progress:P0} ({p.Speed})"
                        : $"Downloading... {p.Progress:P0}",
                    DownloadState.PostProcessing => "Post-processing...",
                    DownloadState.Success => "Complete!",
                    DownloadState.Error => "Error",
                    _ => "Working..."
                };

                var isIndeterminate = p.State == DownloadState.PreProcessing
                    || (p.State == DownloadState.Downloading && p.Progress <= 0);

                inputWindow.UpdateProgress(status, p.Progress, isIndeterminate);
            });

            var outputProgress = new Progress<string>(msg =>
                _logger.LogDebug("Quick DL (tray): {Message}", msg));

            var result = await _orchestrator.DownloadBestAsync(
                analysis.NormalizedUrl,
                StreamKind.AudioAndVideo,
                progress: downloadProgress,
                output: outputProgress);

            if (result.IsSuccess)
            {
                inputWindow.Complete($"✓ {result.Value!.Title}");

                if (_settings.Download.AutoOpenDownloadLocation)
                {
                    var folder = Path.GetDirectoryName(result.Value.FilePath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else
            {
                inputWindow.Complete($"✗ {result.Error}", isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick download (tray) failed.");
            inputWindow.Complete($"✗ Error: {ex.Message}", isError: true);
        }
    }

    private async void OnHotkeyPressed()
    {
        QuickDownloadProgressWindow? progressWindow = null;

        try
        {
            // All clipboard operations must happen on the UI/STA thread
            var url = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // 1. Save current clipboard content
                var originalClipboard = await GetClipboardTextAsync();

                // 2. Clear clipboard so we can detect if Ctrl+C worked
                ClearClipboard();

                // 3. Simulate Ctrl+C to copy user's current selection
                SimulateCtrlC();

                // 4. Wait briefly for the copy to complete
                await Task.Delay(150);

                // 5. Read the copied selection
                var selectedText = await GetClipboardTextAsync();

                // 6. Restore original clipboard
                if (originalClipboard is not null)
                    SetClipboardText(originalClipboard);
                else
                    ClearClipboard();

                return selectedText;
            });

            if (string.IsNullOrWhiteSpace(url))
            {
                _tray.ShowNotification("Quick Download", "No URL selected.");
                return;
            }

            url = url.Trim();

            var analysis = _urlAnalyzer.Analyze(url);
            if (!analysis.IsValid)
            {
                _tray.ShowNotification("Quick Download", $"Not a supported URL: {url}");
                return;
            }

            _logger.LogInformation("Quick download triggered for {Url}", analysis.NormalizedUrl);

            // Show progress window on the UI thread
            progressWindow = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var window = new QuickDownloadProgressWindow();
                window.SetUrl(url);
                window.UpdateProgress($"Downloading from {analysis.Platform}...", 0, isIndeterminate: true);
                window.Activate();
                return window;
            });

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                var status = p.State switch
                {
                    DownloadState.PreProcessing => "Preparing...",
                    DownloadState.Downloading => p.Speed is not null
                        ? $"Downloading... {p.Progress:P0} ({p.Speed})"
                        : $"Downloading... {p.Progress:P0}",
                    DownloadState.PostProcessing => "Post-processing...",
                    DownloadState.Success => "Complete!",
                    DownloadState.Error => "Error",
                    _ => "Working..."
                };

                var isIndeterminate = p.State == DownloadState.PreProcessing
                    || (p.State == DownloadState.Downloading && p.Progress <= 0);

                progressWindow?.UpdateProgress(status, p.Progress, isIndeterminate);
            });

            var outputProgress = new Progress<string>(msg =>
                _logger.LogDebug("Quick DL: {Message}", msg));

            var result = await _orchestrator.DownloadBestAsync(
                analysis.NormalizedUrl,
                StreamKind.AudioAndVideo,
                progress: downloadProgress,
                output: outputProgress);

            if (result.IsSuccess)
            {
                progressWindow?.Complete($"✓ {result.Value!.Title}");

                // Honor the auto-open download location setting
                if (_settings.Download.AutoOpenDownloadLocation)
                {
                    var folder = Path.GetDirectoryName(result.Value.FilePath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
            }
            else
            {
                progressWindow?.Complete($"✗ {result.Error}", isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick download failed.");
            if (progressWindow is not null)
            {
                progressWindow.Complete($"✗ Error: {ex.Message}", isError: true);
            }
            else
            {
                _tray.ShowNotification("Quick Download", $"Error: {ex.Message}");
            }
        }
    }

    #region Clipboard & Input Helpers

    private static async Task<string?> GetClipboardTextAsync()
    {
        return await Clipboard.Default.GetTextAsync();
    }

    private static void ClearClipboard()
    {
        if (OpenClipboard(nint.Zero))
        {
            EmptyClipboard();
            CloseClipboard();
        }
    }

    private static void SetClipboardText(string text)
    {
        if (OpenClipboard(nint.Zero))
        {
            EmptyClipboard();
            var hGlobal = Marshal.StringToHGlobalUni(text);
            SetClipboardData(CF_UNICODETEXT, hGlobal);
            CloseClipboard();
            // Note: Do NOT free hGlobal — the clipboard now owns it
        }
    }

    private static void SimulateCtrlC()
    {
        // Release any modifier keys that might be physically held (from the hotkey combo)
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);     // Alt
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);

        // Small delay to let the key-up register
        Thread.Sleep(50);

        // Send Ctrl+C
        keybd_event(VK_CONTROL, 0, 0, 0);          // Ctrl down
        keybd_event(VK_C, 0, 0, 0);                // C down
        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, 0);  // C up
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0); // Ctrl up
    }

    #endregion

    #region P/Invoke

    private const uint CF_UNICODETEXT = 13;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12;    // Alt
    private const byte VK_SHIFT = 0x10;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    #endregion
}
