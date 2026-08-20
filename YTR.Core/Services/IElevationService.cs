namespace YTR.Core.Services;

/// <summary>
/// Provides elevated file operations for when the app lacks write access
/// to its own install directory (e.g. when installed to Program Files).
/// </summary>
public interface IElevationService
{
    /// <summary>
    /// Returns true if the current process has write access to the given directory.
    /// </summary>
    bool CanWriteTo(string path);

    /// <summary>
    /// Copies a file from source to destination using elevated privileges.
    /// This will trigger a UAC prompt on Windows.
    /// Returns true if the operation succeeded.
    /// </summary>
    Task<Result> ElevatedCopyAsync(IReadOnlyList<(string Source, string Destination)> operations, CancellationToken ct = default);
}
