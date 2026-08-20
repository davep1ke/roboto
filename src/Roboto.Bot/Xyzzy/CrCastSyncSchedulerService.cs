using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Roboto.Bot.Xyzzy;

/// <summary>Ticks CrCastSyncReconciler on a fixed interval - same shape as BirthdaysSchedulerService.
/// Legacy checked every 1 minute (mod_xyzzy.backgroundMins), but each pack's own sync window is
/// measured in days (3-9), so an hourly tick is plenty - being off by up to an hour from a pack's
/// exact NextSyncUtc is inconsequential at that scale.</summary>
public sealed class CrCastSyncSchedulerService(ILogger<CrCastSyncSchedulerService> logger, CrCastSyncReconciler reconciler) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(60);

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
                logger.LogError(ex, "crcast pack sync tick failed");
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
