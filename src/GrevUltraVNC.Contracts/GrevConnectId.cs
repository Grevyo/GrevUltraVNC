namespace GrevUltraVNC.Contracts;

public static class GrevConnectId
{
    public const string Prefix = "GC-";
    public const int MaxLength = 52;

    public static string CreateDefault(string? machineName)
    {
        var suffix = SanitizeSuffix(machineName);
        if (string.IsNullOrWhiteSpace(suffix))
            suffix = "PC";
        return Prefix + suffix;
    }

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a Grev Connect ID such as GC-GrevoServer.";
            return false;
        }

        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            text = Prefix + text;

        var suffix = text[Prefix.Length..];
        if (suffix.Length is < 1 or > MaxLength - Prefix.Length)
        {
            error = $"The part after {Prefix} must be 1-{MaxLength - Prefix.Length} characters.";
            return false;
        }

        if (suffix[0] == '-' || suffix[^1] == '-')
        {
            error = "A Grev Connect ID cannot start or end its name with a hyphen.";
            return false;
        }

        if (suffix.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            error = "Use only letters, numbers and hyphens in a Grev Connect ID.";
            return false;
        }

        normalized = Prefix + suffix;
        return true;
    }

    public static bool Equals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string SanitizeSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var characters = value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character == '-')
            .Take(MaxLength - Prefix.Length)
            .ToArray();

        return new string(characters).Trim('-');
    }
}
