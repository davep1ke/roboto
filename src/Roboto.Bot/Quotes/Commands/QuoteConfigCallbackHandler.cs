using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Quotes.Commands;

/// <summary>Handles taps on QuoteConfigCommand's menu - callback_data
/// "quote:cfg:&lt;chatId&gt;:&lt;duration|toggle&gt;".</summary>
public sealed class QuoteConfigCallbackHandler(IServiceProvider services, QuotesRepository quotes, DmOutbox outbox) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("quote:cfg:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var parts = query.Data!.Split(':', 4);
        if (parts.Length != 4 || !long.TryParse(parts[2], out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var chat = await quotes.GetAsync(chatId, cancellationToken);
        var userId = query.From.Id;

        switch (parts[3])
        {
            case "duration":
                await services.GetRequiredService<ReplyRouter>().AskAsync(bot, chatId, userId, "quote_config", QuoteConfigCommand.AwaitDuration,
                    data: null, "How long between updates?", cancellationToken);
                return "Let's set the duration.";

            case "toggle":
                chat.AutoQuoteEnabled = !chat.AutoQuoteEnabled;
                await quotes.SaveAsync(chat, cancellationToken);
                await bot.SendMessage(chatId, $"Quotes are now {(chat.AutoQuoteEnabled ? "enabled" : "disabled")}", cancellationToken: cancellationToken);
                return chat.AutoQuoteEnabled ? "Enabled." : "Disabled.";

            default:
                // Re-offers the same menu rather than leaving the tapped keyboard dead - see
                // QuoteConfigCommand's doc comment for why (mirrors XyzzySetupCallbackHandler's fix).
                await outbox.EnqueueButtonQuestionAsync(bot, userId, QuoteConfigCommand.BuildStatusText(chat),
                    QuoteConfigCommand.BuildKeyboard(chatId), cancellationToken);
                return "Not a valid choice.";
        }
    }
}
