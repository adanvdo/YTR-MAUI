using Microsoft.Toolkit.Uwp.Notifications;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Windows notification service that uses Windows Toast Notifications (Action Center).
/// Notifications appear in the system notification panel and can be dismissed by the user.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    public Task ShowDownloadCompleteAsync(string title, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        new ToastContentBuilder()
            .AddText("Download Complete")
            .AddText($"{title}")
            .AddText(fileName)
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message)
    {
        new ToastContentBuilder()
            .AddText($"Error: {title}")
            .AddText(message)
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowStatusAsync(string message)
    {
        new ToastContentBuilder()
            .AddText("YTR")
            .AddText(message)
            .Show();
        return Task.CompletedTask;
    }
}
