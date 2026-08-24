using System;
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

    [Fact]
    public void GetAchievementsOnlyReturnsOnesThePlayerHasActuallyUnlocked()
    {
        // GetUserStatsForGame returns one entry per achievement *defined for the game*, each
        // carrying its own achieved 0/1 flag - not one entry per achievement the player has
        // earned. Found via a live round-trip against the real Steam API on the abandoned
        // rewrite branch, confirmed present byte-for-byte in legacy: without filtering on
        // "achieved", checkAchievements() (mod_steam.cs) treats every entry not already in a
        // player's local chievs list as "new", so a player's very first check announced every
        // achievement in the game, including ones they never unlocked.
        using var bot = new TestHarness();
        mod_steam_steamapi.HttpGetOverride = url => """
            { "playerstats": { "achievements": [
                { "name": "UNLOCKED_ONE", "achieved": 1 },
                { "name": "LOCKED_ONE", "achieved": 0 }
            ] } }
            """;

        var result = mod_steam_steamapi.getAchievements("76561197960287930", "440");

        Assert.Contains("UNLOCKED_ONE", result);
        Assert.DoesNotContain("LOCKED_ONE", result);
    }

    private static void AddGaben(TestHarness bot)
    {
        mod_steam_steamapi.HttpGetOverride = url => """
            { "response": { "players": [ { "personaname": "Gaben", "communityvisibilitystate": 3 } ] } }
            """;
        bot.SendGroupMessage(ChatId, Alice, "/steam_addplayer", "Alice");
        bot.TapButton(Alice, "76561197960287930", "Alice");
    }

    /// <summary>checkAchievements() (mod_steam.cs) chains three separate Steam endpoints through the
    /// same sendPOST/HttpGetOverride chokepoint - GetRecentlyPlayedGames, then GetUserStatsForGame per
    /// game, then (since a freshly-added game's achievement cache is empty) GetSchemaForGame to refresh
    /// it before the friendly display text is available. Dispatches on the request URL, same seam
    /// SteamTests' other tests already use for the single-endpoint flows.</summary>
    private static void StubFullAchievementCheck(string gameID, string gameName, string achievementCode, string achievementDisplayName, string achievementDescription)
    {
        mod_steam_steamapi.HttpGetOverride = url =>
        {
            if (url.Contains("GetRecentlyPlayedGames"))
            {
                return $$"""{ "response": { "games": [ { "appid": "{{gameID}}", "name": "{{gameName}}" } ] } } """;
            }
            if (url.Contains("GetUserStatsForGame"))
            {
                return $$"""{ "playerstats": { "achievements": [ { "name": "{{achievementCode}}", "achieved": 1 } ] } } """;
            }
            if (url.Contains("GetSchemaForGame"))
            {
                return $$"""
                    { "game": { "availableGameStats": { "achievements": [
                        { "name": "{{achievementCode}}", "displayName": "{{achievementDisplayName}}", "description": "{{achievementDescription}}" }
                    ] } } }
                    """;
            }
            throw new InvalidOperationException("Unexpected Steam API URL in test: " + url);
        };
    }

    [Fact]
    public void SteamCheckAnnouncesANewlyDetectedAchievement()
    {
        using var bot = new TestHarness();
        AddGaben(bot);
        StubFullAchievementCheck("440", "Team Fortress 2", "FIRST_BLOOD", "First Blood", "Get your first kill");

        bot.SendGroupMessage(ChatId, Alice, "/steam_check", "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId
            && m.Text.Contains("Gaben got the following achievements")
            && m.Text.Contains("*First Blood* - Get your first kill in Team Fortress 2"));
    }

    [Fact]
    public void BackgroundProcessingAlsoAnnouncesNewlyDetectedAchievements()
    {
        // checkChat()/checkAchievements() is reachable two ways - the manual /steam_check command
        // (covered above) and mod_steam.backgroundProcessing's own periodic sweep over every chat's
        // tracked players (mod_steam.cs:447). Only the manual path had any coverage before this.
        using var bot = new TestHarness();
        AddGaben(bot);
        StubFullAchievementCheck("570", "Dota 2", "FIRST_BLOOD_DOTA", "First Blood", "Get your first kill");

        bot.RunBackgroundProcessing();

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId
            && m.Text.Contains("Gaben got the following achievements")
            && m.Text.Contains("*First Blood* - Get your first kill in Dota 2"));
    }

    [Fact]
    public void RemovePlayerStopsTrackingThem()
    {
        // Real bug found while adding this coverage, not from a report: /steam_remove's SendQuestion
        // passed isPrivateMessage:false, the one remaining call site of the group-targeted-question
        // bug MIGRATION.md's other 5 call sites were already fixed for - every call threw
        // NotImplementedException (Messaging.processNewExpectedReply's own guard), so this command was
        // completely non-functional. Fixed alongside adding this test.
        using var bot = new TestHarness();
        AddGaben(bot);

        bot.SendGroupMessage(ChatId, Alice, "/steam_remove", "Alice");
        var keyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        string gabenButton = keyboard.SelectMany(r => r).Single(b => b.Text == "Gaben").Text;
        bot.TapButton(Alice, gabenButton, "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Player Gaben removed"));
        var chatData = (mod_steam_chat_data)Chats.getChat(ChatId).getPluginData(typeof(mod_steam_chat_data));
        Assert.DoesNotContain(chatData.players, p => p.playerName == "Gaben");
    }
}
