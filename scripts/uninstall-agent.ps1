#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [switch]$PurgeData
)

$ErrorActionPreference = 'Stop'

$serviceName = 'GrevUltraVNCAgent'
$firewallName = 'GrevUltraVNC Agent - LAN'
$installDir = Join-Path $env:ProgramFiles 'GrevUltraVNC Agent'
$dataDir = Join-Path $env:ProgramData 'GrevUltraVNC\Agent'

Write-Host ''
Write-Host '=== Remove GrevUltraVNC Agent ===' -ForegroundColor Cyan
Write-Host ''

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Write-Host 'Stopping service...'
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    Write-Host 'Removing service...'
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host 'Removing firewall rule...'
Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

if (Test-Path $installDir) {
    Write-Host 'Removing installed files...'
    Remove-Item $installDir -Recurse -Force
}

if ($PurgeData -and (Test-Path $dataDir)) {
    Write-Host 'Removing pairing/configuration data...'
    Remove-Item $dataDir -Recurse -Force
}

Write-Host ''
Write-Host 'GrevUltraVNC Agent removed.' -ForegroundColor Green
if (-not $PurgeData) {
    Write-Host 'Pairing/configuration data was preserved. Use -PurgeData to remove it too.'
}
Write-Host ''
