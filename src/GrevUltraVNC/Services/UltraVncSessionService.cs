using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public readonly record struct ViewerSurfaceBounds(int Left, int Top, int Width, int Height)
{
    public bool Contains(int x, int y) =>
        x >= Left && y >= Top && x < Left + Width && y < Top + Height;
}

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

        // Grev sessions deliberately begin view-only. Collaboration ownership decides who
        // may send remote mouse/keyboard input after everyone can see each other's cursor.
        var process = StartViewer(machine, settings, configPath: null, startViewOnly: true);
        _sessions[machine.Id] = process;
        return process;
    }

    public async Task<Process> OpenVirtualDisplayAsync(
        Machine machine,
        AppSettings settings,
        int monitorIndex,
        CancellationToken cancellationToken = default)
    {
        if (TryGetVirtualSession(machine.Id, out var existing))
        {
            FocusViewer(existing!);
            return existing!;
        }

        if (monitorIndex < 1)
            throw new InvalidOperationException("The host did not return a valid Screen 2 monitor index.");

        if (string.IsNullOrWhiteSpace(machine.ActiveAddress))
            throw new InvalidOperationException("No LAN or Grev Connect route is currently available for this machine.");

        // The Grev Agent has already created and attached the Windows virtual monitor. Viewer 2
        // must therefore be a plain VNC viewer: it must not request another display topology.
        var configPath = CreateSecondaryViewerConfig(machine.Id);
        var process = StartViewer(machine, settings, configPath, startViewOnly: true);
        _virtualSessions[machine.Id] = process;

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (process.HasExited)
                    throw new InvalidOperationException("UltraVNC closed the Screen 2 viewer before it could select the virtual monitor.");

                var handle = FindPrimaryViewerWindow(process);
                if (handle != IntPtr.Zero)
                {
                    // UltraVNC learns the server monitor count shortly after connection. Send its
                    // exact SetMonitor command more than once so monitor selection cannot race the
                    // initial ServerInit/monitor-capability messages.
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        await Task.Delay(attempt == 0 ? 900 : 450, cancellationToken);
                        if (process.HasExited)
                            throw new InvalidOperationException("The Screen 2 viewer closed while selecting the virtual monitor.");
                        SendMonitorSelection(process, monitorIndex);
                    }

                    SetProcessViewOnly(process, true);
                    FocusViewer(process);
                    return process;
                }

                await Task.Delay(250, cancellationToken);
            }

            throw new InvalidOperationException("Screen 2 did not create a persistent UltraVNC viewer window.");
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

    public bool TryGetViewerWindowHandle(Guid machineId, out IntPtr handle) =>
        TryGetWindowHandle(_sessions, machineId, out handle);

    public bool TryGetVirtualViewerWindowHandle(Guid machineId, out IntPtr handle) =>
        TryGetWindowHandle(_virtualSessions, machineId, out handle);

    public bool TryGetViewerSurfaceBounds(Guid machineId, bool virtualDisplay, out ViewerSurfaceBounds bounds)
    {
        bounds = default;
        var sessions = virtualDisplay ? _virtualSessions : _sessions;
        if (!TryGetTrackedSession(sessions, machineId, out var process) || process is null)
            return false;

        var surface = FindViewerContentWindow(process);
        if (surface == IntPtr.Zero || !GetWindowRect(surface, out var rect))
            return false;

        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        if (width < 32 || height < 32) return false;

        bounds = new ViewerSurfaceBounds(rect.Left, rect.Top, width, height);
        return true;
    }

    public bool TryGetLocalPointer(Guid machineId, out string surface, out double x, out double y)
    {
        surface = "screen1";
        x = 0;
        y = 0;

        if (!GetCursorPos(out var point)) return false;

        if (TryGetViewerSurfaceBounds(machineId, virtualDisplay: true, out var screen2) && screen2.Contains(point.X, point.Y))
        {
            surface = "screen2";
            x = Math.Clamp((point.X - screen2.Left) / (double)screen2.Width, 0, 1);
            y = Math.Clamp((point.Y - screen2.Top) / (double)screen2.Height, 0, 1);
            return true;
        }

        if (TryGetViewerSurfaceBounds(machineId, virtualDisplay: false, out var screen1) && screen1.Contains(point.X, point.Y))
        {
            surface = "screen1";
            x = Math.Clamp((point.X - screen1.Left) / (double)screen1.Width, 0, 1);
            y = Math.Clamp((point.Y - screen1.Top) / (double)screen1.Height, 0, 1);
            return true;
        }

        return false;
    }

    public void SetViewOnly(Guid machineId, bool viewOnly)
    {
        if (TryGetSession(machineId, out var primary) && primary is not null)
            SetProcessViewOnly(primary, viewOnly);

        if (TryGetVirtualSession(machineId, out var secondary) && secondary is not null)
            SetProcessViewOnly(secondary, viewOnly);
    }

    public void SetScale(Guid machineId, int percent)
    {
        percent = Math.Clamp(percent, 10, 300);

        if (TryGetSession(machineId, out var primary) && primary is not null)
            SetProcessScale(primary, percent);

        if (TryGetVirtualSession(machineId, out var secondary) && secondary is not null)
            SetProcessScale(secondary, percent);
    }

    public void FitToWindow(Guid machineId)
    {
        if (TryGetSession(machineId, out var primary) && primary is not null)
            SendViewerMessage(primary, TB_WM_FITSCREEN, IntPtr.Zero, IntPtr.Zero);

        if (TryGetVirtualSession(machineId, out var secondary) && secondary is not null)
            SendViewerMessage(secondary, TB_WM_FITSCREEN, IntPtr.Zero, IntPtr.Zero);
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

    private Process StartViewer(Machine machine, AppSettings settings, string? configPath, bool startViewOnly)
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
        if (startViewOnly) psi.ArgumentList.Add("-viewonly");

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

    private static string CreateSecondaryViewerConfig(Guid machineId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GrevUltraVNC", "VirtualDisplays");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{machineId:N}-screen2.vnc");

        // Screen 2 already exists as a real Windows display owned by Grev Agent. Do not let
        // viewer 2 request ChangeServerRes/ExtendDisplay again; that was the source of duplicate
        // Screen 1 sessions and unstable topology.
        var content = """
                      [options]
                      shared=1
                      viewonly=1
                      showtoolbar=1
                      fullscreen=0
                      AutoScaling=1
                      directx=0
                      allowMonitorSpanning=0
                      ChangeServerRes=0
                      extendDisplay=0
                      showExtend=0
                      use_virt=0
                      useAllMonitors=0
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

    private static bool TryGetWindowHandle(Dictionary<Guid, Process> sessions, Guid machineId, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryGetTrackedSession(sessions, machineId, out var process) || process is null)
            return false;

        handle = FindPrimaryViewerWindow(process);
        return handle != IntPtr.Zero;
    }

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

    private static void SetProcessViewOnly(Process process, bool viewOnly)
    {
        var value = viewOnly ? new IntPtr(1) : IntPtr.Zero;
        SendViewerMessage(process, WM_SETVIEWONLY, value, IntPtr.Zero);
    }

    private static void SetProcessScale(Process process, int percent) =>
        SendViewerMessage(process, WM_SETSCALING, new IntPtr(percent), new IntPtr(100));

    private static void SendMonitorSelection(Process process, int monitorIndex)
    {
        var main = FindPrimaryViewerWindow(process);
        if (main == IntPtr.Zero) return;

        var monitorPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(monitorPointer, monitorIndex);
            var data = new COPYDATASTRUCT
            {
                dwData = new UIntPtr(2),
                cbData = sizeof(int),
                lpData = monitorPointer
            };
            SendMessage(main, WM_COPYDATA, IntPtr.Zero, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(monitorPointer);
        }
    }

    private static void SendViewerMessage(Process process, uint message, IntPtr wParam, IntPtr lParam)
    {
        var main = FindPrimaryViewerWindow(process);
        if (main != IntPtr.Zero)
            PostMessage(main, message, wParam, lParam);

        var surface = FindViewerContentWindow(process);
        if (surface != IntPtr.Zero && surface != main)
            PostMessage(surface, message, wParam, lParam);
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

    private static IntPtr FindViewerContentWindow(Process process)
    {
        var main = FindPrimaryViewerWindow(process);
        if (main == IntPtr.Zero) return IntPtr.Zero;

        var best = IntPtr.Zero;
        long bestArea = 0;
        EnumChildWindows(main, (window, _) =>
        {
            if (!IsWindowVisible(window)) return true;

            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (!string.Equals(className.ToString(), "VNCviewerwindow", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!GetWindowRect(window, out var rect)) return true;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            var area = (long)width * height;
            if (area <= bestArea) return true;

            bestArea = area;
            best = window;
            return true;
        }, IntPtr.Zero);

        return best != IntPtr.Zero ? best : main;
    }

    // UltraVNC creates transient top-level toolbar/tooltip windows. Resolve the largest real
    // top-level viewer instead of Process.MainWindowHandle so Grev's companion panel stays docked.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public UIntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
    private static extern bool GetCursorPos(out POINT lpPoint);

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
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_USER = 0x0400;
    private const uint WM_SETSCALING = WM_USER + 101;
    private const uint WM_SETVIEWONLY = WM_USER + 102;
    private const uint TB_WM_FITSCREEN = WM_USER + 1003;
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
