param(
    [string]$Configuration = "Release",
    [int]$SmokeSeconds = 3,
    [double]$MaxSmokeStartMs = 8000,
    [double]$MaxSmokeStopMs = 8000,
    [double]$MaxSmokeTotalMs = 20000,
    [double]$MinSmokeFileMb = 0.05
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $start = Get-Date
    Write-Host "==> $Name"
    & $Action
    $elapsed = (Get-Date) - $start
    Write-Host "<== $Name completed in $([math]::Round($elapsed.TotalSeconds, 2))s"
}

Invoke-Step "Build Desktop Solution" {
    dotnet build DemoStudio.Desktop.sln -c $Configuration -nologo -v minimal -m:1
}

Invoke-Step "Test Desktop Core" {
    dotnet test tests/DemoStudio.Desktop.Core.Tests/DemoStudio.Desktop.Core.Tests.csproj -c $Configuration -nologo -v minimal -m:1
}

Invoke-Step "Test Desktop App" {
    dotnet test tests/DemoStudio.Desktop.App.Tests/DemoStudio.Desktop.App.Tests.csproj -c $Configuration -nologo -v minimal -m:1
}

Invoke-Step "Smoke SLA" {
    $smokeRoot = Join-Path $root "artifacts\smoke-gate"
    New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
    $metricsPath = Join-Path $smokeRoot "metrics.json"

    dotnet run --project src/DemoStudio.Desktop.Smoke/DemoStudio.Desktop.Smoke.csproj -c $Configuration -- --seconds $SmokeSeconds --output $smokeRoot --metrics $metricsPath

    if (!(Test-Path $metricsPath)) {
        throw "Smoke SLA failed: metrics file was not produced at '$metricsPath'."
    }

    $metrics = Get-Content $metricsPath -Raw | ConvertFrom-Json
    $fileMb = [math]::Round(($metrics.FileSizeBytes / 1MB), 3)
    Write-Host ("Smoke metrics: start={0}ms stop={1}ms total={2}ms size={3}MB" -f `
        [math]::Round($metrics.StartHandshakeMs, 2), `
        [math]::Round($metrics.StopMs, 2), `
        [math]::Round($metrics.TotalMs, 2), `
        $fileMb)

    if ($metrics.StartHandshakeMs -gt $MaxSmokeStartMs) {
        throw "Smoke SLA failed: start handshake ${($metrics.StartHandshakeMs)}ms exceeded ${MaxSmokeStartMs}ms."
    }

    if ($metrics.StopMs -gt $MaxSmokeStopMs) {
        throw "Smoke SLA failed: stop ${($metrics.StopMs)}ms exceeded ${MaxSmokeStopMs}ms."
    }

    if ($metrics.TotalMs -gt $MaxSmokeTotalMs) {
        throw "Smoke SLA failed: total ${($metrics.TotalMs)}ms exceeded ${MaxSmokeTotalMs}ms."
    }

    if ($fileMb -lt $MinSmokeFileMb) {
        throw "Smoke SLA failed: output size ${fileMb}MB below minimum ${MinSmokeFileMb}MB."
    }
}

Write-Host "Desktop quality gate passed."
