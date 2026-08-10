namespace GrevUltraVNC.Models;

public sealed class AppSettings
{
    public string UltraVncViewerPath { get; set; } = string.Empty;
    public bool AutoScaling { get; set; } = true;
    public bool FullScreenByDefault { get; set; }
    public int StatusCheckSeconds { get; set; } = 10;
}
