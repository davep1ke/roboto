namespace RobotoTests;

/// <summary>
/// Deeper mod_quote coverage beyond QuoteTests: multi-line conversation quotes (/quote_conv),
/// /quote_config (Set Duration, Toggle automatic quotes), and the auto-quote background
/// announcement.
/// </summary>
public class QuoteMoreCoverageTests
{
    private const long ChatId = -1300;
    private const long Alice = 130;

    [Fact]
    public void QuoteConvAddsAMultiLineConversation()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/quote_conv", "Alice");
        Assert.Contains("Enter the first speaker", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Bob\\I like Bees", "Alice");
        Assert.Contains("Enter the next line", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Carol\\Bees are dangerous", "Alice");
        bot.TapButton(Alice, "done", "Alice");

        string lastMessage = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("Added quote", lastMessage);
        Assert.Contains("I like Bees", lastMessage);
        Assert.Contains("Bees are dangerous", lastMessage);
    }

    [Fact]
    public void QuoteConvCancelAbortsWithoutSavingAnything()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/quote_conv", "Alice");
        bot.TapButton(Alice, "cancel", "Alice");

        Assert.Contains("Cancelled adding a new quote", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/quote", "Alice");
        Assert.Contains("No quotes in DB", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void QuoteConfigTogglesAutomaticQuotes()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/quote_config", "Alice");
        Assert.Contains("currently enabled", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Toggle automatic quotes", "Alice");

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Quotes are now disabled"));
    }

    [Fact]
    public void AutoQuoteBackgroundProcessingAnnouncesAQuoteWhenDue()
    {
        using var bot = new TestHarness();

        // Adds a quote (and, as a side effect, initializes this chat's mod_quote_data) - the
        // background auto-quote branch is skipped entirely if that data was never created, and
        // nextAutoQuoteAfter defaults to DateTime.MinValue, so it's already "due" the moment quotes
        // exist, with no need to backdate anything for this test.
        bot.SendGroupMessage(ChatId, Alice, "/quote_add", "Alice");
        bot.TapButton(Alice, "Bob", "Alice");
        bot.TapButton(Alice, "I like Bees", "Alice");

        bot.RunBackgroundProcessing();

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Bob") && m.Text.Contains("I like Bees"));
    }
}
