namespace GrevUltraVNC.Services;

public sealed record CursorStyleOption(string Id, string Name);

public static class CursorStyleCatalog
{
    public const string Grev = "grev";
    public const string Arrow = "arrow";
    public const string Crosshair = "crosshair";
    public const string Ring = "ring";
    public const string Diamond = "diamond";
    public const string Pixel = "pixel";

    public static IReadOnlyList<CursorStyleOption> Options { get; } = new[]
    {
        new CursorStyleOption(Grev, "Grev squiggle"),
        new CursorStyleOption(Arrow, "Classic arrow"),
        new CursorStyleOption(Crosshair, "Crosshair"),
        new CursorStyleOption(Ring, "Ring"),
        new CursorStyleOption(Diamond, "Diamond"),
        new CursorStyleOption(Pixel, "Pixel pointer")
    };

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Grev or Arrow or Crosshair or Ring or Diamond or Pixel
            ? normalized
            : Grev;
    }

    public static string DisplayName(string? value)
    {
        var normalized = Normalize(value);
        return Options.First(option => option.Id == normalized).Name;
    }
}
