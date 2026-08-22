using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// mod_steam's core flows (/steam_addplayer, /steam_check) call the real Steam Web API
/// synchronously via mod_steam_steamapi.sendPOST's single chokepoint. mod_steam_steamapi.
/// HttpGetOverride (mirroring TelegramAPI.SetClientForTesting's pattern) lets these tests fake that
/// chokepoint instead of hitting the real network - TestHarness resets it to null before each test.
/// </summary>
public class SteamTests
{
    private const long ChatId = -500;
    private const long Alice = 40;

    [Fact]
    public void SteamHelpExplainsHowToFindASteamId()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/steam_help", "Alice");
        Assert.Contains("steamcommunity.com", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void SteamStatsWithNoPlayersReportsZeroTracked()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/steam_stats", "Alice");
        Assert.Contains("Tracking 0 achievements", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void AddPlayerRejectsANonNumericIdAndAsksAgain()
    {
        // Doesn't reach mod_steam_steamapi.getPlayerInfo's real Steam Web API call (only a
        // successfully-parsed numeric id does) - proves /steam_addplayer's reply is actually
        // reaching replyReceived at all, which it never did before the isPrivateMessage:true fix
        // (Messaging.processNewExpectedReply never registered a group-targeted question for
        // matching, so this whole command was silently dead - see MIGRATION.md).
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/steam_addplayer", "Alice");
        Assert.Contains("Enter the steamID", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "not-a-steam-id", "Alice");

        Assert.Contains("is not a valid playerID", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void AddPlayerCancelStopsAskingWithoutError()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/steam_addplayer", "Alice");
        bot.TapButton(Alice, "Cancel", "Alice");

        Assert.DoesNotContain("is not a valid playerID", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void AddPlayerWithAPublicProfileAddsThemAndAnnouncesAchievementTracking()
    {
        using var bot = new TestHarness();
        mod_steam_steamapi.HttpGetOverride = url => """
            { "response": { "players": [ { "personaname": "Gaben", "communityvisibilitystate": 3 } ] } }
            """;

        bot.SendGroupMessage(ChatId, Alice, "/steam_addplayer", "Alice");
        bot.TapButton(Alice, "76561197960287930", "Alice");

        Assert.Contains("Added Gaben. Any steam achievements will be announced.", bot.BotClient.SentMessages[^1].Text);
        var chatData = (mod_steam_chat_data)Chats.getChat(ChatId).getPluginData(typeof(mod_steam_chat_data));
        Assert.Contains(chatData.players, p => p.playerName == "Gaben");
    }

    [Fact]
    public void AddPlayerWithAPrivateProfileIsRejected()
    {
        using var bot = new TestHarness();
        mod_steam_steamapi.HttpGetOverride = url => """
            { "response": { "players": [ { "personaname": "Incognito", "communityvisibilitystate": 1 } ] } }
            """;

        bot.SendGroupMessage(ChatId, Alice, "/steam_addplayer", "Alice");
        bot.TapButton(Alice, "76561197960287930", "Alice");

        Assert.Contains("Couldn't add Incognito as their profile is set to private", bot.BotClient.SentMessages[^1].Text);
        var chatData = (mod_steam_chat_data)Chats.getChat(ChatId).getPluginData(typeof(mod_steam_chat_data));
        Assert.DoesNotContain(chatData.players, p => p.playerName == "Incognito");
    }
}
