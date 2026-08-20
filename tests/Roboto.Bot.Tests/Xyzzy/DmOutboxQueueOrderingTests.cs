using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Xyzzy;

/// <summary>Covers a real bug found live-testing against the beefy test bot: in a bot-heavy game,
/// answering a card can cascade synchronously through judging and straight into dealing a brand new
/// round (XyzzyRoundService.BeginQuestionAsync/BeginJudgingAsync, reached via SubmitAnswerAsync when
/// the judge and/or remaining answerers are bots). That cascade runs entirely inside DmOutbox's
/// "resolving window" (CallbackQueryRouter.HandleAsync, between RemoveCurrentHeadAsync and
/// PumpNextAsync) for whichever real player's tap triggered it - and the window's queue-jump
/// privilege, meant only for a flow's own genuine continuation (e.g. "pick your next card"), was
/// blanket-applied to *everything* enqueued during it, including the brand new round's own hand
/// keyboard. That let a freshly-dealt round preempt something the player had already explicitly
/// asked for and was legitimately waiting on, like /xyzzy_settings - repeatedly, every time they
/// answered another card, since resolving their own round just dealt them straight into the next
/// one. Fixed via DmOutbox's new allowFrontInsert parameter (default true, only BeginQuestionAsync/
/// BeginJudgingAsync's broadcasts pass false).</summary>
public class DmOutboxQueueOrderingTests
{
    private const long ChatId = -950;
    private const long Alice = 1;

    [Fact]
    public async Task ANewRoundDealtViaABotCascadeDoesNotPreemptAnAlreadyQueuedSettingsMenu()
    {
        using var bot = new TestBot();

        // Solo starter - FillBotSlots tops up to MinPlayers (3) with 2 bots the moment the first
        // round is dealt. Judge rotation is deterministic: round 1 = Players[0] (Alice, since
        // JudgePlayerId starts null), round 2 = Players[1] (the first bot) - so by round 2 Alice is
        // a non-judge answerer with a *bot* judging, exactly the shape that lets a cascade run to
        // completion (judging, dealing round 3) with no further human input once Alice submits.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_start", firstName: "Alice"));
        var choiceMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, choiceMessage.Buttons!.First(b => b.Text == "Use Defaults"));
        var startMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
        await bot.SendCallbackAsync(Alice, startMessage.Buttons!.First(b => b.Text == "Start"));

        // Round 1: Alice judges (both bots already auto-answered) - pick a winner to reach round 2.
        var judgeMessage = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Text.Contains("Pick the winner"));
        await bot.SendCallbackAsync(Alice, judgeMessage.Buttons![0]);

        // Round 2: a bot judges; Alice is a non-judge answerer with an outstanding hand keyboard.
        var round2Hand = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });

        // Alice asks for settings while her round-2 card is still outstanding - queues behind it,
        // not delivered yet.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/xyzzy_settings"));
        Assert.DoesNotContain("Cards Against Humanity settings", bot.BotClient.SentMessages.Last(m => m.ChatId == Alice).Text);

        // Answering her round-2 card is the last submission needed - completes the round, and since
        // the judge is a bot, judging and dealing round 3 both cascade synchronously within this one
        // callback, all before DmOutbox.PumpNextAsync ever runs for Alice.
        await bot.SendCallbackAsync(Alice, round2Hand.Buttons![0]);

        // The settings menu - queued first - must win, not round 3's freshly-dealt hand.
        Assert.Contains("Cards Against Humanity settings", bot.BotClient.SentMessages.Last(m => m.ChatId == Alice).Text);
    }
}
