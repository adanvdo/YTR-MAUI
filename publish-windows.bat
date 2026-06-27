@echo off
REM ============================================================
REM YTR Windows Publish + Installer Build Script
REM ============================================================
REM Prerequisites:
REM   - .NET 10 SDK with MAUI workload
REM   - Inno Setup 6.x installed (iscc.exe on PATH or set below)
REM   - yt-dlp.exe, ffmpeg.exe, ffprobe.exe in installer\tools\
REM ============================================================

setlocal

set CONFIGURATION=Release
set FRAMEWORK=net10.0-windows10.0.19041.0
set RUNTIME=win-x64
set PUBLISH_DIR=publish\win-x64
set INNO_COMPILER="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

echo.
echo [1/3] Cleaning previous publish output...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "publish\installer" rmdir /s /q "publish\installer"

echo.
echo [2/3] Publishing YTR.Maui for Windows x64...
dotnet publish YTR.Maui\YTR.Maui.csproj ^
    -f %FRAMEWORK% ^
    -c %CONFIGURATION% ^
    -r %RUNTIME% ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:PublishTrimmed=false ^
    -o %PUBLISH_DIR%

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: dotnet publish failed.
    exit /b 1
)

echo.
echo [3/3] Building installer with Inno Setup...

REM Check that bundled tools exist
if not exist "installer\tools\yt-dlp.exe" (
    echo WARNING: installer\tools\yt-dlp.exe not found. Installer will fail.
    echo Download from: https://github.com/yt-dlp/yt-dlp/releases/latest
)
if not exist "installer\tools\ffmpeg.exe" (
    echo WARNING: installer\tools\ffmpeg.exe not found. Installer will fail.
    echo Download from: https://www.gyan.dev/ffmpeg/builds/
)
if not exist "installer\tools\ffprobe.exe" (
    echo WARNING: installer\tools\ffprobe.exe not found. Installer will fail.
    echo Download from: https://www.gyan.dev/ffmpeg/builds/
)

if exist %INNO_COMPILER% (
    %INNO_COMPILER% installer\YTR.iss
    if %ERRORLEVEL% neq 0 (
        echo ERROR: Inno Setup compilation failed.
        exit /b 1
    )
    echo.
    echo SUCCESS: Installer created at publish\installer\
) else (
    echo.
    echo Inno Setup not found at %INNO_COMPILER%
    echo Install from: https://jrsoftware.org/isdl.php
    echo Or update the INNO_COMPILER path in this script.
    echo.
    echo Publish output is still available at: %PUBLISH_DIR%\
)

echo.
echo Done.
endlocal
