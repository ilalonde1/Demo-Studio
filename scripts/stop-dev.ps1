param()

$ErrorActionPreference = "SilentlyContinue"

# Stop only dotnet processes that are hosting DemoStudio.Desktop.App.
$demoDotnetProcesses = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -ieq "dotnet.exe" -and
        ($_.CommandLine -like "*DemoStudio.Desktop.App.dll*" -or $_.CommandLine -like "*DemoStudio.Desktop.App.csproj*")
    }

foreach ($proc in $demoDotnetProcesses)
{
    Stop-Process -Id $proc.ProcessId -Force
}

Write-Host "Stopped DemoStudio desktop app processes (if any were running)."
