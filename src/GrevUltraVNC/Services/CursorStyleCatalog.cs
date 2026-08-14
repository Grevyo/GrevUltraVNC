namespace GrevUltraVNC.Services;

public sealed record CursorStyleOption(string Id, string Name);

public static class CursorStyleCatalog
{
    public const string Arrow = "arrow";
    public const string Grev = "grev";
    public const string ChatGpt = "chatgpt";
    public const string Crosshair = "crosshair";
    public const string Ring = "ring";
    public const string Diamond = "diamond";
    public const string Pixel = "pixel";
    public const string SlimArrow = "slimarrow";
    public const string Chevron = "chevron";
    public const string Target = "target";
    public const string Square = "square";
    public const string Bolt = "bolt";
    public const string Hand = "hand";
    public const string Banana = "banana";
    public const string Fish = "fish";
    public const string Ghost = "ghost";
    public const string Crown = "crown";
    public const string Mug = "mug";

    public static IReadOnlyList<CursorStyleOption> Options { get; } = new[]
    {
        new CursorStyleOption(Arrow, "Classic arrow"),
        new CursorStyleOption(Grev, "Grev squiggle"),
        new CursorStyleOption(ChatGpt, "ChatGPT squiggle"),
        new CursorStyleOption(Crosshair, "Crosshair"),
        new CursorStyleOption(Ring, "Ring"),
        new CursorStyleOption(Diamond, "Diamond"),
        new CursorStyleOption(Pixel, "Pixel pointer"),
        new CursorStyleOption(SlimArrow, "Slim arrow"),
        new CursorStyleOption(Chevron, "Chevron"),
        new CursorStyleOption(Target, "Target"),
        new CursorStyleOption(Square, "Hollow square"),
        new CursorStyleOption(Bolt, "Bolt pointer"),
        new CursorStyleOption(Hand, "Hand pointer"),
        new CursorStyleOption(Banana, "Banana"),
        new CursorStyleOption(Fish, "Fish"),
        new CursorStyleOption(Ghost, "Ghost"),
        new CursorStyleOption(Crown, "Crown"),
        new CursorStyleOption(Mug, "Coffee mug")
    };

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Arrow or Grev or ChatGpt or Crosshair or Ring or Diamond or Pixel or
            SlimArrow or Chevron or Target or Square or Bolt or Hand or Banana or Fish or Ghost or Crown or Mug
            ? normalized
            : Arrow;
    }

    public static string DisplayName(string? value)
    {
        var normalized = Normalize(value);
        return Options.First(option => option.Id == normalized).Name;
    }
}
