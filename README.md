# GrevUltraVNC

A LAN-first Windows remote-management app built around UltraVNC plus the optional **GrevUltraVNC Agent**.

UltraVNC remains the remote-desktop engine. GrevUltraVNC adds machine organisation, saved credentials, status monitoring, power/service actions, machine management, an authenticated terminal, self-updating Agents and a docked Grev Control Panel.

## Current app features

- Saved machines: name, static IP, MAC address, VNC port, Grev Agent port, group, notes and favourite state
- JSON machine/settings persistence under `%APPDATA%\GrevUltraVNC`
- VNC passwords stored in Windows Credential Manager rather than JSON
- Grev Agent pairing keys stored separately in Windows Credential Manager rather than JSON
- Automatic ping + VNC TCP-port status checks
- Automatic Grev Agent detection and authenticated telemetry polling
- Search, favourites and favourite quick-connect entries from the system tray
- Double-click a machine to connect immediately
- Visible Connect / Manage / Edit / More controls on machine cards
- UltraVNC Viewer auto-detection
- Auto-scaling/fullscreen preferences
- Wake-on-LAN
- Restart and shut down through Windows remote shutdown
- Start / stop / restart `uvnc_service`
- Set UltraVNC Server to start automatically with Windows
- Open `\\machine\` network shares
- Ping/VNC/service/Agent diagnostics
- Light and Dark themes, with Dark as default
- Start GrevUltraVNC with Windows
- Minimise GrevUltraVNC to the system tray
- Grev-branded blue/violet dashboard and control panel

## Machine Management

Click **Manage** on a paired machine to open the native management window.

### Overview

- live CPU usage and CPU model
- live RAM usage
- uptime
- active Windows user
- Windows/OS description
- fixed-disk free space
- UltraVNC service state
- local VNC listening state
- process/service inventory counts

### Processes

- searchable running-process list
- PID
- working-set memory
- accumulated CPU time
- Windows session ID
- process start time when accessible
- authenticated **End selected process** action
- protected critical Windows processes cannot be terminated through Grev Agent
- the Agent cannot terminate itself
- **Restart Explorer** relaunches Explorer in the active interactive Windows session

### Services

- searchable Windows service list
- service/display names
- running state
- startup mode
- start / stop / restart controls
- destructive service actions require confirmation
- Grev Agent refuses to stop or restart its own Windows service remotely

### Terminal

The **Terminal** tab runs PowerShell or CMD commands on the target without opening an external console window.

- PowerShell and CMD shell selection
- stdout/stderr returned into GrevUltraVNC
- exit code and runtime shown after each command
- maximum command runtime: 30 seconds
- oversized output is truncated by the Agent
- commands execute as the Grev Agent service account (`LocalSystem` with the standard installer)
- command request and response payloads are encrypted with AES-256-GCM using a key derived from the machine pairing key
- the encrypted request envelope is additionally HMAC-SHA256 signed and protected by timestamp + nonce replay checks

## Grev Control Panel

The Grev Control Panel follows the UltraVNC Viewer window and provides:

- Ctrl+Alt+Delete
- Windows / Start
- Ctrl+Shift+Escape / Task Manager
- Alt+Tab
- Alt+F4
- Win+R
- Win+E
- Win+L
- Fullscreen toggle
- Screen refresh
- UltraVNC file transfer
- Bring Viewer to front / disconnect
- UltraVNC service start / restart / stop / start-at-boot
- Wake / restart / shut down machine
- Network shares and diagnostics
- live Grev Agent system-health telemetry when paired
- direct **Update Grev Agent** action from GitHub
- direct **Manage machine** access

## In-app Agent updates

Once a machine has the current management-capable Agent installed, GrevUltraVNC can update it without opening PowerShell on the target.

The update action is available from **Machine Actions** and the docked **Grev Control Panel**.

The controller:

1. uses the authenticated/encrypted Agent terminal channel to download the repository updater script
2. launches the updater independently of the Agent service
3. waits while the Agent service stops, replaces itself and starts again
4. verifies a success/failure marker after the Agent returns
5. preserves the machine's existing pairing configuration

GitHub Actions publishes the current self-contained package to the stable `agent-latest` release tag.

## Grev Agent

The Agent is a separate self-contained Windows service. It currently provides:

- CPU name and live CPU usage
- total/available RAM
- fixed-disk free space
- Windows/OS description
- system uptime
- active console user
- UltraVNC service state
- whether the local VNC TCP port is listening
- process inventory and guarded process termination
- service inventory and service control
- interactive-session Explorer restart
- encrypted authenticated PowerShell/CMD command execution
- Agent version/machine identity

### Agent network/security model

- Default TCP port: `47820`
- installer creates a Windows Firewall inbound rule limited to `LocalSubnet`
- every machine gets its own random 256-bit pairing key
- pairing key is stored on the target under `%ProgramData%\GrevUltraVNC\Agent\agent.json`
- Agent configuration directory is restricted to SYSTEM and Administrators by the installer
- the controller stores its copy of the pairing key in Windows Credential Manager
- authenticated API requests use HMAC-SHA256 over timestamp, nonce, HTTP method, path and body hash
- requests outside the allowed clock-skew window or with reused nonces are rejected
- the pairing key itself is not transmitted in API requests
- terminal command/output payloads use AES-256-GCM with a separate encryption key derived from the pairing key

The current transport still uses HTTP on the trusted LAN for ordinary telemetry and management metadata. Terminal contents are application-layer encrypted, but a future hardening phase should move the entire Agent API to HTTPS/mTLS or an equivalent fully encrypted transport.

## Build and run GrevUltraVNC

```powershell
dotnet run --project .\src\GrevUltraVNC\GrevUltraVNC.csproj
```

Or double-click:

```text
run-dev.cmd
```

## Agent packages and GitHub delivery

Every push to `main` is built on GitHub Actions. The workflow builds the full solution and publishes/replaces:

```text
agent-latest / GrevUltraVNC-Agent-win-x64.zip
```

The Agent is self-contained, so target PCs do not need the .NET runtime or SDK.

For a machine that already has a management-capable Agent, use **Update Grev Agent** inside GrevUltraVNC.

The standalone updater remains available for bootstrap/recovery:

```text
scripts\update-agent-from-github.ps1
```

### Manual package build

For development or recovery, the package can still be built locally:

```powershell
.\scripts\build-agent-package.ps1
```

It creates:

```text
dist\GrevUltraVNC-Agent-win-x64.zip
```

### Install or upgrade from an extracted package

Run `Install-GrevAgent.ps1` as Administrator. The installer:

- stops/replaces an older Grev Agent service if present
- copies the Agent to `C:\Program Files\GrevUltraVNC Agent`
- creates the automatic `GrevUltraVNCAgent` Windows service
- starts the service
- preserves an existing `%ProgramData%\GrevUltraVNC\Agent\agent.json` pairing configuration
- secures `%ProgramData%\GrevUltraVNC\Agent`
- opens the Agent port only to `LocalSubnet`
- prints the machine's pairing key for first-time pairing

For a new machine, paste the displayed key into **Edit machine → Grev Agent → Pairing key** and refresh the dashboard.

## Development install directly from the repo

On a target PC that also has the .NET 10 SDK and a copy of the repository:

```powershell
.\scripts\install-agent.ps1
```

To uninstall:

```powershell
.\scripts\uninstall-agent.ps1
```

Add `-PurgeData` to also delete the Agent pairing/configuration data.

## Build everything

```powershell
dotnet build .\GrevUltraVNC.slnx -c Release
```

GitHub Actions builds the full solution on `windows-latest` so the WPF controller, shared contracts and Windows Agent are compiled together.

## Architecture direction

VNC is one machine capability, not the whole GrevUltraVNC architecture. Logical next additions include:

- HTTPS/mTLS for the complete Agent API
- richer CPU/RAM/network/disk telemetry and history
- command history / reusable quick scripts
- event and connection history
- notifications and health alerts
- native file browser and file operations
- first-time Agent deployment from the controller
- per-machine custom icons and richer group management
- bulk actions across machine groups
- multi-monitor controls
- import/export and backup
- optional application PIN / Windows Hello protection for dangerous controls
- proper packaged GrevUltraVNC Windows releases and installer
