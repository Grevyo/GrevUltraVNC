namespace GrevUltraVNC.Models;

public sealed class AppSettings
{
    public string UltraVncViewerPath { get; set; } = string.Empty;
    public bool AutoScaling { get; set; } = true;
    public bool FullScreenByDefault { get; set; }
    public int StatusCheckSeconds { get; set; } = 10;
    public string Theme { get; set; } = "Dark";
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string GrevName { get; set; } = Environment.UserName;
    public string ControllerId { get; set; } = string.Empty;
}
