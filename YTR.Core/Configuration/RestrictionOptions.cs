using YTR.Core.Enums;

namespace YTR.Core.Configuration;

/// <summary>
/// Settings for download quality/size restrictions.
/// </summary>
public sealed class RestrictionOptions
{
    public bool EnforceRestrictions { get; set; }
    public Resolution MaxResolution { get; set; } = Resolution.Any;
    public int MaxFileSizeMb { get; set; }

    public int MaxResolutionPixels => MaxResolution switch
    {
        Resolution.Sd => 480,
        Resolution.Hd720 => 720,
        Resolution.Hd1080 => 1080,
        Resolution.Uhd2160 => 2160,
        _ => 0
    };
}
