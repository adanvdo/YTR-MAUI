using YTR.Core.Services;

namespace YTR.Maui.Platforms.Android;

/// <summary>
/// Android-specific platform operations.
/// </summary>
public sealed class AndroidPlatformService : IPlatformService
{
    public string AppDataDirectory => FileSystem.AppDataDirectory;

    public string DefaultVideoPath =>
        Path.Combine(global::Android.OS.Environment.GetExternalStoragePublicDirectory(
            global::Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? "/sdcard/Download", "YTR", "Video");

    public string DefaultAudioPath =>
        Path.Combine(global::Android.OS.Environment.GetExternalStoragePublicDirectory(
            global::Android.OS.Environment.DirectoryDownloads)?.AbsolutePath ?? "/sdcard/Download", "YTR", "Audio");

    public string GetResourcePath(string resourceName)
    {
        // On Android, binaries would be in app's native lib directory or extracted to files dir
        var filesDir = FileSystem.AppDataDirectory;

        return resourceName.ToLowerInvariant() switch
        {
            "yt-dlp" or "ytdlp" => Path.Combine(filesDir, "yt-dlp"),
            "ffmpeg" => Path.Combine(filesDir, "ffmpeg"),
            "ffprobe" => Path.Combine(filesDir, "ffprobe"),
            _ => Path.Combine(filesDir, resourceName)
        };
    }

    public async Task OpenFolderAsync(string path)
    {
        // Use MAUI's Launcher to open a file manager
        await Launcher.Default.OpenAsync(new OpenFileRequest
        {
            Title = "Open folder",
            File = new ReadOnlyFile(path)
        });
    }

    public async Task OpenFileAsync(string path)
    {
        if (File.Exists(path))
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Open file",
                File = new ReadOnlyFile(path)
            });
        }
    }
}
