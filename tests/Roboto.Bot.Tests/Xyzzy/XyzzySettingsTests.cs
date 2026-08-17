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
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults").CallbackData); // quick path to Invites
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        return bot;
    }

    private static async Task<XyzzyGameState> GameAsync(TestBot bot) =>
        await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);

    /// <summary>Opens the settings menu and taps the named top-level button ("Abandon" / "Timeout" /
    /// "Throttle" / "Kick" / "Score" / "Cancel").</summary>
    private static async Task<string> OpenSettingsAndTapAsync(TestBot bot, long userId, string buttonText)
    {
        await bot.SendAsync(TestBot.GroupMessage(ChatId, userId, "/xyzzy_settings"));
        var menuMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == userId && m.Buttons is { Count: > 0 });
        var button = menuMessage.Buttons!.First(b => b.Text == buttonText);
        await bot.SendCallbackAsync(userId, button.CallbackData);
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

        await bot.SendCallbackAsync(Alice, kickMessage.Buttons!.First(b => b.Text == "Bob").CallbackData);
        Assert.Contains("Bob was kicked", bot.BotClient.SentMessages[^1].Text);

        var game = await GameAsync(bot);
        Assert.DoesNotContain(game.Players, p => p.DisplayName == "Bob");
        Assert.Equal(2, game.Players.Count);
    }

    [Fact]
    public async Task ATamperedKickTargetIsRejectedCleanly()
    {
        using var bot = await ThreePlayerGameAsync();

        await bot.SendCallbackAsync(Alice, $"xy:se:{ChatId}:kick:999999");
        Assert.Contains("isn't in the game", bot.BotClient.AnsweredCallbacks[^1].Text!);
        Assert.Equal(3, (await GameAsync(bot)).Players.Count);
    }

    [Fact]
    public async Task ScoreShowsAKeyboardThenAsksForThePointsValue()
    {
        using var bot = await ThreePlayerGameAsync();

        await OpenSettingsAndTapAsync(bot, Alice, "Score");
        var scoreMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, scoreMessage.Buttons!.First(b => b.Text == "Bob").CallbackData);
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
        Assert.Equal(XyzzyStatus.Invites, (await GameAsync(bot)).Status);
    }

    [Fact]
    public async Task SettingsDoNotApplyWithNoGameRunning()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        Assert.Contains("No game running", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task StillWaitingOnAnAnswerIsRemindedAfterSettingsCloses()
    {
        // Directly reproduces the reported bug: running /xyzzy_settings mid-round used to leave no
        // way to tell a card selection was still outstanding once the settings interaction ended.
        // Only an admin can open /xyzzy_settings, and judge rotation is deterministic (Players[0] =
        // Alice judges round 1), so play round 1 out fully first - round 2's judge rotates to Bob,
        // leaving Alice (the admin) with an outstanding card of her own to answer.
        using var bot = await ThreePlayerGameAsync();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/addadmin"));

        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start").CallbackData);

        foreach (var playerId in new[] { Bob, Carol })
        {
            var handMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == playerId && m.Buttons is { Count: > 0 });
            await bot.SendCallbackAsync(playerId, handMessage.Buttons![0].CallbackData);
        }

        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the winner"));
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons![0].CallbackData);

        // Round 2: Alice is now a non-judge answerer with a fresh, unactioned hand keyboard.
        var sentBeforeSettings = bot.BotClient.SentMessages.Count(m => m.ChatId == Alice);
        await OpenSettingsAndTapAsync(bot, Alice, "Cancel");

        var afterSettings = bot.BotClient.SentMessages.Where(m => m.ChatId == Alice).Skip(sentBeforeSettings).ToList();
        Assert.Contains(afterSettings, m => m.Text.Contains("Reminder") && m.Buttons is { Count: > 0 });
    }

    [Fact]
    public async Task NoReminderIsSentIfNothingIsOutstanding()
    {
        using var bot = await ThreePlayerGameAsync();

        // Still in Invites - nobody has an outstanding card/judge action yet.
        await OpenSettingsAndTapAsync(bot, Alice, "Cancel");

        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.Text.Contains("Reminder"));
    }
}
