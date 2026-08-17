using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzyStartWizardTests
{
    private const long ChatId = -700;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task<XyzzyGameState> GameAsync(TestBot bot) =>
        await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);

    [Fact]
    public async Task ConfigurePathAsksQuestionLimitThenTimeoutThenThrottle()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "configure", firstName: "Alice"));
        Assert.Contains("How many questions", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "5", firstName: "Alice"));
        Assert.Contains("wait for answers", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "4", firstName: "Alice"));
        Assert.Contains("throttle", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "1.5", firstName: "Alice"));
        Assert.Contains("Setup's done", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.Invites, game.Status);
        Assert.Equal(5, game.QuestionLimit);
        Assert.Equal(4, game.MaxWaitHours);
        Assert.Equal(1.5, game.MinWaitHours);
    }

    [Fact]
    public async Task InvalidValuesAtEachConfigureStepRepromptWithoutAdvancing()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "configure", firstName: "Alice"));

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "banana", firstName: "Alice"));
        Assert.Contains("Not a valid number", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("How many questions", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "3", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "0", firstName: "Alice")); // timeout must be > 0
        Assert.Contains("Not a valid number", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("wait for answers", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "6", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "-1", firstName: "Alice")); // throttle can't be negative
        Assert.Contains("Not a valid number", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("throttle", bot.BotClient.SentMessages[^1].Text);

        // Recoverable - the game is still mid-setup, not stuck.
        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.SettingUp, game.Status);
    }

    [Fact]
    public async Task CancelDuringChoiceResetsTheGameCompletely()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "cancel", firstName: "Alice"));

        Assert.Contains("Cancelled", bot.BotClient.SentMessages.Last(m => m.ChatId == Alice).Text);
        Assert.Contains("cancelled", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.Stopped, game.Status);
        Assert.Empty(game.Players);

        // A fresh /xyzzy_start should work cleanly afterwards, not be blocked by leftover state.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_start", firstName: "Bob"));
        Assert.Contains("is starting a new game", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task AnUnrecognisedChoiceRepromptsInsteadOfSilentlyFailing()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "maybe?", firstName: "Alice"));

        Assert.Contains("Not a valid answer", bot.BotClient.SentMessages[^1].Text);

        // Still recoverable.
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "defaults", firstName: "Alice"));
        Assert.Equal(XyzzyStatus.Invites, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task NoOpenPrivateChatRollsTheGameBackToStopped()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(Alice);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));

        Assert.Contains("needs to open a private chat", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.Stopped, game.Status);
        Assert.Empty(game.Players);
    }

    [Fact]
    public async Task QuestionLimitEndsTheGameAutomatically()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "configure", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "1", firstName: "Alice")); // one-round game
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "4", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "0", firstName: "Alice"));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 }));
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();
        foreach (var playerId in answerers)
        {
            var button = bot.BotClient.SentMessages.Last(m => m.ChatId == playerId && m.Buttons is { Count: > 0 }).Buttons![0];
            await bot.SendCallbackAsync(playerId, button.CallbackData);
        }
        var judgeButton = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 }).Buttons![0];
        await bot.SendCallbackAsync(judgeId, judgeButton.CallbackData);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("end of the game"));
        Assert.Equal(XyzzyStatus.Stopped, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task AbandonedSetupIsAutoResetAfter24Hours()
    {
        using var bot = new TestBot();
        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var reconciler = bot.Services.GetRequiredService<XyzzyRoundReconciler>();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));

        var game = await games.GetAsync(ChatId, CancellationToken.None);
        game.StatusChangedUtc = DateTime.UtcNow - TimeSpan.FromHours(25);
        await games.SaveAsync(game, CancellationToken.None);

        await reconciler.ReconcileAsync(bot.BotClient, game, CancellationToken.None);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("setup timed out"));
        Assert.Equal(XyzzyStatus.Stopped, (await games.GetAsync(ChatId, CancellationToken.None)).Status);
    }
}
