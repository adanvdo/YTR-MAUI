using Microsoft.Extensions.Logging;

namespace YTR.Maui.Logging;

public static class FileLoggerExtensions
{
    /// <summary>
    /// Adds the file logger provider that writes to daily-rotated log files.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="logDirectory">Directory where log files will be written.</param>
    /// <param name="minLevel">Minimum log level to write (default: Warning).</param>
    /// <param name="retainDays">Number of days to retain old log files (default: 30).</param>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder,
        string logDirectory,
        LogLevel minLevel = LogLevel.Warning,
        int retainDays = 30)
    {
        builder.AddProvider(new FileLoggerProvider(logDirectory, minLevel, retainDays));
        return builder;
    }
}
