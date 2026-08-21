namespace RobotoTests;

public class AdminCommandsTests
{
    private const long ChatId = -100;
    private const long Alice = 1;

    [Fact]
    public void StopMutesTheChatAndStartUnmutesIt()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/stop", "Alice");
        Assert.Contains("ignoring all messages", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/start", "Alice");
        Assert.Contains("listening for messages again", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void MutingAChatSuppressesModulesThatArentExemptFromMuting()
    {
        // mod_standard itself is exempt from muting (chatIfMuted=true, so /start/stop/addadmin etc
        // keep working) - mod_xyzzy is not, so it's the one that actually proves muting suppresses a
        // module's chatEvent.
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/stop", "Alice");
        var beforeCount = bot.BotClient.SentMessages.Count;

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_status", "Alice");

        Assert.Equal(beforeCount, bot.BotClient.SentMessages.Count);
    }

    [Fact]
    public void AddAdminWithNoExistingAdminsMakesTheCallerAdmin()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/addadmin", "Alice");

        Assert.Contains("admin", bot.BotClient.SentMessages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }
}
