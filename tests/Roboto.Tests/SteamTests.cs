namespace RobotoTests;

/// <summary>
/// mod_steam's core flows (/steam_addplayer, /steam_check) call the real Steam Web API
/// synchronously (see mod_steam_steamapi.cs's WebClient calls) - no fake HTTP client exists yet, so
/// only the network-free commands are covered here. Exercising the rest needs a fake Steam API
/// client, deferred rather than attempted against the real network from a test.
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
}
