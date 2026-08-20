using YTR.Core.Models;
using YTR.Core.Services;

namespace YTR.Web.Services;

/// <summary>
/// Shared state for the active download. Components subscribe to progress changes.
/// Also manages the CancellationTokenSource for cancelling active downloads.
/// </summary>
public sealed class DownloadStateService
{
    private DownloadProgress _currentProgress = new() { State = DownloadState.None };
    private string _statusMessage = "Ready";
    private CancellationTokenSource? _cts;

    public DownloadProgress CurrentProgress => _currentProgress;
    public string StatusMessage => _statusMessage;
    public bool IsDownloading => _currentProgress.State is DownloadState.Downloading or DownloadState.PreProcessing or DownloadState.PostProcessing;
    public bool CanCancel => _cts is not null && !_cts.IsCancellationRequested;

    public event Action? OnStateChanged;

    /// <summary>
    /// Creates a new CancellationToken for a download operation.
    /// </summary>
    public CancellationToken StartOperation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        OnStateChanged?.Invoke();
        return _cts.Token;
    }

    /// <summary>
    /// Cancels the active download operation.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _currentProgress = new() { State = DownloadState.None };
        _statusMessage = "Cancelling...";
        OnStateChanged?.Invoke();
    }

    public void UpdateProgress(DownloadProgress progress)
    {
        _currentProgress = progress;
        OnStateChanged?.Invoke();
    }

    public void UpdateStatus(string message)
    {
        _statusMessage = message;
        OnStateChanged?.Invoke();
    }

    public void Reset()
    {
        _currentProgress = new() { State = DownloadState.None };
        _statusMessage = "Ready";
        _cts?.Dispose();
        _cts = null;
        OnStateChanged?.Invoke();
    }

    // Error details (for showing full error info in the UI)
    public string? LastError { get; private set; }
    public event Action<string>? OnErrorOccurred;

    /// <summary>
    /// Reports a download error with full details. Triggers the OnErrorOccurred event
    /// so UI components can display an error dialog/alert.
    /// </summary>
    public void ReportError(string error)
    {
        LastError = error;
        _statusMessage = $"Failed: {TruncateForStatus(error)}";
        _currentProgress = new() { State = DownloadState.Error };
        OnErrorOccurred?.Invoke(error);
        OnStateChanged?.Invoke();
    }

    public void ClearError()
    {
        LastError = null;
    }

    private static string TruncateForStatus(string text)
    {
        const int maxLen = 80;
        return text.Length <= maxLen ? text : text[..maxLen] + "…";
    }

    // Playlist progress
    public int PlaylistCompleted { get; private set; }
    public int PlaylistTotal { get; private set; }
    public bool IsPlaylistDownload => PlaylistTotal > 0;

    public void UpdatePlaylistProgress(int completed, int total)
    {
        PlaylistCompleted = completed;
        PlaylistTotal = total;
        OnStateChanged?.Invoke();
    }

    public void ResetPlaylist()
    {
        PlaylistCompleted = 0;
        PlaylistTotal = 0;
    }
}
