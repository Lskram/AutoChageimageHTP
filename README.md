# AutoChageimageHTP

Windows tool for replacing Heartopia in-game photo cache images.

The app does not modify the game install folder. It only replaces photo cache files under the current Windows user's `AppData\\LocalLow` profile after backing up the original cache files.

## Version Notes

### v1.1.0 - Safe Replace Baseline

Tag: `v1.1.0`

- Verified cache-folder selection only.
- Backup-first replacement flow.
- Temporary-file writes before swap.
- Automatic rollback if replacement fails midway.
- Build script aligned with the current framework-dependent Windows publish flow.

## Download / Run

Builds are local outputs. The repo is intended to be cloneable and buildable from source.

If you already built the app locally, the executable will be created at:

```text
dist/HeartopiaPhotoReplacer.exe
```

If Windows SmartScreen warns, it is because this executable is not code-signed.

## Basic Workflow

1. Open `HeartopiaPhotoReplacer.exe`.
2. Select a workspace folder. The app creates:
   - `ReplacementImages`
   - `Backups`
   - `Logs`
3. Add the image you want to use with `Import` or place it in `ReplacementImages`.
4. In Heartopia, take a new photo in-game. You do not need to export it.
5. Click `Refresh Photos`.
6. Select the newest photo ID.
7. Click `Replace Selected`.
8. Refresh the in-game album or restart the game if the old image is still cached in memory.

## What It Does

- Auto-detects Heartopia photo cache folders from `%USERPROFILE%\\AppData\\LocalLow`.
- Finds cache files such as:

```text
134213537632675560_256_144.jpg
134213537632675560_512_288.jpg
134213537632675560_1564_880.jpg
134213537632675560_1920_1080.jpg
```

- Resizes the replacement image to every cache size for the selected photo ID.
- Encodes the image as JPG quality `98`.
- Encrypts the JPG bytes with the same AES format used by the game cache.
- Backs up original cache files before replacing them.
- Only allows verified `ScreenCapture\\Photo` cache folders.
- Writes replacement data through temporary files and restores backups automatically if a replace step fails midway.

## Build From Source

Requirements:

- Windows
- .NET SDK 8 or newer

Quick verify from a fresh clone:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify_repo.ps1
```

Build:

```powershell
dotnet build .\src\HeartopiaPhotoReplacer\HeartopiaPhotoReplacerApp.csproj -c Release
```

Publish a Windows executable:

```powershell
.\build.ps1
```

The published executable will be copied to:

```text
dist/HeartopiaPhotoReplacer.exe
```

The current build script publishes a framework-dependent Windows executable because the `win-x64` self-contained publish path is unstable in the local .NET SDK used for this repo.

## Notes

- The executable is unsigned.
- The repository does not need the generated `.exe` committed in order to build successfully.
- The app is designed for the Windows PC version where Heartopia stores cache files in `AppData\\LocalLow`.
- If the game updates and changes the cache encryption or folder layout, this tool may need to be updated.
