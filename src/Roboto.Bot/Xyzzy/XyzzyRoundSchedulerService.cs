using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Roboto.Bot.Xyzzy;

/// <summary>
/// New infrastructure - before mod_xyzzy needed it, TelegramPollingService's poll loop was the only
/// BackgroundService anywhere in the app, and there was no timer/scheduling abstraction at all.
/// Ticks on a fixed interval and delegates everything to XyzzyRoundReconciler (kept separate and
/// directly testable - a raw BackgroundService is awkward to unit test, same reasoning
/// MessageDispatcher was pulled out of TelegramPollingService).
///
/// Constructs its own TelegramBotClient rather than sharing TelegramPollingService's - matches this
/// codebase's existing convention of threading ITelegramBotClient through as a parameter rather
/// than resolving it via DI (no command/router anywhere takes it as a constructor dependency
/// either). Two lightweight HTTP-wrapper clients existing side by side is cheap and avoids coupling
/// this service to the polling service's lifecycle.
///
/// Registered directly as a hosted service in Program.cs, not inside AddRobotoBot() - same reason
/// TelegramPollingService isn't in there either: tests don't want a real ticking timer running
/// (non-deterministic), they call XyzzyRoundReconciler directly instead.
/// </summary>
public sealed class XyzzyRoundSchedulerService(
    ILogger<XyzzyRoundSchedulerService> logger, IOptions<BotOptions> options, XyzzyRoundReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

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
                logger.LogError(ex, "mod_xyzzy round scheduler tick failed");
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
