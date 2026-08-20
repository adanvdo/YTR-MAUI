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
}
