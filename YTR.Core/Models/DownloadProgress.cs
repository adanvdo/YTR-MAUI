namespace YTR.Core.Models;

/// <summary>
/// Progress information for an active download.
/// </summary>
public sealed record DownloadProgress
{
    public DownloadState State { get; init; }
    public double Progress { get; init; }
    public string? Speed { get; init; }
    public string? Eta { get; init; }
    public string? Data { get; init; }
}

public enum DownloadState
{
    None,
    PreProcessing,
    Downloading,
    PostProcessing,
    Success,
    Error
}
