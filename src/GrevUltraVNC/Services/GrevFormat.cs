namespace GrevUltraVNC.Services;

/// <summary>
/// One place for every human-readable size/time/percentage string in GrevUltraVNC.
/// The dashboard, the control panel, the manage window and the remote file manager
/// all used to carry their own private copies of these helpers.
/// </summary>
public static class GrevFormat
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Compact binary size, e.g. <c>14.6 GB</c>.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < ByteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {ByteUnits[unit]}";
    }

    /// <summary>Gigabytes only, for side-by-side "used / total" memory readouts.</summary>
    public static string Gigabytes(long bytes) =>
        $"{Math.Max(0, bytes) / 1024d / 1024d / 1024d:0.#} GB";

    /// <summary>Very short gigabyte form for dense cards, e.g. <c>14.6G</c>.</summary>
    public static string ShortGigabytes(long bytes) =>
        $"{Math.Max(0, bytes) / 1024d / 1024d / 1024d:0.#}G";

    /// <summary>Uptime as the two most significant units, e.g. <c>3d 4h</c>.</summary>
    public static string Uptime(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m";
        return $"{span.Seconds}s";
    }

    /// <summary>Accumulated process CPU time.</summary>
    public static string CpuTime(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    /// <summary>One-decimal percentage, e.g. <c>37.4%</c>.</summary>
    public static string Percent(double value) => $"{value:0.#}%";

    /// <summary>Used share of a total, clamped to 0-100. Returns 0 when the total is unknown.</summary>
    public static double UsedPercent(long used, long total) =>
        total <= 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);

    /// <summary>"Just now" / "4m ago" / "17:04:11" style relative timestamps.</summary>
    public static string Since(DateTime? localTimestamp)
    {
        if (localTimestamp is null) return "never";

        var elapsed = DateTime.Now - localTimestamp.Value;
        if (elapsed < TimeSpan.Zero) return "just now";
        if (elapsed.TotalSeconds < 10) return "just now";
        if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return localTimestamp.Value.ToString("dd MMM HH:mm");
    }
}
