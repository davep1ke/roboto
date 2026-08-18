using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// DM-only (legacy's isPrivateMessage=true path) - matches an incoming private message against
/// that user's currently-pending free-text question (DmOutbox, phase 8.9). A thin adapter: it
/// preserves the AskAsync/IReplyHandler contract every existing caller (SetQuietHoursCommand, the
/// mod_xyzzy setup/settings flows) already uses, but delivery/sequencing is DmOutbox's job now -
/// see its own doc comment for why (only one thing, of any kind, is ever outstanding per user).
///
/// IMPORTANT for any future IReplyHandler: don't take ReplyRouter as a constructor dependency.
/// ReplyRouter needs every IBotCommand built (including yours, since IReplyHandler extends
/// IBotCommand) before it can exist, so a direct constructor dependency back on ReplyRouter is
/// circular - same shape HelpCommand hit needing CommandRouter. Resolve it lazily via
/// IServiceProvider inside your methods instead (see SetQuietHoursCommand).
/// </summary>
public sealed class ReplyRouter
{
    private readonly DmOutbox _outbox;
    private readonly Dictionary<string, IReplyHandler> _handlers;
    private readonly ILogger<ReplyRouter> _logger;

    public ReplyRouter(IEnumerable<IBotCommand> commands, DmOutbox outbox, ILogger<ReplyRouter> logger)
    {
        _outbox = outbox;
        _logger = logger;
        _handlers = commands.OfType<IReplyHandler>().ToDictionary(h => h.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Queues a DM question (delivered immediately if nothing else is outstanding for this user,
    /// otherwise once its turn comes - see DmOutbox). Returns false only when this user is
    /// definitively unreachable (queue was empty and the send itself failed) - almost always
    /// because they've never opened a private chat with the bot.
    /// </summary>
    public Task<bool> AskAsync(
        ITelegramBotClient bot, long targetChatId, long userId, string handlerCommand, string step, string? data,
        string text, CancellationToken cancellationToken) =>
        _outbox.EnqueueTextQuestionAsync(bot, userId, targetChatId, handlerCommand, step, data, text, cancellationToken);

    public async Task<bool> TryHandleAsync(ITelegramBotClient bot, Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null)
        {
            return false;
        }

        var userId = message.From.Id;
        var head = await _outbox.TryGetHeadTextQuestionAsync(userId, message.ReplyToMessage?.Id, cancellationToken);
        if (head is null)
        {
            return false;
        }

        if (!_handlers.TryGetValue(head.HandlerCommand, out var handler))
        {
            _logger.LogError("Pending reply for user {UserId} references unknown handler {Handler}", userId, head.HandlerCommand);
            await _outbox.RemoveCurrentHeadAsync(userId, cancellationToken);
            await _outbox.PumpNextAsync(bot, userId, cancellationToken);
            return false;
        }

        var pending = new PendingReply
        {
            TargetChatId = head.TargetChatId,
            UserId = userId,
            HandlerCommand = head.HandlerCommand,
            Step = head.Step,
            Data = head.Data,
            AskedUtc = head.QueuedUtc,
        };

        // Remove before calling the handler, same reasoning as always: lets the handler ask a
        // fresh follow-up question (moving to the next step) without it looking like a
        // still-outstanding one, and avoids reprocessing the same entry twice if the handler throws.
        // Deliberately doesn't pump yet - the handler may send its own immediate follow-ups into
        // the now-empty queue, and those should appear before anything else that was queued from an
        // unrelated game (see DmOutbox.RemoveCurrentHeadAsync's doc comment).
        await _outbox.RemoveCurrentHeadAsync(userId, cancellationToken);

        try
        {
            await handler.HandleReplyAsync(bot, pending, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply handler {Handler} threw", pending.HandlerCommand);
        }

        await _outbox.PumpNextAsync(bot, userId, cancellationToken);
        return true;
    }
}
