namespace YTR.Core.Configuration;

/// <summary>
/// Settings for download history behavior.
/// </summary>
public sealed class HistoryOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
}
