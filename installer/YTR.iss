; YTR Inno Setup Script
; Requires Inno Setup 7.x — https://jrsoftware.org/isinfo.php

#define MyAppName "YTR"
; Patched automatically by publish-windows.bat from Directory.Build.props
#define MyAppVersion "1.0.0"
#define MyAppPublisher "JAMGALACTIC"
#define MyAppURL "https://jamgalactic.com"
#define MyAppExeName "YTR.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{B7F3A2E1-4D5C-4F8A-9B2E-1A3C5D7E9F0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\publish\installer
OutputBaseFilename=YTR-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uncomment and set path when you have an .ico file:
SetupIconFile=..\YTR.Core\Resources\appicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Start YTR with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Main application files from publish output
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Bundled tools — place yt-dlp.exe, ffmpeg.exe, ffprobe.exe in installer\tools\ before building
Source: "tools\yt-dlp.exe"; DestDir: "{app}\Resources\App"; Flags: ignoreversion
Source: "tools\ffmpeg.exe"; DestDir: "{app}\Resources\App"; Flags: ignoreversion
Source: "tools\ffprobe.exe"; DestDir: "{app}\Resources\App"; Flags: ignoreversion
Source: "tools\node.exe"; DestDir: "{app}\Resources\App"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\App\appicon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional: start with Windows
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up app data on uninstall (optional — user can keep their data)
; Type: filesandordirs; Name: "{localappdata}\YTR"

[Code]
// Kill running instance before install/upgrade
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
