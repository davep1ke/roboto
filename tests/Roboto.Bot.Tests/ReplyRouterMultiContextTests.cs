using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;
using Telegram.Bot.Types;

namespace Roboto.Bot.Tests;

/// <summary>
/// Covers DmOutbox's strict one-thing-at-a-time-per-user delivery (phase 8.9, user's explicit design
/// call: "queue everything... and only send things when the window is clear" - a still-unanswered
/// question shouldn't be able to scroll off screen and get forgotten, and it shouldn't matter
/// whether the "thing" is a button or a typed reply, they're the same from the user's perspective).
/// Uses /setquiethours as the free-text vehicle - a real two-step DM flow triggered from a group but
/// always answered in the same private chat.
/// </summary>
public class ReplyRouterMultiContextTests
{
    private const long ChatA = -100;
    private const long ChatB = -200;
    private const long UserId = 1;

    [Fact]
    public async Task SecondFlowIsQueuedUntilTheFirstIsAnsweredThenDeliveredAutomatically()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));
        Assert.Contains("start time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        // B's question doesn't even get asked yet - A's is still outstanding.
        var deliveredCount = bot.BotClient.SentMessages.Count(m => m.ChatId == UserId);
        await bot.SendAsync(TestBot.GroupMessage(ChatB, UserId, "/setquiethours"));
        Assert.Equal(deliveredCount, bot.BotClient.SentMessages.Count(m => m.ChatId == UserId));

        // Answering A with plain text still works - only one thing is ever delivered at a time, so
        // there's nothing to disambiguate even though B is queued right behind it.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));
        Assert.Contains("end time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        await bot.SendAsync(TestBot.PrivateMessage(UserId, "08:00:00"));
        // Finishing A's flow reveals B's start-time question automatically - nobody had to re-ask.
        Assert.Contains("start time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        await bot.SendAsync(TestBot.PrivateMessage(UserId, "01:00:00"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "02:00:00"));

        var store = bot.Services.GetRequiredService<IStateStore>();
        var hoursA = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(ChatA), CancellationToken.None);
        var hoursB = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(ChatB), CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(22), hoursA!.Start);
        Assert.Equal(TimeSpan.FromHours(8), hoursA.End);
        Assert.Equal(TimeSpan.FromHours(1), hoursB!.Start);
        Assert.Equal(TimeSpan.FromHours(2), hoursB.End);
    }

    [Fact]
    public async Task SingleOutstandingFlowStillWorksWithoutAnExplicitReply()
    {
        // Backward-compatible common case - unchanged behavior when there's nothing else queued.
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));

        Assert.Contains("end time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);
    }

    [Fact]
    public async Task ReplyingToAnUntrackedMessageIsNotSwallowed()
    {
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));

        // Reply to some message ID we never tracked - not a match, shouldn't be treated as an
        // answer to the pending question, and the real command should still work normally.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "/ping", replyTo: new Message { Id = 999999 }));

        Assert.Equal("pong", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task SetupFlowWaitsBehindRealRoundPlayFromOtherGamesThenProceedsNormally()
    {
        // Direct reproduction of the user's own scenario: mid-round in two other games while
        // starting a brand new third game. Under strict one-thing-at-a-time delivery, the third
        // game's setup choice doesn't even get sent until the other two are cleared - once they
        // are, it proceeds exactly as if nothing else had ever been going on.
        const long GameChatA = -900;
        const long GameChatB = -901;
        const long GameChatC = -902;
        const long StarterA = 101;
        const long StarterB = 102;
        const long OtherAnswerer = 103;

        using var bot = new TestBot();

        async Task StartGameAndJoinUserIdAsync(long chatId, long starterId)
        {
            await bot.SendAsync(TestBot.GroupMessage(chatId, starterId, "/xyzzy_start", firstName: "Starter"));
            var choice = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 });
            await bot.SendCallbackAsync(starterId, choice.Buttons!.First(b => b.Text == "Use Defaults"));
            await bot.SendAsync(TestBot.GroupMessage(chatId, UserId, "/xyzzy_join", firstName: "Player"));
            await bot.SendAsync(TestBot.GroupMessage(chatId, OtherAnswerer, "/xyzzy_join", firstName: "Other"));

            var start = bot.BotClient.SentMessages.Last(m => m.ChatId == starterId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
            await bot.SendCallbackAsync(starterId, start.Buttons!.First(b => b.Text == "Start"));
        }

        // Game A: the starter (always Players[0]) judges round 1 - a non-blocking notice. UserId,
        // joined second, gets dealt a real hand keyboard, delivered immediately since they've got
        // nothing else outstanding yet.
        await StartGameAndJoinUserIdAsync(GameChatA, StarterA);
        var deliveredCount = bot.BotClient.SentMessages.Count(m => m.ChatId == UserId);
        var handA = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);
        Assert.NotNull(handA.Buttons);

        // Game B: UserId's hand this time is queued, not delivered - game A's hand is still
        // outstanding for them.
        await StartGameAndJoinUserIdAsync(GameChatB, StarterB);
        Assert.Equal(deliveredCount, bot.BotClient.SentMessages.Count(m => m.ChatId == UserId));

        // Game C's setup choice doesn't get anywhere near delivery either - it's third in line.
        await bot.SendAsync(TestBot.GroupMessage(GameChatC, UserId, "/xyzzy_start"));
        Assert.Equal(deliveredCount, bot.BotClient.SentMessages.Count(m => m.ChatId == UserId));

        // Resolving game A's hand reveals game B's hand next - not game C's setup choice yet.
        // AnswerHandFullyAsync (not a single tap) since either game's question could be a
        // multi-answer one, drawn randomly from CardCatalog same as any other game.
        await bot.AnswerHandFullyAsync(UserId);
        var handB = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);
        Assert.NotEqual(handA.Id, handB.Id);
        Assert.Contains(handB.Buttons!, b => b.CallbackData.StartsWith($"xy:a:{GameChatB}:", StringComparison.Ordinal));

        // Resolving game B's hand finally reveals game C's setup choice.
        await bot.AnswerHandFullyAsync(UserId);
        var choiceC = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);
        Assert.Contains(choiceC.Buttons!, b => b.Text == "Configure Game");

        // From here, game C's setup proceeds exactly as normal, undisturbed by any of that history.
        await bot.SendCallbackAsync(UserId, choiceC.Buttons!.First(b => b.Text == "Configure Game"));
        Assert.Contains("How many questions", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        await bot.SendAsync(TestBot.PrivateMessage(UserId, "5"));
        Assert.Contains("wait for answers", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        var gameC = await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(GameChatC, CancellationToken.None);
        Assert.Equal(5, gameC.QuestionLimit);
    }
}
