namespace YTR.Core.Services;

/// <summary>
/// Detects actual versions of bundled tools by probing the binaries,
/// and resolves the best (newest) path between bundled and system-installed tools.
/// </summary>
public interface IToolVersionService
{
    /// <summary>
    /// Probes yt-dlp and FFmpeg versions from the resolved tool paths and updates settings.
    /// Should be called once on startup after settings are loaded.
    /// </summary>
    Task DetectVersionsAsync(CancellationToken ct = default);
}
