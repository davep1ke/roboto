using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzySettingsTests
{
    private const long ChatId = -600;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task<TestBot> ThreePlayerGameAsync()
    {
        var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "defaults", firstName: "Alice")); // phase 8.5 setup wizard - quick path to Invites
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        return bot;
    }

    private static async Task<XyzzyGameState> GameAsync(TestBot bot) =>
        await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);

    [Fact]
    public async Task OnlyAnAdminCanOpenTheMenu()
    {
        using var bot = await ThreePlayerGameAsync();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/addadmin")); // bootstraps Alice as the sole admin

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_settings", firstName: "Bob"));
        Assert.Contains("Only a chat admin", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task AbandonStopsTheGame()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "abandon"));

        Assert.Contains("abandoned", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(XyzzyStatus.Stopped, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task TimeoutAndThrottleUpdateTheGame()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "timeout 4"));
        Assert.Equal(4, (await GameAsync(bot)).MaxWaitHours);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "throttle 1.5"));
        Assert.Equal(1.5, (await GameAsync(bot)).MinWaitHours);
    }

    [Fact]
    public async Task InvalidTimeoutIsRejectedWithoutChangingAnything()
    {
        using var bot = await ThreePlayerGameAsync();
        var before = (await GameAsync(bot)).MaxWaitHours;

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "timeout banana"));

        Assert.Contains("didn't understand", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(before, (await GameAsync(bot)).MaxWaitHours);
    }

    [Fact]
    public async Task KickAsksThenRemovesTheNamedPlayer()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "kick"));
        Assert.Contains("Bob", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("Carol", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob"));
        Assert.Contains("Bob was kicked", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.DoesNotContain(game.Players, p => p.DisplayName == "Bob");
        Assert.Equal(2, game.Players.Count);
    }

    [Fact]
    public async Task KickingAnUnknownNameIsRejectedCleanly()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "kick"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Dave"));

        Assert.Contains("No player called", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(3, (await GameAsync(bot)).Players.Count);
    }

    [Fact]
    public async Task ScoreOverridesAPlayersWinCount()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "score Bob 5"));

        Assert.Contains("Bob's score is now 5", bot.BotClient.SentMessages[^1].Text);
        var game = await GameAsync(bot);
        Assert.Equal(5, game.Players.First(p => p.DisplayName == "Bob").Wins);
    }

    [Fact]
    public async Task CancelLeavesEverythingUnchanged()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "cancel"));

        Assert.Contains("Cancelled", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(XyzzyStatus.Invites, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task SettingsDoNotApplyWithNoGameRunning()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        Assert.Contains("No game running", bot.BotClient.SentMessages[^1].Text);
    }
}
