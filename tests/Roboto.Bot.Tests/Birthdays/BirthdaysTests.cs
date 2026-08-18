using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Birthdays;

namespace Roboto.Bot.Tests.Birthdays;

public class BirthdaysTests
{
    private const long ChatId = -400;
    private const long Alice = 1;

    [Fact]
    public async Task AddingABirthdayConfirmsInTheGroupAndPersistsIt()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "01-Jan-1990", firstName: "Alice"));

        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Added Bob's Birthday (1990-01-01)", confirmation.Text);

        var chat = await bot.Services.GetRequiredService<BirthdaysRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Single(chat.Birthdays);
        Assert.Equal("Bob", chat.Birthdays[0].Name);
    }

    [Fact]
    public async Task AnUnparseableDateFailsWithoutAddingAnything()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "not a date", firstName: "Alice"));

        Assert.Contains("Failed to add birthday", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
        var chat = await bot.Services.GetRequiredService<BirthdaysRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Empty(chat.Birthdays);
    }

    [Fact]
    public async Task RemovingABirthdayTakesItOutOfTheList()
    {
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "01-Jan-1990", firstName: "Alice"));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_remove", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Bob", firstName: "Alice"));

        Assert.Contains("Removed birthday for Bob successfully", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
        var chat = await bot.Services.GetRequiredService<BirthdaysRepository>().GetAsync(ChatId, CancellationToken.None);
        Assert.Empty(chat.Birthdays);
    }

    [Fact]
    public async Task RemovingAnUnknownNameReportsFailure()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_remove", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Nobody", firstName: "Alice"));

        Assert.Contains("Removed birthday for Nobody but fell on my ass", bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId).Text);
    }

    [Fact]
    public async Task ListShowsBirthdaysSortedByDayOfYear()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<BirthdaysRepository>();
        var chat = await repo.GetAsync(ChatId, CancellationToken.None);
        chat.Birthdays.Add(new BirthdayEntry { Name = "December Bob", Birthday = new DateTime(1980, 12, 1) });
        chat.Birthdays.Add(new BirthdayEntry { Name = "January Alice", Birthday = new DateTime(2000, 1, 15) });
        await repo.SaveAsync(chat, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_list"));

        var text = bot.BotClient.SentMessages[^1].Text;
        Assert.True(text.IndexOf("January Alice", StringComparison.Ordinal) < text.IndexOf("December Bob", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconcilerAnnouncesMatchingBirthdaysOncePerDay()
    {
        using var bot = new TestBot();
        var repo = bot.Services.GetRequiredService<BirthdaysRepository>();
        var reconciler = bot.Services.GetRequiredService<BirthdaysReconciler>();
        var today = DateTime.UtcNow;

        var chat = await repo.GetAsync(ChatId, CancellationToken.None);
        chat.Birthdays.Add(new BirthdayEntry { Name = "Today Person", Birthday = new DateTime(1970, today.Month, today.Day) });
        chat.Birthdays.Add(new BirthdayEntry { Name = "Other Day Person", Birthday = today.AddDays(10) });
        await repo.SaveAsync(chat, CancellationToken.None);

        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);

        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("Happy Birthday to Today Person!"));
        Assert.DoesNotContain(bot.BotClient.SentMessages, m => m.Text.Contains("Other Day Person"));

        var sentCount = bot.BotClient.SentMessages.Count;
        await reconciler.ReconcileAllAsync(bot.BotClient, CancellationToken.None);
        Assert.Equal(sentCount, bot.BotClient.SentMessages.Count);
    }

    [Fact]
    public async Task NoOpenPrivateChatIsReportedInTheGroup()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(Alice);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/birthday_add", firstName: "Alice"));

        Assert.Contains("needs to open a private chat", bot.BotClient.SentMessages[^1].Text);
    }
}
