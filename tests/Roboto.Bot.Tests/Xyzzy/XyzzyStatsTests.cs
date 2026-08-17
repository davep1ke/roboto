using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Stats;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzyStatsTests
{
    private const long ChatId = -800;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task TapUseDefaultsAsync(TestBot bot, long userId)
    {
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == userId && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(userId, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));
    }

    private static async Task BeginRoundAsync(TestBot bot, long starterId)
    {
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(starterId, startMessage.Buttons!.First(b => b.Text == "Start"));
    }

    [Fact]
    public async Task StartingAGameRecordsGamesStarted()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));

        var series = await stats.GetAsync("xyzzy.games-started", CancellationToken.None);
        Assert.Equal(1, series!.Total);
    }

    [Fact]
    public async Task PlayingAHandRecordsHandsPlayed()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await BeginRoundAsync(bot, Alice); // fills bots since Alice is alone

        // Alice judges round 1 (deterministic - Players[0]); bots auto-answer instantly.
        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the winner"));
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons![0]);

        var series = await stats.GetAsync("xyzzy.hands-played", CancellationToken.None);
        Assert.Equal(1, series!.Total);
    }

    [Fact]
    public async Task AbandoningAGameRecordsGamesEnded()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        await BeginRoundAsync(bot, Alice);
        // Alice judges round 1 (deterministic - Players[0]). With real (non-bot) answerers, judging
        // can't begin until they actually answer, so this is a non-blocking notice, leaving her
        // queue clear to open /xyzzy_settings immediately - same reasoning as XyzzySettingsTests'
        // ThreePlayerGameAsync helper. (Unlike PlayingAHandRecordsHandsPlayed above, which relies on
        // bots auto-answering specifically to prove the hands-played stat fires.)

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        var menuMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, menuMessage.Buttons!.First(b => b.Text == "Abandon"));

        var series = await stats.GetAsync("xyzzy.games-ended", CancellationToken.None);
        Assert.Equal(1, series!.Total);
    }

    [Fact]
    public async Task ReconcilerTickRecordsActiveGameAndPlayerSnapshot()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        var reconciler = bot.Services.GetRequiredService<XyzzyRoundReconciler>();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));

        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        var activeGames = await stats.GetAsync("xyzzy.active-games", CancellationToken.None);
        var activePlayers = await stats.GetAsync("xyzzy.active-players", CancellationToken.None);
        Assert.Equal(1, activeGames!.Total);
        Assert.Equal(3, activePlayers!.Total); // three real players, no bots needed yet
        Assert.Equal(StatMode.Snapshot, activeGames.Mode);
    }

    [Fact]
    public async Task StatsCommandReportsRecordedStats()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/stats"));

        var text = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("xyzzy.games-started", text);
    }
}
