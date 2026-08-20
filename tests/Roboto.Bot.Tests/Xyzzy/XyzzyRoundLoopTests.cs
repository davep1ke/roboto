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
        await bot.SendCallbackAsync(userId, button);
    }

    /// <summary>Taps the "Start" button XyzzyRoundService.FinishSetupAsync DMs the starter once
    /// setup's done - replaces the old group-chat /xyzzy_begin command entirely (phase 8.6).</summary>
    private static async Task BeginRoundAsync(TestBot bot, long starterId)
    {
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        var button = startMessage.Buttons!.First(b => b.Text == "Start");
        await bot.SendCallbackAsync(starterId, button);
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
    public async Task KickingAPlayerBelowMinPlayersIsToppedBackUpWithABotAtTheNextRound()
    {
        // Closes a real gap (MIGRATION.md, fixed alongside this test): FillBotSlots used to run
        // only once, at the very first round - a kick or a leave that dropped the real player
        // count below MinPlayers was never re-topped-up. Now it re-runs at the start of every
        // round (BeginQuestionAsync itself), so this should self-heal the moment the next round
        // begins, with no special-casing needed in the kick/leave handlers themselves.
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice); // Alice judges round 1 (a non-blocking notice), queue clear

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        var menuMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, menuMessage.Buttons!.First(b => b.Text == "Kick"));
        var kickMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, kickMessage.Buttons!.First(b => b.Text == "Carol"));

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        Assert.Equal(2, (await games.GetAsync(ChatId, CancellationToken.None)).Players.Count); // Alice + Bob only

        // Finish round 1 (Bob's the only remaining non-judge answerer) so it advances to round 2.
        await bot.AnswerHandFullyAsync(Bob);
        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the best answer"));
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons![0]);

        var game = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(2, game.RoundNumber);
        Assert.Equal(3, game.Players.Count);
        Assert.Contains(game.Players, p => p.IsBot);
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
        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the best answer"));
        Assert.Equal(2, judgeMessage.Buttons!.Count);
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons[0]);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point"));

        // Round 2: judge rotates to Bot1 (index 1) - Alice just needs to answer, and since the
        // other non-judge player is also a bot (already auto-submitted), her tap alone should
        // complete the round: the bot judge auto-picks a winner with no further input from anyone,
        // chaining all the way to round 3 within this one callback.
        await bot.AnswerHandFullyAsync(Alice);

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
            await bot.AnswerHandFullyAsync(playerId);
        }

        // Judging should have kicked in automatically once both answered - the judge got a DM
        // with a keyboard of the submitted answers.
        var judgeKeyboardMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 });
        Assert.Equal(2, judgeKeyboardMessage.Buttons!.Count);
        Assert.Contains("Pick the best answer", bot.BotClient.SentMessages.First(m => m.ChatId == judgeId && m.Text.Contains("Pick the best answer")).Text);

        // Judge picks a winner.
        var winningButton = judgeKeyboardMessage.Buttons[0];
        await bot.SendCallbackAsync(judgeId, winningButton);

        // A winner was announced in the group, and the round auto-advanced to a new question
        // (round 2) - everyone (including the old judge, now presumably answering) got a fresh
        // hand-selection DM or a "you're judging" DM.
        var groupMessages = bot.BotClient.SentMessages.Where(m => m.ChatId == ChatId).ToList();
        Assert.Contains(groupMessages, m => m.Text.Contains("wins a point"));

        // Ports legacy's judgesResponse win message: every player's score, not just the winner's -
        // previously this only showed the winner's own new tally. The winning answer itself is
        // bolded (legacy wraps it in "*...*" and sends with markDown=true) so it stands out.
        var winMessage = groupMessages.Last(m => m.Text.Contains("wins a point"));
        Assert.Contains("Alice", winMessage.Text);
        Assert.Contains("Bob", winMessage.Text);
        Assert.Contains("Carol", winMessage.Text);
        Assert.Contains($"*{winningButton.Text}*", winMessage.Text);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_status"));
        var status = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Round 2", status);
        Assert.Contains("1 points.", status);
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
        await bot.SendCallbackAsync(answerer, button);
        Assert.Contains("submitted", bot.BotClient.AnsweredCallbacks[^1].Text!);

        // Tapping again on the same (now-resolved and no longer their DmOutbox head) message
        // should be rejected - answering already popped it from their queue, so a second tap on it
        // is now indistinguishable from any other stale button.
        await bot.SendCallbackAsync(answerer, button);
        Assert.Contains("isn't valid any more", bot.BotClient.AnsweredCallbacks[^1].Text!);
    }

    [Fact]
    public async Task TheJudgeCannotSubmitAnAnswer()
    {
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice);

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));

        // The judge never got a hand keyboard to tap in the first place - they have nothing
        // outstanding at all, so even a forged callback for a card they don't hold is rejected at
        // the DmOutbox level (phase 8.9) before it ever reaches the game logic that used to be the
        // only thing guarding against this.
        var forgedData = new XyzzyCallbackData("a", ChatId, 1, "a01").Encode();
        await bot.SendCallbackAsync(judgeId, forgedData, messageId: 0);
        Assert.Contains("isn't valid any more", bot.BotClient.AnsweredCallbacks[^1].Text!);
    }

    [Fact]
    public async Task MultiAnswerQuestionRequiresPickingTheFullSetBeforeJudgingBegins()
    {
        // CardCatalog's multi-answer card ("q31", AnswerCount=2) isn't drawn naturally here -
        // forced directly via the repository so this test is deterministic rather than depending
        // on a random deck shuffle happening to land on it.
        using var bot = await StartedGameWithThreePlayersAsync();
        await BeginRoundAsync(bot, Alice);

        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        game.CurrentQuestionCardId = "q31";
        await games.SaveAsync(game, CancellationToken.None);

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal))));
        var answerers = new[] { Alice, Bob, Carol }.Where(id => id != judgeId).ToArray();

        // First card from the first answerer isn't enough on its own - no judging keyboard yet,
        // and the re-offered hand excludes the card they already picked.
        var firstAnswerer = answerers[0];
        var firstButton = bot.BotClient.SentMessages.Last(m => m.ChatId == firstAnswerer && m.Buttons is { Count: > 0 }).Buttons![0];
        await bot.SendCallbackAsync(firstAnswerer, firstButton);
        Assert.Contains("Pick your next card", bot.BotClient.AnsweredCallbacks[^1].Text!);
        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.ChatId == judgeId && m.Text.Contains("Pick the best answer"));

        var secondHandMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == firstAnswerer && m.Buttons is { Count: > 0 });
        Assert.DoesNotContain(secondHandMessage.Buttons!, b => b.CallbackData == firstButton.CallbackData);

        // Finish both answerers - now judging should start, one button per submitter (not per
        // card), with each submission's two cards joined with " >> ".
        await bot.AnswerHandFullyAsync(firstAnswerer);
        await bot.AnswerHandFullyAsync(answerers[1]);

        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Text.Contains("Pick the best answer"));
        Assert.Equal(2, judgeMessage.Buttons!.Count);
        Assert.Contains(judgeMessage.Buttons!, b => b.Text.Contains(">>", StringComparison.Ordinal));

        await bot.SendCallbackAsync(judgeId, judgeMessage.Buttons![0]);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("wins a point"));

        var finalGame = await games.GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(2, finalGame.RoundNumber);
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
            await bot.AnswerHandFullyAsync(playerId);
        }
        var judgeKeyboardMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == judgeId && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(judgeId, judgeKeyboardMessage.Buttons![0]);

        // Now replay the very first (round-1) button - round 2 has already dealt them a new hand
        // (a different message), so this old one is no longer their DmOutbox head at all and gets
        // rejected before it can even reach the round-staleness check XyzzyCallbackData's own round
        // number provides as a second line of defense.
        await bot.SendCallbackAsync(answerers[0], staleButton);
        Assert.Contains("isn't valid any more", bot.BotClient.AnsweredCallbacks[^1].Text!);
    }
}
