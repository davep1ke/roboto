namespace Roboto.Bot.Tests;

public class AdminCommandsTests
{
    private const long ChatId = -100;
    private const long FirstUser = 1;
    private const long SecondUser = 2;

    [Fact]
    public async Task BareAddAdminWithNoExistingAdminsMakesTheCallerAdmin()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin"));

        Assert.Contains("Added", bot.BotClient.SentMessages[^1].Text);

        // Now an admin - a second /addadmin bare (no reply, admins already exist) should ask for a target.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin"));
        Assert.Contains("Reply to a message", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task ReplyAddsTheRepliedToUserAsAdmin()
    {
        using var bot = new TestBot();

        // FirstUser bootstraps as the first admin.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin"));

        var targetsMessage = TestBot.GroupMessage(ChatId, SecondUser, "hello", firstName: "Bob");
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin", replyTo: targetsMessage));

        Assert.Contains("Added Bob", bot.BotClient.SentMessages[^1].Text);

        // Bob is now an admin too - can add a third user.
        var thirdTarget = TestBot.GroupMessage(ChatId, userId: 3, "hi", firstName: "Carol");
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/addadmin", replyTo: thirdTarget));
        Assert.Contains("Added Carol", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task NonAdminCannotAddAdmins()
    {
        using var bot = new TestBot();

        // FirstUser bootstraps as the first (and only) admin.
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin"));

        var targetsMessage = TestBot.GroupMessage(ChatId, userId: 3, "hi", firstName: "Carol");
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/addadmin", replyTo: targetsMessage));

        // SecondUser isn't an admin, so this should be rejected (the "insufficient privileges" link),
        // not silently add Carol.
        Assert.DoesNotContain("Added", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task RemoveAdminWithNoAdminsExplainsInsteadOfCrashing()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/removeadmin"));

        Assert.Contains("doesn't have any admins", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task RemoveAdminByReplyRemovesThem()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin"));
        var targetsMessage = TestBot.GroupMessage(ChatId, SecondUser, "hello", firstName: "Bob");
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/addadmin", replyTo: targetsMessage));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/removeadmin", replyTo: targetsMessage));

        Assert.Contains("Removed Bob", bot.BotClient.SentMessages[^1].Text);

        // Bob is no longer an admin - can't remove FirstUser now.
        var firstUsersMessage = TestBot.GroupMessage(ChatId, FirstUser, "hi");
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/removeadmin", replyTo: firstUsersMessage));
        Assert.DoesNotContain("Removed", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task AdminCommandsDoNotApplyInPrivateChats()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/addadmin"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/removeadmin"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);
    }
}
