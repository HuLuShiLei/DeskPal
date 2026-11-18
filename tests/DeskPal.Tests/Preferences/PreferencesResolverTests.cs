using DeskPal.Client.Core.Preferences;
using DeskPal.Shared.Api;

namespace DeskPal.Tests.Preferences;

public class PreferencesResolverTests
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly UserPreferences Local = new(10, "system", "zh-CN", "ask", false, new() { [Room] = [Bob] });

    [Fact]
    public void A_new_account_takes_this_computers_values()
    {
        var (use, upload) = PreferencesResolver.Resolve(Local, null, unsynced: false);

        Assert.Equal(Local, use);
        Assert.Equal(Local, upload);
    }

    [Fact]
    public void The_account_wins_and_only_missing_fields_are_uploaded()
    {
        var account = new UserPreferences(0, "dark", Topmost: true);

        var (use, upload) = PreferencesResolver.Resolve(Local, account, unsynced: false);

        Assert.Equal((0, "dark", "zh-CN", "ask", true), (use.SwitchDelaySeconds, use.Theme, use.Language, use.CloseAction, use.Topmost));
        Assert.Equal(new UserPreferences(Language: "zh-CN", CloseAction: "ask", MemberOrder: Local.MemberOrder), upload);
    }

    [Fact]
    public void Nothing_is_uploaded_when_the_account_has_everything()
    {
        var account = new UserPreferences(3, "light", "en", "tray", true, new());

        var (use, upload) = PreferencesResolver.Resolve(Local, account, unsynced: false);

        Assert.Equal(account, use);
        Assert.Null(upload);
    }

    [Fact]
    public void Changes_made_while_offline_replace_the_accounts_values()
    {
        var account = new UserPreferences(3, "light", "en", "tray", true, new());

        var (use, upload) = PreferencesResolver.Resolve(Local, account, unsynced: true);

        Assert.Equal(Local, use);
        Assert.Equal(Local, upload);
    }

    [Fact]
    public void Unknown_values_from_the_account_are_ignored()
    {
        var (use, _) = PreferencesResolver.Resolve(Local, new UserPreferences(7, "pink", "fr", "hide"), unsynced: false);

        Assert.Equal(Local, use);
    }
}