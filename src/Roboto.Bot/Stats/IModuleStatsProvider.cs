namespace Roboto.Bot.Stats;

/// <summary>
/// One module's contribution to /stats - legacy's RobotoModuleTemplate.getStats() override,
/// discovered by the same reflection-registration loop RobotoServiceCollectionExtensions.
/// AddRobotoBot already uses for IBotCommand/ICallbackQueryHandler. Legacy's /stats is a hybrid,
/// not a dump of the stats-engine registry: bot name/uptime/chat count, then each module's own
/// live-computed snapshot line(s) - this interface is exactly that per-module piece.
/// </summary>
public interface IModuleStatsProvider
{
    /// <summary>Section header text, matching legacy's own module names (e.g. "mod_xyzzy").</summary>
    string ModuleName { get; }

    /// <summary>Display order in /stats - lower first, matching roughly the order legacy's own
    /// plugin list happened to load in.</summary>
    int Order { get; }

    Task<string> GetStatsAsync(CancellationToken cancellationToken);
}
