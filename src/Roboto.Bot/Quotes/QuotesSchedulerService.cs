using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Roboto.Bot.Quotes;

/// <summary>Ticks QuotesReconciler on a fixed interval - same shape as
/// XyzzyRoundSchedulerService/BirthdaysSchedulerService. 10-minute interval matches legacy's own
/// backgroundMins for mod_quote.</summary>
public sealed class QuotesSchedulerService(
    ILogger<QuotesSchedulerService> logger, IOptions<BotOptions> options, QuotesReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(10);

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
                logger.LogError(ex, "mod_quote scheduler tick failed");
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
