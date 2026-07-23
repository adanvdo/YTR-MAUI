# Yank - Transform - Render

<p align="center">
  <img src="YTR.Core/Resources/YTRBanner.jpg" alt="YTR Banner" />
</p>

YTR is a fast, feature-rich app for downloading media from popular platforms. Available on Windows and Android.

![Windows](https://img.shields.io/badge/platform-Windows-blue)
![Android](https://img.shields.io/badge/platform-Android-green)
![License](https://img.shields.io/badge/license-GPL--3.0-blue)

---

## Privacy & Transparency

- No ads, ever
- No in-app purchases
- No data collection or tracking
- No background network usage — the app only connects to the internet when you initiate a download
- Update checks are manual — triggered only when you click "Check for Updates" in Settings

---

## Features at a Glance

- Download video and audio from YouTube, Reddit, Twitter/X, Vimeo, Instagram, Twitch, and TikTok
- Full playlist support — download all items or pick specific ones
- Choose exact quality: from 480p SD to 4K UHD
- Extract segments (clips) from any video by specifying start/end times
- Visual crop tool to trim video borders
- Convert to your preferred format on the fly
- Dark and light theme
- Global hotkey for quick downloads without switching windows
- Built-in auto-updater for the app and its download tools
- Download history with search, re-download, and bulk management

---

## System Requirements

### Windows

- Windows 10 (version 1903 or later), 64-bit
- ~150 MB disk space (including bundled tools)
- Internet connection for downloading media

### Android

- *Coming Soon*

---

## Installation

1. Download the latest installer from the [Releases](https://github.com/adanvdo/YTR-MAUI/releases) page.
2. Run `YTR-Setup.exe` and follow the prompts.
3. Launch YTR from the Start Menu or desktop shortcut.

All required tools (yt-dlp, FFmpeg) are bundled with the installer — no extra setup needed.

### Optional install choices

- **Desktop shortcut** — create a shortcut on your desktop (unchecked by default)
- **Start with Windows** — launch YTR automatically on login (unchecked by default)

---

## Getting Started

1. Open YTR.
2. Paste a video or playlist URL into the input bar.
3. Click **List Formats** to see all available quality options, or skip straight to downloading.
4. Choose a download action:
   - **Download Best** — grabs the highest quality video + audio combination
   - **Download Audio** — extracts audio only
   - **Download Selected Format** — downloads the exact stream(s) you selected from the format list
5. Your file is saved to the configured download folder, which opens automatically when done.

---

## Supported Platforms

| Platform | Video | Audio | Playlists |
|----------|:-----:|:-----:|:---------:|
| YouTube  | ✓     | ✓     | ✓         |
| Reddit   | ✓     | ✓     | —         |
| Twitter/X| ✓     | ✓     | —         |
| Vimeo    | ✓     | ✓     | —         |
| Instagram| ✓     | ✓     | —         |
| Twitch   | ✓     | ✓     | —         |
| TikTok   | ✓     | ✓     | —         |

---

## Download Options

Each download can be fine-tuned with the options panel on the right side of the app.

### Segment (Clip Extraction)

Extract a specific portion of a video instead of downloading the whole thing.

- **Start** — the timestamp where the clip begins
- **End Time / Duration** — where it ends (you choose the mode in Settings → Appearance)

### Crop

Trim pixel borders from the video frame (top, bottom, left, right). Use the **Visual Crop Tool** to draw the crop area on a thumbnail preview.

### Convert

Convert the downloaded media to a different format:

| Video formats | Audio formats |
|---------------|---------------|
| MP4           | MP3           |
| MKV           | AAC           |
| WebM          | FLAC          |
| GIF           | Opus          |

If you always want the same output format, enable **Always convert to preferred format** in Settings → Advanced.

### Limits

Cap downloads by resolution or file size:

- **Max Resolution** — 480p, 720p, 1080p, 4K, or Any (no limit)
- **Max File Size** — set a size cap in MB (0 = unlimited)

These can also be enforced globally from Settings → Advanced → Restrictions.

---

## Format Selection Modes

YTR offers two ways to browse available formats:

- **Preset** — shows pre-built video + audio combinations for quick selection
- **Custom** — lets you independently pick a video stream and an audio stream for full control

Switch between modes in Settings → Appearance → Format Mode.

---

## Playlist Downloads

When you paste a playlist URL:

1. YTR displays a grid of all playlist items with thumbnails.
2. Select or deselect individual items.
3. Choose **Download All** (video+audio) or **Download All Audio**.
4. A folder is automatically created for the playlist.

Progress is tracked per-item so you can see how far along the batch is.

---

## History

The **History** page logs every download with:

- Platform, title, format, and date
- File status indicator (shows if the file still exists on disk)
- **Open location** — jump to the file in Explorer
- **Re-download** — re-run the same download with original settings

Bulk actions let you clear logs or delete files + logs, filtered by video or audio.

History retention is configurable (default: 30 days).

---

## Settings

Access settings from the gear icon. Changes take effect after clicking **Save**.

### General

| Setting | Description |
|---------|-------------|
| Video Download Path | Where video files are saved |
| Audio Download Path | Where audio files are saved |
| Use video title as filename | Names the file after the media title instead of its ID |
| Create folder for playlists | Groups playlist items in a dedicated folder |
| Auto-open download location | Opens the folder in Explorer when a download finishes |

### Appearance

| Setting | Description |
|---------|-------------|
| Dark mode | Toggle between dark and light theme |
| Format Mode | Preset (quick pairs) or Custom (pick individual streams) |
| Segment Mode | Choose whether the clip end-point is specified as an end time or a duration |

### Advanced

**Processing**

| Setting | Description |
|---------|-------------|
| Always convert to preferred format | Automatically converts every download to your preferred format |
| Preferred Video Format | Target video format (MP4, MKV, WebM, etc.) |
| Preferred Audio Format | Target audio format (MP3, AAC, FLAC, Opus, etc.) |
| Fetch missing metadata | Retrieves extra info when listing formats |
| Verbose output | Shows detailed processing logs in the status bar |

**Restrictions**

| Setting | Description |
|---------|-------------|
| Enforce restrictions | Applies max resolution and file size limits to every download |
| Max Resolution | Global resolution cap |
| Max File Size (MB) | Global file size cap (0 = unlimited) |

**Hotkeys**

| Setting | Description |
|---------|-------------|
| Enable global hotkey | Triggers a quick download from the clipboard URL without opening the window |
| Modifiers | Ctrl, Shift, Alt, or combinations |
| Key | Any letter, number, or function key |

Default hotkey: `Ctrl+Shift+D`

### About

- Current app version
- Check for app updates (Stable, Beta, or Alpha channels)
- Download and install updates in-app
- Check and update bundled tools (yt-dlp, FFmpeg)

---

## Global Hotkey (Quick Download)

When enabled, pressing your configured hotkey (default `Ctrl+Shift+D`) will:

1. Read the URL currently on your clipboard
2. Download it using your default settings (best quality, preferred format)
3. Save to your configured download folder

No need to open the app window — useful for rapid saving while browsing.

---

## Updating

### App updates

Go to Settings → About and click **Check for Updates**. If a new version is available, click **Download** and then **Install & Restart**. You can choose between Stable, Beta, and Alpha release channels.

### Tool updates (yt-dlp & FFmpeg)

YTR bundles yt-dlp and FFmpeg. To keep them current, go to Settings → About → Dependencies, click **Check Versions**, and update individually if newer versions are available.

---

## Troubleshooting

**Download fails or no formats found**
- The media URL may be private, geo-restricted, or require login.
- Try updating yt-dlp in Settings → About → Dependencies.

**Conversion fails**
- Ensure FFmpeg is up to date via Settings → About → Dependencies.

**Hotkey doesn't work**
- Another application may have registered the same key combination. Try a different one in Settings → Advanced → Hotkeys.

**App won't start after update**
- Reinstall from the latest installer on the [Releases](https://github.com/adanvdo/YTR-MAUI/releases) page. Your settings and history are preserved.

---

## Uninstalling

Use **Add or Remove Programs** in Windows Settings, or run the uninstaller from the Start Menu group. Your download history and settings in `%localappdata%\YTR` are kept unless you remove them manually.

---

## Credits

Built by [JAMGALACTIC](https://jamgalactic.com)

Powered by:
- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — media extraction
- [FFmpeg](https://ffmpeg.org/) — media processing and conversion

---

## License

This project is licensed under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html). You are free to use, modify, and distribute this software, provided that any derivative work is also released under the same license. See the [LICENSE](LICENSE) file for full terms.

## Contact

Questions or feedback? Reach out at jesse@jamgalactic.com
