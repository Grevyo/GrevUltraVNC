namespace GrevUltraVNC.Models;

public enum GrevAgentState
{
    Unknown,
    NotDetected,
    ReadyToPair,
    Connected,
    AuthenticationFailed,
    Error
}
