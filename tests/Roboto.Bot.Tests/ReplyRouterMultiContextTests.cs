using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
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
