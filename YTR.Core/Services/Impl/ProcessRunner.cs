using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Executes external processes (yt-dlp, ffmpeg) with output capture and cancellation support.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Running: {Executable} {Arguments}", request.Executable, request.Arguments);

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
        };

        try
        {
            process.Start();
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
            _logger.LogError(ex, "Process execution failed: {Executable}", request.Executable);
            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = ex.Message,
                WasCancelled = false
            };
        }
    }
}
