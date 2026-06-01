using Windows.Storage.Pickers;
using WinRT.Interop;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        // Get the window handle for the picker
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
