using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzyRoundLoopTests
{
    private const long ChatId = -300;
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private static async Task<TestBot> StartedGameWithThreePlayersAsync()
    {
        var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        return bot;
    }

    /// <summary>Taps the "Use Defaults" button XyzzyStartCommand's setup keyboard sends over DM.</summary>
    private static async Task TapUseDefaultsAsync(TestBot bot, long userId)
    {
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == userId && m.Buttons is { Count: > 0 });
        var button = choiceMessage.Buttons!.First(b => b.Text == "Use Defaults");
        await bot.SendCallbackAsync(userId, button.CallbackData);
    }

    /// <summary>Taps the "Start" button XyzzyRoundService.FinishSetupAsync DMs the starter once
    /// setup's done - replaces the old group-chat /xyzzy_begin command entirely (phase 8.6).</summary>
    private static async Task BeginRoundAsync(TestBot bot, long starterId)
    {
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        var button = startMessage.Buttons!.First(b => b.Text == "Start");
        await bot.SendCallbackAsync(starterId, button.CallbackData);
    }

    private static SentButton FirstHandButton(TestBot bot, long playerId) =>
        bot.BotClient.SentMessages.Last(m => m.ChatId == playerId && m.Buttons is { Count: > 0 }).Buttons![0];

    [Fact]
    public async Task TappingStartWithTooFewPlayersFillsEmptySlotsWithBots()
    {
        // Replaces the old "/xyzzy_begin force" escape hatch entirely - user's explicit feedback
        // that solo/two-player testing was always awkward. No admin gate any more either: only the
        // starter ever receives the Start button (it's in their own DM), so having the button at
        // all *is* the access control - nothing else to check.
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);

        await BeginRoundAsync(bot, Alice); // only the starter has joined so far

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_status"));
        var status = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Round 1", status);
        Assert.Contains("(bot)", status);
        Assert.Equal(3, status.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SoloPlayerCanCompleteFullRoundsAgainstBots()
    {
        // Proves the actual bot-play mechanics, not just that bots get added: a solo starter needs
        // no other real player at any point - bots answer and judge on their own ("pick randomly
        // for now" per the original ask). Judge rotation is deterministic here (Players is always
        // [Alice, Bot1, Bot2] in insertion order), so which round has a human vs. bot judge is
        // known ahead of time, not just "whichever it happens to be".
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await BeginRoundAsync(bot, Alice);

        // Round 1: Alice (the starter) is first in judge rotation - both bots should already have
        // auto-submitted an answer with no callback from anyone, so her judging keyboard should be
        // waiting immediately.
        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the winner"));
        Assert.Equal(2, judgeMessage.Buttons!.Count);
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons[0].CallbackData);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins the round"));

        // Round 2: judge rotates to Bot1 (index 1) - Alice just needs to answer, and since the
        // other non-judge player is also a bot (already auto-submitted), her tap alone should
        // complete the round: the bot judge auto-picks a winner with no further input from anyone,
        // chaining all the way to round 3 within this one callback.
        var handMessage = bot.BotClient.SentMessages.Last(m =>
            m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal)));
        await bot.SendCallbackAsync(Alice, handMessage.Buttons![0].CallbackData);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_status"));
        Assert.Contains("Round 3", bot.BotClient.SentMessages[^1].Text);

        // Exactly one player wins each completed round - who (Alice or a bot, since judges don't
        // submit) is random, but the total across the roster after 2 rounds isn't.
        var game = await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(2, game.Players.Sum(p => p.Wins));
    }

    [Fact]
    public async Task GameEndsWhenOnlyBotsAreLeft()
    {
        // Safety guard, not just a nicety: without this, a game that lost all its real players
        // would otherwise keep dealing itself hands forever with nobody watching (bots always
        // auto-act, so nothing would ever pause it).
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        await TapUseDefaultsAsync(bot, Alice);
        await BeginRoundAsync(bot, Alice);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_leave"));

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Not enough real players"));
    }

    [Fact]
    public async Task FullRoundEndToEndDealAnswerJudgeAndAdvance()
    {
        using var bot = await StartedGameWithThreePlayersAsync();

        await BeginRoundAsync(bot, Alice);

        // Everyone got dealt a hand except whoever's judging this round.
        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();
        Assert.Equal(2, answerers.Length);

        // Both non-judge players play a card from their hand.
        foreach (var playerId in answerers)
        {
            var button = FirstHandButton(bot, playerId);
            await bot.SendCallbackAsync(playerId, button.CallbackData);
        }

        // Judging should have kicked in automatically once both answered - the judge got a DM
        // with a keyboard of the submitted answers.
        var judgeKeyboardMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 });
        Assert.Equal(2, judgeKeyboardMessage.Buttons!.Count);
        Assert.Contains("Pick the winner", bot.BotClient.SentMessages.First(m => m.ChatId == judgeId && m.Text.Contains("Pick the winner")).Text);

        // Judge picks a winner.
        var winningButton = judgeKeyboardMessage.Buttons[0];
        await bot.SendCallbackAsync(judgeId, winningButton.CallbackData);

        // A winner was announced in the group, and the round auto-advanced to a new question
        // (round 2) - everyone (including the old judge, now presumably answering) got a fresh
        // hand-selection DM or a "you're judging" DM.
        var groupMessages = bot.BotClient.SentMessages.Where(m => m.ChatId == ChatId).ToList();
        Assert.Contains(groupMessages, m => m.Text.Contains("wins the round"));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_status"));
        var status = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Round 2", status);
        Assert.Contains("1 win(s)", status);
    }

    [Fact]
    public async Task AnsweringTwiceInTheSameRoundIsRejected()
    {
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice);

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));
        var answerer = new[] { Alice, Bob, Carol }.First(id => id != judgeId);

        var button = FirstHandButton(bot, answerer);
        await bot.SendCallbackAsync(answerer, button.CallbackData);
        Assert.Contains("submitted", bot.BotClient.AnsweredCallbacks[^1].Text!);

        // Tapping again (even a different card from the same hand-deal message) should be rejected -
        // the first card was already consumed from Hand and the round already has their submission.
        var repeatAnswer = FirstHandButton(bot, answerer);
        await bot.SendCallbackAsync(answerer, repeatAnswer.CallbackData);
        Assert.Contains("already answered", bot.BotClient.AnsweredCallbacks[^1].Text!);
    }

    [Fact]
    public async Task TheJudgeCannotSubmitAnAnswer()
    {
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice);

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));

        // The judge never got a hand keyboard to tap in the first place, but even a forged
        // callback for a card they don't hold should be rejected cleanly.
        var forgedData = new XyzzyCallbackData("a", ChatId, 1, "a01").Encode();
        await bot.SendCallbackAsync(judgeId, forgedData);
        string[] acceptableReasons = ["The judge doesn't submit an answer.", "That card isn't in your hand any more."];
        Assert.Contains(bot.BotClient.AnsweredCallbacks[^1].Text!, acceptableReasons);
    }

    [Fact]
    public async Task AStaleTapAfterTheRoundHasMovedOnIsRejected()
    {
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice);

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();

        var staleButton = FirstHandButton(bot, answerers[0]);

        // Play the round out normally first so the round number advances past the stale button.
        foreach (var playerId in answerers)
        {
            await bot.SendCallbackAsync(playerId, FirstHandButton(bot, playerId).CallbackData);
        }
        var judgeKeyboardMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(judgeId, judgeKeyboardMessage.Buttons![0].CallbackData);

        // Now replay the very first (round-1) button - the round's moved on to round 2.
        await bot.SendCallbackAsync(answerers[0], staleButton.CallbackData);
        Assert.Contains("round's already over", bot.BotClient.AnsweredCallbacks[^1].Text!);
    }
}
