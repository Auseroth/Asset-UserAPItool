#Requires -RunAsAdministrator
<#
    Installs the LdapCloudSync Windows Service.
    Run from an elevated PowerShell prompt.
#>

$ServiceName = "LdapCloudSync"
$DisplayName = "LDAP Cloud Sync Service"
$Description = "Synchronizes Active Directory objects to cloud services (Reftab, Snipe-IT, etc.)"
$ExePath = Join-Path $PSScriptRoot "LdapCloudSync.Service.exe"

if (-not (Test-Path $ExePath)) {
    Write-Error "Service executable not found at: $ExePath"
    Write-Error "Build the project first: dotnet publish -c Release"
    exit 1
}

# Check if service already exists
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Warning "Service '$ServiceName' already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Installing service '$ServiceName'..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName $ExePath `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType Automatic

# Create ProgramData directory
$dataDir = Join-Path $env:ProgramData "LdapCloudSync"
if (-not (Test-Path $dataDir)) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    Write-Host "Created data directory: $dataDir" -ForegroundColor Green
}

Write-Host "Service installed successfully." -ForegroundColor Green
Write-Host "Start with: Start-Service $ServiceName" -ForegroundColor Yellow