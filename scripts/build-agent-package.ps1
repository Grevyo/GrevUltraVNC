[CmdletBinding()]
param(
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\GrevUltraVNC.Agent\GrevUltraVNC.Agent.csproj'
$distRoot = Join-Path $repoRoot 'dist'
$packageDir = Join-Path $distRoot ('GrevUltraVNC-Agent-' + $Runtime)
$zipPath = Join-Path $distRoot ('GrevUltraVNC-Agent-' + $Runtime + '.zip')
$packageInstaller = Join-Path $PSScriptRoot 'install-agent-package.ps1'
$uninstaller = Join-Path $PSScriptRoot 'uninstall-agent.ps1'

Write-Host ''
Write-Host '=== Build GrevUltraVNC Agent Package ===' -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path $project)) {
    throw "Agent project not found: $project"
}

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

Write-Host ('Publishing self-contained agent for {0}...' -f $Runtime)
dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $packageDir

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

$agentExe = Join-Path $packageDir 'GrevUltraVNC.Agent.exe'
if (-not (Test-Path $agentExe)) {
    throw "Published agent executable not found: $agentExe"
}

Copy-Item $packageInstaller (Join-Path $packageDir 'Install-GrevAgent.ps1') -Force
Copy-Item $uninstaller (Join-Path $packageDir 'Uninstall-GrevAgent.ps1') -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host 'Agent package built successfully.' -ForegroundColor Green
Write-Host $zipPath -ForegroundColor White
Write-Host ''
Write-Host 'Copy that ZIP to the target PC, extract it, then run Install-GrevAgent.ps1 as Administrator.'
Write-Host ''
