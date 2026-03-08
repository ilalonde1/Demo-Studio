param(
    [string]$InstallRoot = "$env:LocalAppData/DemoStudio/RecorderDesktopApp"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Copy-Directory([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) {
        throw "Rollback source not found: $Source"
    }

    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Sync-Directory([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Source)) {
        throw "Sync source not found: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $sourceArg = $Source.TrimEnd('\')
    $destArg = $Destination.TrimEnd('\')
    & robocopy $sourceArg $destArg /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    if ($code -ge 8) {
        throw "robocopy sync failed with exit code $code"
    }
}

$installRootFullPath = Resolve-FullPath $InstallRoot
$versionsRoot = Join-Path $installRootFullPath 'versions'
$currentRoot = Join-Path $installRootFullPath 'current'
$statePath = Join-Path $installRootFullPath 'install-state.json'
$historyPath = Join-Path $installRootFullPath 'install-history.jsonl'

if (-not (Test-Path $statePath)) {
    throw "Install state not found: $statePath"
}

$state = Get-Content $statePath -Raw | ConvertFrom-Json
$currentVersion = [string]$state.currentVersion
$previousVersion = [string]$state.previousVersion

if ([string]::IsNullOrWhiteSpace($previousVersion)) {
    throw "No previous version recorded; rollback is not available."
}

$rollbackSource = Join-Path $versionsRoot $previousVersion
Sync-Directory -Source $rollbackSource -Destination $currentRoot

$newState = [ordered]@{
    currentVersion  = $previousVersion
    previousVersion = $currentVersion
    updatedUtc      = (Get-Date).ToUniversalTime().ToString('o')
}
$newState | ConvertTo-Json -Depth 4 | Set-Content -Path $statePath -Encoding UTF8

$historyEntry = [ordered]@{
    action          = 'rollback'
    timestampUtc    = (Get-Date).ToUniversalTime().ToString('o')
    rolledBackTo    = $previousVersion
    previousCurrent = $currentVersion
}
Add-Content -Path $historyPath -Value (($historyEntry | ConvertTo-Json -Compress)) -Encoding UTF8

return [pscustomobject]@{
    InstallRoot     = $installRootFullPath
    CurrentVersion  = $newState.currentVersion
    PreviousVersion = $newState.previousVersion
    CurrentExe      = (Join-Path $currentRoot 'DemoStudio.Desktop.App.exe')
}
