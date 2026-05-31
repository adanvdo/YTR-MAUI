using YTR.Core.Models;

namespace YTR.Web.Services;

/// <summary>
/// Shared state for the active download, allowing components to react to progress changes.
/// </summary>
public sealed class DownloadStateService
{
    private DownloadProgress _currentProgress = new() { State = DownloadState.None };
    private string _statusMessage = "Ready";

    public DownloadProgress CurrentProgress => _currentProgress;
    public string StatusMessage => _statusMessage;
    public bool IsDownloading => _currentProgress.State is DownloadState.Downloading or DownloadState.PreProcessing or DownloadState.PostProcessing;

    public event Action? OnStateChanged;

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
        OnStateChanged?.Invoke();
    }
}
