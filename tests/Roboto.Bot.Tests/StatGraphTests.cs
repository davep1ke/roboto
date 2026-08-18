using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Stats;

namespace Roboto.Bot.Tests;

public class StatGraphTests
{
    private const long ChatId = -600;
    private const long Alice = 1;

    [Fact]
    public async Task NoArgumentListsAvailableStats()
    {
        using var bot = new TestBot();
        await bot.Services.GetRequiredService<StatsRecorder>().RecordAsync("widgets", 3, StatMode.Cumulative, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/statgraph"));

        Assert.Contains("Usage: /statgraph", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("widgets", bot.BotClient.SentMessages[^1].Text);
        Assert.Empty(bot.BotClient.SentPhotos);
    }

    [Fact]
    public async Task UnknownNameReportsNoHistoryAndListsAvailableStats()
    {
        using var bot = new TestBot();
        await bot.Services.GetRequiredService<StatsRecorder>().RecordAsync("widgets", 3, StatMode.Cumulative, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/statgraph nonexistent"));

        Assert.Contains("No recorded history for 'nonexistent'", bot.BotClient.SentMessages[^1].Text);
        Assert.Contains("widgets", bot.BotClient.SentMessages[^1].Text);
        Assert.Empty(bot.BotClient.SentPhotos);
    }

    [Fact]
    public async Task KnownStatRendersAndSendsAPngPhoto()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        await stats.RecordAsync("widgets", 1, StatMode.Cumulative, CancellationToken.None);
        await stats.RecordAsync("widgets", 2, StatMode.Cumulative, CancellationToken.None);
        await stats.RecordAsync("widgets", 3, StatMode.Cumulative, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/statgraph widgets"));

        var photo = Assert.Single(bot.BotClient.SentPhotos);
        Assert.Equal(ChatId, photo.ChatId);
        Assert.Equal("widgets", photo.Caption);
        Assert.Equal("widgets.png", photo.FileName);

        // A real PNG - starts with the standard 8-byte PNG signature.
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(pngSignature, photo.Content.Take(8));
        Assert.True(photo.Content.Length > 100);
    }

    [Fact]
    public async Task NameMatchIsCaseInsensitive()
    {
        using var bot = new TestBot();
        await bot.Services.GetRequiredService<StatsRecorder>().RecordAsync("Widgets", 1, StatMode.Cumulative, CancellationToken.None);

        await bot.SendAsync(TestBot.GroupMessage(ChatId, Alice, "/statgraph widgets"));

        Assert.Single(bot.BotClient.SentPhotos);
    }
}
