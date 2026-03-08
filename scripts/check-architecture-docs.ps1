param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

pwsh -NoLogo -NoProfile -File ./scripts/update-architecture-docs.ps1

if (!(Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Warning "git not found; skipping docs diff check. Docs were regenerated."
    exit 0
}

git update-index --refresh | Out-Null
git diff --exit-code -- docs/ | Out-Null

Write-Host "Architecture docs are in sync."
