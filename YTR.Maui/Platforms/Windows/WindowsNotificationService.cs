using Microsoft.Toolkit.Uwp.Notifications;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Windows notification service that uses Windows Toast Notifications (Action Center).
/// Notifications appear in the system notification panel and can be dismissed by the user.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private static readonly Uri AppIconUri = new(Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon", "appicon.png"));

    public Task ShowDownloadCompleteAsync(string title, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        new ToastContentBuilder()
            .AddAppLogoOverride(AppIconUri)
            .AddText("Download Complete")
            .AddText($"{title}")
            .AddText(fileName)
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message)
    {
        new ToastContentBuilder()
            .AddAppLogoOverride(AppIconUri)
            .AddText($"Error: {title}")
            .AddText(message)
            .Show();
        return Task.CompletedTask;
    }

    public Task ShowStatusAsync(string message)
    {
        new ToastContentBuilder()
            .AddAppLogoOverride(AppIconUri)
            .AddText("YTR")
            .AddText(message)
            .Show();
        return Task.CompletedTask;
    }
}
