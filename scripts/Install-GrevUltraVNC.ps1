<#
.SYNOPSIS
    Installs GrevUltraVNC on a fresh Windows PC and starts it.

.DESCRIPTION
    Takes a machine with nothing on it and leaves you with a working GrevUltraVNC
    controller. It will, as needed:

      * check the machine can run it at all (Windows 10+ x64, TLS 1.2)
      * fetch the source for the chosen branch (no git required)
      * install the .NET SDK if this PC does not already have a suitable one
      * publish GrevUltraVNC self-contained, so the app keeps working even if
        the SDK is later removed
      * download the pinned UltraVNC Viewer build, verify its SHA-256, and
        bundle it beside the app so remote desktop works out of the box
      * install to a per-user folder (no administrator rights needed)
      * create Start Menu and Desktop shortcuts
      * optionally install the Grev Agent service on this same PC, so the
        machine can also be connected *to* (this part does need administrator)
      * launch the app

    Re-running it upgrades an existing install in place. Machines, settings,
    saved commands and activity history live in %APPDATA%\GrevUltraVNC and are
    never touched. VNC passwords and Agent pairing keys live in Windows
    Credential Manager and are likewise left alone.

.PARAMETER Source
    Build   - fetch the source and compile it here (default; most predictable).
    Release - download and silently run the newest published GrevUltraVNC
              installer from GitHub Releases instead. Much faster, but depends
              on a release being published.

.PARAMETER InstallDir
    Where to install. Defaults to %LOCALAPPDATA%\Programs\GrevUltraVNC.
    Pass a path under Program Files to make it machine-wide (needs admin).

.PARAMETER Branch
    Branch to build from. Defaults to main.

.PARAMETER SkipViewer
    Do not download or bundle the UltraVNC Viewer. Use this if UltraVNC is
    already installed on the PC; GrevUltraVNC auto-detects the usual locations.

.PARAMETER IncludeAgent
    Also install the Grev Agent Windows service on this PC and print its
    pairing key. Requires an elevated PowerShell session.

.PARAMETER StartWithWindows
    Register GrevUltraVNC to start when this user signs in.

.PARAMETER NoDesktopShortcut
    Skip the Desktop shortcut (the Start Menu shortcut is always created).

.PARAMETER NoLaunch
    Install but do not start the app afterwards.

.EXAMPLE
    # Normal fresh-device install, from an ordinary (non-admin) PowerShell:
    powershell -ExecutionPolicy Bypass -File .\Install-GrevUltraVNC.ps1

.EXAMPLE
    # Controller plus the Agent service, so this PC can also be connected to.
    # Run this one from an ELEVATED PowerShell:
    powershell -ExecutionPolicy Bypass -File .\Install-GrevUltraVNC.ps1 -IncludeAgent -StartWithWindows

.EXAMPLE
    # No .NET SDK download: grab the published installer instead.
    powershell -ExecutionPolicy Bypass -File .\Install-GrevUltraVNC.ps1 -Source Release
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Release')]
    [string]$Source = 'Build',

    [string]$InstallDir,

    [string]$Branch = 'main',

    [string]$Repository = 'Grevyo/GrevUltraVNC',

    [switch]$SkipViewer,
    [switch]$IncludeAgent,
    [switch]$StartWithWindows,
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# --- Pinned third-party dependency -------------------------------------------------
# Same build and hash the release pipeline packages, so a hand-run install and a
# published installer end up with byte-identical viewers.
$UltraVncUrl = 'https://uvnc.eu/download/1800/UltraVNC_1824.zip'
$UltraVncSha256 = '8AF948089626008F02EDD1254AFC15C814E454EC5FC9E3EAA860356F19D4F113'
$UltraVncLicenseUrl = 'https://raw.githubusercontent.com/ultravnc/UltraVNC/b1f54118e74124407705b342f4cc2269c74e8ca0/LICENSE'

$AppName = 'GrevUltraVNC'
$ExeName = 'GrevUltraVNC.exe'
$RequiredSdkMajor = 10

# ==================================================================================
# Output helpers
# ==================================================================================

$script:StepNumber = 0

function Write-Step {
    param([string]$Message)
    $script:StepNumber++
    Write-Host ''
    Write-Host ("[{0}] {1}" -f $script:StepNumber, $Message) -ForegroundColor Cyan
}

function Write-Detail {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Write-Good {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Yellow
}

function Write-Banner {
    Write-Host ''
    Write-Host '  ####  ####  ###   #   #' -ForegroundColor Cyan
    Write-Host '  #     #  #  #  #  #   #   GrevUltraVNC installer' -ForegroundColor Cyan
    Write-Host '  # ##  ####  ###   #   #   Household remote control' -ForegroundColor Cyan
    Write-Host '  #  #  #  #  #  #   # #    LAN and Zima / Grev Connect' -ForegroundColor Cyan
    Write-Host '  ####  #  #  #  #    #' -ForegroundColor Cyan
    Write-Host ''
}

# ==================================================================================
# Preflight
# ==================================================================================

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Prerequisites {
    Write-Step 'Checking this PC can run GrevUltraVNC'

    if (-not $IsWindowsPlatform) {
        throw 'GrevUltraVNC is a Windows application. Run this installer on Windows.'
    }

    $os = Get-CimInstance Win32_OperatingSystem
    Write-Detail ("Windows: {0} (build {1})" -f $os.Caption.Trim(), $os.BuildNumber)

    if ([int]$os.BuildNumber -lt 10240) {
        throw "GrevUltraVNC needs Windows 10 or newer. This PC reports build $($os.BuildNumber)."
    }

    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'GrevUltraVNC ships as 64-bit only. This PC is running a 32-bit version of Windows.'
    }

    Write-Detail ("PowerShell: {0}" -f $PSVersionTable.PSVersion)
    Write-Detail ("Elevated: {0}" -f (Test-Administrator))

    # Windows PowerShell 5.1 still negotiates TLS 1.0 by default, which GitHub refuses.
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    catch {
        Write-Warn 'Could not raise the TLS version; downloads may fail on very old builds.'
    }

    if ($IncludeAgent -and -not (Test-Administrator)) {
        throw '-IncludeAgent installs a Windows service, so this script must be run from an elevated PowerShell. Right-click PowerShell and choose "Run as administrator", then run it again.'
    }

    Write-Good 'This PC can run GrevUltraVNC.'
}

# ==================================================================================
# Paths
# ==================================================================================

function Resolve-InstallDirectory {
    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
        return [IO.Path]::GetFullPath($InstallDir)
    }
    return Join-Path $env:LOCALAPPDATA "Programs\$AppName"
}

function New-WorkspaceDirectory {
    $path = Join-Path ([IO.Path]::GetTempPath()) ("GrevUltraVNC-Setup-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

# ==================================================================================
# .NET SDK
# ==================================================================================

function Get-DotnetCommand {
    $candidates = @()

    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }

    $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $candidates += Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path $candidate)) { continue }

        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $sdks) { continue }

        foreach ($line in $sdks) {
            if ($line -match '^(\d+)\.') {
                if ([int]$Matches[1] -ge $RequiredSdkMajor) {
                    return $candidate
                }
            }
        }
    }

    return $null
}

function Install-DotnetSdk {
    Write-Detail "No .NET $RequiredSdkMajor SDK found on this PC."

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Detail 'Installing the .NET SDK with winget...'
        & winget install --id "Microsoft.DotNet.SDK.$RequiredSdkMajor" --source winget `
            --accept-package-agreements --accept-source-agreements --silent | Out-Host
        # winget reports several non-zero codes that still mean "it is installed now",
        # so trust the probe below rather than the exit code.
        $found = Get-DotnetCommand
        if ($found) {
            Write-Good "Installed via winget: $found"
            return $found
        }
        Write-Warn 'winget did not produce a usable SDK; falling back to the official install script.'
    }

    # Fallback that needs neither winget nor administrator rights.
    $installRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $bootstrap = Join-Path ([IO.Path]::GetTempPath()) 'dotnet-install.ps1'

    Write-Detail 'Downloading the official dotnet-install script...'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $bootstrap -UseBasicParsing

    Write-Detail "Installing the .NET $RequiredSdkMajor SDK into $installRoot (this can take a few minutes)..."
    & $bootstrap -Channel "$RequiredSdkMajor.0" -InstallDir $installRoot -NoPath | Out-Host

    $dotnet = Join-Path $installRoot 'dotnet.exe'
    if (-not (Test-Path $dotnet)) {
        throw "The .NET $RequiredSdkMajor SDK could not be installed automatically. Install it manually from https://dotnet.microsoft.com/download and run this script again."
    }

    Write-Good "Installed: $dotnet"
    return $dotnet
}

function Resolve-DotnetSdk {
    Write-Step 'Making sure a .NET SDK is available'

    $dotnet = Get-DotnetCommand
    if ($dotnet) {
        Write-Good "Using existing SDK: $dotnet"
        return $dotnet
    }

    return Install-DotnetSdk
}

# ==================================================================================
# Source
# ==================================================================================

function Get-LocalRepositoryRoot {
    # When the script is run from a checkout, build that checkout rather than
    # downloading a second copy of the same code.
    $candidate = Split-Path -Parent $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($candidate)) { return $null }

    $project = Join-Path $candidate 'src\GrevUltraVNC\GrevUltraVNC.csproj'
    if (Test-Path $project) { return $candidate }

    return $null
}

function Get-SourceTree {
    param([string]$Workspace)

    Write-Step 'Getting the GrevUltraVNC source'

    $local = Get-LocalRepositoryRoot
    if ($local) {
        Write-Good "Using the checkout this script came from: $local"
        return $local
    }

    $zipUrl = "https://codeload.github.com/$Repository/zip/refs/heads/$Branch"
    $zipPath = Join-Path $Workspace 'source.zip'
    $extractDir = Join-Path $Workspace 'source'

    Write-Detail "Downloading $Repository ($Branch)..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $root = Get-ChildItem -Path $extractDir -Directory | Select-Object -First 1
    if (-not $root) {
        throw "The downloaded archive for $Repository ($Branch) was empty."
    }

    Write-Good "Downloaded to $($root.FullName)"
    return $root.FullName
}

# ==================================================================================
# Build
# ==================================================================================

function Invoke-Publish {
    param(
        [string]$Dotnet,
        [string]$RepoRoot,
        [string]$OutputDir
    )

    Write-Step 'Building GrevUltraVNC'

    $project = Join-Path $RepoRoot 'src\GrevUltraVNC\GrevUltraVNC.csproj'
    if (-not (Test-Path $project)) {
        throw "Could not find GrevUltraVNC.csproj under $RepoRoot."
    }

    Write-Detail 'Publishing self-contained win-x64 (first run downloads NuGet packages)...'
    & $Dotnet publish $project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $OutputDir `
        --nologo `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "The GrevUltraVNC build failed with exit code $LASTEXITCODE. The compiler output above says why."
    }

    $exe = Join-Path $OutputDir $ExeName
    if (-not (Test-Path $exe)) {
        throw "The build finished but $ExeName was not produced in $OutputDir."
    }

    Write-Good "Built $exe"
}

# ==================================================================================
# UltraVNC viewer
# ==================================================================================

function Add-BundledViewer {
    param(
        [string]$Workspace,
        [string]$StagingDir
    )

    Write-Step 'Adding the UltraVNC Viewer'

    if ($SkipViewer) {
        Write-Warn 'Skipped by -SkipViewer. GrevUltraVNC will look for an installed UltraVNC instead.'
        return
    }

    $zipPath = Join-Path $Workspace 'UltraVNC.zip'
    $extractDir = Join-Path $Workspace 'UltraVNC'

    try {
        Write-Detail 'Downloading the official UltraVNC 1.8.2.4 portable build...'
        Invoke-WebRequest -Uri $UltraVncUrl -OutFile $zipPath -UseBasicParsing
    }
    catch {
        Write-Warn "Could not download UltraVNC: $($_.Exception.Message)"
        Write-Warn 'GrevUltraVNC will still install. Set the viewer path later in Settings.'
        return
    }

    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $UltraVncSha256) {
        # Refusing here is deliberate: an unexpected hash means the bytes are not the
        # build this installer was pinned against, and it is not going to be shipped.
        Write-Warn "UltraVNC download hash mismatch (expected $UltraVncSha256, got $actual). Not bundling it."
        Write-Warn 'Install UltraVNC yourself, or set the viewer path in Settings after install.'
        return
    }
    Write-Detail "SHA-256 verified: $actual"

    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $viewers = @(Get-ChildItem -Path $extractDir -Filter 'vncviewer.exe' -File -Recurse)
    if ($viewers.Count -eq 0) {
        Write-Warn 'The UltraVNC archive did not contain vncviewer.exe. Skipping the bundled viewer.'
        return
    }

    $viewer = $viewers | Where-Object { $_.FullName -match '(?i)[\\/]x64[\\/]' } |
        Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if (-not $viewer) {
        $viewer = $viewers | Sort-Object Length -Descending | Select-Object -First 1
    }

    $targetDir = Join-Path $StagingDir 'UltraVNC'
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -Path $viewer.FullName -Destination (Join-Path $targetDir 'vncviewer.exe') -Force

    $licenseDir = Join-Path $StagingDir 'Licenses'
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    try {
        Invoke-WebRequest -Uri $UltraVncLicenseUrl -OutFile (Join-Path $licenseDir 'UltraVNC-LICENSE.txt') -UseBasicParsing
    }
    catch {
        Write-Warn 'Could not fetch the UltraVNC licence text; the viewer itself was bundled.'
    }

    Write-Good "Bundled $($viewer.FullName)"
}

# ==================================================================================
# Install
# ==================================================================================

function Stop-RunningApp {
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue
    if (-not $running) { return }

    Write-Detail 'GrevUltraVNC is already running; closing it so files can be replaced...'
    foreach ($process in $running) {
        try { $process.CloseMainWindow() | Out-Null } catch { }
    }
    Start-Sleep -Seconds 2

    $stillRunning = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue
    foreach ($process in $stillRunning) {
        try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    Start-Sleep -Seconds 1
}

function Install-Files {
    param(
        [string]$StagingDir,
        [string]$TargetDir
    )

    Write-Step "Installing to $TargetDir"

    # This function clears the target folder, so refuse anything that is obviously not
    # an install folder. A mistyped -InstallDir should not be able to empty a repo,
    # a profile folder or a drive root.
    $resolved = [IO.Path]::GetFullPath($TargetDir).TrimEnd('\')
    $forbidden = @(
        [IO.Path]::GetPathRoot($resolved).TrimEnd('\'),
        $env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA,
        $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramData, $env:SystemRoot,
        [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments')
    ) | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }

    if ($forbidden -contains $resolved) {
        throw "Refusing to install into $resolved, because installing there would clear the folder. Pass a dedicated -InstallDir."
    }

    if (Test-Path (Join-Path $resolved '.git')) {
        throw "Refusing to install into $resolved: it looks like a git checkout, and installing there would delete it."
    }

    Stop-RunningApp

    if (Test-Path $TargetDir) {
        Write-Detail 'Replacing the previous install (settings and machines are stored elsewhere and are kept)...'
        Get-ChildItem -Path $TargetDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Copy-Item -Path (Join-Path $StagingDir '*') -Destination $TargetDir -Recurse -Force

    $exe = Join-Path $TargetDir $ExeName
    if (-not (Test-Path $exe)) {
        throw "Install finished but $ExeName is missing from $TargetDir."
    }

    $size = [math]::Round(((Get-ChildItem -Path $TargetDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Good "Installed $size MB to $TargetDir"
    return $exe
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetExe,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $shortcut.TargetPath = $TargetExe
        $shortcut.WorkingDirectory = Split-Path -Parent $TargetExe
        $shortcut.IconLocation = "$TargetExe,0"
        $shortcut.Description = $Description
        $shortcut.Save()
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Add-Shortcuts {
    param([string]$TargetExe)

    Write-Step 'Creating shortcuts'

    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
    $startMenuLink = Join-Path $startMenu "$AppName.lnk"
    New-Shortcut -ShortcutPath $startMenuLink -TargetExe $TargetExe -Description 'GrevUltraVNC household remote control'
    Write-Good "Start Menu: $startMenuLink"

    if ($NoDesktopShortcut) {
        Write-Detail 'Desktop shortcut skipped by -NoDesktopShortcut.'
        return
    }

    $desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
    New-Shortcut -ShortcutPath $desktopLink -TargetExe $TargetExe -Description 'GrevUltraVNC household remote control'
    Write-Good "Desktop: $desktopLink"
}

function Set-StartWithWindows {
    param([string]$TargetExe)

    if (-not $StartWithWindows) { return }

    Write-Step 'Registering GrevUltraVNC to start with Windows'

    # Same registry value the app's own Settings screen writes, so the two agree.
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name $AppName -Value ('"{0}"' -f $TargetExe)
    Write-Good 'GrevUltraVNC will start when this user signs in.'
}

# ==================================================================================
# Optional: Grev Agent on this same PC
# ==================================================================================

function Install-GrevAgentService {
    param([string]$Workspace)

    if (-not $IncludeAgent) { return }

    Write-Step 'Installing the Grev Agent service on this PC'

    $zipPath = Join-Path $Workspace 'agent.zip'
    $extractDir = Join-Path $Workspace 'agent'
    $assetUrl = "https://github.com/$Repository/releases/download/agent-latest/GrevUltraVNC-Agent-win-x64.zip"

    try {
        Write-Detail 'Downloading the latest Grev Agent package...'
        Invoke-WebRequest -Uri $assetUrl -OutFile $zipPath -UseBasicParsing
    }
    catch {
        Write-Warn "Could not download the Grev Agent package: $($_.Exception.Message)"
        Write-Warn 'The controller is installed. Install the Agent later with scripts\install-agent-package.ps1.'
        return
    }

    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $installer = Get-ChildItem -Path $extractDir -Filter 'Install-GrevAgent.ps1' -File -Recurse |
        Select-Object -First 1
    if (-not $installer) {
        Write-Warn 'The Agent package did not contain Install-GrevAgent.ps1. Skipping the Agent.'
        return
    }

    Write-Detail 'Running the Agent installer (creates the service and the LocalSubnet firewall rule)...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer.FullName

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "The Agent installer exited with code $LASTEXITCODE. The controller is still installed."
        return
    }

    Write-Good 'Grev Agent installed. The pairing key it printed above is what other PCs need.'
}

# ==================================================================================
# Alternative: published installer
# ==================================================================================

function Install-FromRelease {
    param([string]$Workspace)

    Write-Step 'Fetching the newest published GrevUltraVNC installer'

    $headers = @{ 'User-Agent' = 'GrevUltraVNC-Installer' }
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=30" `
        -Headers $headers -UseBasicParsing

    $asset = $null
    foreach ($release in $releases) {
        $candidate = $release.assets | Where-Object { $_.name -like '*Setup.exe' } | Select-Object -First 1
        if ($candidate) {
            Write-Detail "Release: $($release.name) ($($release.tag_name))"
            $asset = $candidate
            break
        }
    }

    if (-not $asset) {
        throw "No published GrevUltraVNC installer was found in $Repository. Re-run without -Source Release to build from source instead."
    }

    $setupPath = Join-Path $Workspace $asset.name
    Write-Detail "Downloading $($asset.name)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $setupPath -UseBasicParsing

    Write-Detail 'Running the installer silently...'
    $process = Start-Process -FilePath $setupPath `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL' `
        -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "The published installer exited with code $($process.ExitCode)."
    }

    $searchRoots = @(
        (Join-Path $env:LOCALAPPDATA "Programs\$AppName"),
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        (Join-Path $env:ProgramFiles $AppName),
        (Join-Path ${env:ProgramFiles(x86)} $AppName)
    )

    $installed = $null
    foreach ($root in $searchRoots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) { continue }
        $installed = Get-ChildItem -Path $root -Filter $ExeName -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($installed) { break }
    }

    if (-not $installed) {
        throw 'The published installer ran, but GrevUltraVNC.exe could not be located afterwards.'
    }

    Write-Good "Installed: $($installed.FullName)"
    return $installed.FullName
}

# ==================================================================================
# Main
# ==================================================================================

$IsWindowsPlatform = ($env:OS -eq 'Windows_NT')
$workspace = $null
$startedAt = Get-Date

try {
    Write-Banner
    Assert-Prerequisites

    $workspace = New-WorkspaceDirectory
    Write-Detail "Working folder: $workspace"

    if ($Source -eq 'Release') {
        $installedExe = Install-FromRelease -Workspace $workspace
    }
    else {
        $dotnet = Resolve-DotnetSdk
        $repoRoot = Get-SourceTree -Workspace $workspace

        $staging = Join-Path $workspace 'app'
        Invoke-Publish -Dotnet $dotnet -RepoRoot $repoRoot -OutputDir $staging
        Add-BundledViewer -Workspace $workspace -StagingDir $staging

        $installedExe = Install-Files -StagingDir $staging -TargetDir (Resolve-InstallDirectory)
    }

    Add-Shortcuts -TargetExe $installedExe
    Set-StartWithWindows -TargetExe $installedExe
    Install-GrevAgentService -Workspace $workspace

    $elapsed = [math]::Round(((Get-Date) - $startedAt).TotalSeconds)
    Write-Host ''
    Write-Host '===================================================================' -ForegroundColor Green
    Write-Host " GrevUltraVNC is installed. ($elapsed seconds)" -ForegroundColor Green
    Write-Host '===================================================================' -ForegroundColor Green
    Write-Host ''
    Write-Host "  App          $installedExe"
    Write-Host "  Your data    $(Join-Path $env:APPDATA 'GrevUltraVNC')"
    Write-Host '  Secrets      Windows Credential Manager (VNC passwords, Agent keys)'
    Write-Host ''
    Write-Host '  Next: click "Connect by ID" and enter a GrevConnect ID such as GC-LivingRoom.' -ForegroundColor Cyan
    Write-Host '  Off the LAN? Bring up your Zima / Grev Connect network first, then connect.' -ForegroundColor Cyan
    if (-not $IncludeAgent) {
        Write-Host ''
        Write-Host '  To let other PCs connect TO this one, re-run this script from an' -ForegroundColor DarkGray
        Write-Host '  elevated PowerShell with -IncludeAgent.' -ForegroundColor DarkGray
    }
    Write-Host ''

    if (-not $NoLaunch) {
        Write-Host 'Starting GrevUltraVNC...' -ForegroundColor Cyan
        Start-Process -FilePath $installedExe -WorkingDirectory (Split-Path -Parent $installedExe)
    }
}
catch {
    Write-Host ''
    Write-Host '===================================================================' -ForegroundColor Red
    Write-Host ' GrevUltraVNC was NOT installed.' -ForegroundColor Red
    Write-Host '===================================================================' -ForegroundColor Red
    Write-Host ''
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    exit 1
}
finally {
    if ($workspace -and (Test-Path $workspace)) {
        Remove-Item -Path $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}
