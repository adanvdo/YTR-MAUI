using Microsoft.Extensions.Logging;
using YTR.Core.Enums;
using YTR.Core.Models;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Handles the quick-download workflow triggered by the global hotkey:
/// 1. Read URL from clipboard
/// 2. Analyze URL
/// 3. Download best format
/// 4. Show notification on completion
/// </summary>
public sealed class QuickDownloadHandler
{
    private readonly IHotkeyService _hotkey;
    private readonly ITrayService _tray;
    private readonly IUrlAnalyzer _urlAnalyzer;
    private readonly IDownloadOrchestrator _orchestrator;
    private readonly INotificationService _notifications;
    private readonly ILogger<QuickDownloadHandler> _logger;

    public QuickDownloadHandler(
        IHotkeyService hotkey,
        ITrayService tray,
        IUrlAnalyzer urlAnalyzer,
        IDownloadOrchestrator orchestrator,
        INotificationService notifications,
        ILogger<QuickDownloadHandler> logger)
    {
        _hotkey = hotkey;
        _tray = tray;
        _urlAnalyzer = urlAnalyzer;
        _orchestrator = orchestrator;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Wires up the hotkey and tray quick-download events.
    /// </summary>
    public void Initialize()
    {
        _hotkey.HotkeyPressed += OnHotkeyPressed;
        _tray.QuickDownloadRequested += OnHotkeyPressed;
    }

    private async void OnHotkeyPressed()
    {
        try
        {
            // Read clipboard on the UI thread
            var clipboardText = await MainThread.InvokeOnMainThreadAsync(() =>
                Clipboard.Default.GetTextAsync());

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                _tray.ShowNotification("Quick Download", "No URL found on clipboard.");
                return;
            }

            var analysis = _urlAnalyzer.Analyze(clipboardText);
            if (!analysis.IsValid)
            {
                _tray.ShowNotification("Quick Download", $"Not a supported URL: {clipboardText}");
                return;
            }

            _tray.ShowNotification("Quick Download", $"Downloading from {analysis.Platform}...");
            _logger.LogInformation("Quick download triggered for {Url}", analysis.NormalizedUrl);

            var progress = new Progress<string>(msg =>
                _logger.LogDebug("Quick DL: {Message}", msg));

            var result = await _orchestrator.DownloadBestAsync(
                analysis.NormalizedUrl,
                StreamKind.AudioAndVideo,
                output: progress);

            if (result.IsSuccess)
            {
                await _notifications.ShowDownloadCompleteAsync(
                    result.Value!.Title,
                    result.Value.FilePath);
            }
            else
            {
                await _notifications.ShowErrorAsync("Quick Download Failed", result.Error!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick download failed.");
            _tray.ShowNotification("Quick Download", $"Error: {ex.Message}");
        }
    }
}
