using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Roboto.Bot.Steam;

/// <summary>Ticks SteamReconciler on a fixed interval - same shape as
/// XyzzyRoundSchedulerService/BirthdaysSchedulerService/QuotesSchedulerService. 15-minute interval
/// matches legacy's own backgroundMins for mod_steam. A no-op (logs once, does nothing) when
/// BotOptions.SteamApiKey is blank - see SteamReconciler.</summary>
public sealed class SteamSchedulerService(
    ILogger<SteamSchedulerService> logger, IOptions<BotOptions> options, SteamReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

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
                logger.LogError(ex, "mod_steam scheduler tick failed");
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
