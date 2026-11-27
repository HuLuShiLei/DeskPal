using System.Text;
using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Providers;

/// <summary>What a provider plugin said in one line of its output.</summary>
public abstract record ProviderMessage;

/// <summary>Optional first line: who the plugin is and what it supplies.</summary>
/// <param name="Provides">The capabilities the plugin supplies; null when the line does not say (then
/// <see cref="ProviderCapabilities.PluginDefault"/>). Names this version does not know are left out.</param>
/// <param name="Apps">Process names a detail plugin covers, normalized like <see cref="ProviderForeground.Process"/>;
/// null when the line does not say.</param>
/// <param name="Reads">What the plugin wants DeskPal to write to its standard input, from <see cref="ProviderReads"/>;
/// null when the line does not say. Names this version does not know are left out.</param>
public sealed record ProviderHello(
    string Name,
    string? Version,
    IReadOnlySet<ProviderCapability>? Provides = null,
    IReadOnlySet<string>? Apps = null,
    IReadOnlySet<string>? Reads = null) : ProviderMessage;

/// <summary>
/// What a plugin can ask to read, in the "reads" list of its hello line. DeskPal writes these to the plugin's standard
/// input, one JSON object per line; a plugin that asks for nothing gets a closed standard input.
/// </summary>
public static class ProviderReads
{
    /// <summary>The current room's members and their states, without any titles (<see cref="RoomFeed"/>).</summary>
    public const string Room = "room";

    public static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    public static bool TryParse(string? name, out string read)
    {
        read = name == Room ? Room : "";
        return read.Length > 0;
    }
}

/// <summary>The window the user is working in.</summary>
/// <param name="Process">Program name used by the rules: file name without folder and without ".exe".</param>
/// <param name="Name">Name shown to people; the process name is shown when missing.</param>
/// <param name="Path">Full path of the program, used to recognize game library folders.</param>
public sealed record ProviderForeground(string Process, string? Name, string Title, bool Fullscreen, string? Path) : ProviderMessage;

/// <summary>Host of the page in the foreground browser window; null when it cannot be read.</summary>
public sealed record ProviderDomain(string? Domain) : ProviderMessage;

/// <summary>Seconds since the last keyboard or mouse input. DeskPal compares it with the user's idle setting.</summary>
public sealed record ProviderIdle(double Seconds) : ProviderMessage;

/// <summary>Whether the screen is locked.</summary>
public sealed record ProviderLock(bool Locked) : ProviderMessage;

/// <summary>
/// A more specific title for the window of <paramref name="Process"/>, for example the open chat in a messenger.
/// <paramref name="Window"/>, when given, is the window title the plugin saw, so the detail only applies to that window.
/// A null <paramref name="Title"/> takes the plugin's detail back.
/// </summary>
public sealed record ProviderDetail(string Process, string? Window, string? Title) : ProviderMessage;

/// <summary>What is playing. <paramref name="Source"/> is the program name of the player, like a foreground process.</summary>
public sealed record ProviderMedia(bool Playing, string? Title, string? Artist, string? Source) : ProviderMessage;

/// <summary>One parsed line. Both null: nothing to do (blank line, or a message type this version does not know).</summary>
public readonly record struct ProviderParseResult(ProviderMessage? Message, string? Error);

/// <summary>
/// The line protocol between DeskPal and a plugin, version 1. A plugin is any program that writes one JSON
/// object per line to its standard output, for example
/// <c>{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"Inbox","fullscreen":false}</c>.
/// A plugin whose hello line asks to read something (<see cref="ProviderReads"/>) also gets lines on its standard
/// input, built by <see cref="RoomFeed"/>; every other plugin's standard input is closed.
/// Described for plugin authors in docs/provider-guide.md and checked by the skill's check_provider.py; keep the three
/// in step.
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
    public const int MaxApps = 32;

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
                "idle" => ParseIdle(root),
                "lock" => ParseLock(root),
                "media" => ParseMedia(root),
                "detail" => ParseDetail(root),
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

    /// <summary>The signal the built-in collectors would publish for the same playback.</summary>
    public static MediaSignal ToSignal(ProviderMedia media) => media.Playing
        ? new MediaSignal(true, media.Title, media.Artist, media.Source)
        : new MediaSignal(false, null, null, null);

    /// <summary>
    /// The host of a site as the rules compare it, or null when the value is not one: lower case, no "www.", no scheme,
    /// path or port. Accepts a full address too, though plugins should only pass the host.
    /// </summary>
    public static string? NormalizeDomain(string? raw)
    {
        var value = Clean(raw, MaxPathLength);
        if (value.Length == 0 || value.Contains(' '))
        {
            return null;
        }

        var host = RulePattern.NormalizeHost(value);
        var valid = host == "localhost" || (host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.'));
        return valid ? host : null;
    }

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

        return new ProviderParseResult(new ProviderDomain(NormalizeDomain(raw)), null);
    }

    private static ProviderParseResult ParseIdle(JsonElement root)
    {
        if (!root.TryGetProperty("seconds", out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var seconds) || seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return Fail("idle needs \"seconds\" (a number of seconds since the last input, 0 or more)");
        }

        return new ProviderParseResult(new ProviderIdle(seconds), null);
    }

    private static ProviderParseResult ParseLock(JsonElement root)
    {
        if (!root.TryGetProperty("locked", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Fail("lock needs \"locked\" (true or false)");
        }

        return new ProviderParseResult(new ProviderLock(value.GetBoolean()), null);
    }

    private static ProviderParseResult ParseMedia(JsonElement root)
    {
        if (!root.TryGetProperty("playing", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Fail("media needs \"playing\" (true or false)");
        }

        if (!TryString(root, "title", out var title) || !TryString(root, "artist", out var artist) || !TryString(root, "source", out var source))
        {
            return Fail("\"title\", \"artist\" and \"source\" must be strings");
        }

        var playing = value.GetBoolean();
        var cleanTitle = Clean(title, MaxTitleLength);
        var cleanArtist = Clean(artist, MaxTitleLength);
        var cleanSource = NormalizeProcess(source);
        return new ProviderParseResult(
            new ProviderMedia(
                playing,
                playing && cleanTitle.Length > 0 ? cleanTitle : null,
                playing && cleanArtist.Length > 0 ? cleanArtist : null,
                playing && cleanSource.Length > 0 ? cleanSource : null),
            null);
    }

    private static ProviderParseResult ParseDetail(JsonElement root)
    {
        if (!TryString(root, "process", out var rawProcess) || rawProcess is null)
        {
            return Fail("detail needs \"process\" (a string)");
        }

        var process = NormalizeProcess(rawProcess);
        if (process.Length == 0)
        {
            return Fail("detail \"process\" is empty");
        }

        if (!TryString(root, "title", out var title) || !TryString(root, "window", out var window))
        {
            return Fail("\"title\" and \"window\" must be strings or null");
        }

        var cleanTitle = Clean(title, MaxTitleLength);
        var cleanWindow = Clean(window, MaxTitleLength);
        return new ProviderParseResult(
            new ProviderDetail(process, cleanWindow.Length == 0 ? null : cleanWindow, cleanTitle.Length == 0 ? null : cleanTitle),
            null);
    }

    private static ProviderParseResult ParseHello(JsonElement root)
    {
        if (!TryString(root, "name", out var name) || !TryString(root, "version", out var version))
        {
            return Fail("\"name\" and \"version\" must be strings");
        }

        var cleanName = Clean(name, MaxNameLength);
        var cleanVersion = Clean(version, MaxNameLength);
        if (cleanName.Length == 0)
        {
            return Fail("hello needs \"name\"");
        }

        HashSet<ProviderCapability>? provides = null;
        if (root.TryGetProperty("provides", out var list) && list.ValueKind != JsonValueKind.Null)
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                return Fail("\"provides\" must be a list of names, like [\"foreground\", \"domain\"]");
            }

            provides = [];
            foreach (var item in list.EnumerateArray())
            {
                // Names a later version adds are skipped, so a newer plugin still works with this DeskPal.
                if (item.ValueKind == JsonValueKind.String && ProviderCapabilities.TryParse(item.GetString(), out var capability))
                {
                    provides.Add(capability);
                }
            }
        }

        HashSet<string>? apps = null;
        if (root.TryGetProperty("apps", out var appList) && appList.ValueKind != JsonValueKind.Null)
        {
            if (appList.ValueKind != JsonValueKind.Array)
            {
                return Fail("\"apps\" must be a list of process names, like [\"QQ\"]");
            }

            apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in appList.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && NormalizeProcess(item.GetString()) is { Length: > 0 } app && apps.Count < MaxApps)
                {
                    apps.Add(app);
                }
            }
        }

        HashSet<string>? reads = null;
        if (root.TryGetProperty("reads", out var readList) && readList.ValueKind != JsonValueKind.Null)
        {
            if (readList.ValueKind != JsonValueKind.Array)
            {
                return Fail("\"reads\" must be a list of names, like [\"room\"]");
            }

            reads = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in readList.EnumerateArray())
            {
                // Like "provides": names a later version adds are skipped.
                if (item.ValueKind == JsonValueKind.String && ProviderReads.TryParse(item.GetString(), out var read))
                {
                    reads.Add(read);
                }
            }
        }

        return new ProviderParseResult(new ProviderHello(cleanName, cleanVersion.Length == 0 ? null : cleanVersion, provides, apps, reads), null);
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
