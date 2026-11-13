using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using DeskPal.Client.Core.Localization;
using DeskPal.Shared.Api;

namespace DeskPal.Tests.Localization;

internal static class TestCulture
{
    /// <summary>Assertions are written against the Chinese text, whatever the machine's display language is.</summary>
    [ModuleInitializer]
    internal static void UseChinese()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
    }
}

public class LocalizationTests
{
    private static Dictionary<string, string> Entries(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [Fact]
    public void English_has_every_key()
    {
        var zh = Entries(CultureInfo.InvariantCulture);
        var en = Entries(CultureInfo.GetCultureInfo("en"));

        Assert.Empty(zh.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(zh.Keys));
    }

    [Fact]
    public void Every_server_error_code_has_text()
    {
        var codes = typeof(ApiErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.Name != nameof(ApiErrorCodes.LimitArg))
            .Select(f => (string)f.GetRawConstantValue()!);
        var zh = Entries(CultureInfo.InvariantCulture);
        var en = Entries(CultureInfo.GetCultureInfo("en"));

        Assert.All(codes, code =>
        {
            Assert.Contains(ApiErrorText.ResourceKey(code), zh.Keys);
            Assert.Contains(ApiErrorText.ResourceKey(code), en.Keys);
        });
    }

    [Fact]
    public void Error_text_fills_in_the_limit()
    {
        var args = new Dictionary<string, string> { [ApiErrorCodes.LimitArg] = "3" };

        Assert.Equal("该房间已满（上限 3 人）", ApiErrorText.Describe(ApiErrorCodes.RoomFull, args));
        Assert.Null(ApiErrorText.Describe("room.fromTheFuture"));
    }

    [Theory]
    [InlineData(null, "zh-CN", "zh-CN")]
    [InlineData("system", "zh-TW", "zh-CN")]
    [InlineData("system", "ja-JP", "en")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("fr", "en-US", "en")]
    public void Resolves_the_language(string? setting, string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(setting, CultureInfo.GetCultureInfo(system)));
}