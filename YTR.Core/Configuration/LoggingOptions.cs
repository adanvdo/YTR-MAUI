namespace YTR.Core.Configuration;

/// <summary>
/// Settings for application error logging.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// Directory where log files are written. Empty/null means the default
    /// location (%LOCALAPPDATA%\YTR\Logs).
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;
}
