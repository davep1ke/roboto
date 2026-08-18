using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Roboto.Bot;
using Roboto.Bot.Persistence;
using Roboto.Bot.Steam;

namespace Roboto.Bot.Tests.Steam;

public class SteamTests
{
    private const long ChatId = -200;
    private const long Alice = 1;

    private const string PlayerSummaryJson =
        """{"response":{"players":[{"steamid":"7656119","personaname":"CoolGamer","communityvisibilitystate":3}]}}""";

    private const string PrivatePlayerSummaryJson =
        """{"response":{"players":[{"steamid":"7656119","personaname":"ShyGamer","communityvisibilitystate":1}]}}""";

    private const string RecentGamesJson =
        """{"response":{"total_count":1,"games":[{"appid":440,"name":"Team Fortress 2"}]}}""";

    private const string UserStatsJson =
        """{"playerstats":{"achievements":[{"name":"KILL_LONG_DISTANCE","achieved":1},{"name":"NOT_YET","achieved":0}]}}""";

    private const string SchemaJson =
        """{"game":{"availableGameStats":{"achievements":[{"name":"KILL_LONG_DISTANCE","displayName":"Long Distance Kill","description":"Kill from afar."}]}}}""";

    [Fact]
    public async Task ApiClientParsesRealisticSteamResponseShapes()
    {
        var handler = new FakeSteamHttpHandler();
        handler.ResponsesByUrlContains["GetPlayerSummaries"] = PlayerSummaryJson;
        handler.ResponsesByUrlContains["GetRecentlyPlayedGames"] = RecentGamesJson;
        handler.ResponsesByUrlContains["GetUserStatsForGame"] = UserStatsJson;
        handler.ResponsesByUrlContains["GetSchemaForGame"] = SchemaJson;
        var client = new SteamApiClient(new HttpClient(handler));

        var summary = await client.GetPlayerSummaryAsync("key", "7656119", CancellationToken.None);
        Assert.Equal("CoolGamer", summary!.PersonaName);
        Assert.False(summary.IsPrivate);

        var games = await client.GetRecentlyPlayedGamesAsync("key", "7656119", CancellationToken.None);
        Assert.Single(games);
        Assert.Equal("Team Fortress 2", games[0].Name);
        Assert.Equal(440, games[0].AppId);

        // Only the achieved==1 entry comes back - NOT_YET is filtered out (see SteamApiClient's
        // doc comment on why this is a deliberate correction, not a faithful-bug port).
        var achieved = await client.GetAchievedCodesAsync("key", "7656119", "440", CancellationToken.None);
        Assert.Equal(["KILL_LONG_DISTANCE"], achieved);

        var schema = await client.GetGameSchemaAsync("key", "440", CancellationToken.None);
        Assert.Single(schema);
        Assert.Equal("Long Distance Kill", schema[0].DisplayName);
    }

    [Fact]
    public async Task ReconcilerDoesNothingWithoutAnApiKeyConfigured()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<SteamRepository>();
        var chat = await repo.GetChatAsync(ChatId, CancellationToken.None);
        chat.Players.Add(new SteamPlayer { SteamId = "7656119", PlayerName = "CoolGamer" });
        await repo.SaveChatAsync(chat, CancellationToken.None);

        // Default TestBot has no SteamApiKey set - the reconciler should just no-op, not throw.
        var reconciler = bot.Services.GetRequiredService<SteamReconciler>();
        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        Assert.Empty(bot.BotClient.SentMessages);
    }

    [Fact]
    public async Task ReconcilerAnnouncesNewlyEarnedAchievementsAndRecordsThem()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        var repo = new SteamRepository(store);
        var chat = await repo.GetChatAsync(ChatId, CancellationToken.None);
        chat.Players.Add(new SteamPlayer { SteamId = "7656119", PlayerName = "CoolGamer" });
        await repo.SaveChatAsync(chat, CancellationToken.None);

        var handler = new FakeSteamHttpHandler();
        handler.ResponsesByUrlContains["GetRecentlyPlayedGames"] = RecentGamesJson;
        handler.ResponsesByUrlContains["GetUserStatsForGame"] = UserStatsJson;
        handler.ResponsesByUrlContains["GetSchemaForGame"] = SchemaJson;
        var apiClient = new SteamApiClient(new HttpClient(handler));
        var options = Options.Create(new BotOptions { SteamApiKey = "test-key" });
        var reconciler = new SteamReconciler(apiClient, repo, options, bot.Services.GetRequiredService<ILogger<SteamReconciler>>());

        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        var announcement = Assert.Single(bot.BotClient.SentMessages, m => m.ChatId == ChatId);
        Assert.Contains("CoolGamer got the following achievements", announcement.Text);
        Assert.Contains("Long Distance Kill", announcement.Text);
        Assert.Contains("Team Fortress 2", announcement.Text);

        var after = await repo.GetChatAsync(ChatId, CancellationToken.None);
        Assert.Single(after.Players[0].Chievs);
        Assert.Equal("KILL_LONG_DISTANCE", after.Players[0].Chievs[0].ChievCode);

        // Running it again shouldn't re-announce the same achievement.
        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);
        Assert.Single(bot.BotClient.SentMessages, m => m.ChatId == ChatId);
    }

    [Fact]
    public async Task AddPlayerWithoutAnApiKeyConfiguredIsReportedInTheGroup()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/steam_addplayer", firstName: "Alice"));

        Assert.Contains("isn't configured for this bot", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task HelpExplainsHowToFindAPlayerId()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/steam_help"));

        Assert.Contains("steamcommunity.com", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task StatsWithNoPlayersTrackedStillReportsCleanly()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/steam_stats"));

        Assert.Contains("Currently watching achievements", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("Tracking 0 achievements across 0 games", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task RemovePlayerShowsAPickerAndRemovesTheChosenOne()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<SteamRepository>();
        var chat = await repo.GetChatAsync(ChatId, CancellationToken.None);
        chat.Players.Add(new SteamPlayer { SteamId = "7656119", PlayerName = "CoolGamer" });
        await repo.SaveChatAsync(chat, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/steam_remove", firstName: "Alice"));
        var picker = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, picker.Buttons!.First(b => b.Text == "CoolGamer"));

        Assert.Contains("Player removed", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
        var after = await repo.GetChatAsync(ChatId, CancellationToken.None);
        Assert.Empty(after.Players);
    }

    [Fact]
    public async Task RemovePlayerWithNoneTrackedSaysSoWithoutShowingAPicker()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/steam_remove", firstName: "Alice"));

        Assert.Contains("No players being tracked here", bot.BotClient.SentMessages[^1].Text);
    }
}
