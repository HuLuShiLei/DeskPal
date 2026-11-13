using System.Text.Json;
using DeskPal.Client.Core.Ui;
using DeskPal.Shared;

namespace DeskPal.Client.Core.Updates;

/// <summary>
/// Compares the changelog shipped with this build with the items already shown on this computer.
/// Without a record (first install, or the first version that has this feature) it only records what is there,
/// so nobody gets a list of old changes. The dialog text is chosen when the panel asks, so it follows the current language.
/// </summary>
public sealed class WhatsNewStore
{
    private readonly string _seenPath;
    private readonly string _version;
    private readonly string? _previousVersion;
    private readonly IReadOnlyList<ChangelogSection> _sections;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private bool _show;

    /// <param name="changelog">Contents of CHANGELOG.md; null when the file is missing (e.g. dotnet run without it).</param>
    public WhatsNewStore(string? changelog, string seenPath, string version)
    {
        _seenPath = seenPath;
        _version = version;
        _sections = changelog is null ? [] : Changelog.Parse(changelog);
        if (_sections.Count == 0)
        {
            return;
        }

        if (Load() is not { } record)
        {
            Save();
            return;
        }

        _seen.UnionWith(record.Items ?? []);
        _previousVersion = record.Version;
        _show = Changelog.WhatsNew(_sections, _seen, record.Version, version) is not null;
        if (!_show && record.Version != version)
        {
            Save();
        }
    }

    public WhatsNewView? Pending
    {
        get
        {
            lock (_lock)
            {
                return _show ? Changelog.WhatsNew(_sections, _seen, _previousVersion, _version) : null;
            }
        }
    }

    /// <summary>The dialog was closed: remember every item of this build.</summary>
    public void Acknowledge()
    {
        lock (_lock)
        {
            if (!_show)
            {
                return;
            }

            _show = false;
            Save();
        }
    }

    private SeenFile? Load()
    {
        try
        {
            return File.Exists(_seenPath) ? JsonSerializer.Deserialize<SeenFile>(File.ReadAllText(_seenPath), DeskPalJson.Options) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save()
    {
        _seen.UnionWith(Changelog.AllItems(_sections));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_seenPath)!);
            File.WriteAllText(_seenPath, JsonSerializer.Serialize(new SeenFile(_version, [.. _seen.Order(StringComparer.Ordinal)]), DeskPalJson.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record SeenFile(string? Version, IReadOnlyList<string>? Items);
}
