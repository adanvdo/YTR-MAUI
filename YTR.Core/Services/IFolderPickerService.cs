namespace YTR.Core.Services;

/// <summary>
/// Platform-specific folder picker dialog.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Opens a folder picker dialog. Returns the selected path, or null if cancelled.
    /// </summary>
    /// <param name="startDirectory">Optional directory to open the picker at.</param>
    Task<string?> PickFolderAsync(string? startDirectory = null);

    /// <summary>
    /// Opens a file picker dialog filtered to the specified extensions (e.g. ".txt").
    /// Returns the selected file path, or null if cancelled.
    /// </summary>
    /// <param name="startDirectory">Optional directory to open the picker at.</param>
    /// <param name="extensions">Allowed file extensions (e.g. ".txt").</param>
    Task<string?> PickFileAsync(string? startDirectory = null, params string[] extensions);
}
