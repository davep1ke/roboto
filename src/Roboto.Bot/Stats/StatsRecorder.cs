using Roboto.Bot.Persistence;

namespace Roboto.Bot.Stats;

/// <summary>
/// Replaces legacy's Roboto.Settings.stats.registerStatType/logStat - a lightweight named-counter
/// engine, not a full charting subsystem (that's /statgraph, still separately deferred: needs
/// ScottPlot/SkiaSharp and a debian-slim base image change, see MIGRATION.md). Every recorded value
/// is also kept as a bounded time series (RecentPoints) so a future /statgraph has real history to
/// plot without needing a data migration when it lands - this is the data-collection half, not the
/// rendering half.
///
/// No registration step needed unlike legacy (registerStatType before logStat) - a series is
/// created on first use with whatever StatMode the caller passes, same "just add a property, no
/// schema migration" philosophy as the rest of IStateStore.
/// </summary>
public sealed class StatsRecorder(IStateStore store)
{
    public const int MaxRecentPoints = 500;

    public async Task RecordAsync(string name, double value, StatMode mode, CancellationToken cancellationToken)
    {
        var series = await store.LoadAsync<StatSeries>(Key(name), cancellationToken) ?? new StatSeries { Name = name, Mode = mode };
        series.Mode = mode;
        series.Total = mode == StatMode.Cumulative ? series.Total + value : value;
        series.RecentPoints.Add(new StatPoint(DateTime.UtcNow, series.Total));

        if (series.RecentPoints.Count > MaxRecentPoints)
        {
            series.RecentPoints.RemoveRange(0, series.RecentPoints.Count - MaxRecentPoints);
        }

        await store.SaveAsync(Key(name), series, cancellationToken);
    }

    public Task<StatSeries?> GetAsync(string name, CancellationToken cancellationToken) =>
        store.LoadAsync<StatSeries>(Key(name), cancellationToken);

    public Task<IReadOnlyList<StatSeries>> GetAllAsync(CancellationToken cancellationToken) =>
        store.LoadAllAsync<StatSeries>("stats:%", cancellationToken);

    private static string Key(string name) => $"stats:{name}";
}
