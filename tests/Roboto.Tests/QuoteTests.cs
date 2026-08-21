namespace RobotoTests;

public class QuoteTests
{
    private const long ChatId = -300;
    private const long Alice = 20;

    [Fact]
    public void AddQuoteThenRetrieveIt()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/quote_add", "Alice");
        Assert.Contains("Who is the quote by", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Bob", "Alice");
        Assert.Contains("What was the quote from Bob", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "I like Bees", "Alice");
        Assert.Contains("Added I like Bees by Bob successfully", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/quote", "Alice");
        Assert.Contains("Bob", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("I like Bees", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void CancellingAddQuoteAbortsWithoutSavingAnything()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/quote_add", "Alice");
        bot.TapButton(Alice, "cancel", "Alice");
        Assert.Contains("Cancelled adding a new quote", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/quote", "Alice");
        Assert.Contains("No quotes in DB", bot.BotClient.SentMessages[^1].Text);
    }
}
