namespace DeskPal.Client.Native;

/// <summary>
/// Finds the name an application shows in the launcher from its .desktop file, by application id, window class
/// or executable name. Read once, on first use.
/// </summary>
internal static class DesktopEntries
{
    private static readonly Lazy<Dictionary<string, Entry>> Index = new(Load);

    private sealed record Entry(string Name, string? LocalName);

    /// <summary>null when no launcher entry matches any of the keys.</summary>
    public static string? NameFor(params string?[] keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var id = key.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? key[..^8] : key;
            if (Index.Value.TryGetValue(id, out var entry))
            {
                return UiLanguage.Current == "zh-CN" && entry.LocalName is { Length: > 0 } local ? local : entry.Name;
            }
        }

        return null;
    }

    private static Dictionary<string, Entry> Load()
    {
        var index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } custom ? custom : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);
        string[] roots =
        [
            dataHome,
            .. dataDirs,
            "/var/lib/flatpak/exports/share",
            Path.Combine(dataHome, "flatpak", "exports", "share"),
            "/var/lib/snapd/desktop",
        ];
        foreach (var root in roots.Distinct())
        {
            var folder = Path.Combine(root, "applications");
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*.desktop", SearchOption.AllDirectories))
                {
                    Add(index, file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return index;
    }

    /// <summary>Earlier folders win, the same order the desktop uses.</summary>
    private static void Add(Dictionary<string, Entry> index, string file)
    {
        string? name = null, zhCn = null, zh = null, wmClass = null, exec = null;
        var inEntry = false;
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    if (inEntry)
                    {
                        break;
                    }

                    inEntry = line == "[Desktop Entry]";
                    continue;
                }

                var equals = line.IndexOf('=');
                if (!inEntry || equals <= 0)
                {
                    continue;
                }

                var value = line[(equals + 1)..].Trim();
                switch (line[..equals].Trim())
                {
                    case "Name": name = value; break;
                    case "Name[zh_CN]": zhCn = value; break;
                    case "Name[zh]": zh = value; break;
                    case "StartupWMClass": wmClass = value; break;
                    case "Exec": exec = value; break;
                    case "NoDisplay" when value == "true": return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var entry = new Entry(name, zhCn ?? zh);
        index.TryAdd(Path.GetFileNameWithoutExtension(file), entry);
        if (!string.IsNullOrEmpty(wmClass))
        {
            index.TryAdd(wmClass, entry);
        }

        // "env VAR=1 /usr/bin/code --flag %F" → code
        var program = exec?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(part => part != "env" && !part.Contains('='));
        if (!string.IsNullOrEmpty(program))
        {
            index.TryAdd(Path.GetFileName(program.Trim('"')), entry);
        }
    }
}
