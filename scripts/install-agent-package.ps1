#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$serviceName = 'GrevUltraVNCAgent'
$displayName = 'GrevUltraVNC Agent'
$firewallName = 'GrevUltraVNC Agent - LAN'
$sourceExe = Join-Path $PSScriptRoot 'GrevUltraVNC.Agent.exe'
$installDir = Join-Path $env:ProgramFiles 'GrevUltraVNC Agent'
$exePath = Join-Path $installDir 'GrevUltraVNC.Agent.exe'
$dataDir = Join-Path $env:ProgramData 'GrevUltraVNC\Agent'
$configPath = Join-Path $dataDir 'agent.json'

function Wait-ProcessExit {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds = 20
    )

    if ($ProcessId -le 0) { return }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host "Old Agent process $ProcessId is still running; forcing it to close..." -ForegroundColor Yellow
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue

        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
                return
            }
            Start-Sleep -Milliseconds 250
        }

        throw "The old Grev Agent process $ProcessId could not be stopped."
    }
}

function Stop-OrphanAgentProcesses {
    param([string]$InstalledExePath)

    $expectedPath = [System.IO.Path]::GetFullPath($InstalledExePath)
    $candidates = Get-CimInstance Win32_Process -Filter "Name='GrevUltraVNC.Agent.exe'" -ErrorAction SilentlyContinue

    foreach ($candidate in $candidates) {
        $candidatePath = $candidate.ExecutablePath
        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            try {
                if (-not [string]::Equals(
                        [System.IO.Path]::GetFullPath($candidatePath),
                        $expectedPath,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }
            }
            catch {
                continue
            }
        }

        $processId = [int]$candidate.ProcessId
        if ($processId -le 0) { continue }

        Write-Host "Found lingering Grev Agent process $processId; closing it before install..." -ForegroundColor Yellow
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        Wait-ProcessExit -ProcessId $processId -TimeoutSeconds 10
    }
}

function Wait-FileUnlocked {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 30
    )

    if (-not (Test-Path $Path)) { return }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $stream = $null
        try {
            $stream = [System.IO.File]::Open(
                $Path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            return
        }
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 400
        }
        finally {
            if ($stream) { $stream.Dispose() }
        }
    }

    throw "Timed out waiting for the old Agent executable to be released: $Path"
}

function Copy-WithRetry {
    param(
        [string]$Source,
        [string]$Destination,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
            return
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 500
        }
    }

    if ($lastError) { throw $lastError }
    throw "Timed out copying the Grev Agent executable to $Destination"
}

Write-Host ''
Write-Host '=== GrevUltraVNC Agent Setup ===' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $sourceExe)) {
    throw "GrevUltraVNC.Agent.exe was not found beside this installer: $sourceExe"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    $oldProcessId = if ($serviceInfo) { [int]$serviceInfo.ProcessId } else { 0 }

    Write-Host 'Stopping existing Grev agent service...'
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }

    # A service can report Stopped slightly before its executable/image handle is fully released.
    Wait-ProcessExit -ProcessId $oldProcessId -TimeoutSeconds 20
    Stop-OrphanAgentProcesses -InstalledExePath $exePath
    Wait-FileUnlocked -Path $exePath -TimeoutSeconds 30

    try { $existing.Dispose() } catch { }

    & sc.exe delete $serviceName | Out-Null

    $deleteDeadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deleteDeadline) {
        if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 300
    }

    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw 'The old Grev Agent service is still marked for deletion. Wait a few seconds and try the update again.'
    }
}

Write-Host 'Installing agent files...'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
# Also recover an orphan left behind by a previous failed update where the service was already deleted.
Stop-OrphanAgentProcesses -InstalledExePath $exePath
Wait-FileUnlocked -Path $exePath -TimeoutSeconds 30
Copy-WithRetry -Source $sourceExe -Destination $exePath -TimeoutSeconds 30

Write-Host 'Securing agent configuration folder...'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
& icacls.exe $dataDir /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' | Out-Null

$binaryPath = [char]34 + $exePath + [char]34

Write-Host 'Installing Windows service...'
New-Service `
    -Name $serviceName `
    -BinaryPathName $binaryPath `
    -DisplayName $displayName `
    -Description 'Authenticated LAN telemetry and management agent for GrevUltraVNC.' `
    -StartupType Automatic | Out-Null

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

Write-Host 'Configuring LAN-only firewall rule...'
Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule `
    -DisplayName $firewallName `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort ([int]$config.Port) `
    -RemoteAddress LocalSubnet `
    -Action Allow | Out-Null

Write-Host ''
Write-Host 'GrevUltraVNC Agent is installed and running.' -ForegroundColor Green
Write-Host ('Machine:      {0}' -f $env:COMPUTERNAME)
Write-Host ('Agent port:   {0}' -f $config.Port)
Write-Host ('VNC port:     {0}' -f $config.UltraVncPort)
Write-Host ''
Write-Host 'PAIRING KEY - paste this into Edit Machine > Grev Agent in GrevUltraVNC:' -ForegroundColor Yellow
Write-Host $config.SharedKey -ForegroundColor White
Write-Host ''
