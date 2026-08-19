using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzySettingsTests
{
    private const long ChatId = -600;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    /// <summary>Gets a game to Question status with Alice judging round 1 (deterministic -
    /// Players[0]) so her queue is clear afterward: judging sends her a non-blocking notice, not a
    /// question, leaving her free to open /xyzzy_settings immediately - matches the realistic case
    /// (most settings adjustments happen mid-game, not while still waiting in Invites).</summary>
    private static async Task<TestBot> ThreePlayerGameAsync()
    {
        var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));

        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start"));

        return bot;
    }

    private static async Task<XyzzyGameState> GameAsync(TestBot bot) =>
        await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);

    /// <summary>Opens the settings menu and taps the named top-level button ("Abandon" / "Timeout" /
    /// "Throttle" / "Kick" / "Score" / "Cancel"). Assumes the caller's DmOutbox queue is currently
    /// clear (see ThreePlayerGameAsync) - if it isn't, the menu won't be there to find at all, which
    /// is exactly the behavior StillWaitingOnACardBlocksTheSettingsMenu below is testing for.</summary>
    private static async Task<string> OpenSettingsAndTapAsync(TestBot bot, long userId, string buttonText)
    {
        await bot.SendAsync(TestBot.GroupMessage(ChatId, userId, "/xyzzy_settings"));
        var menuMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == userId && m.Buttons is { Count: > 0 });
        var button = menuMessage.Buttons!.First(b => b.Text == buttonText);
        await bot.SendCallbackAsync(userId, button);
        return bot.BotClient.AnsweredCallbacks[^1].Text!;
    }

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

        await OpenSettingsAndTapAsync(bot, Alice, "Abandon");

        Assert.Contains("abandoned", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(XyzzyStatus.Stopped, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task TimeoutAndThrottleUpdateTheGame()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Timeout");
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "4"));
        Assert.Equal(4, (await GameAsync(bot)).MaxWaitHours);

        await OpenSettingsAndTapAsync(bot, Alice, "Throttle");
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "1.5"));
        Assert.Equal(1.5, (await GameAsync(bot)).MinWaitHours);
    }

    [Fact]
    public async Task InvalidTimeoutIsRejectedWithoutChangingAnything()
    {
        using var bot = await ThreePlayerGameAsync();
        var before = (await GameAsync(bot)).MaxWaitHours;

        await OpenSettingsAndTapAsync(bot, Alice, "Timeout");
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "banana"));

        Assert.Contains("Not a valid number", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(before, (await GameAsync(bot)).MaxWaitHours);
    }

    [Fact]
    public async Task KickShowsAKeyboardAndRemovesTheChosenPlayer()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Kick");
        var kickMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        Assert.Contains(kickMessage.Buttons!, b => b.Text == "Bob");
        Assert.Contains(kickMessage.Buttons!, b => b.Text == "Carol");

        await bot.SendCallbackAsync(Alice, kickMessage.Buttons!.First(b => b.Text == "Bob"));
        Assert.Contains("Bob was kicked", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.DoesNotContain(game.Players, p => p.DisplayName == "Bob");
        Assert.Equal(2, game.Players.Count);
    }

    [Fact]
    public async Task ATamperedKickTargetIsRejectedCleanly()
    {
        using var bot = await ThreePlayerGameAsync();

        // A genuinely current kick-target keyboard (real, currently-outstanding message), but with
        // a forged player ID substituted into the tapped callback data.
        await OpenSettingsAndTapAsync(bot, Alice, "Kick");
        var kickMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, $"xy:se:{ChatId}:kick:999999", kickMessage.Id);

        Assert.Contains("isn't in the game", bot.BotClient.AnsweredCallbacks[^1].Text!);
        Assert.Equal(3, (await GameAsync(bot)).Players.Count);
    }

    [Fact]
    public async Task ScoreShowsAKeyboardThenAsksForThePointsValue()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Score");
        var scoreMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, scoreMessage.Buttons!.First(b => b.Text == "Bob"));
        Assert.Contains("Bob's new score", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "5"));

        Assert.Contains("Bob's score is now 5", bot.BotClient.SentMessages[^1].Text);
        var game = await GameAsync(bot);
        Assert.Equal(5, game.Players.First(p => p.DisplayName == "Bob").Wins);
    }

    [Fact]
    public async Task CancelLeavesEverythingUnchanged()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Cancel");

        Assert.Contains("Cancelled", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(XyzzyStatus.Question, (await GameAsync(bot)).Status); // round 1 is already under way
    }

    [Fact]
    public async Task SettingsDoNotApplyWithNoGameRunning()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        Assert.Contains("No game running", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task StillWaitingOnACardBlocksTheSettingsMenuUntilItsAnswered()
    {
        // Structural replacement for the old RemindIfActionPendingAsync nudge (phase 8.7): the
        // reported bug ("/xyzzy_settings buried my still-outstanding card, no way to tell") can no
        // longer happen at all, because the settings menu itself can't be delivered while a card is
        // still outstanding - it queues behind it instead of interleaving before/after it.
        using var bot = await ThreePlayerGameAsync();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/addadmin"));

        // Round 1: Alice judges (gets a non-blocking notice); Bob and Carol answer so judging can
        // begin and Alice can pick a winner, rotating the judge to Bob for round 2.
        foreach (var playerId in new[] { Bob, Carol })
        {
            await bot.AnswerHandFullyAsync(playerId);
        }

        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the winner"));
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons![0]);

        // Round 2: Alice is now a non-judge answerer with a fresh, unactioned hand keyboard - the
        // settings menu should not appear at all while that's still outstanding.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        var afterSettingsCommand = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice);
        Assert.DoesNotContain("Cards Against Humanity settings", afterSettingsCommand.Text);

        // Resolving the card reveals the settings menu, exactly as if it had been waiting patiently.
        await bot.AnswerHandFullyAsync(Alice);
        Assert.Contains("Cards Against Humanity settings", bot.BotClient.SentMessages.Last(m => m.ChatId == Alice).Text);
    }

    [Fact]
    public async Task SettingsMenuAppearsImmediatelyWhenNothingIsOutstanding()
    {
        using var bot = await ThreePlayerGameAsync();

        // Alice is judging round 1 (a non-blocking notice, not a question) - nothing of hers is
        // outstanding, so the menu should appear the instant she asks for it.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        Assert.Contains("Cards Against Humanity settings", bot.BotClient.SentMessages.Last(m => m.ChatId == Alice).Text);
    }

    [Fact]
    public async Task ResetScoresZeroesEveryPlayer()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Score");
        var scoreMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, scoreMessage.Buttons!.First(b => b.Text == "Bob"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "5"));
        Assert.Equal(5, (await GameAsync(bot)).Players.First(p => p.DisplayName == "Bob").Wins);

        await OpenSettingsAndTapAsync(bot, Alice, "Reset Scores");

        Assert.Contains("Scores have been reset", bot.BotClient.SentMessages[^1].Text);
        Assert.All((await GameAsync(bot)).Players, p => Assert.Equal(0, p.Wins));
    }

    [Fact]
    public async Task GameLengthUpdatesTheQuestionLimit()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Game Length");
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "10"));

        Assert.Contains("Game length set to 10 questions", bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(10, (await GameAsync(bot)).QuestionLimit);
    }

    [Fact]
    public async Task RedealClearsHandsAndStartsAFreshQuestion()
    {
        using var bot = await ThreePlayerGameAsync();
        var beforeQuestion = (await GameAsync(bot)).CurrentQuestionCardId;

        var result = await OpenSettingsAndTapAsync(bot, Alice, "Re-deal");

        Assert.Equal("Redealt.", result);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Reshuffled"));
        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.Question, game.Status);
        Assert.All(game.Players, p => Assert.NotEmpty(p.Hand)); // cleared, then topped back up by BeginQuestionAsync
        Assert.NotNull(game.CurrentQuestionCardId);
        _ = beforeQuestion; // a fresh deal may legitimately draw the same question again by chance
    }

    [Fact]
    public async Task ForceQuestionAdvancesAStuckRound()
    {
        using var bot = await ThreePlayerGameAsync();

        var result = await OpenSettingsAndTapAsync(bot, Alice, "Force Question");

        Assert.Equal("Forced!", result);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Nobody answered in time"));
    }

    [Fact]
    public async Task ExtendResumesAStoppedGameWithTheSameRosterAndScores()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Score");
        var scoreMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, scoreMessage.Buttons!.First(b => b.Text == "Bob"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "7"));

        await OpenSettingsAndTapAsync(bot, Alice, "Abandon");
        Assert.Equal(XyzzyStatus.Stopped, (await GameAsync(bot)).Status);

        var result = await OpenSettingsAndTapAsync(bot, Alice, "Extend");

        Assert.Equal("Extended!", result);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Extending the game"));
        var game = await GameAsync(bot);
        Assert.Equal(XyzzyStatus.Question, game.Status);
        Assert.Equal(3, game.Players.Count);
        Assert.Equal(7, game.Players.First(p => p.DisplayName == "Bob").Wins);
    }
}
