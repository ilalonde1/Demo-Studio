param(
    [string]$Configuration = "Release",
    [int]$SmokeSeconds = 3
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

Invoke-Step "Stop lingering test hosts" {
    pwsh -NoLogo -NoProfile -File ./scripts/stop-testhost.ps1
}

Invoke-Step "Build Desktop Solution" {
    dotnet build DemoStudio.Desktop.sln -c $Configuration -nologo -v minimal -m:1
}

Invoke-Step "Run Release Confidence Tests" {
    dotnet test tests/DemoStudio.Desktop.App.Tests/DemoStudio.Desktop.App.Tests.csproj `
        -c $Configuration `
        --no-build `
        -nologo `
        -v minimal `
        --filter "Gate=ReleaseConfidence" `
        -m:1
}

Invoke-Step "Run Desktop Smoke SLA" {
    $smokeRoot = Join-Path $root "artifacts\release-confidence\smoke"
    New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
    $metricsPath = Join-Path $smokeRoot "metrics.json"

    dotnet run --project src/DemoStudio.Desktop.Smoke/DemoStudio.Desktop.Smoke.csproj -c $Configuration -- --seconds $SmokeSeconds --output $smokeRoot --metrics $metricsPath

    if (!(Test-Path $metricsPath)) {
        throw "Release confidence gate failed: smoke metrics file missing at '$metricsPath'."
    }
}

Write-Host "Release confidence gate passed."
