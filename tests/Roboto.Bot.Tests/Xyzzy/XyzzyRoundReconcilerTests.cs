using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzyRoundReconcilerTests
{
    private const long ChatId = -500;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task<TestBot> ThreePlayerGameInProgressAsync()
    {
        var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        await BeginRoundAsync(bot, Alice);
        return bot;
    }

    private static async Task TapUseDefaultsAsync(TestBot bot, long userId)
    {
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == userId && m.Buttons is { Count: > 0 });
        var button = choiceMessage.Buttons!.First(b => b.Text == "Use Defaults");
        await bot.SendCallbackAsync(userId, button);
    }

    private static async Task BeginRoundAsync(TestBot bot, long starterId)
    {
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        var button = startMessage.Buttons!.First(b => b.Text == "Start");
        await bot.SendCallbackAsync(starterId, button);
    }

    /// <summary>Who's judging - identified by "didn't get a hand-answer keyboard", not just "didn't
    /// get any message with buttons" (the starter also gets setup/Start keyboards earlier, which
    /// would otherwise make them look like the judge every time).</summary>
    private static long JudgeIdOf(TestBot bot) => new[] { Alice, Bob, Carol }.First(id =>
        !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));

    /// <summary>Directly backdates StatusChangedUtc to simulate elapsed time deterministically,
    /// rather than actually waiting - the reconciler is driven directly (not via the real
    /// BackgroundService timer), same reasoning MessageDispatcher-level tests don't spin up
    /// TelegramPollingService.</summary>
    private static async Task<XyzzyGameState> BackdateAsync(TestBot bot, TimeSpan by)
    {
        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        game.StatusChangedUtc = DateTime.UtcNow - by;
        await games.SaveAsync(game, CancellationToken.None);
        return game;
    }

    private static async Task ReconcileAsync(TestBot bot)
    {
        var reconciler = bot.Services.GetRequiredService<XyzzyRoundReconciler>();
        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        await reconciler.ReconcileAsync(bot.BotClient, game, CancellationToken.None);
    }

    [Fact]
    public async Task ReminderIsSentAt75PercentAndOnlyOnce()
    {
        using var bot = await ThreePlayerGameInProgressAsync();
        await BackdateAsync(bot, TimeSpan.FromHours(9.5)); // 12h default MaxWaitHours * 0.8

        await ReconcileAsync(bot);
        var remindersAfterFirst = bot.BotClient.SentMessages.Count(m => m.Text.Contains("Reminder"));
        Assert.True(remindersAfterFirst > 0);

        await ReconcileAsync(bot);
        var remindersAfterSecond = bot.BotClient.SentMessages.Count(m => m.Text.Contains("Reminder"));
        Assert.Equal(remindersAfterFirst, remindersAfterSecond); // ReminderSent guards against duplicates.
    }

    [Fact]
    public async Task TimeoutWithPartialAnswersForceAdvancesToJudging()
    {
        using var bot = await ThreePlayerGameInProgressAsync();

        var judgeId = JudgeIdOf(bot);
        var answerer = new[] { Alice, Bob, Carol }.First(id => id != judgeId);
        var button = bot.BotClient.SentMessages.Last(m => m.ChatId == answerer && m.Buttons is { Count: > 0 }).Buttons![0];
        await bot.SendCallbackAsync(answerer, button);

        await BackdateAsync(bot, TimeSpan.FromHours(13)); // past the 12h default MaxWaitHours
        await ReconcileAsync(bot);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Judging with whoever"));
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == judgeId && m.Text.Contains("Pick the winner"));
    }

    [Fact]
    public async Task TimeoutWithNoAnswersSkipsToANewQuestion()
    {
        using var bot = await ThreePlayerGameInProgressAsync();

        await BackdateAsync(bot, TimeSpan.FromHours(13));
        await ReconcileAsync(bot);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Nobody answered in time"));

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(2, game.RoundNumber);
        Assert.Equal(XyzzyStatus.Question, game.Status);
    }

    [Fact]
    public async Task JudgingTimeoutAutoPicksAWinner()
    {
        using var bot = await ThreePlayerGameInProgressAsync();

        var judgeId = JudgeIdOf(bot);
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();
        foreach (var playerId in answerers)
        {
            await bot.AnswerHandFullyAsync(playerId);
        }

        await BackdateAsync(bot, TimeSpan.FromHours(13));
        await ReconcileAsync(bot);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("auto-picking a winner"));
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins the round"));
    }

    [Fact]
    public async Task ThrottleHoldsTheNextHandUntilItElapses()
    {
        using var bot = await ThreePlayerGameInProgressAsync();

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        game.MinWaitHours = 2;
        await games.SaveAsync(game, CancellationToken.None);

        var judgeId = JudgeIdOf(bot);
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();
        foreach (var playerId in answerers)
        {
            await bot.AnswerHandFullyAsync(playerId);
        }
        var judgeButton = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 }).Buttons![0];
        await bot.SendCallbackAsync(judgeId, judgeButton);

        game = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(XyzzyStatus.WaitingForNextHand, game.Status);

        // Not enough time has passed yet - reconciling shouldn't deal the next round.
        await ReconcileAsync(bot);
        game = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(XyzzyStatus.WaitingForNextHand, game.Status);

        // Backdate past the throttle - now it should proceed.
        game.StatusChangedUtc = DateTime.UtcNow - TimeSpan.FromHours(3);
        await games.SaveAsync(game, CancellationToken.None);
        await ReconcileAsync(bot);

        game = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(XyzzyStatus.Question, game.Status);
        Assert.Equal(2, game.RoundNumber);
    }
}
