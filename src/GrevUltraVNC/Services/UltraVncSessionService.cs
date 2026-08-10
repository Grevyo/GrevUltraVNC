using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed class UltraVncSessionService
{
    private readonly Dictionary<Guid, Process> _sessions = [];

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

        var viewer = FindViewer(settings.UltraVncViewerPath)
            ?? throw new FileNotFoundException("UltraVNC Viewer was not found. Set its path in Settings.");

        var psi = new ProcessStartInfo
        {
            FileName = viewer,
            UseShellExecute = true
        };
        psi.ArgumentList.Add("-connect");
        psi.ArgumentList.Add($"{machine.IpAddress}::{machine.VncPort}");
        if (settings.AutoScaling) psi.ArgumentList.Add("-autoscaling");
        if (settings.FullScreenByDefault) psi.ArgumentList.Add("-fullscreen");

        var process = Process.Start(psi) ?? throw new InvalidOperationException("UltraVNC Viewer did not start.");
        _sessions[machine.Id] = process;
        return process;
    }

    public bool HasActiveSession(Guid machineId) => TryGetSession(machineId, out _);

    public bool TryGetViewerWindowHandle(Guid machineId, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryGetSession(machineId, out var process) || process is null)
            return false;

        process.Refresh();
        handle = process.MainWindowHandle;
        return handle != IntPtr.Zero;
    }

    public void BringViewerToFront(Guid machineId) => FocusViewer(GetSession(machineId));

    public void Disconnect(Guid machineId)
    {
        if (!TryGetSession(machineId, out var process) || process is null)
            return;

        try
        {
            if (!process.CloseMainWindow())
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        finally
        {
            _sessions.Remove(machineId);
        }
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

    private Process GetSession(Guid machineId) =>
        TryGetSession(machineId, out var process)
            ? process!
            : throw new InvalidOperationException("Open a VNC session to this machine first.");

    private bool TryGetSession(Guid machineId, out Process? process)
    {
        process = null;
        if (!_sessions.TryGetValue(machineId, out var candidate)) return false;

        try
        {
            if (candidate.HasExited)
            {
                _sessions.Remove(machineId);
                return false;
            }
        }
        catch
        {
            _sessions.Remove(machineId);
            return false;
        }

        process = candidate;
        return true;
    }

    private static void SendRemoteChordWithScrollLock(Process process, params byte[] keys)
    {
        var scrollWasOn = (GetKeyState(VK_SCROLL) & 1) != 0;
        if (!scrollWasOn) TapKey(VK_SCROLL);
        FocusViewer(process);
        SendChord(keys);
        if (!scrollWasOn) TapKey(VK_SCROLL);
    }

    private static void FocusViewer(Process process)
    {
        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            try
            {
                process.WaitForInputIdle(3000);
            }
            catch
            {
                // Viewer may still be starting; refresh below and report a clean error if no window exists yet.
            }

            process.Refresh();
            handle = process.MainWindowHandle;
        }

        if (handle == IntPtr.Zero) throw new InvalidOperationException("Could not locate the UltraVNC Viewer window yet.");
        ShowWindow(handle, SW_RESTORE);
        SetForegroundWindow(handle);
        Thread.Sleep(120);
    }

    private static void SendChord(params byte[] keys)
    {
        foreach (var key in keys) KeyDown(key);
        for (var i = keys.Length - 1; i >= 0; i--) KeyUp(keys[i]);
    }

    private static void TapKey(byte key)
    {
        KeyDown(key);
        KeyUp(key);
        Thread.Sleep(60);
    }

    private static void KeyDown(byte key) => keybd_event(key, 0, 0, UIntPtr.Zero);
    private static void KeyUp(byte key) => keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_SCROLL = 0x91;
    private const byte VK_F4 = 0x73;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
