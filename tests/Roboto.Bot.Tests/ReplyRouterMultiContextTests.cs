using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;
using Telegram.Bot.Types;

namespace Roboto.Bot.Tests;

/// <summary>
/// Covers ReplyRouter holding several pending replies for the same user at once (phase 9, per user
/// feedback: "not uncommon for users to be in multiple groups" and the settings menu "all going to
/// pot" if two flows collided). Uses /setquiethours as the test vehicle - a real two-step DM flow
/// that's triggered from a group but always answered in the same private chat, exactly the shape
/// that needs disambiguating when it happens for two different chats at once.
/// </summary>
public class ReplyRouterMultiContextTests
{
    private const long ChatA = -100;
    private const long ChatB = -200;
    private const long UserId = 1;

    [Fact]
    public async Task TwoSimultaneousFlowsAreDisambiguatedByReplyToEvenWithIdenticalQuestionText()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));
        var askStartA = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);

        await bot.SendAsync(TestBot.GroupMessage(ChatB, UserId, "/setquiethours"));
        var askStartB = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);

        // Answer B's start-time question first.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00", replyTo: TestBot.ReplyTo(askStartB)));
        var askEndB = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);
        Assert.Contains("end time", askEndB.Text);

        // Now answer A's start-time question - despite askEndB (identical wording) also being
        // outstanding, replying to askStartA specifically must resolve A's flow, not B's.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "06:00:00", replyTo: TestBot.ReplyTo(askStartA)));
        var askEndA = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId);
        Assert.Contains("end time", askEndA.Text);
        Assert.NotEqual(askEndB.Id, askEndA.Id);

        // Finish both, again by replying to each flow's own end-time question specifically.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "23:00:00", replyTo: TestBot.ReplyTo(askEndB)));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "07:00:00", replyTo: TestBot.ReplyTo(askEndA)));

        var store = bot.Services.GetRequiredService<IStateStore>();
        var hoursA = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(ChatA), CancellationToken.None);
        var hoursB = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(ChatB), CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(6), hoursA!.Start);
        Assert.Equal(TimeSpan.FromHours(7), hoursA.End);
        Assert.Equal(TimeSpan.FromHours(22), hoursB!.Start);
        Assert.Equal(TimeSpan.FromHours(23), hoursB.End);
    }

    [Fact]
    public async Task AmbiguousPlainTextAsksForAnExplicitReplyInsteadOfGuessing()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.GroupMessage(ChatB, UserId, "/setquiethours"));

        var before = bot.BotClient.SentMessages.Count(m => m.ChatId == UserId);
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00")); // no reply-to - ambiguous with two outstanding

        var after = bot.BotClient.SentMessages.Where(m => m.ChatId == UserId).Skip(before).ToList();
        Assert.Single(after);
        Assert.Contains("reply directly", after[0].Text);

        // Neither flow silently advanced or got clobbered - both still resolvable afterward.
        var askAgain = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId && m.Id != after[0].Id);
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00", replyTo: TestBot.ReplyTo(askAgain)));
        Assert.Contains("end time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);
    }

    [Fact]
    public async Task SingleOutstandingFlowStillWorksWithoutAnExplicitReply()
    {
        // Backward-compatible common case - unchanged behavior when there's nothing to disambiguate.
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatA, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));

        Assert.Contains("end time", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);
    }

    [Fact]
    public async Task SetupFlowIsUndisturbedByConcurrentRoundPlayInOtherGames()
    {
        // Direct reproduction of the user's own scenario: mid-round in two other games (Telegram
        // group chats, real gameplay, dealing hands / "you're judging" notices - all of it
        // button-driven and never touching ReplyRouter at all) while starting a brand new third
        // game and moving into its free-text "configure" step. Answering that question must always
        // advance game C's setup specifically, undisturbed by anything happening in A or B - and
        // since round-play never creates a competing PendingReply, no reply-to is even needed here.
        const long GameChatA = -900;
        const long GameChatB = -901;
        const long GameChatC = -902;

        using var bot = new TestBot();

        async Task StartSoloBotFilledGameAsync(long chatId)
        {
            await bot.SendAsync(TestBot.GroupMessage(chatId, UserId, "/xyzzy_start"));
            var choice = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Use Defaults"));
            await bot.SendCallbackAsync(UserId, choice.Buttons!.First(b => b.Text == "Use Defaults").CallbackData);
            var start = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Start"));
            await bot.SendCallbackAsync(UserId, start.Buttons!.First(b => b.Text == "Start").CallbackData);
        }

        // Two other real games, both mid-round, both having DMed this user actual gameplay
        // messages (a hand keyboard or a "you're judging" notice, depending on the deal).
        await StartSoloBotFilledGameAsync(GameChatA);
        await StartSoloBotFilledGameAsync(GameChatB);

        // Start a third game and pick "Configure Game" - the point a real free-text question
        // becomes outstanding.
        await bot.SendAsync(TestBot.GroupMessage(GameChatC, UserId, "/xyzzy_start"));
        var choiceC = bot.BotClient.SentMessages.Last(m => m.ChatId == UserId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.Text == "Configure Game"));
        await bot.SendCallbackAsync(UserId, choiceC.Buttons!.First(b => b.Text == "Configure Game").CallbackData);
        Assert.Contains("How many questions", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        // While that's outstanding, act on whichever of A/B this user still has a card to play for
        // (real gameplay, purely button-driven) - must not touch game C's pending question at all.
        var otherHandMessage = bot.BotClient.SentMessages.LastOrDefault(m =>
            m.ChatId == UserId && m.Buttons is { Count: > 0 } && m.Buttons.Any(b => b.CallbackData.StartsWith("xy:a:", StringComparison.Ordinal)));
        if (otherHandMessage is not null)
        {
            await bot.SendCallbackAsync(UserId, otherHandMessage.Buttons![0].CallbackData);
        }

        // Answer game C's question with plain text - it's still the *only* free-text thing
        // pending (A/B never created one), so no reply-to is needed, and it resolves to C.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "5"));
        Assert.Contains("wait for answers", bot.BotClient.SentMessages.Last(m => m.ChatId == UserId).Text);

        var gameC = await bot.Services.GetRequiredService<XyzzyGameRepository>().GetAsync(GameChatC, CancellationToken.None);
        Assert.Equal(5, gameC.QuestionLimit);
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
}
