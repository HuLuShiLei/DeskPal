using System.Text.RegularExpressions;

namespace DeskPal.Client.Core.Presence;

/// <summary>
/// Removes the parts of a window title that change without the content changing: unread counters such as "(12) "
/// in front or " – (255549)" at the end, the tab state a browser adds at the end such as " - 正在播放音频" or
/// " - 内存用量高 - 917 MB", and invisible direction marks. Otherwise every new message starts a new segment.
/// </summary>
public static partial class TitleText
{
    public static string? Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var text = DirectionMarks().Replace(title, "");
        text = LeadingCounter().Replace(text, "");
        text = TrailingCounter().Replace(text, "");
        text = TrailingTabState().Replace(text, "");
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("[\u200e\u200f\u202a-\u202e\u2066-\u2069]")]
    private static partial Regex DirectionMarks();

    [GeneratedRegex(@"^\s*[(\[]\d+\+?[)\]]\s+")]
    private static partial Regex LeadingCounter();

    [GeneratedRegex(@"\s*[-–—]\s*\(\d+\)\s*$")]
    private static partial Regex TrailingCounter();

    [GeneratedRegex(@"(?:\s+[-–—]\s+(?:正在播放音频|音频已静音|已静音|Audio playing|Audio muted|(?:内存用量高|内存使用量高|内存用量|内存使用量|記憶體用量高|記憶體用量|High memory usage|Memory usage)\s+[-–—]\s+\d[\d.,]*\s*[KMG]B))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTabState();
}
