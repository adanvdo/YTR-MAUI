namespace YTR.Core.Models;

/// <summary>
/// A single item within a playlist.
/// </summary>
public sealed record PlaylistItem
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string? Title { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? ThumbnailUrl { get; init; }
    public bool Selected { get; set; } = true;
}
