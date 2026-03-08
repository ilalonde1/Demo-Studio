param()

$ErrorActionPreference = "SilentlyContinue"

# Stop active test hosts that can lock .artifacts test binaries.
Get-Process testhost, vstest.console | Stop-Process -Force

$dotnetTestHosts = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -ieq "dotnet.exe" -and
        ($_.CommandLine -like "*testhost.dll*" -or $_.CommandLine -like "*vstest*")
    }

foreach ($proc in $dotnetTestHosts)
{
    Stop-Process -Id $proc.ProcessId -Force
}

Write-Host "Stopped testhost/vstest processes (if any were running)."
