using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;

namespace Roboto.Bot.Tests;

/// <summary>Covers DmOutbox.PumpAllOutstandingAsync - the startup safety net that delivers a queue
/// left with an undelivered head, whether from a prior crash mid-pump or (the real motivating case)
/// phase 11's XmlImporter deliberately writing resumed in-flight questions as undelivered data,
/// never sent live during import itself.</summary>
public class DmOutboxStartupSweepTests
{
    private const long Alice = 1;
    private const long Bob = 2;

    [Fact]
    public async Task DeliversAnUndeliveredHeadLeftSittingInAQueue()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();

        // Simulates exactly what an import writes: a queue entry with no DeliveredMessageId - as
        // if the process had never actually sent anything for it yet.
        var entry = new DmOutboxEntry
        {
            Text = "Resumed: pick a card",
            ExpectsResponse = true,
            Keyboard = [[new DmButton("Card A", "xy:a:-100:1:a1")]],
        };
        await store.SaveAsync("dm-outbox:" + Alice, new List<DmOutboxEntry> { entry }, CancellationToken.None);

        var outbox = bot.Services.GetRequiredService<DmOutbox>();
        await outbox.PumpAllOutstandingAsync(bot.BotClient, CancellationToken.None);

        var sent = Assert.Single(bot.BotClient.SentMessages, m => m.ChatId == Alice);
        Assert.Equal("Resumed: pick a card", sent.Text);
        Assert.NotNull(sent.Buttons);
    }

    [Fact]
    public async Task LeavesAnAlreadyDeliveredHeadAlone()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();

        var entry = new DmOutboxEntry { Text = "Already sent", ExpectsResponse = false, DeliveredMessageId = 42 };
        await store.SaveAsync("dm-outbox:" + Bob, new List<DmOutboxEntry> { entry }, CancellationToken.None);

        var outbox = bot.Services.GetRequiredService<DmOutbox>();
        await outbox.PumpAllOutstandingAsync(bot.BotClient, CancellationToken.None);

        Assert.Empty(bot.BotClient.SentMessages);
    }

    [Fact]
    public async Task DoesNothingWhenNoQueuesExist()
    {
        using var bot = new TestBot();
        var outbox = bot.Services.GetRequiredService<DmOutbox>();

        await outbox.PumpAllOutstandingAsync(bot.BotClient, CancellationToken.None);

        Assert.Empty(bot.BotClient.SentMessages);
    }
}
