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
            RuleKind.AppTitle => NormalizeAppTitle(value),
            _ => value,
        };
    }

    /// <summary>Pattern of an <see cref="RuleKind.AppTitle"/> rule; empty when the process or the keyword is empty.</summary>
    public static string AppTitle(string? process, string? keyword) => Normalize(RuleKind.AppTitle, $"{process}/{keyword}");

    /// <summary>Process and keyword of an <see cref="RuleKind.AppTitle"/> pattern, or null when it is not one.</summary>
    public static (string Process, string Keyword)? SplitAppTitle(string? pattern)
    {
        var value = NormalizeAppTitle((pattern ?? "").Trim().ToLowerInvariant());
        if (value.Length == 0)
        {
            return null;
        }

        var slash = value.IndexOf('/');
        return (value[..slash], value[(slash + 1)..]);
    }

    /// <summary>Process names never contain "/", so the first one separates the process from the keyword.</summary>
    private static string NormalizeAppTitle(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0)
        {
            return "";
        }

        var process = Normalize(RuleKind.Process, value[..slash]);
        var keyword = value[(slash + 1)..].Trim();
        return process.Length == 0 || keyword.Length == 0 ? "" : $"{process}/{keyword}";
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