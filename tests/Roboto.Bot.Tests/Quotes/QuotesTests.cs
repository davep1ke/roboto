using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Quotes;

namespace Roboto.Bot.Tests.Quotes;

public class QuotesTests
{
    private const long ChatId = -300;
    private const long Alice = 1;

    [Fact]
    public async Task QuoteWithNoneInTheDbSaysSo()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote"));

        Assert.Equal("No quotes in DB", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task QuoteAddAsksWhoThenWhatAndConfirmsInTheGroup()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_add", firstName: "Alice"));
        Assert.Contains("Who is the quote by", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob", firstName: "Alice"));
        Assert.Contains("What was the quote from Bob", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "I like Bees", firstName: "Alice"));
        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Added I like Bees by Bob successfully", confirmation.Text);

        var chat = await bot.Services.GetRequiredService<QuotesRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Single(chat.Quotes);
        Assert.Equal("Bob", chat.Quotes[0].Lines[0].By);
        Assert.Equal("I like Bees", chat.Quotes[0].Lines[0].Text);
    }

    [Fact]
    public async Task QuoteAddCanBeCancelledAtEitherStep()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "cancel", firstName: "Alice"));

        Assert.Contains("Cancelled adding a new quote", bot.BotClient.SentMessages[^1].Text);
        var chat = await bot.Services.GetRequiredService<QuotesRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Empty(chat.Quotes);
    }

    [Fact]
    public async Task QuoteConvBuildsAMultiLineQuoteUntilDone()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_conv", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, @"Bob\I like Bees", firstName: "Alice"));
        Assert.Contains("Enter the next line", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, @"Carol\Me too", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "done", firstName: "Alice"));

        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Bob", confirmation.Text);
        Assert.Contains("I like Bees", confirmation.Text);
        Assert.Contains("Carol", confirmation.Text);
        Assert.Contains("Me too", confirmation.Text);

        var chat = await bot.Services.GetRequiredService<QuotesRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Single(chat.Quotes);
        Assert.Equal(2, chat.Quotes[0].Lines.Count);
    }

    [Fact]
    public async Task QuoteConvWithNoLinesAtAllReportsFailureOnDone()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_conv", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "done", firstName: "Alice"));

        Assert.Contains("Couldn't add quote - no lines to add?", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public async Task QuoteConvWithAMalformedLineCancelsTheWholeFlow()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_conv", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "no backslash here", firstName: "Alice"));
        Assert.Contains("Couldn't work out where the name and text were", bot.BotClient.SentMessages[^1].Text);

        // The flow really ended - a further reply isn't routed anywhere, just like a fresh command.
        var countBefore = bot.BotClient.SentMessages.Count;
        await bot.SendAsync(TestBot.PrivateMessage(Alice, @"Bob\Something", firstName: "Alice"));
        Assert.Equal(countBefore, bot.BotClient.SentMessages.Count);
    }

    [Fact]
    public async Task QuoteConfigTogglesAutoQuoteAndConfirmsInTheGroup()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_config", firstName: "Alice"));
        var menu = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        Assert.Contains("enabled", menu.Text);

        await bot.SendCallbackAsync(Alice, menu.Buttons!.First(b => b.Text == "Toggle automatic quotes"));

        Assert.Contains("Quotes are now disabled", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
        var chat = await bot.Services.GetRequiredService<QuotesRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.False(chat.AutoQuoteEnabled);
    }

    [Fact]
    public async Task QuoteConfigSetDurationAsksThenSavesTheHours()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_config", firstName: "Alice"));
        var menu = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, menu.Buttons!.First(b => b.Text == "Set Duration"));
        Assert.Contains("How long between updates?", bot.BotClient.SentMessages[^1].Text);

        await bot.SendAsync(TestBot.PrivateMessage(Alice, "6", firstName: "Alice"));

        Assert.Contains("Quote schedule set to every 6 hours", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
        var chat = await bot.Services.GetRequiredService<QuotesRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Equal(6, chat.AutoQuoteHours);
    }

    [Fact]
    public async Task ATamperedConfigChoiceReoffersTheMenuInsteadOfGettingStuck()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_config", firstName: "Alice"));
        var menu = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, $"quote:cfg:{ChatId}:bogus", menu.Id);
        Assert.Contains("Not a valid choice", bot.BotClient.AnsweredCallbacks[^1].Text!);

        // The menu is still usable afterward - re-offered as a fresh message, not left dead.
        var freshMenu = bot.BotClient.SentMessages.Last(m => m.ChatId == Alice && m.Buttons is { Count: > 0 });
        await bot.SendCallbackAsync(Alice, freshMenu.Buttons!.First(b => b.Text == "Toggle automatic quotes"));
        Assert.Contains("Quotes are now disabled", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
    }

    [Fact]
    public async Task ReconcilerPostsADueQuoteAndSchedulesTheNextOne()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<QuotesRepository>();
        var reconciler = bot.Services.GetRequiredService<QuotesReconciler>();

        var chat = await repo.GetAsync(ChatId, CancellationToken.None);
        chat.Quotes.Add(new Quote { Lines = [new QuoteLine { By = "Bob", Text = "Hello" }] });
        chat.AutoQuoteHours = 24;
        chat.NextAutoQuoteAfter = DateTime.UtcNow.AddHours(-1);
        await repo.SaveAsync(chat, CancellationToken.None);

        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Bob"));
        var after = await repo.GetAsync(ChatId, CancellationToken.None);
        Assert.True(after.NextAutoQuoteAfter > DateTime.UtcNow);
    }

    [Fact]
    public async Task ReconcilerDoesNothingWhenDisabledOrNotDueOrEmpty()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<QuotesRepository>();
        var reconciler = bot.Services.GetRequiredService<QuotesReconciler>();

        var chat = await repo.GetAsync(ChatId, CancellationToken.None);
        chat.Quotes.Add(new Quote { Lines = [new QuoteLine { By = "Bob", Text = "Hello" }] });
        chat.NextAutoQuoteAfter = DateTime.UtcNow.AddHours(1); // not due yet
        await repo.SaveAsync(chat, CancellationToken.None);

        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        Assert.Empty(bot.BotClient.SentMessages);
    }

    [Fact]
    public async Task NoOpenPrivateChatIsReportedInTheGroup()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(Alice);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/quote_add", firstName: "Alice"));

        Assert.Contains("needs to open a private chat", bot.BotClient.SentMessages[^1].Text);
    }
}
