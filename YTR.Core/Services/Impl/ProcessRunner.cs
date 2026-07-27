using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Executes external processes (yt-dlp, ffmpeg) with output capture and cancellation support.
/// Tracks active processes so they can be killed on app shutdown.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public void KillAll()
    {
        foreach (var kvp in _activeProcesses)
        {
            try
            {
                if (!kvp.Value.HasExited)
                    kvp.Value.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited
            }
        }
        _activeProcesses.Clear();
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var executableName = Path.GetFileNameWithoutExtension(request.Executable);
        _logger.LogDebug("[{Tool}] Command started at {StartTime:yyyy-MM-dd HH:mm:ss.fff UTC}", executableName, startTime);
        _logger.LogDebug("[{Tool}] Full command: {Executable} {Arguments}", executableName, request.Executable, request.Arguments);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            request.OnOutputLine?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            request.OnErrorLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
            _activeProcesses.TryAdd(process.Id, process);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Register cancellation to kill the process
            await using var registration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may have already exited
                }
            });

            await process.WaitForExitAsync(ct);

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;
            _logger.LogDebug("[{Tool}] Command finished at {EndTime:yyyy-MM-dd HH:mm:ss.fff UTC} | Duration: {Duration} | Exit code: {ExitCode}",
                executableName, endTime, FormatDuration(duration), process.ExitCode);

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                WasCancelled = ct.IsCancellationRequested
            };
        }
        catch (OperationCanceledException)
        {
            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;
            _logger.LogDebug("[{Tool}] Command cancelled at {EndTime:yyyy-MM-dd HH:mm:ss.fff UTC} | Duration before cancel: {Duration}",
                executableName, endTime, FormatDuration(duration));

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                WasCancelled = true
            };
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;
            _logger.LogError(ex, "[{Tool}] Process execution failed at {EndTime:yyyy-MM-dd HH:mm:ss.fff UTC} | Duration: {Duration}",
                executableName, endTime, FormatDuration(duration));

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = ex.Message,
                WasCancelled = false
            };
        }
        finally
        {
            _activeProcesses.TryRemove(process.Id, out _);
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return duration.ToString(@"h\:mm\:ss\.fff");
        if (duration.TotalMinutes >= 1)
            return duration.ToString(@"m\:ss\.fff");
        return $"{duration.TotalSeconds:F3}s";
    }
}
