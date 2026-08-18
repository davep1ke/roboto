using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Roboto.Bot.Birthdays;

/// <summary>
/// Ticks BirthdaysReconciler on a fixed interval - same shape as XyzzyRoundSchedulerService (own
/// TelegramBotClient rather than sharing TelegramPollingService's, registered directly in
/// Program.cs rather than AddRobotoBot() so tests can call BirthdaysReconciler directly instead of
/// waiting on a real timer). An hourly tick is plenty for a once-a-day check - legacy used 120
/// minutes for the same reason.
/// </summary>
public sealed class BirthdaysSchedulerService(
    ILogger<BirthdaysSchedulerService> logger, IOptions<BotOptions> options, BirthdaysReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bot = new TelegramBotClient(options.Value.TelegramToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reconciler.ReconcileAllAsync(bot, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "mod_birthdays scheduler tick failed");
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
