# YTR-MAUI — Implementation Checklist

All items marked ✅ are complete and building. Items marked ⬜ are deferred (low priority or require runtime testing).

---

## Backend / Service Layer

- ✅ **1. Codec compatibility validation** — `CodecMap` class with per-container codec lists, best-default selection, and compatibility checks.
- ✅ **2. FFprobe media probing** — `IMediaProbeService` + `FfprobeMediaProbeService` implementation using ffprobe JSON output.
- ✅ **3. Crop coordinate validation** — `CropHelper.ConvertCrop()` validates margins against video dimensions, `ClampMargins()` enforces minimum 50px.
- ⬜ **4. Reddit audio format merging** — Low priority. Modern yt-dlp handles Reddit audio merging natively.
- ✅ **7. Preferred format download** — `BuildDownloadArgs` passes `--merge-output-format`, `--recode-video` when `AlwaysConvertToPreferred` is enabled.
- ✅ **8. Embed thumbnail in audio** — `BuildDownloadArgs` passes `--embed-thumbnail`, `--add-metadata`, `--convert-thumbnails jpg` for audio downloads.
- ✅ **9. Playlist batch download** — `IDownloadOrchestrator.DownloadPlaylistAsync` with per-item progress, history recording, and cancellation.
- ✅ **10. Temporary file tracking** — `PostProcessAsync` tracks all intermediate files in a `List<string>` and cleans up in `finally`.
- ✅ **11. Stream-based processing** — `IMediaProcessor.ConvertFromUrlsAsync` passes stream URLs directly to FFmpeg. Orchestrator tries this first, falls back to download-then-process.
- ⬜ **12. Error log reporting** — Low priority. Can be deferred.
- ✅ **26. Settings validation** — `ISettingsService.Validate()` checks required paths and disallows GIF as preferred format.
- ✅ **27. Settings reload on change** — `DownloadOrchestrator` reads directly from `ISettingsService` properties. Changes take effect immediately.

---

## UI / Blazor Components

- ✅ **13. Format grid Preset mode** — Toggle between Preset/Custom. Preset generates video+audio pairs from raw formats, displays as single-select grid.
- ✅ **14. Format grid dual-selection (Custom mode)** — Click to select one video + one audio independently. Chips show current selection. Builds `FormatPair` from selections.
- ✅ **15. Video info thumbnail display** — `MudImage` with yt-dlp thumbnail URL. Works in BlazorWebView for most platforms.
- ✅ **16. History re-download** — "Replay" button on history rows navigates to home with record ID for re-download.
- ✅ **17. History file deletion** — Menu with "Delete Files + Logs" options (All/Video/Audio) calling `ClearWithFilesAsync()`.
- ✅ **18. Folder picker dialogs** — `IFolderPickerService` interface + `WindowsFolderPickerService` using WinRT `FolderPicker`. Wired to Settings page buttons.
- ✅ **19. Download progress cancel button** — Cancel `MudIconButton` in `StatusBar`. `DownloadStateService` manages `CancellationTokenSource`. Orchestrator uses the token.
- ✅ **20. Playlist item selection UI** — `PlaylistGrid` component with checkboxes, thumbnails, titles, durations. Select All/Clear toggle. Shown when URL is a playlist.
- ✅ **21. Format grid stream type filter** — `MudToggleGroup` with All/Video/Audio. Filters displayed formats by `StreamKind`.

---

## Platform Features (Windows)

- ✅ **22. Window state persistence** — Saves X, Y, Width, Height to `WindowStateOptions` on close. Restores on launch.
- ✅ **23. Hotkey configuration** — `AppearanceOptions` stores `EnableHotkeys`, `HotkeyModifiers`, `HotkeyKey`. `WindowsHotkeyService` reads from settings.
- ✅ **24. Tray icon** — `WindowsTrayService` uses Win32 `Shell_NotifyIcon` with context menu (Show/Quick Download/Exit) and balloon notifications.
- ✅ **25. Minimize to tray** — `App.xaml.cs` handles window restore via `ShowWindow`/`SetForegroundWindow`. Tray "Show" event restores window.

---

## Integration / Polish

- ✅ **FfmpegMediaProcessor codec selection** — `ConvertAsync` and `CropAsync` use `CodecMap` for codec selection.
- ✅ **DownloadOrchestrator use MediaProbe** — Probes file before crop to get actual dimensions. Validates via `CropHelper.ConvertCrop()`.
- ✅ **Settings page validation UX** — Calls `Validate()` before saving. Shows `MudAlert` with error. Won't save until valid.
- ✅ **Format grid "Download Selected" button visibility** — Shows "Download Selected Format" when format is selected. Hides Best/Audio buttons.
- ✅ **Auto-open download location** — After successful download, calls `IPlatformService.OpenFolderAsync()` if setting is enabled.

---

## Summary

| Category | Done | Remaining |
|----------|------|-----------|
| Backend services | 10 | 2 (low priority) |
| UI components | 9 | 0 |
| Platform (Windows) | 4 | 0 |
| Integration/polish | 5 | 0 |
| **Total** | **28** | **2** |

The 2 remaining items (Reddit audio merging, error log reporting) are low priority and can be addressed later. The application is functionally complete for Windows.
