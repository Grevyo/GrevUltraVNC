# GrevUltraVNC

A LAN-first Windows remote-management app built around UltraVNC plus the optional **GrevUltraVNC Agent**.

UltraVNC remains the remote-desktop engine. GrevUltraVNC adds machine organisation, saved credentials, status monitoring, power/service actions and a docked Grev Control Panel. The Grev Agent adds authenticated machine telemetry and is the foundation for future native management features.

## Current app features

- Saved machines: name, static IP, MAC address, VNC port, Grev Agent port, group, notes and favourite state
- JSON machine/settings persistence under `%APPDATA%\GrevUltraVNC`
- VNC passwords stored in Windows Credential Manager rather than JSON
- Grev Agent pairing keys stored separately in Windows Credential Manager rather than JSON
- Automatic ping + VNC TCP-port status checks
- Automatic Grev Agent detection and authenticated telemetry polling
- Search, favourites and favourite quick-connect entries from the system tray
- Double-click a machine to connect immediately
- Visible Connect / Edit / More controls on every machine card
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
- Grev-branded dashboard/control panel

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
- Live Grev Agent system-health telemetry when paired

## Grev Agent v0.1

The Agent is a separate self-contained Windows service. The current Agent is intentionally **read-only** and reports:

- CPU name and live CPU usage
- total/available RAM
- fixed-disk free space
- Windows/OS description
- system uptime
- active console user
- UltraVNC service state
- whether the local VNC TCP port is listening
- Agent version/machine identity

### Agent network/security model

- Default TCP port: `47820`
- Installer creates a Windows Firewall inbound rule limited to `LocalSubnet`
- Every machine gets its own random 256-bit pairing key
- Pairing key is stored on the target under `%ProgramData%\GrevUltraVNC\Agent\agent.json`
- Agent configuration directory is restricted to SYSTEM and Administrators by the installer
- The controller stores its copy of the pairing key in Windows Credential Manager
- Authenticated API requests use HMAC-SHA256 over timestamp, nonce, HTTP method, path and body hash
- Requests outside the allowed clock-skew window or with reused nonces are rejected
- The pairing key itself is not transmitted in API requests

The current transport is HTTP on the trusted LAN. Before command execution or other high-impact Agent functions are added, the transport layer should be upgraded to encrypted authenticated transport (HTTPS/mTLS or an equivalent encrypted protocol).

## Build and run GrevUltraVNC

```powershell
dotnet run --project .\src\GrevUltraVNC\GrevUltraVNC.csproj
```

Or double-click:

```text
run-dev.cmd
```

## Build a deployable Grev Agent package

Run this on the development/control PC:

```powershell
.\scripts\build-agent-package.ps1
```

It creates:

```text
dist\GrevUltraVNC-Agent-win-x64.zip
```

The Agent is published self-contained, so the target PC does not need the .NET runtime or SDK.

### Install on a target PC

1. Copy the generated ZIP to the target PC.
2. Extract it.
3. Open PowerShell as Administrator in the extracted folder.
4. Run:

```powershell
.\Install-GrevAgent.ps1
```

The installer:

- copies the Agent to `C:\Program Files\GrevUltraVNC Agent`
- creates the automatic `GrevUltraVNCAgent` Windows service
- starts the service
- secures `%ProgramData%\GrevUltraVNC\Agent`
- opens the Agent port only to `LocalSubnet`
- prints the machine's pairing key

Then in GrevUltraVNC:

1. Edit the machine.
2. Leave Agent port at `47820` unless intentionally changed.
3. Paste the printed key into **Grev Agent → Pairing key**.
4. Save.
5. Refresh the dashboard.

The machine card should change to **AGENT CONNECTED** and begin showing CPU/RAM/uptime information.

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

GitHub Actions builds the full solution on `windows-latest` so both the WPF controller and Windows Agent are compiled.

## Architecture direction

VNC is one machine capability, not the whole GrevUltraVNC architecture. The Agent is the base for later additions such as:

- native process/service management
- remote terminal/command execution after encrypted transport is in place
- richer CPU/RAM/network/disk telemetry
- event/connection history
- notifications and health alerts
- native file browser and file operations
- automatic Agent deployment/update
- machine detail pages
- bulk/group management

Remote command execution is deliberately not part of Agent v0.1.
