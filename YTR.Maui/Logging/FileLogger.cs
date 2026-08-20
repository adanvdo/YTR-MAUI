using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;

namespace YTR.Maui.Logging;

/// <summary>
/// A lightweight file logger that writes log entries to daily-rotated files
/// in the application data directory. Only logs Warning level and above by default.
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string categoryName, FileLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var level = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var logLine = $"[{timestamp}] [{level}] [{_categoryName}] {message}";
        if (exception is not null)
            logLine += Environment.NewLine + exception;

        _provider.WriteEntry(logLine);
    }
}

/// <summary>
/// Provider that manages the file output for <see cref="FileLogger"/>.
/// Writes to daily log files with automatic cleanup of old files.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly BlockingCollection<string> _entryQueue = new(1024);
    private readonly Task _writerTask;
    private readonly int _retainDays;

    public LogLevel MinLevel { get; }

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Warning, int retainDays = 30)
    {
        _logDirectory = logDirectory;
        MinLevel = minLevel;
        _retainDays = retainDays;

        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();

        _writerTask = Task.Run(ProcessQueue);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));
    }

    internal void WriteEntry(string entry)
    {
        // Non-blocking — drops entries if queue is full (backpressure safety)
        _entryQueue.TryAdd(entry);
    }

    private void ProcessQueue()
    {
        foreach (var entry in _entryQueue.GetConsumingEnumerable())
        {
            try
            {
                var fileName = $"ytr-{DateTime.Now:yyyy-MM-dd}.log";
                var filePath = Path.Combine(_logDirectory, fileName);
                File.AppendAllText(filePath, entry + Environment.NewLine);
            }
            catch
            {
                // Swallow write failures — logging should never crash the app
            }
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retainDays);
            foreach (var file in Directory.GetFiles(_logDirectory, "ytr-*.log"))
            {
                if (File.GetCreationTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public void Dispose()
    {
        _entryQueue.CompleteAdding();
        // Give the writer a moment to flush
        _writerTask.Wait(TimeSpan.FromSeconds(2));
        _entryQueue.Dispose();
    }
}
