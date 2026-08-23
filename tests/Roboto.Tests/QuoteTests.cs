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

    [Fact]
    public void QuoteConfigDoesNotCrashTheMainLoopWhenTheDmFailsToSend()
    {
        // Real live bug: /quote_config's CONFIG menu is a DM (isPrivateMessage:true) - if Telegram
        // rejects that send for any reason (live report: 400 "message to be replied not found", a
        // stale reply-to target; reproduced here with the same 403 "can't initiate conversation"
        // TelegramAPI.postExpectedReplyToPlayer already handles elsewhere), Messaging.
        // parseFailedReply used to call replyReceived(er, null, true) - every module's
        // replyReceived unconditionally dereferences m (e.g. m.text_msg.ToLower()), so passing null
        // threw a NullReferenceException that propagated all the way up and took the whole main
        // loop down (confirmed present byte-for-byte in legacy). TestHarness.Send drives
        // TelegramAPI.DispatchUpdate directly, with no top-level catch of its own (that only exists
        // in getUpdates()'s real long-poll loop) - so this test simply not throwing is the proof
        // the crash is fixed.
        using var bot = new TestHarness();
        bot.BotClient.UnreachableChatIds.Add(Alice);

        bot.SendGroupMessage(ChatId, Alice, "/quote_config", "Alice");

        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.ChatId == Alice);
    }
}
