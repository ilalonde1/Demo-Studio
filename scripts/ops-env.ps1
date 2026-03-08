param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("show", "set", "clear", "set-session")]
    [string]$Action,

    [ValidateSet("Machine", "User", "Process")]
    [string]$Scope = "Machine",

    [string]$EnvironmentName = "Production",

    [string]$ConnectionString = "",

    [switch]$UseDefaults
)

$envVarName = "ASPNETCORE_ENVIRONMENT"
$connVarName = "ConnectionStrings__DemoStudio"
$defaultConn = "Server=(localdb)\MSSQLLocalDB;Database=DemoStudio;Trusted_Connection=True;TrustServerCertificate=True;"

function Get-Val([string]$name, [string]$scopeName) {
    return [Environment]::GetEnvironmentVariable($name, $scopeName)
}

function Set-Val([string]$name, [string]$value, [string]$scopeName) {
    [Environment]::SetEnvironmentVariable($name, $value, $scopeName)
}

function Show-Values {
    Write-Host "=== Process ==="
    Write-Host "$envVarName = $(Get-Val $envVarName Process)"
    Write-Host "$connVarName = $(Get-Val $connVarName Process)"
    Write-Host ""
    Write-Host "=== User ==="
    Write-Host "$envVarName = $(Get-Val $envVarName User)"
    Write-Host "$connVarName = $(Get-Val $connVarName User)"
    Write-Host ""
    Write-Host "=== Machine ==="
    Write-Host "$envVarName = $(Get-Val $envVarName Machine)"
    Write-Host "$connVarName = $(Get-Val $connVarName Machine)"
}

switch ($Action) {
    "show" {
        Show-Values
        break
    }
    "set-session" {
        $connToSet = if ($UseDefaults) { $defaultConn } elseif ($ConnectionString) { $ConnectionString } else { "" }
        if ([string]::IsNullOrWhiteSpace($connToSet)) {
            throw "Provide -ConnectionString or use -UseDefaults."
        }
        $env:ASPNETCORE_ENVIRONMENT = $EnvironmentName
        $env:ConnectionStrings__DemoStudio = $connToSet
        Write-Host "Set process/session variables."
        Show-Values
        break
    }
    "set" {
        $connToSet = if ($UseDefaults) { $defaultConn } elseif ($ConnectionString) { $ConnectionString } else { "" }
        if ([string]::IsNullOrWhiteSpace($connToSet)) {
            throw "Provide -ConnectionString or use -UseDefaults."
        }
        Set-Val $envVarName $EnvironmentName $Scope
        Set-Val $connVarName $connToSet $Scope
        Write-Host "Set $Scope variables. Restart app/service to pick up changes."
        Show-Values
        break
    }
    "clear" {
        Set-Val $envVarName $null $Scope
        Set-Val $connVarName $null $Scope
        Write-Host "Cleared $Scope variables. Restart app/service to pick up changes."
        Show-Values
        break
    }
}
