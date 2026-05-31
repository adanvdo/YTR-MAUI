# YTR — Media Downloader (MAUI Blazor Hybrid)

Cross-platform media downloader rebuilt from the ground up with .NET 10, MAUI Blazor Hybrid, and MudBlazor.

## Solution Structure

```
YTR.slnx
├── YTR.Core/          .NET 10 class library — domain models, service interfaces, EF Core
├── YTR.Web/           Razor Class Library — Blazor UI components (MudBlazor)
└── YTR.Maui/          .NET MAUI host — Windows + Android
```

## Prerequisites

- .NET 10 SDK with MAUI workload (`dotnet workload install maui`)
- Windows 10 SDK (for Windows target)
- Android SDK (for Android target)

## Build

```bash
# Core + Web libraries
dotnet build YTR.Core/YTR.Core.csproj
dotnet build YTR.Web/YTR.Web.csproj

# MAUI app (Windows)
dotnet build YTR.Maui/YTR.Maui.csproj -f net10.0-windows10.0.19041.0

# MAUI app (Android)
dotnet build YTR.Maui/YTR.Maui.csproj -f net10.0-android
```

## Run (Windows)

```bash
dotnet run --project YTR.Maui/YTR.Maui.csproj -f net10.0-windows10.0.19041.0
```

## Architecture

- **Clean Architecture** — Core library has zero UI dependencies
- **Dependency Injection** — All services registered in `MauiProgram.cs`
- **Result Pattern** — Expected failures return `Result<T>` instead of throwing
- **EF Core + SQLite** — Download history with proper async queries
- **IOptions Pattern** — Focused settings classes (`DownloadOptions`, `RestrictionOptions`, etc.)
- **Platform Abstraction** — `IPlatformService`, `IProcessRunner` for cross-platform ops

## Key Services

| Interface | Responsibility |
|-----------|---------------|
| `IUrlAnalyzer` | URL detection and normalization |
| `IYtDlpService` | yt-dlp process invocation |
| `IMediaProcessor` | FFmpeg operations |
| `IDownloadOrchestrator` | Full download workflow coordination |
| `IHistoryService` | Download history CRUD |
| `ISettingsService` | Settings persistence |
| `IAppUpdateService` | App update management |
| `IDependencyUpdateService` | yt-dlp/FFmpeg updates |

## Migration Status

- [x] Phase 1: Core library foundation (models, enums, interfaces, EF Core, UrlAnalyzer, HistoryService)
- [x] Phase 2: Blazor UI skeleton (Home, History, Settings, Updates pages + shared components)
- [x] Phase 3: MAUI host wired up (DI, MudBlazor, routing)
- [ ] Phase 4: Download engine implementation (IYtDlpService, IMediaProcessor)
- [ ] Phase 5: Data migration from old app
- [ ] Phase 6: Platform features (tray, hotkeys, share intent)
