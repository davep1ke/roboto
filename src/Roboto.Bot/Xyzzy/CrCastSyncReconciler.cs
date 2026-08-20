using Microsoft.Extensions.Logging;

namespace Roboto.Bot.Xyzzy;

/// <summary>Ports legacy's mod_xyzzy_coredata.packSyncCheck() - re-syncs crcast-sourced packs whose
/// NextSyncUtc has passed, capped at MaxPacksPerTick per tick (legacy's maxPacksToSyncInOneGo=3),
/// so a large catalog's syncs never all land in one pass. Pulled out of CrCastSyncSchedulerService
/// (a BackgroundService, awkward to test directly) for direct testability - same split as every
/// other Reconciler/SchedulerService pair in this codebase.</summary>
public sealed class CrCastSyncReconciler(CrCastPackImportService importer, ILogger<CrCastSyncReconciler> logger)
{
    public const int MaxPacksPerTick = 3;

    public async Task ReconcileAllAsync(CancellationToken cancellationToken)
    {
        var due = CardCatalog.Packs
            .Where(p => p.PackCode is not null && p.NextSyncUtc is not null && p.NextSyncUtc <= DateTime.UtcNow)
            .OrderBy(p => p.NextSyncUtc)
            .Take(MaxPacksPerTick)
            .ToList();

        foreach (var pack in due)
        {
            try
            {
                var outcome = await importer.ImportOrSyncAsync(pack.PackCode!, cancellationToken);
                if (!outcome.Success)
                {
                    await importer.RescheduleAfterFailureAsync(pack.Id, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Syncing crcast pack {PackCode} failed", pack.PackCode);
                await importer.RescheduleAfterFailureAsync(pack.Id, cancellationToken);
            }
        }
    }
}
