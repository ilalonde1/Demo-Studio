# Desktop Release Pipeline

This folder provides a production-oriented package/install/rollback workflow for `DemoStudio.Desktop.App`.

## Scripts

- `Build-DesktopRelease.ps1`
  - Publishes the desktop app and creates a versioned zip package with:
    - `release-manifest.json`
    - `payload/` (published files)
- `Install-DesktopRelease.ps1`
  - Installs/updates to `InstallRoot` using versioned folders:
    - `versions/<version>/`
    - `current/` active deployment
    - `install-state.json` (`currentVersion`, `previousVersion`)
    - `install-history.jsonl` audit log
- `Rollback-DesktopRelease.ps1`
  - Rolls back active deployment to `previousVersion`.
- `Validate-DesktopRelease.ps1`
  - End-to-end validation:
    - build v1
    - build v2
    - install v1
    - install v2
    - rollback to v1
    - verify current executable exists

## Quick Commands

```powershell
pwsh ./scripts/desktop-release/Build-DesktopRelease.ps1 -Version 2026.03.08.1
pwsh ./scripts/desktop-release/Install-DesktopRelease.ps1 -PackageZip ./.artifacts/desktop-release/DemoStudio.Desktop.2026.03.08.1.zip
pwsh ./scripts/desktop-release/Rollback-DesktopRelease.ps1
pwsh ./scripts/desktop-release/Validate-DesktopRelease.ps1
```

## Notes

- Default install root is `%LOCALAPPDATA%/DemoStudio/RecorderDesktopApp`.
- Installer is non-destructive to version history; it swaps `current/` to the selected version.
- Rollback requires at least one prior install in `install-state.json`.
