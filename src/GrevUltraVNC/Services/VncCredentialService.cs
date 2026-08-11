using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevUltraVNC.Services;

public sealed class VncCredentialService
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public void Save(Guid machineId, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            Delete(machineId);
            return;
        }

        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);

            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName(machineId),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "UltraVNC"
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager could not save the VNC password.");
        }
        finally
        {
            if (bytes.Length > 0)
                Array.Clear(bytes, 0, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public bool TryRead(Guid machineId, out string password)
    {
        password = string.Empty;

        if (!CredRead(TargetName(machineId), CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return false;

            throw new Win32Exception(error, "Windows Credential Manager could not read the VNC password.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return false;

            password = Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / 2)) ?? string.Empty;

            return password.Length > 0;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public bool HasSavedPassword(Guid machineId) => TryRead(machineId, out _);

    public void Delete(Guid machineId)
    {
        if (CredDelete(TargetName(machineId), CredTypeGeneric, 0))
            return;

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error, "Windows Credential Manager could not delete the VNC password.");
    }

    private static string TargetName(Guid machineId) => $"GrevUltraVNC/VNC/{machineId:N}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
