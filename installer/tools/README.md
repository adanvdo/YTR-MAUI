# Bundled Tools

Place the following executables in this folder before building the installer:

- **yt-dlp.exe** — Download from https://github.com/yt-dlp/yt-dlp/releases/latest
- **ffmpeg.exe** — Download from https://www.gyan.dev/ffmpeg/builds/ (essentials build)
- **ffprobe.exe** — Included in the FFmpeg download above

These are bundled into the installer and placed in `{install dir}\Resources\App\` at install time.

> Do NOT commit these binaries to source control. They are listed in .gitignore.
