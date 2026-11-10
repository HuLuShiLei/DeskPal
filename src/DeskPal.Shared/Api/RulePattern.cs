namespace DeskPal.Shared.Api;

public static class RulePattern
{
    public static string Normalize(RuleKind kind, string? pattern)
    {
        var value = (pattern ?? "").Trim().ToLowerInvariant();
        return kind switch
        {
            RuleKind.Process => value.EndsWith(".exe", StringComparison.Ordinal) ? value[..^4] : value,
            RuleKind.Domain => NormalizeHost(value),
            _ => value,
        };
    }

    /// <summary>Accepts "https://www.Example.com:8080/a?b" or "example.com/path" and returns "example.com".</summary>
    public static string NormalizeHost(string value)
    {
        value = value.Trim().ToLowerInvariant();
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            value = value[(schemeEnd + 3)..];
        }

        var end = value.IndexOfAny(['/', '?', '#', ':']);
        if (end >= 0)
        {
            value = value[..end];
        }

        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }
}