param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$generatedDir = Join-Path $root "docs\_generated"
New-Item -ItemType Directory -Force -Path $generatedDir | Out-Null

$mainVmPath = Join-Path $root "src\DemoStudio.Desktop.App\ViewModels\MainWindowViewModel.cs"
if (!(Test-Path $mainVmPath)) {
    throw "MainWindowViewModel.cs not found at '$mainVmPath'."
}

$mainVmRaw = Get-Content $mainVmPath -Raw

$matches = [regex]::Matches($mainVmRaw, "private readonly ([A-Za-z0-9_]+ViewModel) _([A-Za-z0-9_]+);")
$childVms = @()
foreach ($m in $matches) {
    $childVms += [PSCustomObject]@{
        Type  = $m.Groups[1].Value
        Field = "_" + $m.Groups[2].Value
    }
}
$childVms = $childVms | Sort-Object Type -Unique

$boundaryLines = @()
$boundaryLines += "# Generated ViewModel Boundaries"
$boundaryLines += ""
$boundaryLines += "Generated UTC: $(Get-Date -AsUTC -Format 'yyyy-MM-dd HH:mm:ss')"
$boundaryLines += ""
$boundaryLines += '```mermaid'
$boundaryLines += "flowchart TD"
$boundaryLines += "    Shell[MainWindowViewModel]"
foreach ($vm in $childVms) {
    $safeNode = ($vm.Type -replace '[^A-Za-z0-9_]', '_')
    $boundaryLines += ("    Shell --> {0}[{1}]" -f $safeNode, $vm.Type)
}
$boundaryLines += '```'
$boundaryLines += ""
$boundaryLines += "| Child ViewModel | Field |"
$boundaryLines += "|---|---|"
foreach ($vm in $childVms) {
    $boundaryLines += "| $($vm.Type) | $($vm.Field) |"
}

$boundaryPath = Join-Path $generatedDir "viewmodel-boundaries.md"
Set-Content -Path $boundaryPath -Value ($boundaryLines -join [Environment]::NewLine)

$viewModelsDir = Join-Path $root "src\DemoStudio.Desktop.App\ViewModels"
$hotspot = Get-ChildItem $viewModelsDir -File | ForEach-Object {
    [PSCustomObject]@{
        Name  = $_.Name
        Lines = (Get-Content $_.FullName | Measure-Object -Line).Lines
    }
} | Sort-Object Lines -Descending

$hotspotLines = @()
$hotspotLines += "# Generated ViewModel Hotspots"
$hotspotLines += ""
$hotspotLines += "Generated UTC: $(Get-Date -AsUTC -Format 'yyyy-MM-dd HH:mm:ss')"
$hotspotLines += ""
$hotspotLines += "| File | Lines |"
$hotspotLines += "|---|---:|"
foreach ($row in $hotspot) {
    $hotspotLines += "| $($row.Name) | $($row.Lines) |"
}

$hotspotPath = Join-Path $generatedDir "viewmodel-hotspots.md"
Set-Content -Path $hotspotPath -Value ($hotspotLines -join [Environment]::NewLine)

Write-Host "Updated architecture docs:"
Write-Host " - $boundaryPath"
Write-Host " - $hotspotPath"
