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
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "defaults", firstName: "Alice")); // phase 8.5 setup wizard - quick path to Invites
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Carol, "/xyzzy_join", firstName: "Carol"));
        return bot;
    }

    private static SentButton FirstHandButton(TestBot bot, long playerId) =>
        bot.BotClient.SentMessages.Last(m => m.ChatId == playerId && m.Buttons is { Count: > 0 }).Buttons![0];

    [Fact]
    public async Task BeginRequiresAnAdminAndEnoughPlayers()
    {
        using var bot = await StartedGameWithThreePlayersAsync();

        // Bob isn't an admin (no admins have ever been set, so per ChatState.IsAdmin, *everyone*
        // currently counts as admin - bootstrap the chat's own admin list first via /addadmin so
        // this actually exercises the gate rather than trivially passing).
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/addadmin"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Bob, "/xyzzy_begin", firstName: "Bob"));
        Assert.Contains("Only a chat admin", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_leave"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/addadmin"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));
        Assert.Contains("Need at least 3 players", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task FullRoundEndToEndDealAnswerJudgeAndAdvance()
    {
        using var bot = await StartedGameWithThreePlayersAsync();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));

        // Everyone got dealt a hand except whoever's judging this round.
        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 }));
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
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 }));
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
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 }));

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
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_begin"));

        var judgeId = new[] { Alice, Bob, Carol }.First(id =>
            !bot.BotClient.SentMessages.Any(m => m.ChatId == id && m.Buttons is { Count: > 0 }));
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
