using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Stats;

namespace Roboto.Bot.Tests;

public class StatsRecorderTests
{
    [Fact]
    public async Task CumulativeStatsAccumulate()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await stats.RecordAsync("test.counter", 1, StatMode.Cumulative, CancellationToken.None);
        await stats.RecordAsync("test.counter", 1, StatMode.Cumulative, CancellationToken.None);
        await stats.RecordAsync("test.counter", 3, StatMode.Cumulative, CancellationToken.None);

        var series = await stats.GetAsync("test.counter", CancellationToken.None);
        Assert.Equal(5, series!.Total);
        Assert.Equal(3, series.RecentPoints.Count);
    }

    [Fact]
    public async Task SnapshotStatsReplaceRatherThanAccumulate()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await stats.RecordAsync("test.gauge", 5, StatMode.Snapshot, CancellationToken.None);
        await stats.RecordAsync("test.gauge", 2, StatMode.Snapshot, CancellationToken.None);

        var series = await stats.GetAsync("test.gauge", CancellationToken.None);
        Assert.Equal(2, series!.Total);
    }

    [Fact]
    public async Task GetAllReturnsEveryRecordedSeries()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        await stats.RecordAsync("test.a", 1, StatMode.Cumulative, CancellationToken.None);
        await stats.RecordAsync("test.b", 1, StatMode.Cumulative, CancellationToken.None);

        var all = await stats.GetAllAsync(CancellationToken.None);
        Assert.Contains(all, s => s.Name == "test.a");
        Assert.Contains(all, s => s.Name == "test.b");
    }

    [Fact]
    public async Task RecentPointsAreBoundedRatherThanGrowingForever()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        for (var i = 0; i < StatsRecorder.MaxRecentPoints + 10; i++)
        {
            await stats.RecordAsync("test.bounded", 1, StatMode.Cumulative, CancellationToken.None);
        }

        var series = await stats.GetAsync("test.bounded", CancellationToken.None);
        Assert.Equal(StatsRecorder.MaxRecentPoints, series!.RecentPoints.Count);
        Assert.Equal(StatsRecorder.MaxRecentPoints + 10, series.Total); // the total isn't truncated, only the history
    }

    [Fact]
    public async Task StatsSurviveARestart()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        await stats.RecordAsync("test.persisted", 7, StatMode.Cumulative, CancellationToken.None);

        using var restarted = bot.Restart();
        var series = await restarted.Services.GetRequiredService<StatsRecorder>().GetAsync("test.persisted", CancellationToken.None);

        Assert.Equal(7, series!.Total);
    }
}
