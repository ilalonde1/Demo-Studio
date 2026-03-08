param(
    [int]$Seconds = 8,
    [string]$Output = "",
    [string]$Ffmpeg = "ffmpeg"
)

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\DemoStudio.Desktop.Smoke\DemoStudio.Desktop.Smoke.csproj"

if (-not (Test-Path $project)) {
    Write-Error "Smoke project not found: $project"
    exit 2
}

$args = @("run", "--project", $project, "--", "--seconds", "$Seconds", "--ffmpeg", "$Ffmpeg")
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $args += @("--output", $Output)
}

dotnet @args
exit $LASTEXITCODE
