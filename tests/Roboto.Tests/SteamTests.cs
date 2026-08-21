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
}
