using Microsoft.Extensions.Logging;
using Roboto.Bot.Birthdays;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Quotes;
using Roboto.Bot.Stats;
using Roboto.Bot.Steam;
using Roboto.Bot.Xyzzy;

namespace Roboto.Bot.Chats;

/// <summary>
/// Ports legacy's Chats.removeDormantChats() / chat.tryPurgeData(): once a chat's gone idle past
/// PurgeInactiveAfterDays with no incoming message (ChatState.LastActiveUtc, bumped by
/// ChatRepository.TouchAsync from MessageDispatcher), every module gets a say on whether its data
/// for that chat can go. If even one objects, the whole chat is skipped entirely this pass - all
/// or nothing, matching legacy's own "purge only if every module agrees" rule, not a selective
/// per-module cleanup. Deletion is real and irreversible - this only runs against a chat that's
/// been silent for months, per the user's explicit go-ahead to build full automated purge matching
/// legacy (not a log-only/dry-run mode).
///
/// Per-module rules mirror legacy exactly:
/// - Quotes: blocks purge only while it actually has quotes (mod_quote.isPurgable()).
/// - Birthdays: blocks purge permanently once the module has ever been touched for a chat, even if
///   every birthday was later removed (mod_birthdays.isPurgable() - a legacy quirk, reproduced
///   faithfully rather than "fixed").
/// - Xyzzy: blocks purge unless its own StatusChangedUtc is also past XyzzyKillInactiveAfterDays
///   (mod_xyzzy_chatdata.isPurgable(), legacy's separate killInactiveChatsAfterXDays=30 setting) -
///   in practice always already satisfied by the time the outer 100-day gate trips, since playing
///   xyzzy is itself chat activity that would keep LastActiveUtc fresh.
/// - Steam and quiet-hours never override isPurgable() in legacy - always purgable, no check.
/// </summary>
public sealed class ChatPurgeReconciler(
    ChatRepository chats, QuotesRepository quotes, BirthdaysRepository birthdays, SteamRepository steam,
    XyzzyGameRepository xyzzyGames, IStateStore store, StatsRecorder stats, ILogger<ChatPurgeReconciler> logger)
{
    public const int PurgeInactiveAfterDays = 100;
    public const int XyzzyKillInactiveAfterDays = 30;

    private const string ChatsPurgedStat = "chats.purged";

    public async Task ReconcileAllAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-PurgeInactiveAfterDays);

        foreach (var chat in await chats.GetAllAsync(cancellationToken))
        {
            if (chat.LastActiveUtc > cutoff)
            {
                continue;
            }

            try
            {
                await TryPurgeAsync(chat, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purge sweep failed for chat {ChatId}", chat.ChatId);
            }
        }
    }

    private async Task TryPurgeAsync(ChatState chat, CancellationToken cancellationToken)
    {
        var quotesState = await quotes.GetAsync(chat.ChatId, cancellationToken);
        if (quotesState.Quotes.Count > 0)
        {
            logger.LogInformation("Skipping purge of chat {ChatId} - still has quotes", chat.ChatId);
            return;
        }

        if (await birthdays.ExistsAsync(chat.ChatId, cancellationToken))
        {
            logger.LogInformation("Skipping purge of chat {ChatId} - birthdays module was used here", chat.ChatId);
            return;
        }

        if (await xyzzyGames.ExistsAsync(chat.ChatId, cancellationToken))
        {
            var game = await xyzzyGames.GetAsync(chat.ChatId, cancellationToken);
            if (game.StatusChangedUtc > DateTime.UtcNow.AddDays(-XyzzyKillInactiveAfterDays))
            {
                logger.LogInformation("Skipping purge of chat {ChatId} - xyzzy data still within its own inactivity window", chat.ChatId);
                return;
            }
        }

        await quotes.DeleteAsync(chat.ChatId, cancellationToken);
        await birthdays.DeleteAsync(chat.ChatId, cancellationToken); // no-op if never touched
        await steam.DeleteChatAsync(chat.ChatId, cancellationToken);
        await xyzzyGames.DeleteAsync(chat.ChatId, cancellationToken); // no-op if never touched
        await store.DeleteAsync(SetQuietHoursCommand.QuietHoursKey(chat.ChatId), cancellationToken);
        await chats.DeleteAsync(chat.ChatId, cancellationToken);

        await stats.RecordAsync(ChatsPurgedStat, 1, StatMode.Cumulative, cancellationToken);
        logger.LogInformation("Purged all data for dormant chat {ChatId} (inactive since {LastActive:u})", chat.ChatId, chat.LastActiveUtc);
    }
}
