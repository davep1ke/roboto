namespace RobotoTests;

public class WordcraftTests
{
    private const long ChatId = -900;
    private const long Alice = 90;

    [Fact]
    public void CraftSendsANonEmptyPhraseToTheChat()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/craft", "Alice");

        Assert.NotEmpty(bot.BotClient.SentMessages[^1].Text);
        Assert.Equal(ChatId, bot.BotClient.SentMessages[^1].ChatId);
    }

    [Fact]
    public void AddThenRemoveAWord()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/craft_add", "Alice");
        Assert.Contains("Enter the word to add", bot.BotClient.SentMessages[^1].Text);

        bot.TapButton(Alice, "Zargle", "Alice");
        Assert.Contains("Added Zargle for Alice", bot.BotClient.SentMessages[^1].Text);

        bot.SendGroupMessage(ChatId, Alice, "/craft_remove", "Alice");
        bot.TapButton(Alice, "Zargle", "Alice");
        Assert.Contains("Removed Zargle for Alice successfully", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void RemovingAWordThatWasNeverAddedReportsFailure()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/craft_remove", "Alice");
        bot.TapButton(Alice, "NeverExisted", "Alice");

        Assert.Contains("fell on my ass", bot.BotClient.SentMessages[^1].Text);
    }
}
