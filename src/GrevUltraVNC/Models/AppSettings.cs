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
    public string GrevName { get; set; } = "User";
    public string ControllerId { get; set; } = string.Empty;
    public string CollaborationColor { get; set; } = "#32CFF0";
    public string CursorStyle { get; set; } = "arrow";

    /// <summary>
    /// Comma-separated ids of Grev Control Panel sections the user has collapsed.
    /// Stored so the panel comes back the way it was left instead of re-expanding
    /// everything on every connection.
    /// </summary>
    public string ControlPanelCollapsedSections { get; set; } = string.Empty;
}
