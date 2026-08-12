using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed class UltraVncSessionService
{
    private readonly Dictionary<Guid, Process> _sessions = [];
    private readonly Dictionary<Guid, Process> _virtualSessions = [];
    private readonly VncCredentialService _credentials = new();

    public string? FindViewer(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "uvnc bvba", "UltraVNC", "vncviewer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UltraVNC", "vncviewer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "uvnc bvba", "UltraVNC", "vncviewer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "UltraVNC", "vncviewer.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public Process Launch(Machine machine, AppSettings settings)
    {
        if (TryGetSession(machine.Id, out var existing))
        {
            FocusViewer(existing!);
            return existing!;
        }

        var process = StartViewer(machine, settings, configPath: null);
        _sessions[machine.Id] = process;
        return process;
    }

    public async Task<Process> OpenVirtualDisplayAsync(
        Machine machine,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (TryGetVirtualSession(machine.Id, out var existing))
        {
            FocusViewer(existing!);
            return existing!;
        }

        if (string.IsNullOrWhiteSpace(machine.ActiveAddress))
            throw new InvalidOperationException("No LAN or Grev Connect route is currently available for this machine.");

        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
        var width = Math.Clamp(bounds?.Width ?? 1920, 800, 7680);
        var height = Math.Clamp(bounds?.Height ?? 1080, 600, 4320);
        var configPath = CreateVirtualDisplayConfig(machine.Id, width, height);
        var process = StartViewer(machine, settings, configPath);
        _virtualSessions[machine.Id] = process;

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (process.HasExited)
                    throw new InvalidOperationException(
                        "UltraVNC closed the Screen 2 viewer while creating the virtual display. " +
                        "The target must run UltraVNC Server as a service/administrator, use DDengine capture, " +
                        "and support UltraVNC virtual displays (1.3.0 or newer).");

                var handle = FindPrimaryViewerWindow(process);
                if (handle != IntPtr.Zero)
                {
                    FocusViewer(process);
                    return process;
                }

                await Task.Delay(250, cancellationToken);
            }

            throw new InvalidOperationException(
                "Screen 2 did not create a persistent UltraVNC viewer window. " +
                "The remote display may flicker once while Windows adds the virtual monitor, but the second viewer should remain open.");
        }
        catch
        {
            _virtualSessions.Remove(machine.Id);
            TryCloseProcess(process);
            throw;
        }
    }

    public bool HasActiveSession(Guid machineId) => TryGetSession(machineId, out _);
    public bool HasVirtualSession(Guid machineId) => TryGetVirtualSession(machineId, out _);

    public bool TryGetViewerWindowHandle(Guid machineId, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryGetSession(machineId, out var process) || process is null)
            return false;

        handle = FindPrimaryViewerWindow(process);
        return handle != IntPtr.Zero;
    }

    public bool TryGetVirtualViewerWindowHandle(Guid machineId, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryGetVirtualSession(machineId, out var process) || process is null)
            return false;

        handle = FindPrimaryViewerWindow(process);
        return handle != IntPtr.Zero;
    }

    public void BringViewerToFront(Guid machineId) => FocusViewer(GetSession(machineId));
    public void BringVirtualViewerToFront(Guid machineId) => FocusViewer(GetVirtualSession(machineId));

    public void ToggleFullScreen(Guid machineId)
    {
        var process = GetSession(machineId);
        FocusViewer(process);
        SendChord(VK_CONTROL, VK_MENU, VK_F12);
    }

    public void OpenFileTransfer(Guid machineId)
    {
        var process = GetSession(machineId);
        FocusViewer(process);
        SendChord(VK_CONTROL, VK_MENU, VK_F7);
    }

    public void RequestScreenRefresh(Guid machineId)
    {
        var process = GetSession(machineId);
        SendRemoteChordWithScrollLock(process, VK_SNAPSHOT);
    }

    public void CloseVirtualDisplay(Guid machineId)
    {
        if (!TryGetVirtualSession(machineId, out var process) || process is null)
            return;

        _virtualSessions.Remove(machineId);
        TryCloseProcess(process);
    }

    public void Disconnect(Guid machineId)
    {
        if (TryGetVirtualSession(machineId, out var virtualProcess) && virtualProcess is not null)
        {
            _virtualSessions.Remove(machineId);
            TryCloseProcess(virtualProcess);
        }

        if (!TryGetSession(machineId, out var process) || process is null)
            return;

        _sessions.Remove(machineId);
        TryCloseProcess(process);
    }

    public void SendCtrlAltDelete(Guid machineId)
    {
        var process = GetSession(machineId);
        FocusViewer(process);
        SendChord(VK_CONTROL, VK_MENU, VK_F4);
    }

    public void SendWindowsKey(Guid machineId)
    {
        var process = GetSession(machineId);
        SendRemoteChordWithScrollLock(process, VK_CONTROL, VK_ESCAPE);
    }

    public void SendCtrlShiftEscape(Guid machineId)
    {
        var process = GetSession(machineId);
        SendRemoteChordWithScrollLock(process, VK_CONTROL, VK_SHIFT, VK_ESCAPE);
    }

    public void SendAltTab(Guid machineId) =>
        SendRemoteChordWithScrollLock(GetSession(machineId), VK_MENU, VK_TAB);

    public void SendAltF4(Guid machineId) =>
        SendRemoteChordWithScrollLock(GetSession(machineId), VK_MENU, VK_F4);

    public void SendWinR(Guid machineId) =>
        SendRemoteChordWithScrollLock(GetSession(machineId), VK_LWIN, VK_R);

    public void SendWinE(Guid machineId) =>
        SendRemoteChordWithScrollLock(GetSession(machineId), VK_LWIN, VK_E);

    public void SendWinL(Guid machineId) =>
        SendRemoteChordWithScrollLock(GetSession(machineId), VK_LWIN, VK_L);

    private Process StartViewer(Machine machine, AppSettings settings, string? configPath)
    {
        if (string.IsNullOrWhiteSpace(machine.ActiveAddress))
            throw new InvalidOperationException("No LAN or Grev Connect route is currently available for this machine.");

        var viewer = FindViewer(settings.UltraVncViewerPath)
            ?? throw new FileNotFoundException("UltraVNC Viewer was not found. Set its path in Settings.");

        var psi = new ProcessStartInfo
        {
            FileName = viewer,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            psi.ArgumentList.Add("-config");
            psi.ArgumentList.Add(configPath);
        }

        psi.ArgumentList.Add("-connect");
        psi.ArgumentList.Add($"{machine.ActiveAddress}::{machine.VncPort}");
        psi.ArgumentList.Add("-shared");

        if (_credentials.TryRead(machine.Id, out var password) && !string.IsNullOrEmpty(password))
        {
            psi.ArgumentList.Add("-password");
            psi.ArgumentList.Add(password);
        }

        if (settings.AutoScaling) psi.ArgumentList.Add("-autoscaling");
        if (settings.FullScreenByDefault && string.IsNullOrWhiteSpace(configPath))
            psi.ArgumentList.Add("-fullscreen");

        return Process.Start(psi) ?? throw new InvalidOperationException("UltraVNC Viewer did not start.");
    }

    private static string CreateVirtualDisplayConfig(Guid machineId, int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GrevUltraVNC", "VirtualDisplays");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{machineId:N}-screen2.vnc");

        // UltraVNC virtual-display mode:
        // ChangeServerRes + requested resolution + extendDisplay keeps the host's physical
        // display enabled and adds a virtual monitor for this viewer. showExtend asks this
        // viewer to show the newly-created extended display rather than Screen 1.
        var content = $"""
                      [options]
                      shared=1
                      viewonly=0
                      showtoolbar=1
                      fullscreen=0
                      AutoScaling=1
                      directx=0
                      allowMonitorSpanning=0
                      ChangeServerRes=1
                      extendDisplay=1
                      showExtend=1
                      use_virt=0
                      useAllMonitors=0
                      requestedWidth={width}
                      requestedHeight={height}
                      """;

        File.WriteAllText(path, content);
        return path;
    }

    private Process GetSession(Guid machineId) =>
        TryGetSession(machineId, out var process)
            ? process!
            : throw new InvalidOperationException("Open Screen 1 to this machine first.");

    private Process GetVirtualSession(Guid machineId) =>
        TryGetVirtualSession(machineId, out var process)
            ? process!
            : throw new InvalidOperationException("Screen 2 is not open for this machine.");

    private bool TryGetSession(Guid machineId, out Process? process) =>
        TryGetTrackedSession(_sessions, machineId, out process);

    private bool TryGetVirtualSession(Guid machineId, out Process? process) =>
        TryGetTrackedSession(_virtualSessions, machineId, out process);

    private static bool TryGetTrackedSession(Dictionary<Guid, Process> sessions, Guid machineId, out Process? process)
    {
        process = null;
        if (!sessions.TryGetValue(machineId, out var candidate)) return false;

        try
        {
            if (candidate.HasExited)
            {
                sessions.Remove(machineId);
                return false;
            }
        }
        catch
        {
            sessions.Remove(machineId);
            return false;
        }

        process = candidate;
        return true;
    }

    private static void TryCloseProcess(Process process)
    {
        try
        {
            if (process.HasExited) return;

            var handle = FindPrimaryViewerWindow(process);
            if (handle != IntPtr.Zero && PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                return;

            if (!process.CloseMainWindow())
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static void SendRemoteChordWithScrollLock(Process process, params byte[] keys)
    {
        FocusViewer(process);
        KeyPress(VK_SCROLL);
        Thread.Sleep(35);
        SendChord(keys);
    }

    private static void SendChord(params byte[] keys)
    {
        foreach (var key in keys) KeyDown(key);
        Thread.Sleep(25);
        for (var i = keys.Length - 1; i >= 0; i--) KeyUp(keys[i]);
    }

    private static void KeyPress(byte key)
    {
        KeyDown(key);
        Thread.Sleep(20);
        KeyUp(key);
    }

    private static void KeyDown(byte key) => keybd_event(key, 0, 0, UIntPtr.Zero);
    private static void KeyUp(byte key) => keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    private static void FocusViewer(Process process)
    {
        var handle = FindPrimaryViewerWindow(process);
        if (handle == IntPtr.Zero) return;

        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
        ShowWindow(handle, SW_SHOW);
        SetForegroundWindow(handle);
    }

    // UltraVNC creates transient top-level toolbar/tooltip windows. Process.MainWindowHandle
    // can briefly follow one of those, which made the Grev Control Panel jump around when
    // hovering the viewer toolbar. Always resolve the largest visible top-level window owned
    // by the viewer process instead. That remains the actual desktop viewer in windowed and
    // fullscreen modes while ignoring tiny controls and popups.
    private static IntPtr FindPrimaryViewerWindow(Process process)
    {
        try
        {
            if (process.HasExited) return IntPtr.Zero;

            var processId = (uint)process.Id;
            var bestHandle = IntPtr.Zero;
            long bestArea = 0;

            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != processId || !IsWindowVisible(window))
                    return true;

                if (GetWindow(window, GW_OWNER) != IntPtr.Zero)
                    return true;

                if (!GetWindowRect(window, out var rect))
                    return true;

                var width = Math.Max(0, rect.Right - rect.Left);
                var height = Math.Max(0, rect.Bottom - rect.Top);
                if (width < 320 || height < 220)
                    return true;

                var area = (long)width * height;
                if (area <= bestArea)
                    return true;

                bestArea = area;
                bestHandle = window;
                return true;
            }, IntPtr.Zero);

            if (bestHandle != IntPtr.Zero)
                return bestHandle;

            process.Refresh();
            return process.MainWindowHandle;
        }
        catch
        {
            try
            {
                process.Refresh();
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4;
    private const uint WM_CLOSE = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_TAB = 0x09;
    private const byte VK_F4 = 0x73;
    private const byte VK_F7 = 0x76;
    private const byte VK_F12 = 0x7B;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_R = 0x52;
    private const byte VK_E = 0x45;
    private const byte VK_L = 0x4C;
    private const byte VK_SCROLL = 0x91;
    private const byte VK_SNAPSHOT = 0x2C;
}
