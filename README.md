# GrevUltraVNC

A LAN-first Windows machine manager built around UltraVNC. Save the static-IP PCs on your network, see whether they are online, double-click a machine to open its action panel, and launch/administer the active UltraVNC session from one place.

## Current first build

- Saved machines: name, static IP, MAC address, VNC port, group and notes
- JSON persistence under `%APPDATA%\GrevUltraVNC`
- Automatic online/offline + VNC-port checking
- Double-click machine action panel
- Launch UltraVNC Viewer directly to the saved machine
- Wake-on-LAN
- Restart and shut down through Windows remote shutdown
- Open `\\machine\` network shares
- Ping/VNC connection diagnostics
- Remote-key buttons for Ctrl+Alt+Del, Windows/Start, and Ctrl+Shift+Esc (Task Manager)
- UltraVNC path auto-detection
- Auto-scaling/fullscreen preferences

## Requirements

- Windows 11/10 control PC
- .NET 10 Desktop Runtime (or build self-contained later)
- UltraVNC Viewer installed on the control PC
- UltraVNC Server installed/running on each target PC
- Static/reserved LAN IPs

Remote restart/shutdown uses Windows' built-in `shutdown.exe /m` support. The account/firewall/Local Security Policy on the target PCs must permit remote shutdown.

## UltraVNC remote keys

GrevUltraVNC keeps the external UltraVNC Viewer as the actual VNC client in the first build. The app tracks the Viewer process it launches and sends UltraVNC's documented Viewer shortcuts to that active session. Ctrl+Alt+Del uses UltraVNC's `Ctrl+Alt+F4` send-CAD shortcut. Start and Ctrl+Shift+Esc use UltraVNC's Scroll-Lock remote-key capture mode.

Official UltraVNC viewer docs:
- https://uvnc.com/docs/ultravnc-viewer/71-ultravnc-viewer-gui.html
- https://uvnc.com/docs/ultravnc-viewer/52-ultravnc-viewer-commandline-parameters.html

## Build

```powershell
dotnet build .\src\GrevUltraVNC\GrevUltraVNC.csproj -c Release
```

## Direction

The machine/action architecture intentionally treats VNC as one capability rather than the whole app. Later versions can add secure credential storage, service control, remote terminal, richer system telemetry, machine groups/icons, file transfer, tray mode, startup with Windows, and an optional Grev agent without replacing the machine library.
