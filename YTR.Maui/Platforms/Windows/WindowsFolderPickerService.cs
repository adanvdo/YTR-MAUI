using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;
using YTR.Core.Services;

namespace YTR.Maui.Platforms.Windows;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string? startDirectory = null)
    {
        // If a start directory is specified and exists, use the Win32 COM dialog
        // which supports setting an arbitrary initial folder.
        if (!string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory))
        {
            return await Task.Run(() => PickWithComDialogAsync(startDirectory));
        }

        // Default: use the WinRT FolderPicker
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickFileAsync(string? startDirectory = null, params string[] extensions)
    {
        if (!string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory))
        {
            return await Task.Run(() => PickFileWithComDialog(startDirectory, extensions));
        }

        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        if (extensions is { Length: > 0 })
        {
            foreach (var ext in extensions)
            {
                if (!string.IsNullOrWhiteSpace(ext))
                    picker.FileTypeFilter.Add(ext.StartsWith('.') ? ext : $".{ext}");
            }
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window is not null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Uses the Win32 IFileOpenDialog COM interface to show a folder picker
    /// with a specific initial directory.
    /// </summary>
    private static string? PickWithComDialogAsync(string startDirectory)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            result = ShowComFolderDialog(startDirectory);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string? PickFileWithComDialog(string startDirectory, string[] extensions)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            result = ShowComFileDialog(startDirectory, extensions);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string? ShowComFolderDialog(string startDirectory)
    {
        var hr = CoCreateInstance(
            ref CLSID_FileOpenDialog, nint.Zero, 1 /* CLSCTX_INPROC_SERVER */,
            ref IID_IFileOpenDialog, out var dialogPtr);

        if (hr != 0 || dialogPtr == nint.Zero)
            return null;

        var dialog = (IFileOpenDialog)Marshal.GetObjectForIUnknown(dialogPtr);
        Marshal.Release(dialogPtr);

        try
        {
            // Set folder picker mode
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

            // Set the initial directory
            if (!string.IsNullOrEmpty(startDirectory))
            {
                var riid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(startDirectory, nint.Zero, ref riid, out var folderItem);
                if (folderItem is not null)
                {
                    dialog.SetFolder(folderItem);
                }
            }

            // Get the window handle
            var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            var hwnd = window is not null ? WindowNative.GetWindowHandle(window) : nint.Zero;

            hr = dialog.Show(hwnd);
            if (hr != 0) // User cancelled or error
                return null;

            dialog.GetResult(out var item);
            if (item is null)
                return null;

            item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
            return path;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private static string? ShowComFileDialog(string startDirectory, string[] extensions)
    {
        var hr = CoCreateInstance(
            ref CLSID_FileOpenDialog, nint.Zero, 1 /* CLSCTX_INPROC_SERVER */,
            ref IID_IFileOpenDialog, out var dialogPtr);

        if (hr != 0 || dialogPtr == nint.Zero)
            return null;

        var dialog = (IFileOpenDialog)Marshal.GetObjectForIUnknown(dialogPtr);
        Marshal.Release(dialogPtr);

        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST);

            if (extensions is { Length: > 0 })
            {
                var patterns = string.Join(";", extensions.Select(e => e.StartsWith('.') ? $"*{e}" : $"*.{e}"));
                var filters = new[]
                {
                    new COMDLG_FILTERSPEC { pszName = $"Supported Files ({patterns})", pszSpec = patterns },
                    new COMDLG_FILTERSPEC { pszName = "All Files (*.*)", pszSpec = "*.*" }
                };
                dialog.SetFileTypes((uint)filters.Length, filters);
                dialog.SetFileTypeIndex(1);
            }

            if (!string.IsNullOrEmpty(startDirectory))
            {
                var riid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(startDirectory, nint.Zero, ref riid, out var folderItem);
                if (folderItem is not null)
                {
                    dialog.SetFolder(folderItem);
                }
            }

            var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            var hwnd = window is not null ? WindowNative.GetWindowHandle(window) : nint.Zero;

            hr = dialog.Show(hwnd);
            if (hr != 0)
                return null;

            dialog.GetResult(out var item);
            if (item is null)
                return null;

            item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
            return path;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    #region COM Interop

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private static Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszSpec;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, nint pUnkOuter, int dwClsContext,
        ref Guid riid, out nint ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, nint pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint hwndOwner);
        void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray)] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise();  // unused
        void Unadvise();  // unused
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void SetFolder([MarshalAs(UnmanagedType.Interface)] IShellItem psi);
        void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
        // remaining methods not needed
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();  // unused
        void GetParent();  // unused
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        // remaining methods not needed
    }

    #endregion
}
