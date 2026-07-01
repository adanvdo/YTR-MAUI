using Microsoft.Extensions.Logging;

namespace YTR.Core.Services.Impl;

/// <summary>
/// Probes ffmpeg for available hardware encoders and caches results.
/// Checks in order: NVENC (NVIDIA) → QSV (Intel) → AMF (AMD) → software fallback.
/// </summary>
public sealed class HardwareEncoderService : IHardwareEncoderService
{
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformService _platform;
    private readonly ILogger<HardwareEncoderService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public string H264Encoder { get; private set; } = "libx264";
    public string HevcEncoder { get; private set; } = "libx265";
    public bool HasHardwareEncoder { get; private set; }
    public string HwEncoderExtraArgs { get; private set; } = "";

    public HardwareEncoderService(
        IProcessRunner processRunner,
        IPlatformService platform,
        ILogger<HardwareEncoderService> logger)
    {
        _processRunner = processRunner;
        _platform = platform;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var ffmpegPath = _platform.GetResourcePath("ffmpeg");
            var encoders = await GetAvailableEncodersAsync(ffmpegPath, ct);

            if (encoders.Contains("h264_nvenc"))
            {
                // NVIDIA NVENC — fastest, widely available
                H264Encoder = "h264_nvenc";
                HevcEncoder = encoders.Contains("hevc_nvenc") ? "hevc_nvenc" : "libx265";
                HwEncoderExtraArgs = "-preset p4 -rc vbr -cq 23";
                HasHardwareEncoder = true;
                _logger.LogInformation("Hardware encoder detected: NVIDIA NVENC");
            }
            else if (encoders.Contains("h264_qsv"))
            {
                // Intel Quick Sync Video
                H264Encoder = "h264_qsv";
                HevcEncoder = encoders.Contains("hevc_qsv") ? "hevc_qsv" : "libx265";
                HwEncoderExtraArgs = "-preset faster -global_quality 23";
                HasHardwareEncoder = true;
                _logger.LogInformation("Hardware encoder detected: Intel QSV");
            }
            else if (encoders.Contains("h264_amf"))
            {
                // AMD AMF
                H264Encoder = "h264_amf";
                HevcEncoder = encoders.Contains("hevc_amf") ? "hevc_amf" : "libx265";
                HwEncoderExtraArgs = "-quality speed -rc cqp -qp_i 23 -qp_p 23";
                HasHardwareEncoder = true;
                _logger.LogInformation("Hardware encoder detected: AMD AMF");
            }
            else
            {
                // Software fallback with fast preset
                H264Encoder = "libx264";
                HevcEncoder = "libx265";
                HwEncoderExtraArgs = "-preset veryfast";
                HasHardwareEncoder = false;
                _logger.LogInformation("No hardware encoder detected, using software encoding with fast preset");
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<HashSet<string>> GetAvailableEncodersAsync(string ffmpegPath, CancellationToken ct)
    {
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var request = new Models.ProcessRequest
            {
                Executable = ffmpegPath,
                Arguments = "-hide_banner -encoders"
            };

            var result = await _processRunner.RunAsync(request, ct);
            if (result.ExitCode != 0) return encoders;

            // Parse output: each encoder line looks like " V..... h264_nvenc ..."
            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimStart();
                // Video encoder lines start with "V" in the capabilities column
                if (trimmed.Length > 7 && trimmed[0] == 'V')
                {
                    var parts = trimmed[7..].TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                        encoders.Add(parts[0]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe hardware encoders, falling back to software");
        }

        return encoders;
    }
}
