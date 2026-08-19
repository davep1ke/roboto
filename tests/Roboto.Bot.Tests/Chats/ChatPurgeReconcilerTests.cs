using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Birthdays;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Quotes;
using Roboto.Bot.Steam;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Tests.Chats;

/// <summary>Covers ChatPurgeReconciler - legacy's dormant-chat automated deletion, ported to run
/// per-module against the rewrite's own repositories. Every test manipulates ChatState.LastActiveUtc
/// directly (bypassing ChatRepository.TouchAsync, which always stamps "now") so it can simulate a
/// chat that's genuinely been silent for months without needing to actually wait that long.</summary>
public class ChatPurgeReconcilerTests
{
    private const long ChatId = -800;

    private static readonly DateTime WellPastCutoff = DateTime.UtcNow.AddDays(-(ChatPurgeReconciler.PurgeInactiveAfterDays + 10));

    private static async Task MakeDormantAsync(TestBot bot, DateTime lastActiveUtc)
    {
        var chats = bot.Services.GetRequiredService<ChatRepository>();
        var chat = await chats.GetAsync(ChatId, CancellationToken.None);
        chat.LastActiveUtc = lastActiveUtc;
        await chats.SaveAsync(chat, CancellationToken.None);
    }

    [Fact]
    public async Task ADormantChatWithNoProtectedDataIsFullyPurged()
    {
        using var bot = new TestBot();
        var store = bot.Services.GetRequiredService<IStateStore>();
        await store.SaveAsync(SetQuietHoursCommand.QuietHoursKey(ChatId), new QuietHours(TimeSpan.Zero, TimeSpan.FromHours(8)), CancellationToken.None);
        await MakeDormantAsync(bot, WellPastCutoff);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var chats = bot.Services.GetRequiredService<ChatRepository>();
        var afterChat = await chats.GetAsync(ChatId, CancellationToken.None);
        // GetAsync's "?? new" default proves the real stored record is gone - a fresh default has a
        // brand new LastActiveUtc, nowhere near WellPastCutoff.
        Assert.True(afterChat.LastActiveUtc > WellPastCutoff.AddDays(1));

        Assert.Null(await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(ChatId), CancellationToken.None));
    }

    [Fact]
    public async Task AChatStillHoldingQuotesIsNotPurged()
    {
        using var bot = new TestBot();
        var quotes = bot.Services.GetRequiredService<QuotesRepository>();
        var quoteState = await quotes.GetAsync(ChatId, CancellationToken.None);
        quoteState.Quotes.Add(new Quote { On = DateTime.UtcNow, Lines = [new QuoteLine { By = "Bob", Text = "Hi" }] });
        await quotes.SaveAsync(quoteState, CancellationToken.None);
        await MakeDormantAsync(bot, WellPastCutoff);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var survivingQuotes = await quotes.GetAsync(ChatId, CancellationToken.None);
        Assert.Single(survivingQuotes.Quotes);
    }

    [Fact]
    public async Task AChatThatEverTouchedBirthdaysIsNeverPurgedEvenIfEmptyNow()
    {
        using var bot = new TestBot();
        var birthdays = bot.Services.GetRequiredService<BirthdaysRepository>();
        // Saved once (e.g. an add followed by a remove), now genuinely empty - legacy's own quirk
        // still blocks purge permanently once the module's ever been touched.
        await birthdays.SaveAsync(new BirthdayChatState { ChatId = ChatId }, CancellationToken.None);
        await MakeDormantAsync(bot, WellPastCutoff);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var chats = bot.Services.GetRequiredService<ChatRepository>();
        var afterChat = await chats.GetAsync(ChatId, CancellationToken.None);
        Assert.True(afterChat.LastActiveUtc <= WellPastCutoff.AddSeconds(1)); // still the real, dormant record
    }

    [Fact]
    public async Task ARecentlyActiveChatIsNeverEvenConsideredForPurge()
    {
        using var bot = new TestBot();
        await MakeDormantAsync(bot, DateTime.UtcNow); // "dormant" in name only - LastActiveUtc is fresh

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var chats = bot.Services.GetRequiredService<ChatRepository>();
        var afterChat = await chats.GetAsync(ChatId, CancellationToken.None);
        Assert.True(afterChat.LastActiveUtc > WellPastCutoff.AddDays(1));
    }

    [Fact]
    public async Task XyzzyDataBlocksPurgeUntilItsOwnInactivityWindowAlsoPasses()
    {
        using var bot = new TestBot();
        var games = bot.Services.GetRequiredService<XyzzyGameRepository>();
        var game = await games.GetAsync(ChatId, CancellationToken.None);
        game.StatusChangedUtc = DateTime.UtcNow.AddDays(-(ChatPurgeReconciler.XyzzyKillInactiveAfterDays - 5)); // recent by xyzzy's own rule
        await games.SaveAsync(game, CancellationToken.None);
        await MakeDormantAsync(bot, WellPastCutoff);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var chats = bot.Services.GetRequiredService<ChatRepository>();
        Assert.True((await chats.GetAsync(ChatId, CancellationToken.None)).LastActiveUtc <= WellPastCutoff.AddSeconds(1));

        // Now push the game's own StatusChangedUtc past its own window too - purge should proceed.
        game.StatusChangedUtc = DateTime.UtcNow.AddDays(-(ChatPurgeReconciler.XyzzyKillInactiveAfterDays + 5));
        await games.SaveAsync(game, CancellationToken.None);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        Assert.True((await chats.GetAsync(ChatId, CancellationToken.None)).LastActiveUtc > WellPastCutoff.AddDays(1));
    }

    [Fact]
    public async Task SteamDataIsAlwaysPurgedRegardlessOfContent()
    {
        using var bot = new TestBot();
        var steam = bot.Services.GetRequiredService<SteamRepository>();
        var steamChat = await steam.GetChatAsync(ChatId, CancellationToken.None);
        steamChat.Players.Add(new SteamPlayer { SteamId = "1", PlayerName = "Gamer" });
        await steam.SaveChatAsync(steamChat, CancellationToken.None);
        await MakeDormantAsync(bot, WellPastCutoff);

        await bot.Services.GetRequiredService<ChatPurgeReconciler>().ReconcileAllAsync(CancellationToken.None);

        var survivingSteam = await steam.GetChatAsync(ChatId, CancellationToken.None);
        Assert.Empty(survivingSteam.Players); // no protection in legacy either - deleted along with the chat
    }
}
