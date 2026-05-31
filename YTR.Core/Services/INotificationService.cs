namespace YTR.Core.Services;

/// <summary>
/// Cross-platform notification service for download completion and status updates.
/// </summary>
public interface INotificationService
{
    Task ShowDownloadCompleteAsync(string title, string filePath);
    Task ShowErrorAsync(string title, string message);
    Task ShowStatusAsync(string message);
}
