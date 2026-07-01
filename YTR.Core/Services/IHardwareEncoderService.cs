namespace YTR.Core.Services;

/// <summary>
/// Detects and caches available hardware video encoders (NVENC, QSV, AMF).
/// Probes ffmpeg once on first access and stores the results for the session.
/// </summary>
public interface IHardwareEncoderService
{
    /// <summary>
    /// Probes ffmpeg for available hardware encoders. Safe to call multiple times — results are cached.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the best available H.264 encoder (hw if available, else libx264).
    /// </summary>
    string H264Encoder { get; }

    /// <summary>
    /// Gets the best available HEVC/H.265 encoder (hw if available, else libx265).
    /// </summary>
    string HevcEncoder { get; }

    /// <summary>
    /// Whether hardware encoding is available.
    /// </summary>
    bool HasHardwareEncoder { get; }

    /// <summary>
    /// Gets extra arguments needed for the hardware encoder (e.g., -preset p4 for nvenc).
    /// </summary>
    string HwEncoderExtraArgs { get; }
}
