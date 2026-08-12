namespace GrevUltraVNC.Services;

public static class AppServices
{
    public static UltraVncSessionService Vnc { get; } = new();
}
