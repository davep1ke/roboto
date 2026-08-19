using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Roboto.Bot.Chats;

/// <summary>
/// Ticks ChatPurgeReconciler on a fixed interval - same shape as BirthdaysSchedulerService. Doesn't
/// need its own ITelegramBotClient (unlike the other scheduler services) since purging never sends
/// a message, only deletes data - a daily tick is plenty for a threshold measured in months.
/// </summary>
public sealed class ChatPurgeSchedulerService(ILogger<ChatPurgeSchedulerService> logger, ChatPurgeReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reconciler.ReconcileAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dormant-chat purge sweep failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }
    }
}
