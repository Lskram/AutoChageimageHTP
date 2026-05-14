# Changelog

## v1.5.0 - Release Hardening

- Added a first-run usage notice tied to the current app version.
- Added `Safety & Support` actions for backup policy, manual cleanup, and support bundle export.
- Added automatic backup retention cleanup after replace and restore flows.
- Added self-contained `win-x64` release packaging with versioned folder and zip output.
- Added support bundle export with diagnostics, config, logs, and packaged docs.
- Added automated regression coverage for probe, replace, restore, and cleanup behavior.

## v1.4.0 - Health and Backup History

- Added a compact health summary for cache state, probe state, backup count, and disk space.
- Added history-based restore selection for older backup snapshots.

## v1.3.0 - Recovery and Preflight Guard

- Added `Restore Latest`.
- Added probe-required replacement flow.
- Added disk-space checks, file-lock checks, and output verification.

## v1.2.0 - Compatibility Guard Update

- Added the compatibility probe.
- Added fail-closed validation for unsupported cache format changes.

## v1.1.0 - Safe Replace Baseline

- Added verified cache-folder selection.
- Added backup-first replacement, temp-file writes, and rollback.
