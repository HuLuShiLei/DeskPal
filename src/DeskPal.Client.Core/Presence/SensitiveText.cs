using System.Text.RegularExpressions;

namespace DeskPal.Client.Core.Presence;

/// <summary>
/// Removes details people show in a title without meaning to share them: IP addresses (with the address around them),
/// phone numbers, e-mail addresses, ID card numbers, numbers of 7 or more digits such as remote desktop IDs, and the
/// value after a label such as 验证码 or 密码. Every title goes through it before it is shown, sent or recorded; a website
/// opened by IP address is named <see cref="IpAddressName"/>. Classification still sees the original title.
/// </summary>
public static partial class SensitiveText
{
    /// <summary>Name sent and recorded for a website opened by IP address. Translated where it is shown.</summary>
    public const string IpAddressName = "IP 地址";

    /// <summary>The text without sensitive parts, and without the brackets and separators they leave behind. May be empty.</summary>
    public static string Redact(string text)
    {
        var redacted = Labeled().Replace(text, "");
        redacted = Ipv4().Replace(redacted, "");
        redacted = Ipv6().Replace(redacted, "");
        redacted = Email().Replace(redacted, "");
        redacted = IdCard().Replace(redacted, "");
        redacted = Landline().Replace(redacted, "");
        redacted = Mobile().Replace(redacted, "");
        redacted = Digits().Replace(redacted, LongNumber);
        return redacted == text ? text : Tidy(redacted);
    }

    /// <summary>A title with sensitive parts removed; null when nothing is left.</summary>
    public static string? RedactTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? title : Redact(title).Trim() is { Length: > 0 } redacted ? redacted : null;

    /// <summary><see cref="IpAddressName"/> when the app name is an IP address (a website opened by address), else the name.</summary>
    public static string? MaskHost(string? name) => name is not null && IpHost().IsMatch(name.Trim()) ? IpAddressName : name;

    /// <summary>Grouped like a phone number or an ID ("123 456 789", "6222 0000 1111 2222") it all goes; otherwise only runs of 7 or more digits.</summary>
    private static string LongNumber(Match m)
    {
        var groups = m.Value.Split(' ', '-');
        if (groups.Length > 1 && groups.Sum(g => g.Length) >= 7 && groups[0].Length <= 4 && groups.Skip(1).All(g => g.Length is 3 or 4))
        {
            return "";
        }

        return LongRun().Replace(m.Value, "");
    }

    private static string Tidy(string text)
    {
        text = EmptyBrackets().Replace(text, "");
        text = SeparatorRun().Replace(text, m =>
        {
            var separator = m.Value.Trim()[0];
            return separator is '-' or '–' or '—' or '|' or '/' or '·' or '•' ? $" {separator} " : separator.ToString();
        });
        text = LeadingSeparators().Replace(text, "");
        text = TrailingSeparators().Replace(text, "");
        return Spaces().Replace(text, " ");
    }

    [GeneratedRegex(@"(?:验证码|校验码|动态码|激活码|邀请码|识别码|设备码|设备代码|连接码|安全码|口令|临时密码|密码|password|passcode|verification code|access code|invite code)\s*[:：]\s*(?:[0-9][0-9 -]*[0-9]|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex Labeled();

    private const string Octet = "(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])";

    [GeneratedRegex(@"(?:[A-Za-z][A-Za-z0-9+.-]*://)?(?:[A-Za-z0-9._-]+@)?(?<![A-Za-z0-9_.])" + Octet + @"(?:\." + Octet + @"){3}(?::[0-9]{1,5})?(?:/\S*)?(?![A-Za-z0-9_]|\.[0-9])")]
    private static partial Regex Ipv4();

    [GeneratedRegex("^" + Octet + @"(?:\." + Octet + @"){3}(?::[0-9]{1,5})?$")]
    private static partial Regex IpHost();

    private const string Hex = "[0-9A-Fa-f]{1,4}";

    [GeneratedRegex(@"(?<![A-Za-z0-9_:])\[?(?:" + Hex + "(?::" + Hex + "){7}|" + Hex + "(?::" + Hex + "){0,6}::(?:" + Hex + "(?::" + Hex + "){0,6})?|::" + Hex + "(?::" + Hex + @"){0,6})\]?(?::[0-9]{1,5})?(?![A-Za-z0-9_:])")]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![A-Za-z0-9_])")]
    private static partial Regex Email();

    [GeneratedRegex("(?<![A-Za-z0-9_])[1-9][0-9]{16}[0-9Xx](?![A-Za-z0-9_])")]
    private static partial Regex IdCard();

    [GeneratedRegex("(?<![A-Za-z0-9_])0[0-9]{2,3}-[0-9]{7,8}(?![A-Za-z0-9_])")]
    private static partial Regex Landline();

    [GeneratedRegex(@"(?<![A-Za-z0-9_+])(?:\+?86[ -]?)?1[3-9][0-9](?:[ -]?[0-9]{4}){2}(?![A-Za-z0-9_])")]
    private static partial Regex Mobile();

    /// <summary>Numbers, possibly grouped by single spaces or hyphens. Not part of a word, a version or a time.</summary>
    [GeneratedRegex("(?<![A-Za-z0-9_]|[0-9][.:])[0-9]+(?:[ -][0-9]+)*(?![A-Za-z0-9_]|[.:][0-9])")]
    private static partial Regex Digits();

    [GeneratedRegex("[0-9]{7,}")]
    private static partial Regex LongRun();

    [GeneratedRegex(@"[(\[（【「<]\s*[)\]）】」>]")]
    private static partial Regex EmptyBrackets();

    [GeneratedRegex(@"(?:\s*[-–—|/·•:：,，;；@]\s*){2,}")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"^[\s\-–—|/·•:：,，;；@]+")]
    private static partial Regex LeadingSeparators();

    [GeneratedRegex(@"[\s\-–—|/·•:：,，;；@]+$")]
    private static partial Regex TrailingSeparators();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();
}