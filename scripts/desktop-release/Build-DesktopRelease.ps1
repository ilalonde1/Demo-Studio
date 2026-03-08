param(
    [string]$Version = (Get-Date -Format 'yyyy.MM.dd.HHmm'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '.artifacts/desktop-release',
    [string]$ProjectPath = 'src/DemoStudio.Desktop.App/DemoStudio.Desktop.App.csproj',
    [switch]$SelfContained = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

$projectFullPath = Resolve-FullPath $ProjectPath
if (-not (Test-Path $projectFullPath)) {
    throw "Project not found: $projectFullPath"
}

$outputRootFullPath = Resolve-FullPath $OutputRoot
$releaseRoot = Join-Path $outputRootFullPath "v$Version"
$publishPath = Join-Path $releaseRoot 'publish'
$packagePath = Join-Path $releaseRoot 'package'
$payloadPath = Join-Path $packagePath 'payload'
$zipPath = Join-Path $outputRootFullPath "DemoStudio.Desktop.$Version.zip"

if (Test-Path $releaseRoot) { Remove-Item $releaseRoot -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
New-Item -ItemType Directory -Force -Path $payloadPath | Out-Null

$sc = if ($SelfContained) { 'true' } else { 'false' }
$publishArgs = @(
    'publish', $projectFullPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $sc,
    '-o', $publishPath,
    '-nologo',
    '-v', 'minimal',
    '/p:PublishSingleFile=false',
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

Write-Host "[release] dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishPath 'DemoStudio.Desktop.App.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected executable not found: $exePath"
}

Copy-Item -Path (Join-Path $publishPath '*') -Destination $payloadPath -Recurse -Force

$manifest = [ordered]@{
    appId         = 'DemoStudio.Desktop'
    version       = $Version
    builtUtc      = (Get-Date).ToUniversalTime().ToString('o')
    runtime       = $Runtime
    configuration = $Configuration
    selfContained = [bool]$SelfContained
    entryExe      = 'DemoStudio.Desktop.App.exe'
    payload       = [ordered]@{
        fileCount = (Get-ChildItem $payloadPath -Recurse -File | Measure-Object).Count
        exeSha256 = (Get-FileHash -Path (Join-Path $payloadPath 'DemoStudio.Desktop.App.exe') -Algorithm SHA256).Hash
    }
}

$manifestPath = Join-Path $packagePath 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

Compress-Archive -Path (Join-Path $packagePath '*') -DestinationPath $zipPath -CompressionLevel Optimal

$result = [pscustomobject]@{
    Version      = $Version
    ZipPath      = $zipPath
    ManifestPath = $manifestPath
    PayloadPath  = $payloadPath
}

return $result
