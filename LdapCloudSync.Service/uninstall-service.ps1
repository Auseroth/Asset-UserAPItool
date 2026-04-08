#Requires -RunAsAdministrator
<#
    Uninstalls the LdapCloudSync Windows Service.
    Run from an elevated PowerShell prompt.
#>

$ServiceName = "LdapCloudSync"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Warning "Service '$ServiceName' is not installed."
    exit 0
}

Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Cyan
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Removing service '$ServiceName'..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Null

Write-Host "Service uninstalled successfully." -ForegroundColor Green
Write-Host "Note: Configuration data in $env:ProgramData\LdapCloudSync was NOT removed." -ForegroundColor Yellow