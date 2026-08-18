using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Wordcraft;

namespace Roboto.Bot.Tests.Wordcraft;

public class WordcraftTests
{
    private const long ChatId = -500;
    private const long Alice = 1;

    [Fact]
    public async Task CraftReturnsANonEmptyPhraseFromTheDefaultWords()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft"));

        var text = bot.BotClient.SentMessages[^1].Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        var words = await bot.Services.GetRequiredService<WordcraftStore>().GetWordsAsync(CancellationToken.None);
        Assert.Contains(text.Split(' ')[0], words);
    }

    [Fact]
    public async Task AddingAWordMakesItAvailableToCraftAndConfirmsInTheGroup()
    {
        using var bot = new TestBot();

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Zorbo", firstName: "Alice"));

        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Added Zorbo for Alice", confirmation.Text);

        var words = await bot.Services.GetRequiredService<WordcraftStore>().GetWordsAsync(CancellationToken.None);
        Assert.Contains("Zorbo", words);
    }

    [Fact]
    public async Task RemovingAWordTakesItOutOfTheListAndConfirmsInTheGroup()
    {
        using var bot = new TestBot();
        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft_add", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Zorbo", firstName: "Alice"));

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft_remove", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "Zorbo", firstName: "Alice"));

        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Removed Zorbo for Alice successfully", confirmation.Text);

        var words = await bot.Services.GetRequiredService<WordcraftStore>().GetWordsAsync(CancellationToken.None);
        Assert.DoesNotContain("Zorbo", words);
    }

    [Fact]
    public async Task RemovingAWordThatDoesntExistReportsFailureWithoutChangingTheList()
    {
        using var bot = new TestBot();
        var before = await bot.Services.GetRequiredService<WordcraftStore>().GetWordsAsync(CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft_remove", firstName: "Alice"));
        await bot.SendAsync(TestBot.PrivateMessage(Alice, "NoSuchWord", firstName: "Alice"));

        var confirmation = bot.BotClient.SentMessages.Last(m => m.ChatId == ChatId);
        Assert.Contains("Removed NoSuchWord for Alice but fell on my ass", confirmation.Text);

        var after = await bot.Services.GetRequiredService<WordcraftStore>().GetWordsAsync(CancellationToken.None);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task NoOpenPrivateChatIsReportedInTheGroup()
    {
        using var bot = new TestBot();
        bot.BotClient.UnreachableChatIds.Add(Alice);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/craft_add", firstName: "Alice"));

        Assert.Contains("needs to open a private chat", bot.BotClient.SentMessages[^1].Text);
    }
}
