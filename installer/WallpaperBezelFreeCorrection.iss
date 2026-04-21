; Inno Setup script for Wallpaper Bezel Free Correction.
;
; Produces a single .exe installer that:
;   - drops the framework-dependent app into Program Files
;   - registers an uninstaller in Add/Remove Programs
;   - creates Start Menu + optional Desktop shortcuts
;   - detects whether .NET 9 Desktop Runtime x64 is present and, if
;     not, runs the BUNDLED runtime installer (no network access at
;     install time, to avoid Defender's downloader-trojan heuristic)
;
; Build with:   iscc installer\WallpaperBezelFreeCorrection.iss
; Expects:      installer\deps\windowsdesktop-runtime-9.0-win-x64.exe
;                 (~60 MB, downloaded out-of-band by tooling, not committed)
;
; Output:       installer\output\WallpaperBezelFreeCorrection-vX.Y.Z.exe

#define MyAppName "Wallpaper Bezel Free Correction"
#define MyAppShortName "WallpaperBezelFreeCorrection"
#define MyAppVersion "1.0.8"
#define MyAppPublisher "BrunoSilva1978PT"
#define MyAppURL "https://github.com/BrunoSilva1978PT/bezel-free-negative-correction"
#define MyAppExeName "BezelFreeCorrection.exe"
#define DotnetRuntimeFile "windowsdesktop-runtime-9.0-win-x64.exe"

[Setup]
AppId={{7B2A6B8C-5D4E-4F1B-9C2E-7F3A1B8C9D0E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppContact={#MyAppURL}
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
AppComments=Generates wallpapers with negative bezel correction for triple-monitor sim rigs.
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename={#MyAppShortName}-v{#MyAppVersion}
; Normal compression instead of ultra — keeps the installer under
; antivirus heuristics that distrust aggressively packed binaries.
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\BezelFreeCorrection\app.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main app binary — emitted by `dotnet publish --self-contained false`.
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";                         DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Bundled .NET 9 Desktop Runtime x64 installer. Extracted to {tmp} at
; install time, executed only when the runtime is missing, cleaned up
; at the end so Program Files stays small.
Source: "deps\{#DotnetRuntimeFile}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}";                   Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Silent install of the bundled runtime when missing.
Filename: "{tmp}\{#DotnetRuntimeFile}"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Installing .NET 9 Desktop Runtime…"; \
    Check: not IsDotNetRuntimeInstalled; Flags: waituntilterminated
; Launch the app after install. postinstall (without skipifsilent)
; means the entry runs in silent mode too — essential for the in-app
; auto-update path — while still appearing as a Finish-page checkbox
; in interactive installs. runasoriginaluser launches the app as the
; normal user even though Setup itself runs elevated.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall runasoriginaluser

[Code]
// Check whether .NET 9 Desktop Runtime x64 is present by looking for
// any 9.x folder under the shared WindowsDesktop.App directory. No
// network calls from this script, at install or at compile time.
function IsDotNetRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  Path: String;
begin
  Result := False;
  Path := ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(Path) then
  begin
    if FindFirst(Path + '\9.*', FindRec) then
    begin
      try
        Result := True;
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;
