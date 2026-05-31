namespace YTR.Core.Services;

/// <summary>
/// Platform-specific operations (file system, notifications, etc.).
/// </summary>
public interface IPlatformService
{
    string AppDataDirectory { get; }
    string DefaultVideoPath { get; }
    string DefaultAudioPath { get; }

    /// <summary>
    /// Gets the path to a bundled resource (yt-dlp, ffmpeg, etc.).
    /// </summary>
    string GetResourcePath(string resourceName);

    /// <summary>
    /// Opens a folder in the platform's file manager.
    /// </summary>
    Task OpenFolderAsync(string path);

    /// <summary>
    /// Opens a file with the system default handler.
    /// </summary>
    Task OpenFileAsync(string path);
}
