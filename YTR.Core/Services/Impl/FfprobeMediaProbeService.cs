using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Uses ffprobe to get stream information from a file or URL.
/// </summary>
public sealed class FfprobeMediaProbeService : IMediaProbeService
{
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platform;
    private readonly ILogger<FfprobeMediaProbeService> _logger;

    public FfprobeMediaProbeService(
        IProcessRunner processRunner,
        IPlatformService platform,
        ILogger<FfprobeMediaProbeService> logger)
    {
        _processRunner = processRunner;
        _platform = platform;
        _logger = logger;
    }

    public async Task<Result<MediaProbeResult>> ProbeAsync(string pathOrUrl, CancellationToken ct = default)
    {
        var ffprobePath = _platform.GetResourcePath("ffprobe");
        var args = $"-v quiet -print_format json -show_streams -show_format \"{pathOrUrl}\"";

        var result = await _processRunner.RunAsync(new Models.ProcessRequest
        {
            Executable = ffprobePath,
            Arguments = args
        }, ct);

        if (result.WasCancelled)
            return Result<MediaProbeResult>.Failure("Probe cancelled.");

        if (!result.Success && string.IsNullOrWhiteSpace(result.StandardOutput))
            return Result<MediaProbeResult>.Failure($"ffprobe failed: {result.StandardError.Trim()}");

        try
        {
            var output = string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            int? width = null, height = null;
            double? framerate = null;
            string? videoCodec = null, audioCodec = null, pixelFormat = null;
            TimeSpan? duration = null;

            // Parse format duration
            if (root.TryGetProperty("format", out var format))
            {
                var durStr = format.GetPropertyOrDefault("duration", null);
                if (durStr is not null && double.TryParse(durStr, out var durSec))
                    duration = TimeSpan.FromSeconds(durSec);
            }

            // Parse streams
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.GetPropertyOrDefault("codec_type", null);

                    if (codecType == "video" && videoCodec is null)
                    {
                        videoCodec = stream.GetPropertyOrDefault("codec_name", null);
                        width = stream.GetIntOrDefault("width");
                        height = stream.GetIntOrDefault("height");
                        pixelFormat = stream.GetPropertyOrDefault("pix_fmt", null);

                        // Parse framerate from r_frame_rate (e.g. "30/1" or "24000/1001")
                        var rFrameRate = stream.GetPropertyOrDefault("r_frame_rate", null);
                        if (rFrameRate is not null && rFrameRate.Contains('/'))
                        {
                            var parts = rFrameRate.Split('/');
                            if (double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
                                framerate = num / den;
                        }

                        // Stream-level duration overrides format duration for video
                        var streamDur = stream.GetPropertyOrDefault("duration", null);
                        if (streamDur is not null && double.TryParse(streamDur, out var vDur))
                            duration = TimeSpan.FromSeconds(vDur);
                    }
                    else if (codecType == "audio" && audioCodec is null)
                    {
                        audioCodec = stream.GetPropertyOrDefault("codec_name", null);
                    }
                }
            }

            return Result<MediaProbeResult>.Success(new MediaProbeResult
            {
                Width = width,
                Height = height,
                Framerate = framerate,
                Duration = duration,
                VideoCodec = videoCodec,
                AudioCodec = audioCodec,
                PixelFormat = pixelFormat
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse ffprobe output.");
            return Result<MediaProbeResult>.Failure($"Failed to parse probe result: {ex.Message}");
        }
    }
}
