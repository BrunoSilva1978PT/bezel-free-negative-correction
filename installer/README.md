# Release build — Wallpaper Bezel Free Correction

Producing a new version ships through three stages: bump the version,
publish the app, then wrap it in the Inno Setup installer. Every step
is a single command.

## Prerequisites

- .NET 9 SDK (the project targets `net9.0-windows`).
- Inno Setup 6.1 or newer, with the command-line compiler `ISCC.exe`
  available (default location is `C:\Program Files (x86)\Inno Setup 6`).
- GitHub CLI (`gh`) — only needed when publishing the release.

## 1. Bump the version

Update both places so the HUD, splash, update checker and installer
agree:

- `src/BezelFreeCorrection/BezelFreeCorrection.csproj` →
  `<Version>`, `<AssemblyVersion>`, `<FileVersion>`,
  `<InformationalVersion>`.
- `installer/WallpaperBezelFreeCorrection.iss` → `MyAppVersion`.

Commit the bump on `dev`, then merge to `main`.

## 2. Publish the app (framework-dependent)

From the repo root:

```powershell
dotnet publish src/BezelFreeCorrection/BezelFreeCorrection.csproj `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o publish/win-x64
```

Output is ~300 KB; the .NET 9 Desktop Runtime is resolved on the
user's machine at launch time.

## 3. Build the installer

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
    installer\WallpaperBezelFreeCorrection.iss
```

Result: `installer/output/WallpaperBezelFreeCorrection-vX.Y.Z.exe`.

The installer:
- Writes to `Program Files\Wallpaper Bezel Free Correction` (admin).
- Registers an uninstaller in Add/Remove Programs.
- Creates Start Menu entries; Desktop shortcut is a checkbox on the
  "Additional tasks" page.
- Detects .NET 9 Desktop Runtime x64. If absent, downloads the
  official Microsoft installer (~55 MB) and runs it silently before
  copying the app files.

## 4. Publish the GitHub release

Tag, push the tag, then create the release with the installer as a
release asset (the in-app update checker looks for `*.exe` assets):

```powershell
git tag -a v1.0.0 -m "Wallpaper Bezel Free Correction v1.0.0"
git push origin v1.0.0
gh release create v1.0.0 `
    installer/output/WallpaperBezelFreeCorrection-v1.0.0.exe `
    --title "v1.0.0" `
    --notes-file RELEASE_NOTES.md
```

`UpdateChecker.cs` polls `releases/latest` and triggers the
self-update dialog when the user is on an older version.
