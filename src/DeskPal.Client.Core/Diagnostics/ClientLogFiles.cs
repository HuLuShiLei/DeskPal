using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskPal.Client.Core.Diagnostics;

/// <summary>
/// Append-only client log under one directory. The active file is deskpal.log; when it reaches the size
/// limit it is renamed to deskpal-yyyyMMdd-HHmmss.log and a new file is started. Archives older than the
/// age limit, and archives beyond the kept count, are deleted. Lines are truncated and tokens are redacted.
/// Callers must not pass window titles, media titles, domains, manual text, poke text, or invite codes.
/// </summary>
public sealed partial class ClientLogFiles : IDisposable
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxArchiveFiles = 5;
    public const int MaxLineChars = 4000;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly int _maxFileBytes;
    private readonly int _maxArchiveFiles;
    private readonly TimeSpan _maxAge;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Lock _lock = new();
    private bool _pruned;

    public ClientLogFiles(
        string directory,
        TimeProvider? time = null,
        int maxFileBytes = MaxFileBytes,
        int maxArchiveFiles = MaxArchiveFiles,
        TimeSpan? maxAge = null)
    {
        _directory = directory;
        _time = time ?? TimeProvider.System;
        _maxFileBytes = maxFileBytes;
        _maxArchiveFiles = maxArchiveFiles;
        _maxAge = maxAge ?? MaxAge;
    }

    public string Directory => _directory;

    public string ActivePath => Path.Combine(_directory, "deskpal.log");

    public void Write(string level, string category, string message)
    {
        var line = Format(level, category, message) + "\n";
        var bytes = Utf8.GetByteCount(line);
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(_directory);
            if (!_pruned)
            {
                Prune();
                _pruned = true;
            }

            var length = File.Exists(ActivePath) ? new FileInfo(ActivePath).Length : 0;
            if (length > 0 && length + bytes > _maxFileBytes)
            {
                Rotate();
            }

            File.AppendAllText(ActivePath, line, Utf8);
        }
    }

    public void Dispose()
    {
    }

    private string Format(string level, string category, string message)
    {
        var stamp = _time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var text = message.Replace('\r', ' ').Replace('\n', ' ');
        text = TokenPattern().Replace(text, "$1=(redacted)");
        text = BearerPattern().Replace(text, "Bearer (redacted)");
        if (text.Length > MaxLineChars)
        {
            text = text[..MaxLineChars] + "…";
        }

        var name = string.IsNullOrWhiteSpace(category) ? "DeskPal" : category.Trim();
        return $"{stamp} {level} {name} {text}";
    }

    private void Rotate()
    {
        if (!File.Exists(ActivePath) || new FileInfo(ActivePath).Length == 0)
        {
            return;
        }

        var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(_directory, $"deskpal-{stamp}.log");
        var suffix = 1;
        while (File.Exists(target))
        {
            target = Path.Combine(_directory, $"deskpal-{stamp}-{suffix}.log");
            suffix++;
        }

        File.Move(ActivePath, target);
        Prune();
    }

    private void Prune()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var file in Archives().ToList())
        {
            if (now - file.LastWriteTimeUtc > _maxAge)
            {
                file.Delete();
            }
        }

        foreach (var file in Archives().OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name).Skip(_maxArchiveFiles))
        {
            file.Delete();
        }
    }

    private IEnumerable<FileInfo> Archives() =>
        System.IO.Directory.EnumerateFiles(_directory, "deskpal-*.log").Select(path => new FileInfo(path));

    [GeneratedRegex(@"(?i)(access_token|password|token)=([^&\s]+)")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)bearer\s+\S+")]
    private static partial Regex BearerPattern();
}
