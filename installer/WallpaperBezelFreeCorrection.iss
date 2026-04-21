; Inno Setup script for Wallpaper Bezel Free Correction.
;
; Produces a single .exe installer that:
;   - drops the framework-dependent app into Program Files
;   - registers an uninstaller in Add/Remove Programs
;   - creates Start Menu + optional Desktop shortcuts
;   - detects whether .NET 9 Desktop Runtime x64 is present and, if not,
;     downloads the official installer from Microsoft and runs it
;     silently before copying app files
;
; Build with:   iscc installer\WallpaperBezelFreeCorrection.iss
; Output is at: installer\output\WallpaperBezelFreeCorrection-vX.Y.Z.exe

#define MyAppName "Wallpaper Bezel Free Correction"
#define MyAppShortName "WallpaperBezelFreeCorrection"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "BrunoSilva1978PT"
#define MyAppURL "https://github.com/BrunoSilva1978PT/bezel-free-negative-correction"
#define MyAppExeName "BezelFreeCorrection.exe"

[Setup]
AppId={{7B2A6B8C-5D4E-4F1B-9C2E-7F3A1B8C9D0E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename={#MyAppShortName}-v{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main app binaries — emitted by `dotnet publish --self-contained false`.
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\win-x64\*.pdb";            DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md";                         DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}";                   Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";             Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Silent install of the downloaded .NET runtime if missing.
Filename: "{tmp}\dotnet-runtime.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "Installing .NET 9 Desktop Runtime…"; \
    Check: not IsDotNetRuntimeInstalled; Flags: waituntilterminated
; Launch the app after install, but not in silent mode.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

// Check whether .NET 9 Desktop Runtime x64 is present by looking for
// any 9.x folder under the shared WindowsDesktop.App directory.
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

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing),
    'Fetching .NET 9 Desktop Runtime installer…',
    nil);
end;

// After the user confirms installation, if the runtime is missing, we
// download its installer into {tmp} and let the [Run] section invoke
// it silently before the app files land in Program Files.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (not IsDotNetRuntimeInstalled()) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(
      'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe',
      'dotnet-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if SuppressibleMsgBox(
             'Could not download the .NET 9 Desktop Runtime: ' + GetExceptionMessage +
             Chr(13) + Chr(10) +
             'Install it manually from https://dotnet.microsoft.com/download and retry.',
             mbCriticalError, MB_OK, IDOK) = IDOK then
          Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
