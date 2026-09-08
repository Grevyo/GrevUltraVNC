namespace GrevUltraVNC.Services;

/// <summary>
/// Stores per-machine UltraVNC passwords in Windows Credential Manager rather than
/// alongside the machine list in JSON.
/// </summary>
public sealed class VncCredentialService
{
    private readonly WindowsCredentialStore _store =
        new("GrevUltraVNC/VNC", "UltraVNC", "VNC password");

    public void Save(Guid machineId, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            Delete(machineId);
            return;
        }

        _store.Save(machineId, password);
    }

    public bool TryRead(Guid machineId, out string password) => _store.TryRead(machineId, out password);

    public bool HasSavedPassword(Guid machineId) => TryRead(machineId, out _);

    public void Delete(Guid machineId) => _store.Delete(machineId);
}
