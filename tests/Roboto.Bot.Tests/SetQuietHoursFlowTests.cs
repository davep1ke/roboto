namespace Roboto.Bot.Tests;

public class SetQuietHoursFlowTests
{
    private const long ChatId = -100;
    private const long UserId = 1;

    [Fact]
    public async Task FullFlowAsksStartThenEndThenConfirms()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        Assert.Contains("start time", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));
        Assert.Contains("end time", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(UserId, "08:00:00"));
        Assert.Contains("22:00:00", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("08:00:00", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task CancelAbortsWithoutSaving()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "cancel"));

        Assert.Contains("Cancelled", bot.BotClient.SentMessages[^1].Text);

        // Cancelling clears the pending reply - a stray follow-up message shouldn't be swallowed
        // as if it were still mid-conversation.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "/ping"));
        Assert.Equal("pong", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task DisableClearsAnyExistingQuietHours()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "08:00:00"));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "disable"));

        Assert.Contains("disabled", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task InvalidValueRePromptsInsteadOfCrashingOrAdvancing()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "not a time"));
        Assert.Contains("Invalid value", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("start time", bot.BotClient.SentMessages[^1].Text);

        // Still recoverable - a valid value at this point continues the flow rather than being lost.
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));
        Assert.Contains("end time", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task NoOpenPrivateChatExplainsInTheGroupInsteadOfSilentlyFailing()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(UserId);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));

        Assert.Contains("private chat", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task PendingReplySurvivesARestart()
    {
        // The actual point of persisting PendingReply via IStateStore rather than keeping it in
        // memory: a conversation started before a crash/restart should still resolve correctly
        // afterwards. Restart() builds a brand new service provider (fresh singletons, nothing
        // carried over in memory) pointed at the same on-disk SQLite file.
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, UserId, "/setquiethours"));
        await bot.SendAsync(TestBot.PrivateMessage(UserId, "22:00:00"));

        using var restarted = bot.Restart();
        await restarted.SendAsync(TestBot.PrivateMessage(UserId, "08:00:00"));

        Assert.Contains("22:00:00", restarted.BotClient.SentMessages[^1].Text);
        Assert.Contains("08:00:00", restarted.BotClient.SentMessages[^1].Text);
    }
}
