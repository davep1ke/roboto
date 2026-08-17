using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Dispatches CallbackQuery updates (inline-keyboard button taps) to whichever ICallbackQueryHandler
/// claims the callback_data - but only if the tapped message is actually the user's current
/// DmOutbox head (phase 11). A tap on anything else (an old, already-resolved keyboard still
/// visually sitting in the chat) is rejected without ever reaching a handler, and doesn't advance
/// the queue - only a tap that genuinely was the current question does. The head is removed
/// *before* dispatching (same reasoning ReplyRouter uses for text answers) so the handler's own
/// immediate follow-ups send right away into the now-empty queue; only once the handler is fully
/// done does the queue get pumped for whatever else was waiting.
///
/// Always answers the callback query itself - a real Telegram API requirement, not a nicety: until
/// answerCallbackQuery is called, the tapped button shows a perpetual loading spinner client-side.
/// Centralizing the answer here (rather than each handler answering itself) means it happens
/// exactly once, on every path (no match, handler throws, or a normal result), instead of every
/// future handler needing to remember to do it.
/// </summary>
public sealed class CallbackQueryRouter(IEnumerable<ICallbackQueryHandler> handlers, DmOutbox outbox, ILogger<CallbackQueryRouter> logger)
{
    public async Task HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var userId = query.From.Id;
        var messageId = query.Message?.Id;
        var isCurrentHead = messageId is not null && await outbox.IsCurrentHeadAsync(userId, messageId.Value, cancellationToken);

        string? answerText;
        if (!isCurrentHead)
        {
            answerText = "That button isn't valid any more.";
        }
        else
        {
            await outbox.RemoveCurrentHeadAsync(userId, cancellationToken);

            try
            {
                var handler = query.Data is { } data ? handlers.FirstOrDefault(h => h.CanHandle(data)) : null;
                if (handler is null)
                {
                    logger.LogWarning("No handler for callback data {Data}", query.Data);
                    answerText = "That button isn't valid any more.";
                }
                else
                {
                    answerText = await handler.HandleAsync(bot, query, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Callback handler for {Data} threw", query.Data);
                answerText = "Something went wrong - try again.";
            }

            await outbox.PumpNextAsync(bot, userId, cancellationToken);
        }

        await bot.AnswerCallbackQuery(query.Id, answerText, cancellationToken: cancellationToken);
    }
}
