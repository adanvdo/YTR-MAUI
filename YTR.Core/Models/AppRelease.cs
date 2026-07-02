using YTR.Core.Enums;

namespace YTR.Core.Models;

/// <summary>
/// Represents an application release available for update.
/// </summary>
public sealed record AppRelease
{
    public required string TagName { get; init; }
    public required Version Version { get; init; }
    public ReleaseChannel Channel { get; init; }
    public DateTime ReleaseDate { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
}
