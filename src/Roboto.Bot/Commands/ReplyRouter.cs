using Microsoft.Extensions.Logging;
using Roboto.Bot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// DM-only for now (legacy's isPrivateMessage=true path) - matches an incoming private message
/// against that user's pending reply. The legacy group-chat variant (ask in the group, match by
/// the user replying-to that specific message via Telegram's force-reply) isn't built - every
/// flow so far (just /setquiethours) asks over DM, add group-reply matching if/when a flow
/// actually needs it rather than building it speculatively.
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
    /// Sends a DM question and persists the pending state. Returns false (and sends nothing to
    /// track) if the DM itself failed to send - almost always because the user has never opened a
    /// private chat with the bot, matching the legacy "X needs to open a private chat to..." case.
    /// </summary>
    public async Task<bool> AskAsync(
        ITelegramBotClient bot, long targetChatId, long userId, string handlerCommand, string step, string? data,
        string text, CancellationToken cancellationToken)
    {
        try
        {
            await bot.SendMessage(userId, text, cancellationToken: cancellationToken);
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
        };
        await _store.SaveAsync(Key(userId), pending, cancellationToken);
        return true;
    }

    public async Task<bool> TryHandleAsync(ITelegramBotClient bot, Message message, CancellationToken cancellationToken)
    {
        if (message.Chat.Type != ChatType.Private || message.From is null)
        {
            return false;
        }

        var pending = await _store.LoadAsync<PendingReply>(Key(message.From.Id), cancellationToken);
        if (pending is null)
        {
            return false;
        }

        if (!_handlers.TryGetValue(pending.HandlerCommand, out var handler))
        {
            _logger.LogError("Pending reply for user {UserId} references unknown handler {Handler}",
                message.From.Id, pending.HandlerCommand);
            await _store.DeleteAsync(Key(message.From.Id), cancellationToken);
            return false;
        }

        // Clear before calling the handler, same reasoning as the legacy code: lets the handler ask
        // a fresh follow-up question (e.g. moving to the next step) without it looking like a
        // still-outstanding one, and avoids reprocessing the same pending entry twice if the handler
        // throws.
        await _store.DeleteAsync(Key(message.From.Id), cancellationToken);

        try
        {
            await handler.HandleReplyAsync(bot, pending, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reply handler {Handler} threw", pending.HandlerCommand);
        }

        return true;
    }

    private static string Key(long userId) => $"pending-reply:{userId}";
}
