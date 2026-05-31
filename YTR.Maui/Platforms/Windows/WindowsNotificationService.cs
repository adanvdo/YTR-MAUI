using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Windows notification service that uses the tray icon for balloon tips.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private readonly ITrayService _tray;

    public WindowsNotificationService(ITrayService tray)
    {
        _tray = tray;
    }

    public Task ShowDownloadCompleteAsync(string title, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        _tray.ShowNotification("Download Complete", $"{title}\n{fileName}");
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message)
    {
        _tray.ShowNotification($"Error: {title}", message);
        return Task.CompletedTask;
    }

    public Task ShowStatusAsync(string message)
    {
        _tray.ShowNotification("YTR", message);
        return Task.CompletedTask;
    }
}
