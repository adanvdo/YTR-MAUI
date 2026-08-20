using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YTR.Core;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Windows implementation of <see cref="IElevationService"/> that uses the
/// YTR.Elevate helper executable to perform file operations with admin privileges.
/// </summary>
public sealed class WindowsElevationService : IElevationService
{
    private readonly ILogger<WindowsElevationService> _logger;

    public WindowsElevationService(ILogger<WindowsElevationService> logger)
    {
        _logger = logger;
    }

    public bool CanWriteTo(string path)
    {
        try
        {
            // If the path is a file, test the directory
            var directory = File.Exists(path)
                ? Path.GetDirectoryName(path)!
                : Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path)!;

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Try to create and delete a temp file to verify write access
            var testFile = Path.Combine(directory, $".ytr_write_test_{Guid.NewGuid():N}");
            using (File.Create(testFile)) { }
            File.Delete(testFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public async Task<Result> ElevatedCopyAsync(IReadOnlyList<(string Source, string Destination)> operations, CancellationToken ct = default)
    {
        if (operations.Count == 0)
            return Result.Success();

        var elevateExePath = GetElevateExePath();
        if (!File.Exists(elevateExePath))
        {
            _logger.LogError("YTR Updater.exe not found at {Path}", elevateExePath);
            return Result.Failure("Elevation helper not found. Please reinstall the application.");
        }

        try
        {
            if (operations.Count == 1)
            {
                return await RunElevatedSingleAsync(elevateExePath, operations[0].Source, operations[0].Destination, ct);
            }
            else
            {
                return await RunElevatedManifestAsync(elevateExePath, operations, ct);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt
            _logger.LogWarning("User declined UAC elevation prompt.");
            return Result.Failure("Update requires administrator permissions. The elevation request was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elevated copy operation failed.");
            return Result.Failure($"Elevated operation failed: {ex.Message}");
        }
    }

    private async Task<Result> RunElevatedSingleAsync(string elevateExePath, string source, string destination, CancellationToken ct)
    {
        var arguments = $"copy-file \"{source}\" \"{destination}\"";

        _logger.LogInformation("Launching elevated helper: copy-file {Source} -> {Destination}", source, destination);

        var exitCode = await RunProcessAsync(elevateExePath, arguments, ct);

        return exitCode == 0
            ? Result.Success()
            : Result.Failure($"Elevated copy failed (exit code {exitCode}).");
    }

    private async Task<Result> RunElevatedManifestAsync(string elevateExePath, IReadOnlyList<(string Source, string Destination)> operations, CancellationToken ct)
    {
        // Write a manifest JSON file that the helper will read
        var manifestPath = Path.Combine(Path.GetTempPath(), $"ytr-elevate-{Guid.NewGuid():N}.json");

        var manifest = operations.Select(op => new { Source = op.Source, Destination = op.Destination }).ToArray();
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json, ct);

        _logger.LogInformation("Launching elevated helper: copy-files with {Count} operations", operations.Count);

        var arguments = $"copy-files \"{manifestPath}\"";
        var exitCode = await RunProcessAsync(elevateExePath, arguments, ct);

        // Clean up manifest if helper didn't
        try { if (File.Exists(manifestPath)) File.Delete(manifestPath); } catch { }

        return exitCode == 0
            ? Result.Success()
            : Result.Failure($"Elevated copy failed (exit code {exitCode}).");
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,  // Required for runas/elevation
            Verb = "runas",          // Triggers UAC prompt
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start elevation helper process.");

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    private static string GetElevateExePath()
    {
        // The helper exe is placed next to the main app exe
        return Path.Combine(AppContext.BaseDirectory, "YTR Updater.exe");
    }
}
