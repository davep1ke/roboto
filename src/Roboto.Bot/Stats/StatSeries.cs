namespace Roboto.Bot.Stats;

public enum StatMode
{
    /// <summary>Each recorded value adds to a running total, e.g. "games started" - counts up forever.</summary>
    Cumulative,

    /// <summary>Each recorded value replaces the previous one - a snapshot of "right now", e.g. "active games".</summary>
    Snapshot,
}

public sealed record StatPoint(DateTime Utc, double Value);

public sealed class StatSeries
{
    public string Name { get; set; } = "";
    public StatMode Mode { get; set; } = StatMode.Cumulative;
    public double Total { get; set; }
    public List<StatPoint> RecentPoints { get; set; } = [];
}
