using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Services;

/// <summary>
/// Stores per-machine Grev Agent pairing keys in Windows Credential Manager.
/// Unlike a VNC password, a pairing key has a fixed shape, so it is validated on
/// the way in and on the way back out.
/// </summary>
public sealed class AgentCredentialService
{
    private readonly WindowsCredentialStore _store =
        new("GrevUltraVNC/Agent", "GrevUltraVNC Agent", "Grev Agent pairing key");

    public void Save(Guid machineId, string sharedKey)
    {
        sharedKey = sharedKey.Trim();
        if (!AgentProtocol.IsValidSharedKey(sharedKey))
            throw new ArgumentException("The Grev Agent pairing key is not valid.", nameof(sharedKey));

        _store.Save(machineId, sharedKey);
    }

    public bool TryRead(Guid machineId, out string sharedKey)
    {
        sharedKey = string.Empty;
        if (!_store.TryRead(machineId, out var stored))
            return false;

        sharedKey = stored.Trim();
        return AgentProtocol.IsValidSharedKey(sharedKey);
    }

    public bool HasSavedKey(Guid machineId) => TryRead(machineId, out _);

    public void Delete(Guid machineId) => _store.Delete(machineId);
}
