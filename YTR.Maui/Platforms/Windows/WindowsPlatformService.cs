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
        // Candidate paths in priority order (highest priority first).
        // We'll resolve the actual newest version among all found candidates.
        string? appDataPath = null;
        string? bundledPath = null;
        string? systemPath = null;

        // 1. Check AppData (updated by in-app updater)
        var appDataCandidate = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (File.Exists(appDataCandidate))
            appDataPath = appDataCandidate;

#if DEBUG
        // In debug, use installer/tools/ from the repo so we don't need copies in Resources/App
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                var devToolPath = Path.Combine(dir.FullName, "installer", "tools", fileName);
                if (File.Exists(devToolPath))
                    bundledPath = devToolPath;
                break;
            }
            dir = dir.Parent;
        }
#endif

        // 2. Bundled in Resources/App (placed by installer)
        if (bundledPath is null)
        {
            var resourceCandidate = Path.Combine(baseDir, fileName);
            if (File.Exists(resourceCandidate))
                bundledPath = resourceCandidate;
        }

        // 3. Check system PATH for a user-installed version
        systemPath = FindOnPath(fileName);

        // Determine best candidate: pick the newest version among all found paths.
        // If version comparison fails or is unavailable, prefer AppData > bundled > system.
        var bestPath = PickNewest(fileName, appDataPath, bundledPath, systemPath);
        if (bestPath is not null)
            return bestPath;

        // Fallback: return bundled path even if file doesn't exist (caller will handle)
        return Path.Combine(baseDir, fileName);
    }

    /// <summary>
    /// Searches the system PATH for the given executable.
    /// </summary>
    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Picks the newest tool version among the candidate paths by comparing file modification dates.
    /// For tools that are frequently updated (yt-dlp, ffmpeg), the newest file is almost always the newest version.
    /// </summary>
    private static string? PickNewest(string fileName, string? appDataPath, string? bundledPath, string? systemPath)
    {
        var candidates = new List<(string Path, DateTime Modified)>();

        if (appDataPath is not null)
            candidates.Add((appDataPath, File.GetLastWriteTimeUtc(appDataPath)));
        if (bundledPath is not null)
            candidates.Add((bundledPath, File.GetLastWriteTimeUtc(bundledPath)));
        if (systemPath is not null)
            candidates.Add((systemPath, File.GetLastWriteTimeUtc(systemPath)));

        if (candidates.Count == 0)
            return null;

        // Return the candidate with the most recent modification time
        return candidates.OrderByDescending(c => c.Modified).First().Path;
    }
}
