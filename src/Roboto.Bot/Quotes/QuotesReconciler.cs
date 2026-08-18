using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Roboto.Bot.Quotes;

/// <summary>
/// Ports legacy mod_quote's backgroundProcessing(): posts a random quote to any chat that's due
/// (AutoQuoteEnabled, past NextAutoQuoteAfter, and has at least one quote), then schedules the next
/// one with the same jitter legacy used - back off by 1/8 of the configured interval, then add a
/// random 0-1/4 of it back on, so auto-quotes don't all land on a fixed, predictable cadence. Split
/// out of QuotesSchedulerService for direct testability, same shape as every other
/// Reconciler/SchedulerService pair in this codebase.
/// </summary>
public sealed class QuotesReconciler(QuotesRepository quotes, ILogger<QuotesReconciler> logger)
{
    public async Task ReconcileAllAsync(ITelegramBotClient bot, CancellationToken cancellationToken)
    {
        foreach (var chat in await quotes.GetAllAsync(cancellationToken))
        {
            try
            {
                await ReconcileAsync(bot, chat, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciling mod_quote auto-post for chat {ChatId} failed", chat.ChatId);
            }
        }
    }

    public async Task ReconcileAsync(ITelegramBotClient bot, QuoteChatState chat, CancellationToken cancellationToken)
    {
        if (!chat.AutoQuoteEnabled || DateTime.UtcNow <= chat.NextAutoQuoteAfter || chat.Quotes.Count == 0)
        {
            return;
        }

        var quote = chat.Quotes[Random.Shared.Next(chat.Quotes.Count)];
        await bot.SendMessage(chat.ChatId, quote.GetText(), cancellationToken: cancellationToken);

        var maxMins = chat.AutoQuoteHours * 60;
        var randomMins = Random.Shared.Next(maxMins / 4);
        maxMins = maxMins - maxMins / 8 + randomMins;
        chat.NextAutoQuoteAfter = DateTime.UtcNow.AddMinutes(maxMins);

        await quotes.SaveAsync(chat, cancellationToken);
    }
}
