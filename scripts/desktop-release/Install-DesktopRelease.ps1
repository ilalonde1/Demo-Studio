param(
    [Parameter(Mandatory = $true)]
    [string]$PackageZip,
    [string]$InstallRoot = "$env:LocalAppData/DemoStudio/RecorderDesktopApp",
    [switch]$Force
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

$zipPath = Resolve-FullPath $PackageZip
if (-not (Test-Path $zipPath)) {
    throw "Package zip not found: $zipPath"
}

$installRootFullPath = Resolve-FullPath $InstallRoot
$versionsRoot = Join-Path $installRootFullPath 'versions'
$currentRoot = Join-Path $installRootFullPath 'current'
$statePath = Join-Path $installRootFullPath 'install-state.json'
$historyPath = Join-Path $installRootFullPath 'install-history.jsonl'

New-Item -ItemType Directory -Path $installRootFullPath -Force | Out-Null
New-Item -ItemType Directory -Path $versionsRoot -Force | Out-Null

$extractRoot = Join-Path $env:TEMP ("demostudio-release-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

try {
    Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force

    $manifestPath = Join-Path $extractRoot 'release-manifest.json'
    $payloadPath = Join-Path $extractRoot 'payload'
    if (-not (Test-Path $manifestPath)) {
        throw "Package missing release-manifest.json."
    }
    if (-not (Test-Path $payloadPath)) {
        throw "Package missing payload folder."
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $version = [string]$manifest.version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Manifest version is missing."
    }

    $entryExe = Join-Path $payloadPath ([string]$manifest.entryExe)
    if (-not (Test-Path $entryExe)) {
        throw "Manifest entry executable not found in payload: $entryExe"
    }

    $targetVersionRoot = Join-Path $versionsRoot $version
    if ((Test-Path $targetVersionRoot) -and -not $Force) {
        Write-Host "[install] Version already exists: $version (use -Force to reinstall)."
    }
    else {
        Copy-Directory -Source $payloadPath -Destination $targetVersionRoot
    }

    $state = if (Test-Path $statePath) {
        Get-Content $statePath -Raw | ConvertFrom-Json
    }
    else {
        [pscustomobject]@{
            currentVersion  = ''
            previousVersion = ''
            updatedUtc      = ''
        }
    }

    $oldCurrentVersion = [string]$state.currentVersion
    $newState = [ordered]@{
        currentVersion  = $version
        previousVersion = $oldCurrentVersion
        updatedUtc      = (Get-Date).ToUniversalTime().ToString('o')
    }

    Sync-Directory -Source $targetVersionRoot -Destination $currentRoot
    $newState | ConvertTo-Json -Depth 4 | Set-Content -Path $statePath -Encoding UTF8

    $launcherPath = Join-Path $installRootFullPath 'Launch-DemoStudio.cmd'
    @"
@echo off
setlocal
set APP=%~dp0current\DemoStudio.Desktop.App.exe
if not exist "%APP%" (
  echo DemoStudio executable not found at "%APP%"
  exit /b 1
)
start "" "%APP%"
exit /b 0
"@ | Set-Content -Path $launcherPath -Encoding ASCII

    $historyEntry = [ordered]@{
        action         = 'install'
        timestampUtc   = (Get-Date).ToUniversalTime().ToString('o')
        packageZipPath = $zipPath
        version        = $version
        currentVersion = $newState.currentVersion
        previousVersion = $newState.previousVersion
    }
    Add-Content -Path $historyPath -Value (($historyEntry | ConvertTo-Json -Compress)) -Encoding UTF8

    return [pscustomobject]@{
        InstallRoot      = $installRootFullPath
        CurrentVersion   = $newState.currentVersion
        PreviousVersion  = $newState.previousVersion
        CurrentExe       = (Join-Path $currentRoot 'DemoStudio.Desktop.App.exe')
        LauncherPath     = $launcherPath
        StatePath        = $statePath
        HistoryPath      = $historyPath
    }
}
finally {
    if (Test-Path $extractRoot) {
        Remove-Item $extractRoot -Recurse -Force
    }
}
