using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Probes actual tool versions from binaries and updates stored settings
/// so the About screen reflects reality.
/// </summary>
public sealed class ToolVersionService : IToolVersionService
{
    private readonly IPlatformService _platform;
    private readonly ISettingsService _settings;
    private readonly ILogger<ToolVersionService> _logger;

    public ToolVersionService(
        IPlatformService platform,
        ISettingsService settings,
        ILogger<ToolVersionService> logger)
    {
        _platform = platform;
        _settings = settings;
        _logger = logger;
    }

    public async Task DetectVersionsAsync(CancellationToken ct = default)
    {
        var ytDlpVersion = await ProbeVersionAsync(
            _platform.GetResourcePath("yt-dlp"), "--version", ct);

        var ffmpegVersion = await ProbeFfmpegVersionAsync(
            _platform.GetResourcePath("ffmpeg"), ct);

        var changed = false;

        if (!string.IsNullOrEmpty(ytDlpVersion) &&
            !string.Equals(_settings.Updates.YtDlpLocalVersion, ytDlpVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Detected yt-dlp version: {Version}", ytDlpVersion);
            _settings.Updates.YtDlpLocalVersion = ytDlpVersion;
            changed = true;
        }

        if (!string.IsNullOrEmpty(ffmpegVersion) &&
            !string.Equals(_settings.Updates.FfmpegLocalVersion, ffmpegVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Detected FFmpeg version: {Version}", ffmpegVersion);
            _settings.Updates.FfmpegLocalVersion = ffmpegVersion;
            changed = true;
        }

        if (changed)
            await _settings.SaveAsync(ct);
    }

    /// <summary>
    /// Runs a tool with the given argument and returns the first line of stdout, trimmed.
    /// </summary>
    private async Task<string?> ProbeVersionAsync(string executablePath, string arguments, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            return null;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                  .FirstOrDefault()?.Trim();

            return string.IsNullOrEmpty(firstLine) ? null : firstLine;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to probe version for {Path}", executablePath);
            return null;
        }
    }

    /// <summary>
    /// Probes ffmpeg version. The output format is "ffmpeg version X.Y.Z ..." — we extract the version token.
    /// </summary>
    private async Task<string?> ProbeFfmpegVersionAsync(string executablePath, CancellationToken ct)
    {
        var output = await ProbeVersionAsync(executablePath, "-version", ct);
        if (output is null)
            return null;

        // ffmpeg -version outputs: "ffmpeg version 7.1.1-essentials_build-www.gyan.dev ..."
        // We want the version token (e.g. "7.1.1" or "7.1.1-essentials_build-www.gyan.dev")
        var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[0].Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            var versionToken = parts[2];
            // Extract just the numeric version (before any dash suffix)
            var dashIdx = versionToken.IndexOf('-');
            return dashIdx > 0 ? versionToken[..dashIdx] : versionToken;
        }

        return output;
    }
}
