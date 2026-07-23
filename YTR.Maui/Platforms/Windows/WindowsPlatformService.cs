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

    public string PlatformTag => "win";

    public string InstallerAssetName => "YTR-Setup.exe";

    public string GetResourcePath(string resourceName)
    {
        // Bundled tools are placed in Resources\App by the installer
        var appDir = AppContext.BaseDirectory;
        var toolsDir = Path.Combine(appDir, "Resources", "App");

        return resourceName.ToLowerInvariant() switch
        {
            "yt-dlp" or "ytdlp" => FindExecutable(toolsDir, "yt-dlp.exe"),
            "ffmpeg" => FindExecutable(toolsDir, "ffmpeg.exe"),
            "ffprobe" => FindExecutable(toolsDir, "ffprobe.exe"),
            "node" or "nodejs" => FindExecutable(toolsDir, "node.exe"),
            _ => Path.Combine(toolsDir, resourceName)
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
        // Check AppData first (updated by in-app updater)
        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (File.Exists(appDataPath))
            return appDataPath;

#if DEBUG
        // In debug, use installer/tools/ from the repo so we don't need copies in Resources/App
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                var devToolPath = Path.Combine(dir.FullName, "installer", "tools", fileName);
                if (File.Exists(devToolPath))
                    return devToolPath;
                break;
            }
            dir = dir.Parent;
        }
#endif

        var resourcePath = Path.Combine(baseDir, fileName);
        return resourcePath;
    }
}
