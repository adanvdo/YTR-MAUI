using System.Diagnostics;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Windows-specific platform operations.
/// </summary>
public sealed class WindowsPlatformService : IPlatformService
{
    public string AppDataDirectory => FileSystem.AppDataDirectory;

    public string DefaultVideoPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    public string DefaultAudioPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    public string GetResourcePath(string resourceName)
    {
        // Resources are bundled in the app's resource directory
        var appDir = AppContext.BaseDirectory;
        var resourcesDir = Path.Combine(appDir, "Resources", "App");

        return resourceName.ToLowerInvariant() switch
        {
            "yt-dlp" or "ytdlp" => FindExecutable(resourcesDir, "yt-dlp.exe"),
            "ffmpeg" => FindExecutable(resourcesDir, "ffmpeg.exe"),
            "ffprobe" => FindExecutable(resourcesDir, "ffprobe.exe"),
            "node" or "nodejs" => FindExecutable(resourcesDir, "node.exe"),
            _ => Path.Combine(resourcesDir, resourceName)
        };
    }

    public Task OpenFolderAsync(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        else if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        return Task.CompletedTask;
    }

    public Task OpenFileAsync(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        return Task.CompletedTask;
    }

    private static string FindExecutable(string baseDir, string fileName)
    {
        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (File.Exists(appDataPath))
            return appDataPath;

        var resourcePath = Path.Combine(baseDir, fileName);        
        return resourcePath;
    }
}
