using System.Text;
using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Providers;

/// <summary>What a provider plugin said in one line of its output.</summary>
public abstract record ProviderMessage;

/// <summary>Optional first line: who the plugin is. Only written to the log.</summary>
public sealed record ProviderHello(string Name, string? Version) : ProviderMessage;

/// <summary>The window the user is working in.</summary>
/// <param name="Process">Program name used by the rules: file name without folder and without ".exe".</param>
/// <param name="Name">Name shown to people; the process name is shown when missing.</param>
/// <param name="Path">Full path of the program, used to recognize game library folders.</param>
public sealed record ProviderForeground(string Process, string? Name, string Title, bool Fullscreen, string? Path) : ProviderMessage;

/// <summary>Host of the page in the foreground browser window; null when it cannot be read.</summary>
public sealed record ProviderDomain(string? Domain) : ProviderMessage;

/// <summary>One parsed line. Both null: nothing to do (blank line, or a message type this version does not know).</summary>
public readonly record struct ProviderParseResult(ProviderMessage? Message, string? Error);

/// <summary>
/// The line protocol between DeskPal and a provider plugin, version 1. A plugin is any program that writes one JSON
/// object per line to its standard output, for example
/// <c>{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"Inbox","fullscreen":false}</c>.
/// Described for plugin authors in docs/provider-guide.md; keep the two in step.
/// A plugin can only state these facts. Classification, the no-title rules and the privacy filter run afterwards,
/// the same as for the built-in collectors.
/// </summary>
public static class ProviderProtocol
{
    public const int Version = 1;
    public const int MaxLineLength = 8192;
    public const int MaxTitleLength = 512;
    public const int MaxNameLength = 128;
    public const int MaxPathLength = 1024;

    public static ProviderParseResult Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return default;
        }

        if (line.Length > MaxLineLength)
        {
            return Fail($"line longer than {MaxLineLength} characters");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Fail("not JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("not a JSON object");
            }

            if (root.TryGetProperty("v", out var v) && (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != Version))
            {
                return Fail($"unsupported protocol version (this DeskPal understands v {Version})");
            }

            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                return Fail("missing \"type\"");
            }

            return type.GetString() switch
            {
                "foreground" => ParseForeground(root),
                "domain" => ParseDomain(root),
                "hello" => ParseHello(root),
                _ => default,
            };
        }
    }

    /// <summary>The signal the built-in collectors would publish for the same window.</summary>
    public static ForegroundSignal ToSignal(ProviderForeground foreground) => new(
        foreground.Process,
        foreground.Path,
        string.IsNullOrEmpty(foreground.Name) ? foreground.Process : foreground.Name,
        foreground.Title,
        foreground.Fullscreen,
        Browsers.IsBrowser(foreground.Process));

    /// <summary>Folder and ".exe" removed, so a plugin may pass a full path or a Windows file name.</summary>
    public static string NormalizeProcess(string? value)
    {
        var name = Clean(value, MaxPathLength);
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
    }

    private static ProviderParseResult ParseForeground(JsonElement root)
    {
        if (!TryString(root, "process", out var rawProcess) || rawProcess is null)
        {
            return Fail("foreground needs \"process\" (a string)");
        }

        var process = NormalizeProcess(rawProcess);
        if (process.Length == 0)
        {
            return Fail("foreground \"process\" is empty");
        }

        if (!TryString(root, "name", out var name) || !TryString(root, "title", out var title) || !TryString(root, "path", out var path))
        {
            return Fail("\"name\", \"title\" and \"path\" must be strings");
        }

        var fullscreen = false;
        if (root.TryGetProperty("fullscreen", out var value))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Fail("\"fullscreen\" must be true or false");
            }

            fullscreen = value.GetBoolean();
        }

        var cleanName = Clean(name, MaxNameLength);
        var cleanPath = Clean(path, MaxPathLength);
        return new ProviderParseResult(
            new ProviderForeground(
                process,
                cleanName.Length == 0 ? null : cleanName,
                Clean(title, MaxTitleLength),
                fullscreen,
                cleanPath.Length == 0 ? null : cleanPath),
            null);
    }

    private static ProviderParseResult ParseDomain(JsonElement root)
    {
        if (!TryString(root, "domain", out var raw))
        {
            return Fail("\"domain\" must be a string or null");
        }

        var value = Clean(raw, MaxPathLength);
        if (value.Length == 0 || value.Contains(' '))
        {
            return new ProviderParseResult(new ProviderDomain(null), null);
        }

        var host = RulePattern.NormalizeHost(value);
        var valid = host == "localhost" || (host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.'));
        return new ProviderParseResult(new ProviderDomain(valid ? host : null), null);
    }

    private static ProviderParseResult ParseHello(JsonElement root)
    {
        if (!TryString(root, "name", out var name) || !TryString(root, "version", out var version))
        {
            return Fail("\"name\" and \"version\" must be strings");
        }

        var cleanName = Clean(name, MaxNameLength);
        var cleanVersion = Clean(version, MaxNameLength);
        return cleanName.Length == 0
            ? Fail("hello needs \"name\"")
            : new ProviderParseResult(new ProviderHello(cleanName, cleanVersion.Length == 0 ? null : cleanVersion), null);
    }

    /// <summary>False when the property is there but is neither a string nor null.</summary>
    private static bool TryString(JsonElement root, string property, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    /// <summary>Control characters become spaces, the ends are trimmed, and the text is cut to <paramref name="max"/>.</summary>
    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(Math.Min(value.Length, max));
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var text = builder.ToString().Trim();
        if (text.Length <= max)
        {
            return text;
        }

        // Do not cut a surrogate pair in half.
        var cut = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..cut].TrimEnd();
    }

    private static ProviderParseResult Fail(string error) => new(null, error);
}
