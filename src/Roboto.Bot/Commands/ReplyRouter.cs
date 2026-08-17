using Microsoft.Extensions.Logging;
using Roboto.Bot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// DM-only for now (legacy's isPrivateMessage=true path) - matches an incoming private message
/// against that user's pending replies (plural - see PendingReply's own doc comment for why a
/// single user can have several outstanding). The legacy group-chat variant (ask in the group,
/// match by the user replying-to that specific message via Telegram's force-reply) isn't built -
/// every flow so far asks over DM, add group-reply matching if/when a flow actually needs it rather
/// than building it speculatively.
///
/// IMPORTANT for any future IReplyHandler: don't take ReplyRouter as a constructor dependency.
/// ReplyRouter needs every IBotCommand built (including yours, since IReplyHandler extends
/// IBotCommand) before it can exist, so a direct constructor dependency back on ReplyRouter is
/// circular - same shape HelpCommand hit needing CommandRouter. Resolve it lazily via
/// IServiceProvider inside your methods instead (see SetQuietHoursCommand).
/// </summary>
public sealed class ReplyRouter
{
    private readonly IStateStore _store;
    private readonly Dictionary<string, IReplyHandler> _handlers;
    private readonly ILogger<ReplyRouter> _logger;

    public ReplyRouter(IEnumerable<IBotCommand> commands, IStateStore store, ILogger<ReplyRouter> logger)
    {
        _store = store;
        _logger = logger;
        _handlers = commands.OfType<IReplyHandler>().ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sends a DM question and adds it to the user's list of pending replies. Returns false (and
    /// tracks nothing) if the DM itself failed to send - almost always because the user has never
    /// opened a private chat with the bot, matching the legacy "X needs to open a private chat
    /// to..." case.
    /// </summary>
    public async Task<bool> AskAsync(
        ITelegramBotClient bot, long targetChatId, long userId, string handlerCommand, string step, string? data,
        string text, CancellationToken cancellationToken)
    {
        Message sent;
        try
        {
            sent = await bot.SendMessage(userId, text, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't DM user {UserId} to ask a question", userId);
            return false;
        }

        var pending = new PendingReply
        {
            TargetChatId = targetChatId,
            UserId = userId,
            HandlerCommand = handlerCommand,
            Step = step,
            Data = data,
            AskedUtc = DateTime.UtcNow,
            QuestionMessageId = sent.Id,
        };

        var all = await LoadAllAsync(userId, cancellationToken);
        all.Add(pending);
        await SaveAllAsync(userId, all, cancellationToken);
        return true;
    }

    public async Task<bool> TryHandleAsync(ITelegramBotClient bot, Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null)
        {
            return false;
        }

        var userId = message.From.Id;
        var all = await LoadAllAsync(userId, cancellationToken);
        if (all.Count == 0)
        {
            return false;
        }

        PendingReply matched;
        if (message.ReplyToMessage is { } replyTo)
        {
            // An explicit reply always matches by message ID, regardless of how many are
            // outstanding - if it doesn't match anything we're tracking, it's not for us (e.g. the
            // user replied to some unrelated old message), so let it fall through to normal command
            // dispatch rather than swallowing it.
            var found = all.FirstOrDefault(p => p.QuestionMessageId == replyTo.Id);
            if (found is null)
            {
                return false;
            }
            matched = found;
        }
        else if (all.Count == 1)
        {
            // No reply-to needed when there's only one possible thing this could be answering -
            // same behavior as before this could ever be ambiguous.
            matched = all[0];
        }
        else
        {
            // Genuinely ambiguous: more than one thing outstanding and no reply-to to disambiguate.
            // Guess wrong here and an answer silently applies to the wrong game - ask instead.
            await bot.SendMessage(userId,
                "I'm waiting on a few different things from you right now - please reply directly " +
                "to the specific question you're answering.",
                cancellationToken: cancellationToken);
            return true;
        }

        if (!_handlers.TryGetValue(matched.HandlerCommand, out var handler))
        {
            _logger.LogError("Pending reply for user {UserId} references unknown handler {Handler}",
                userId, matched.HandlerCommand);
            all.Remove(matched);
            await SaveAllAsync(userId, all, cancellationToken);
            return false;
        }

        // Remove before calling the handler, same reasoning as the legacy code: lets the handler ask
        // a fresh follow-up question (e.g. moving to the next step) without it looking like a
        // still-outstanding one, and avoids reprocessing the same pending entry twice if the handler
        // throws.
        all.Remove(matched);
        await SaveAllAsync(userId, all, cancellationToken);

        try
        {
            await handler.HandleReplyAsync(bot, matched, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply handler {Handler} threw", matched.HandlerCommand);
        }

        return true;
    }

    private async Task<List<PendingReply>> LoadAllAsync(long userId, CancellationToken cancellationToken) =>
        await _store.LoadAsync<List<PendingReply>>(Key(userId), cancellationToken) ?? [];

    private Task SaveAllAsync(long userId, List<PendingReply> all, CancellationToken cancellationToken) =>
        _store.SaveAsync(Key(userId), all, cancellationToken);

    // Plural, deliberately a different key than the old single-PendingReply-per-user design (not
    // "pending-reply:") - the stored shape changed from one object to a list, and reusing the old
    // key would mean deserializing old data into the wrong shape. Any pending reply outstanding
    // from before this change is simply orphaned on deploy, not migrated - an acceptable one-time
    // hiccup for what's already just a DM conversation, not state anyone depends on surviving.
    private static string Key(long userId) => $"pending-replies:{userId}";
}
