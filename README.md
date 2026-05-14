# AutoChageimageHTP

Windows tool for replacing Heartopia in-game photo cache images.

The app does not modify the game install folder. It only replaces encrypted photo cache files under the current Windows user's `AppData\LocalLow` profile after creating backups first.

## Current Release

### v1.5.0 - Release Hardening

Status: current `main`

- Adds a first-run usage notice that must be acknowledged before the tool is used.
- Adds a `Safety & Support` surface for backup retention, manual cleanup, notice review, and support bundle export.
- Exports a support bundle with diagnostics, current health state, config, and log files.
- Adds automatic backup retention cleanup after replace and restore operations.
- Publishes a self-contained `win-x64` package and a versioned `.zip`.
- Adds automated regression coverage for probe, replace, restore, and backup cleanup flows.

## Version History

### v1.4.0 - Health and Backup History

- Adds a compact in-app health summary for cache validation, probe state, backup count, retention policy, and free disk space.
- Adds backup history selection so you can restore a chosen snapshot instead of only the newest one.

### v1.3.0 - Recovery and Preflight Guard

- Adds `Restore Latest` in the UI for rolling a selected photo ID back to its newest backup snapshot.
- Requires a successful compatibility probe on the current cache before replacement can start.
- Checks free disk space before creating backups or temporary replacement files.
- Checks whether target cache files are locked or read-only before replacement or restore.
- Verifies the encrypted image files again after each replace or restore write.

### v1.2.0 - Compatibility Guard Update

- Adds a `Probe` action for checking whether the selected Heartopia photo cache still matches the supported encrypted image format.
- Validates the selected target cache files before any backup or replacement write begins.
- Fails closed when the cache can no longer be decrypted or parsed, instead of writing into an unsupported format.

### v1.1.0 - Safe Replace Baseline

- Verified cache-folder selection only.
- Backup-first replacement flow.
- Temporary-file writes before swap.
- Automatic rollback if replacement fails midway.

## Download / Run

Builds are local outputs. The repo is intended to be cloneable and buildable from source.

After running the release build:

```text
dist/HeartopiaPhotoReplacer-v1.5.0-win-x64/HeartopiaPhotoReplacer.exe
dist/HeartopiaPhotoReplacer-v1.5.0-win-x64.zip
```

The package is self-contained for `win-x64`, so the target PC does not need a separate .NET runtime installed.

If Windows SmartScreen warns, it is because the executable is not code-signed yet.

## Basic Workflow

1. Open `HeartopiaPhotoReplacer.exe`.
2. Accept the usage notice on first launch.
3. Select a workspace folder. The app creates:
   - `ReplacementImages`
   - `Backups`
   - `Logs`
   - `SupportBundles`
4. Add the image you want to use with `Import` or place it in `ReplacementImages`.
5. In Heartopia, take a new photo in-game. You do not need to export it.
6. Click `Refresh Photos`.
7. Select the newest photo ID.
8. Click `Probe`.
9. Click `Replace Selected`, `Restore Latest`, or `History...`.
10. Use `Safety & Support` to export diagnostics or clean old backups if needed.
11. Refresh the in-game album or restart the game if the old image is still cached in memory.

## Safety Measures

- Only allows verified `ScreenCapture\Photo` cache folders.
- Requires a successful compatibility probe before replacement starts.
- Validates encrypted target files before replace or restore.
- Writes through temporary files before swapping the real cache files.
- Creates a backup snapshot before replace and a restore point before restore.
- Rolls back automatically if a replace or restore step fails midway.
- Verifies the encrypted output again after writing, instead of assuming success from the file operation alone.
- Checks free disk space and file locks before replace or restore operations.
- Lets you restore either the latest backup or a chosen snapshot from history.
- Applies backup retention cleanup automatically after successful replace or restore operations.

## Safety & Support

The `Safety & Support` button opens maintenance actions for release use:

- review the current compatibility and health state
- change backup retention policy
- trigger backup cleanup immediately
- open the backup folder
- re-read the usage notice
- export a support bundle as a `.zip`

The support bundle includes:

- `diagnostics.json`
- `health_summary.txt`
- `config.json` if present
- `replacer.log` if present
- package docs such as `README.md`, `CHANGELOG.md`, `NOTICE.txt`, and `EULA.txt` when available

## Build From Source

Requirements:

- Windows
- .NET SDK 8 or newer

Quick verify from a fresh clone:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify_repo.ps1
```

Run automated tests only:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test_repo.ps1
```

Build a release package:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The release build now:

- publishes `win-x64` as a self-contained single-file executable
- creates a versioned package folder under `dist`
- creates a matching `.zip`
- copies `README.md`, `CHANGELOG.md`, `NOTICE.txt`, and `EULA.txt` into the package
- optionally code-signs the executable if signing environment variables are configured

### Optional code signing hook

`build.ps1` will sign the packaged executable if these environment variables are available:

- `SIGNTOOL_PATH`
- `CODE_SIGN_PFX`
- `CODE_SIGN_PASSWORD`
- optional: `CODE_SIGN_TIMESTAMP_URL`

If they are not configured, the package is built unsigned.

## Development Notes

- The app is designed for the Windows PC version where Heartopia stores cache files in `AppData\LocalLow`.
- If the game updates and changes the cache encryption or folder layout, the compatibility probe should fail closed and block replacement until the tool is reviewed.
- The repository does not need generated release binaries committed in order to build successfully.
