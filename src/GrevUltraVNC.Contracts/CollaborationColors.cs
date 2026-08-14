namespace GrevUltraVNC.Contracts;

public static class CollaborationColors
{
    public const string Default = "#32CFF0";

    public static readonly IReadOnlyList<string> Palette =
    [
        "#32CFF0", // Grev cyan
        "#8C7CFF", // Violet
        "#50DC91", // Green
        "#FFB84D", // Orange
        "#FF6B8A", // Pink
        "#5EA8FF", // Blue
        "#FFE066", // Yellow
        "#2DD4BF", // Teal
        "#FF4D5E", // Red
        "#A3E635", // Lime
        "#EC4899", // Magenta
        "#A855F7", // Purple
        "#6366F1", // Indigo
        "#38BDF8", // Sky
        "#F59E0B", // Amber
        "#6EE7B7"  // Mint
    ];

    public static string Normalize(string? value)
    {
        var text = value?.Trim();
        return Palette.FirstOrDefault(colour =>
                   string.Equals(colour, text, StringComparison.OrdinalIgnoreCase))
               ?? Default;
    }

    public static string PickAvailable(string? preferred, IEnumerable<string> coloursInUse)
    {
        var requested = Normalize(preferred);
        var used = new HashSet<string>(
            coloursInUse.Select(Normalize),
            StringComparer.OrdinalIgnoreCase);

        var start = Palette
            .Select((colour, index) => (colour, index))
            .First(item => string.Equals(item.colour, requested, StringComparison.OrdinalIgnoreCase))
            .index;

        for (var offset = 0; offset < Palette.Count; offset++)
        {
            var candidate = Palette[(start + offset) % Palette.Count];
            if (!used.Contains(candidate))
                return candidate;
        }

        // More than 16 simultaneous participants is unusual; if every palette colour is
        // occupied, reuse the user's preferred colour rather than inventing an unstable one.
        return requested;
    }
}
