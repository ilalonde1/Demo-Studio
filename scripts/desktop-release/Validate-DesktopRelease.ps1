param(
    [string]$ReleaseRoot = '.artifacts/desktop-release',
    [string]$InstallRoot = '.artifacts/local-install'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

$releaseRootFullPath = Resolve-FullPath $ReleaseRoot
$installRootFullPath = Resolve-FullPath $InstallRoot
$scriptsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $scriptsRoot 'Build-DesktopRelease.ps1'
$installScript = Join-Path $scriptsRoot 'Install-DesktopRelease.ps1'
$rollbackScript = Join-Path $scriptsRoot 'Rollback-DesktopRelease.ps1'

if (Test-Path $installRootFullPath) {
    Remove-Item $installRootFullPath -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRootFullPath -Force | Out-Null
New-Item -ItemType Directory -Path $installRootFullPath -Force | Out-Null

$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$v1 = "soak-$stamp-1"
$v2 = "soak-$stamp-2"

$build1 = (& $buildScript -Version $v1 -OutputRoot $releaseRootFullPath | Select-Object -Last 1)
$build2 = (& $buildScript -Version $v2 -OutputRoot $releaseRootFullPath | Select-Object -Last 1)

$null = & $installScript -PackageZip $build1.ZipPath -InstallRoot $installRootFullPath
$null = & $installScript -PackageZip $build2.ZipPath -InstallRoot $installRootFullPath

$statePath = Join-Path $installRootFullPath 'install-state.json'
$state = Get-Content $statePath -Raw | ConvertFrom-Json
if ($state.currentVersion -ne $v2) {
    throw "Validation failed: expected current version '$v2' after second install, got '$($state.currentVersion)'."
}

$null = & $rollbackScript -InstallRoot $installRootFullPath
$stateAfterRollback = Get-Content $statePath -Raw | ConvertFrom-Json
if ($stateAfterRollback.currentVersion -ne $v1) {
    throw "Validation failed: expected current version '$v1' after rollback, got '$($stateAfterRollback.currentVersion)'."
}

$currentExe = Join-Path $installRootFullPath 'current/DemoStudio.Desktop.App.exe'
if (-not (Test-Path $currentExe)) {
    throw "Validation failed: current executable missing after rollback."
}

return [pscustomobject]@{
    Passed             = $true
    ReleaseRoot        = $releaseRootFullPath
    InstallRoot        = $installRootFullPath
    FirstVersion       = $v1
    SecondVersion      = $v2
    RolledBackTo       = $stateAfterRollback.currentVersion
    CurrentExe         = $currentExe
    StatePath          = $statePath
    HistoryPath        = (Join-Path $installRootFullPath 'install-history.jsonl')
}
