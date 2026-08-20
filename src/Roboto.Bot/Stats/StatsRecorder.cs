using Roboto.Bot.Persistence;

namespace Roboto.Bot.Stats;

/// <summary>
/// Replaces legacy's Roboto.Settings.stats.registerStatType/logStat - a named-counter engine with
/// two parallel tracks per series (StatSeries): a genuine all-time Total (legacy never had this -
/// its stats are a pure rolling window) alongside legacy's own bucketed 15-min/48h time series
/// (Buckets, for /statgraph). No registration step needed unlike legacy (registerStatType before
/// logStat) - a series is created on first use with whatever StatMode the caller passes, same
/// "just add a property, no schema migration" philosophy as the rest of IStateStore.
/// </summary>
public sealed class StatsRecorder(IStateStore store)
{
    /// <summary>Legacy's stats.granularity (Roboto/Core/stats.cs).</summary>
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(15);

    /// <summary>Legacy's stats.graphYAxisCount - 192 buckets * 15 min = 48 hours of history.</summary>
    public const int MaxBuckets = 192;

    public static DateTime FloorToBucket(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % BucketSize.Ticks, DateTimeKind.Utc);

    public Task RecordAsync(string name, double value, StatMode mode, CancellationToken cancellationToken) =>
        RecordAtAsync(name, value, mode, DateTime.UtcNow, cancellationToken);

    /// <summary>Internal so bucket-rollover/pruning can be tested with synthetic timestamps rather
    /// than waiting on real 15-minute boundaries - RecordAsync always calls this with "now".</summary>
    internal async Task RecordAtAsync(string name, double value, StatMode mode, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var series = await store.LoadAsync<StatSeries>(Key(name), cancellationToken) ?? new StatSeries { Name = name, Mode = mode };
        series.Mode = mode;

        series.Total = mode == StatMode.Cumulative ? series.Total + value : value;
        series.Latest = value;
        series.FirstRecordedUtc ??= nowUtc;
        series.LastRecordedUtc = nowUtc;

        var bucketStart = FloorToBucket(nowUtc);
        var lastBucket = series.Buckets.Count > 0 ? series.Buckets[^1] : null;
        if (lastBucket is not null && lastBucket.StartUtc == bucketStart)
        {
            lastBucket.Value = mode == StatMode.Cumulative ? lastBucket.Value + value : value;
        }
        else
        {
            // Almost always append-in-order (writes arrive roughly chronologically) - only scan
            // backwards for a rare out-of-order/backwards-clock write, rather than legacy's
            // getSlice, which did a full linear Where scan on every single write.
            var existing = series.Buckets.FindLast(b => b.StartUtc == bucketStart);
            if (existing is not null)
            {
                existing.Value = mode == StatMode.Cumulative ? existing.Value + value : value;
            }
            else
            {
                var insertAt = series.Buckets.FindLastIndex(b => b.StartUtc < bucketStart) + 1;
                series.Buckets.Insert(insertAt, new StatBucket { StartUtc = bucketStart, Value = value });
            }
        }

        var cutoff = bucketStart - BucketSize * (MaxBuckets - 1);
        series.Buckets.RemoveAll(b => b.StartUtc < cutoff);

        await store.SaveAsync(Key(name), series, cancellationToken);
    }

    public Task<StatSeries?> GetAsync(string name, CancellationToken cancellationToken) =>
        store.LoadAsync<StatSeries>(Key(name), cancellationToken);

    public Task<IReadOnlyList<StatSeries>> GetAllAsync(CancellationToken cancellationToken) =>
        store.LoadAllAsync<StatSeries>("stats:%", cancellationToken);

    private static string Key(string name) => $"stats:{name}";
}
