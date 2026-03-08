param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot/stop-dev.ps1"
& "$PSScriptRoot/stop-testhost.ps1"

Push-Location $repoRoot
try {
    Write-Host "Running build..."
    dotnet build DemoStudio.Desktop.sln -c Release -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }

    Write-Host "Running tests..."
    dotnet test tests/DemoStudio.Desktop.Core.Tests/DemoStudio.Desktop.Core.Tests.csproj -c Release --no-build -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop core tests failed with exit code $LASTEXITCODE."
    }

    dotnet test tests/DemoStudio.Desktop.App.Tests/DemoStudio.Desktop.App.Tests.csproj -c Release --no-build -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }

    Write-Host "Fast check complete."

}
finally {
    Pop-Location
}
