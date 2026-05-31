using YTR.Core.Enums;

namespace YTR.Core.Configuration;

/// <summary>
/// Settings for media processing preferences.
/// </summary>
public sealed class ProcessingOptions
{
    public bool AlwaysConvertToPreferred { get; set; } = true;
    public VideoFormat PreferredVideoFormat { get; set; } = VideoFormat.Mp4;
    public AudioFormat PreferredAudioFormat { get; set; } = AudioFormat.Mp3;
    public bool FetchMissingMetadata { get; set; } = true;
    public bool VerboseOutput { get; set; }
}
