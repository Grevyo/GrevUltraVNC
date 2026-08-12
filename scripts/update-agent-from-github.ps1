#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Repository = 'Grevyo/GrevUltraVNC'
)

$ErrorActionPreference = 'Stop'

$assetName = 'GrevUltraVNC-Agent-win-x64.zip'
$releaseUrl = "https://github.com/$Repository/releases/download/agent-latest/$assetName"
$workRoot = Join-Path $env:TEMP 'GrevUltraVNC-Agent-Update'
$zipPath = Join-Path $workRoot $assetName
$packageDir = Join-Path $workRoot 'Package'

Write-Host ''
Write-Host '=== GrevUltraVNC Agent Update ===' -ForegroundColor Cyan
Write-Host ''

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

try {
    Write-Host 'Downloading latest Agent package from GitHub...'
    Invoke-WebRequest -Uri $releaseUrl -OutFile $zipPath -UseBasicParsing

    if (-not (Test-Path $zipPath)) {
        throw "GitHub download did not create $zipPath"
    }

    Write-Host 'Extracting package...'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $packageDir -Force

    $installer = Join-Path $packageDir 'Install-GrevAgent.ps1'
    if (-not (Test-Path $installer)) {
        throw "Install-GrevAgent.ps1 was not found in the downloaded package."
    }

    Write-Host 'Installing latest Agent...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
    if ($LASTEXITCODE -ne 0) {
        throw "Agent installer exited with code $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host 'GrevUltraVNC Agent update completed.' -ForegroundColor Green
}
finally {
    if (Test-Path $workRoot) {
        Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
