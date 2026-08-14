param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'friend-app'
$downloadDir = Join-Path $dist 'friend-downloads'
$uvncExtract = Join-Path $downloadDir 'UltraVNC-1824'
$uvncTarget = Join-Path $publish 'UltraVNC'
$licenses = Join-Path $publish 'Licenses'

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $downloadDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $downloadDir, $licenses -Force | Out-Null

Write-Host 'Publishing GrevUltraVNC 1.1.4 self-contained win-x64...'
dotnet publish (Join-Path $root 'src\GrevUltraVNC\GrevUltraVNC.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publish `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Controller publish failed.' }

$uvncUrl = 'https://uvnc.eu/download/1800/UltraVNC_1824.zip'
$uvncZip = Join-Path $downloadDir 'UltraVNC_1824.zip'
$expectedUvncSha256 = '8AF948089626008F02EDD1254AFC15C814E454EC5FC9E3EAA860356F19D4F113'

Write-Host 'Downloading official UltraVNC 1.8.2.4 portable binaries...'
Invoke-WebRequest -Uri $uvncUrl -OutFile $uvncZip -UseBasicParsing
$actualUvncSha256 = (Get-FileHash -Path $uvncZip -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actualUvncSha256 -ne $expectedUvncSha256) {
    throw "UltraVNC ZIP hash mismatch. Expected $expectedUvncSha256 but got $actualUvncSha256. Refusing to package it."
}

Expand-Archive -Path $uvncZip -DestinationPath $uvncExtract -Force
$viewerCandidates = @(Get-ChildItem -Path $uvncExtract -Filter 'vncviewer.exe' -File -Recurse)
if ($viewerCandidates.Count -eq 0) {
    throw 'UltraVNC archive did not contain vncviewer.exe.'
}

$viewer = $viewerCandidates |
    Where-Object { $_.FullName -match '(?i)[\\/]x64[\\/]' } |
    Sort-Object { $_.FullName.Length } |
    Select-Object -First 1
if (-not $viewer) {
    $viewer = $viewerCandidates | Sort-Object Length -Descending | Select-Object -First 1
}

New-Item -ItemType Directory -Path $uvncTarget -Force | Out-Null
$bundledViewer = Join-Path $uvncTarget 'vncviewer.exe'
Copy-Item -Path $viewer.FullName -Destination $bundledViewer -Force
if (-not (Test-Path $bundledViewer)) {
    throw 'Bundled UltraVNC viewer was not copied to the expected location.'
}

Write-Host 'Smoke testing the bundled UltraVNC viewer...'
$viewerProcess = Start-Process -FilePath $bundledViewer -PassThru
Start-Sleep -Seconds 3
if ($viewerProcess.HasExited) {
    throw "Bundled UltraVNC viewer exited during startup smoke test with exit code $($viewerProcess.ExitCode)."
}
Stop-Process -Id $viewerProcess.Id -Force
Remove-Item (Join-Path $uvncTarget 'options.vnc') -Force -ErrorAction SilentlyContinue

$uvncLicenseUrl = 'https://raw.githubusercontent.com/ultravnc/UltraVNC/b1f54118e74124407705b342f4cc2269c74e8ca0/LICENSE'
Invoke-WebRequest -Uri $uvncLicenseUrl -OutFile (Join-Path $licenses 'UltraVNC-LICENSE.txt') -UseBasicParsing
Copy-Item (Join-Path $root 'installer\THIRD-PARTY-NOTICES.txt') (Join-Path $publish 'THIRD-PARTY-NOTICES.txt') -Force

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it before building the friend installer.'
}

$iss = Join-Path $root 'installer\GrevUltraVNC-Friend.iss'
Write-Host 'Building single EXE installer...'
& $iscc "/DSourceDir=$publish" "/DOutputDir=$dist" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installer = Join-Path $dist 'GrevUltraVNC-1.1.4-Friend-Setup.exe'
if (-not (Test-Path $installer)) {
    throw 'Expected installer EXE was not created.'
}

$installerHash = (Get-FileHash -Path $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Friend installer: $installer"
Write-Host "SHA256: $installerHash"
