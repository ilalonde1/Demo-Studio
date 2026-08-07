# Stability Baseline

## Baseline Lock
- Baseline tag: `known-good-window-capture-2026-03-09`
- Baseline commit: `bf1b4ba`
- Purpose: known-good reference for Window capture stability and smoke validation.

## Required Gates (Must Pass)
Run from repo root:

```powershell
dotnet build DemoStudio.Desktop.sln -c Release -nologo
dotnet test tests/DemoStudio.Desktop.App.Tests/DemoStudio.Desktop.App.Tests.csproj -c Release -nologo
dotnet test tests/DemoStudio.Desktop.Core.Tests/DemoStudio.Desktop.Core.Tests.csproj -c Release -nologo
dotnet run --no-build --project src/DemoStudio.Desktop.Smoke/DemoStudio.Desktop.Smoke.csproj -c Release -- --mode Window --window-process msedge --prefer-exact-handle false --fallback-to-desktop false --iterations 3 --seconds 3 --startup-retries 0 --ffmpeg-leak-grace-seconds 3 --output .artifacts/smoke
```

## Manual Sanity Gate (Must Pass)
1. Launch `DemoStudio.Desktop.App` in `Release`.
2. Select `Window` mode target and click `Focus`.
3. Record for 30 seconds, then stop.
4. Verify:
- `Clip Count > 0`
- `Capture Size > 0`
- Session status is `Completed`

## Merge Rule
- No commit is considered release-ready unless all required gates and manual sanity gate pass in the same cycle.
- If any gate fails, do not merge. Fix or revert the failing change.

## Rollback
Return to baseline at any time:

```powershell
git checkout known-good-window-capture-2026-03-09
```

