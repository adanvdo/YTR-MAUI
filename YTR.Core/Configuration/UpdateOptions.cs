using YTR.Core.Enums;

namespace YTR.Core.Configuration;

/// <summary>
/// Settings for update behavior.
/// </summary>
public sealed class UpdateOptions
{
    public ReleaseChannel Channel { get; set; } = ReleaseChannel.Stable;
    public string YtDlpLocalVersion { get; set; } = string.Empty;
    public string FfmpegLocalVersion { get; set; } = string.Empty;
}
