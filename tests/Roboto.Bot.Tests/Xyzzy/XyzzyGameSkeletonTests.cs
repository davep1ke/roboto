namespace Roboto.Bot.Tests.Xyzzy;

public class XyzzyGameSkeletonTests
{
    private const long ChatId = -200;
    private const long FirstUser = 1;
    private const long SecondUser = 2;
    private const long ThirdUser = 3;

    [Fact]
    public async Task StartCreatesAGameWithTheCallerAsFirstPlayer()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));

        Assert.Contains("started a game", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_status"));
        var status = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Invites", status);
        Assert.Contains("Test", status); // default TestBot display name
    }

    [Fact]
    public async Task CannotStartASecondGameWhileOneIsRunning()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_start"));

        Assert.Contains("already in progress", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task JoinRequiresAGameToAlreadyBeRunning()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_join"));

        Assert.Contains("No game's running", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task JoinAddsThePlayerAndDMsThemAConfirmation()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_join", firstName: "Bob"));

        // Confirmation DM sent directly to the joining player, plus the group announcement.
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == SecondUser && m.Text.Contains("You joined"));
        Assert.Contains("Bob joined", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, ThirdUser, "/xyzzy_status", firstName: "Carol"));
        var status = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("2/3 minimum", status);
    }

    [Fact]
    public async Task CannotJoinTwice()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_join"));

        Assert.Contains("already in this game", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task JoinFailsCleanlyIfThePlayerCantBeDmed()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(SecondUser);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_join", firstName: "Bob"));

        Assert.Contains("couldn't DM", bot.BotClient.SentMessages[^1].Text);

        // And they genuinely weren't added - a follow-up join attempt should still succeed once
        // they open a DM, not say "already in this game".
        bot.BotClient.UnreachableChatIds.Remove(SecondUser);
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_join", firstName: "Bob"));
        Assert.Contains("Bob joined", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task LeaveRemovesThePlayer()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_join", firstName: "Bob"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_leave", firstName: "Bob"));

        Assert.Contains("Bob left", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, ThirdUser, "/xyzzy_status"));
        Assert.DoesNotContain("Bob", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task LeavingWhenNotInTheGameIsHandledGracefully()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_leave", firstName: "Bob"));

        Assert.Contains("isn't in this game", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task GameCommandsDoNotApplyInPrivateChats()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/xyzzy_start"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/xyzzy_join"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/xyzzy_leave"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(FirstUser, "/xyzzy_status"));
        Assert.Contains("group chats", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task GetSettingsReportsTheCatalogAndTimingDefaults()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_get_settings"));

        var text = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("questions", text);
        Assert.Contains("answers", text);
        Assert.Contains("Max wait", text);
    }

    [Fact]
    public async Task GameStateSurvivesARestart()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, FirstUser, "/xyzzy_start"));
        await bot.SendAsync(TestBot.GroupMessage(ChatId, SecondUser, "/xyzzy_join", firstName: "Bob"));

        using var restarted = bot.Restart();
        await restarted.SendAsync(TestBot.GroupMessage(ChatId, ThirdUser, "/xyzzy_status", firstName: "Carol"));

        var status = restarted.BotClient.SentMessages[^1].Text;
        Assert.Contains("Bob", status);
        Assert.Contains("2/3 minimum", status);
    }
}
