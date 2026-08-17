namespace Roboto.Bot.Tests;

public class CommandRouterTests
{
    [Fact]
    public async Task PingRepliesWithPong()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(userId: 1, "/ping"));

        var sent = Assert.Single(bot.BotClient.SentMessages);
        Assert.Equal("pong", sent.Text);
    }

    [Fact]
    public async Task HelpListsAllRegisteredCommands()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(userId: 1, "/help"));

        var sent = Assert.Single(bot.BotClient.SentMessages);
        Assert.Contains("/ping", sent.Text);
        Assert.Contains("/stop", sent.Text);
        Assert.Contains("/setquiethours", sent.Text);
    }

    [Fact]
    public async Task StopSilencesFurtherCommandsInThatGroup()
    {
        using var bot = new TestBot();
        const long chatId = -100;

        await bot.SendAsync(TestBot.GroupMessage(chatId, userId: 1, "/stop"));
        bot.BotClient.SentMessages.Clear();

        await bot.SendAsync(TestBot.GroupMessage(chatId, userId: 1, "/ping"));

        Assert.Empty(bot.BotClient.SentMessages);
    }

    [Fact]
    public async Task StartUnsilencesAfterStop()
    {
        using var bot = new TestBot();
        const long chatId = -100;

        await bot.SendAsync(TestBot.GroupMessage(chatId, userId: 1, "/stop"));
        await bot.SendAsync(TestBot.GroupMessage(chatId, userId: 1, "/start"));
        bot.BotClient.SentMessages.Clear();

        await bot.SendAsync(TestBot.GroupMessage(chatId, userId: 1, "/ping"));

        var sent = Assert.Single(bot.BotClient.SentMessages);
        Assert.Equal("pong", sent.Text);
    }

    [Fact]
    public async Task MutingOneGroupDoesNotAffectAnother()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(chatId: -100, userId: 1, "/stop"));
        bot.BotClient.SentMessages.Clear();

        await bot.SendAsync(TestBot.GroupMessage(chatId: -200, userId: 1, "/ping"));

        var sent = Assert.Single(bot.BotClient.SentMessages);
        Assert.Equal("pong", sent.Text);
    }

    [Fact]
    public async Task MutingDoesNotApplyToPrivateChats()
    {
        // The `chat` concept (and therefore muting) only exists for group chats, matching the
        // legacy app - /stop in a private chat is a no-op that just explains that.
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(userId: 1, "/stop"));
        bot.BotClient.SentMessages.Clear();

        await bot.SendAsync(TestBot.PrivateMessage(userId: 1, "/ping"));

        var sent = Assert.Single(bot.BotClient.SentMessages);
        Assert.Equal("pong", sent.Text);
    }

    [Fact]
    public async Task UnknownCommandIsSilentlyIgnored()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(userId: 1, "/notarealcommand"));

        Assert.Empty(bot.BotClient.SentMessages);
    }
}
