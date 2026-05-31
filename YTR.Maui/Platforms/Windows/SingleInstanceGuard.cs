using System.Threading;

namespace YTR.Maui.Platforms.Windows;

/// <summary>
/// Ensures only one instance of the application is running using a named Mutex.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\YTR_MediaDownloader_SingleInstance";
    private Mutex? _mutex;
    private bool _ownsHandle;

    /// <summary>
    /// Attempts to acquire the single-instance lock.
    /// Returns true if this is the first instance, false if another is already running.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out _ownsHandle);
        return _ownsHandle;
    }

    public void Dispose()
    {
        if (_ownsHandle && _mutex is not null)
        {
            _mutex.ReleaseMutex();
        }
        _mutex?.Dispose();
    }
}
