namespace Roboto.Bot.Stats;

public enum StatMode
{
    /// <summary>Each recorded value adds to a running total, e.g. "games started" - counts up forever.</summary>
    Cumulative,

    /// <summary>Each recorded value replaces the previous one - a snapshot of "right now", e.g. "active games".</summary>
    Snapshot,
}

/// <summary>One 15-minute slice of a series' history (legacy's statSlice, Roboto/Core/stats.cs) -
/// StartUtc is always floored to a StatsRecorder.BucketSize boundary. A new value combines into the
/// current bucket per StatMode: Cumulative adds, Snapshot overwrites - the same rule StatMode
/// already applies to Total, just scoped to one 15-minute window instead of all time.</summary>
public sealed class StatBucket
{
    public DateTime StartUtc { get; set; }
    public double Value { get; set; }
}

public sealed class StatSeries
{
    public string Name { get; set; } = "";
    public StatMode Mode { get; set; } = StatMode.Cumulative;

    /// <summary>All-time, never pruned. Cumulative: sum of every value ever recorded - legacy never
    /// had this (its stats are a pure rolling window), it's what backs /stats' "all-time" lines.
    /// Snapshot: mirrors Latest - a gauge (e.g. "active games") has no meaningful additive all-time
    /// total, so this intentionally does NOT tick up every time the gauge is merely re-recorded.</summary>
    public double Total { get; set; }

    public double Latest { get; set; }
    public DateTime? FirstRecordedUtc { get; set; }
    public DateTime? LastRecordedUtc { get; set; }

    /// <summary>Only Cumulative series have a meaningful all-time Total - a property of the data,
    /// not a rule /stats' renderer has to remember separately.</summary>
    public bool HasAllTimeTotal => Mode == StatMode.Cumulative;

    /// <summary>Rolling 15-min/48h window (legacy's granularity/graphYAxisCount exactly), sparse -
    /// only buckets that actually got a value exist - and kept in StartUtc order. Pruned inline on
    /// every write (StatsRecorder.RecordAtAsync), not by a separate housekeeping sweep: every series
    /// is already loaded-and-rewritten per write, so pruning there is free and keeps the 192-bucket
    /// bound structural rather than eventually-consistent.</summary>
    public List<StatBucket> Buckets { get; set; } = [];
}
