using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Dispatches CallbackQuery updates (inline-keyboard button taps) to whichever ICallbackQueryHandler
/// claims the callback_data. Always answers the callback query itself - a real Telegram API
/// requirement, not a nicety: until answerCallbackQuery is called, the tapped button shows a
/// perpetual loading spinner client-side. Centralizing the answer here (rather than each handler
/// answering itself) means it happens exactly once, on every path (no match, handler throws, or a
/// normal result), instead of every future handler needing to remember to do it.
/// </summary>
public sealed class CallbackQueryRouter(IEnumerable<ICallbackQueryHandler> handlers, ILogger<CallbackQueryRouter> logger)
{
    public async Task HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        string? answerText;
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

        await bot.AnswerCallbackQuery(query.Id, answerText, cancellationToken: cancellationToken);
    }
}
