namespace YTR.Core.Models;

/// <summary>
/// Represents a single thumbnail image available for a media item.
/// </summary>
public sealed record ThumbnailInfo
{
    public required string Url { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Id { get; init; }
}
