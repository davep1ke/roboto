using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Roboto.Bot.Birthdays;

/// <summary>
/// Ports legacy mod_birthdays' backgroundProcessing(): once per calendar day (guarded by
/// LastDayProcessed, so a scheduler tick that lands twice in the same day is a no-op the second
/// time), announces anyone whose day/month matches today. Pulled out of
/// BirthdaysSchedulerService (a BackgroundService, awkward to test directly) so it's directly
/// callable/testable - same split as XyzzyRoundReconciler/XyzzyRoundSchedulerService.
/// </summary>
public sealed class BirthdaysReconciler(BirthdaysRepository birthdays, ILogger<BirthdaysReconciler> logger)
{
    public async Task ReconcileAllAsync(ITelegramBotClient bot, CancellationToken cancellationToken)
    {
        foreach (var chat in await birthdays.GetAllAsync(cancellationToken))
        {
            try
            {
                await ReconcileAsync(bot, chat, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciling mod_birthdays for chat {ChatId} failed", chat.ChatId);
            }
        }
    }

    public async Task ReconcileAsync(ITelegramBotClient bot, BirthdayChatState chat, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow;
        if (chat.LastDayProcessed.Month == today.Month && chat.LastDayProcessed.Day == today.Day)
        {
            return;
        }

        chat.LastDayProcessed = today;
        await birthdays.SaveAsync(chat, cancellationToken);

        foreach (var birthday in chat.Birthdays.Where(b => b.Birthday.Day == today.Day && b.Birthday.Month == today.Month))
        {
            await bot.SendMessage(chat.ChatId, $"Happy Birthday to {birthday.Name}!", cancellationToken: cancellationToken);
        }
    }
}
