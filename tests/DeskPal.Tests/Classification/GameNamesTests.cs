using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Classification;

public class GameNamesTests
{
    private static ActivitySnapshot Game(string path, string? description = null)
    {
        var file = Path.GetFileNameWithoutExtension(path.Replace('/', '\\')[(path.Replace('/', '\\').LastIndexOf('\\') + 1)..]);
        return ActivitySnapshot.Empty.Apply(new ForegroundSignal(file, path, description ?? file, "", false, IsBrowser: false));
    }

    private static ClassificationResult Classify(ActivitySnapshot snapshot, LayeredClassifier? classifier = null) =>
        (classifier ?? new LayeredClassifier()).Classify(snapshot);

    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Black Myth Wukong\b1\Binaries\Win64\b1-Win64-Shipping.exe", null, "Black Myth Wukong")]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Indie\game.exe", "UnityPlayer", "Indie")]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", null, "Fortnite")]
    [InlineData(@"D:\WeGameApps\英雄联盟\Game\League of Legends.exe", "", "英雄联盟")]
    [InlineData(@"D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\ffxiv_dx11.exe", null, "FINAL FANTASY XIV - A Realm Reborn")]
    [InlineData("/Users/me/Library/Application Support/Steam/steamapps/common/Indie/Indie.app/Contents/MacOS/Indie", null, "Indie")]
    public void Unreadable_game_names_come_from_the_library_folder(string path, string? description, string expected)
    {
        var result = Classify(Game(path, description));

        Assert.Equal((ActivityCategory.Game, expected, RuleLayer.Heuristic), (result.Category, result.AppDisplayName, result.Layer));
    }

    [Fact]
    public void Readable_description_is_kept()
    {
        var result = Classify(Game(@"D:\SteamLibrary\steamapps\common\GTAV\GTA5.exe", "Grand Theft Auto V"));

        Assert.Equal("Grand Theft Auto V", result.AppDisplayName);
    }

    [Theory]
    [InlineData(@"D:\Games\Black Myth Wukong\b1\Binaries\Win64\b1-Win64-Shipping.exe", "Black Myth Wukong")]
    [InlineData(@"D:\Games\Stray\Binaries\Win64\Stray-Win64-Shipping.exe", "Stray")]
    [InlineData(@"E:\b1-Win64-Shipping.exe", "b1")]
    public void Unreal_shipping_build_outside_libraries_is_a_named_game(string path, string expected)
    {
        var result = Classify(Game(path));

        Assert.Equal((ActivityCategory.Game, expected, RuleLayer.Heuristic), (result.Category, result.AppDisplayName, result.Layer));
    }

    [Fact]
    public void Unreal_crash_reporter_is_not_a_game()
    {
        Assert.Equal(ActivityCategory.Other, Classify(Game(@"D:\Games\Stray\Engine\Binaries\Win64\CrashReportClient.exe")).Category);
    }

    [Fact]
    public void Steam_manifest_names_the_game()
    {
        var library = Path.Combine(Path.GetTempPath(), $"deskpal-steam-{Guid.NewGuid():N}", "steamapps");
        Directory.CreateDirectory(Path.Combine(library, "common", "BlackMythWukong"));
        File.WriteAllText(Path.Combine(library, "appmanifest_2358720.acf"), """
            "AppState"
            {
            	"appid"		"2358720"
            	"name"		"Black Myth: Wukong"
            	"installdir"		"BlackMythWukong"
            }
            """);
        try
        {
            var path = Path.Combine(library, "common", "BlackMythWukong", "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");

            Assert.Equal("Black Myth: Wukong", Classify(Game(path)).AppDisplayName);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(library)!, recursive: true);
        }
    }

    [Fact]
    public void Folders_from_the_builtin_rules_replace_the_defaults()
    {
        var classifier = new LayeredClassifier();
        var snapshot = Game(@"D:\Battle.net\Diablo IV\Diablo IV.exe");
        Assert.Equal(ActivityCategory.Other, Classify(snapshot, classifier).Category);

        classifier.SetFolders([new FolderRuleDto("/Battle.net/", ActivityCategory.Game)]);
        Assert.Equal((ActivityCategory.Game, "Diablo IV"), (Classify(snapshot, classifier).Category, Classify(snapshot, classifier).AppDisplayName));
        Assert.Equal(ActivityCategory.Other, Classify(Game(@"D:\SteamLibrary\steamapps\common\Indie\game.exe"), classifier).Category);

        classifier.SetFolders(null);
        Assert.Equal(ActivityCategory.Game, Classify(Game(@"D:\SteamLibrary\steamapps\common\Indie\game.exe"), classifier).Category);
    }

    [Fact]
    public void Builtin_rule_name_wins_over_the_guess()
    {
        var classifier = new LayeredClassifier();
        classifier.SetBuiltin(new RuleSet([new RuleDto(RuleKind.Process, "b1-win64-shipping", ActivityCategory.Game, "黑神话：悟空")]));

        var result = Classify(Game(@"D:\SteamLibrary\steamapps\common\Black Myth Wukong\b1\Binaries\Win64\b1-Win64-Shipping.exe"), classifier);

        Assert.Equal((ActivityCategory.Game, "黑神话：悟空", RuleLayer.Builtin), (result.Category, result.AppDisplayName, result.Layer));
    }
}
