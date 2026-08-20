/// <summary>
/// YTR Updater — A minimal elevated helper for file operations that require admin rights.
/// 
/// Usage:
///   "YTR Updater.exe" copy-file "source" "destination"
///   "YTR Updater.exe" copy-files "manifest.json"
///
/// Exit codes:
///   0 = Success
///   1 = Invalid arguments
///   2 = File operation failed
///
/// The manifest.json format (for copy-files):
/// [
///   { "Source": "C:\\temp\\yt-dlp.exe", "Destination": "C:\\Program Files\\YTR\\Resources\\App\\yt-dlp.exe" },
///   { "Source": "C:\\temp\\ffmpeg.exe", "Destination": "C:\\Program Files\\YTR\\Resources\\App\\ffmpeg.exe" }
/// ]
/// </summary>

using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: \"YTR Updater.exe\" <command> <args...>");
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  copy-file <source> <destination>");
    Console.Error.WriteLine("  copy-files <manifest.json>");
    return 1;
}

var command = args[0].ToLowerInvariant();

try
{
    return command switch
    {
        "copy-file" => CopyFile(args),
        "copy-files" => CopyFiles(args),
        _ => InvalidCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}

static int CopyFile(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: \"YTR Updater.exe\" copy-file <source> <destination>");
        return 1;
    }

    var source = args[1];
    var destination = args[2];

    if (!File.Exists(source))
    {
        Console.Error.WriteLine($"Source file not found: {source}");
        return 2;
    }

    var destDir = Path.GetDirectoryName(destination);
    if (!string.IsNullOrEmpty(destDir))
        Directory.CreateDirectory(destDir);

    // Delete existing file if present (it may be locked — retry a couple times)
    RetryDelete(destination);

    File.Copy(source, destination, overwrite: true);
    Console.WriteLine($"Copied: {source} -> {destination}");
    return 0;
}

static int CopyFiles(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: \"YTR Updater.exe\" copy-files <manifest.json>");
        return 1;
    }

    var manifestPath = args[1];
    if (!File.Exists(manifestPath))
    {
        Console.Error.WriteLine($"Manifest file not found: {manifestPath}");
        return 2;
    }

    var json = File.ReadAllText(manifestPath);
    var operations = JsonSerializer.Deserialize(json, AppJsonContext.Default.FileCopyOperationArray);

    if (operations is null || operations.Length == 0)
    {
        Console.Error.WriteLine("Manifest is empty or invalid.");
        return 1;
    }

    var errors = new List<string>();

    foreach (var op in operations)
    {
        if (string.IsNullOrEmpty(op.Source) || string.IsNullOrEmpty(op.Destination))
        {
            errors.Add("Invalid entry: Source and Destination are required.");
            continue;
        }

        if (!File.Exists(op.Source))
        {
            errors.Add($"Source not found: {op.Source}");
            continue;
        }

        try
        {
            var destDir = Path.GetDirectoryName(op.Destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            RetryDelete(op.Destination);
            File.Copy(op.Source, op.Destination, overwrite: true);
            Console.WriteLine($"Copied: {op.Source} -> {op.Destination}");
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to copy {op.Source}: {ex.Message}");
        }
    }

    // Clean up the manifest file
    try { File.Delete(manifestPath); } catch { /* best effort */ }

    if (errors.Count > 0)
    {
        foreach (var error in errors)
            Console.Error.WriteLine(error);
        return 2;
    }

    return 0;
}

static int InvalidCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 1;
}

static void RetryDelete(string path)
{
    if (!File.Exists(path))
        return;

    for (var i = 0; i < 3; i++)
    {
        try
        {
            File.Delete(path);
            return;
        }
        catch (IOException) when (i < 2)
        {
            Thread.Sleep(200);
        }
    }
}

internal sealed class FileCopyOperation
{
    public string? Source { get; set; }
    public string? Destination { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(FileCopyOperation[]))]
internal sealed partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
