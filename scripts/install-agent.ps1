#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$serviceName = 'GrevUltraVNCAgent'
$displayName = 'GrevUltraVNC Agent'
$firewallName = 'GrevUltraVNC Agent - LAN'
$agentPort = 47820
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\GrevUltraVNC.Agent\GrevUltraVNC.Agent.csproj'
$installDir = Join-Path $env:ProgramFiles 'GrevUltraVNC Agent'
$exePath = Join-Path $installDir 'GrevUltraVNC.Agent.exe'
$dataDir = Join-Path $env:ProgramData 'GrevUltraVNC\Agent'
$configPath = Join-Path $dataDir 'agent.json'

Write-Host ''
Write-Host '=== GrevUltraVNC Agent Setup ===' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $project)) {
    throw "Agent project not found: $project"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Stopping existing Grev agent service...'
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

Write-Host 'Publishing self-contained Grev agent...'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $installDir

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

if (-not (Test-Path $exePath)) {
    throw "Published agent executable not found: $exePath"
}

Write-Host 'Securing agent configuration folder...'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
& icacls.exe $dataDir /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null

Write-Host 'Installing Windows service...'
New-Service `
    -Name $serviceName `
    -BinaryPathName ('"{0}"' -f $exePath).Replace('\"','"') `
    -DisplayName $displayName `
    -Description 'Authenticated LAN telemetry and management agent for GrevUltraVNC.' `
    -StartupType Automatic | Out-Null

Write-Host 'Configuring LAN-only firewall rule...'
Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule `
    -DisplayName $firewallName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort $agentPort `
    -RemoteAddress LocalSubnet `
    -Action Allow | Out-Null

Write-Host 'Starting Grev agent...'
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))

$deadline = (Get-Date).AddSeconds(20)
while (-not (Test-Path $configPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
}

if (-not (Test-Path $configPath)) {
    throw "The service started but did not create $configPath"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json

Write-Host ''
Write-Host 'GrevUltraVNC Agent is installed and running.' -ForegroundColor Green
Write-Host ('Machine:      {0}' -f $env:COMPUTERNAME)
Write-Host ('Agent port:   {0}' -f $config.Port)
Write-Host ('VNC port:     {0}' -f $config.UltraVncPort)
Write-Host ''
Write-Host 'PAIRING KEY - paste this into Edit Machine > Grev Agent in GrevUltraVNC:' -ForegroundColor Yellow
Write-Host $config.SharedKey -ForegroundColor White
Write-Host ''
Write-Host 'The pairing key is stored in a SYSTEM/Administrators-only folder under ProgramData.'
Write-Host ''
