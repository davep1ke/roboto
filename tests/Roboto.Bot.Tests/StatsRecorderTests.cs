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
        // All three writes land "now" - the same 15-min bucket - so they accumulate into one.
        var bucket = Assert.Single(series.Buckets);
        Assert.Equal(5, bucket.Value);
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
        Assert.Equal(2, Assert.Single(series.Buckets).Value);
        Assert.False(series.HasAllTimeTotal); // a gauge has no meaningful additive all-time total
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
    public async Task BucketsAreBoundedToTheRetentionWindowRatherThanGrowingForever()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < StatsRecorder.MaxBuckets + 10; i++)
        {
            await stats.RecordAtAsync("test.bounded", 1, StatMode.Cumulative, start + StatsRecorder.BucketSize * i, CancellationToken.None);
        }

        var series = await stats.GetAsync("test.bounded", CancellationToken.None);
        Assert.Equal(StatsRecorder.MaxBuckets, series!.Buckets.Count);
        Assert.Equal(StatsRecorder.MaxBuckets + 10, series.Total); // the total isn't truncated, only the bucket history
        // The oldest surviving bucket is exactly the retention cutoff, not older.
        var expectedOldest = StatsRecorder.FloorToBucket(start + StatsRecorder.BucketSize * (StatsRecorder.MaxBuckets + 9))
                              - StatsRecorder.BucketSize * (StatsRecorder.MaxBuckets - 1);
        Assert.Equal(expectedOldest, series.Buckets[0].StartUtc);
    }

    [Fact]
    public async Task WritesWithinTheSameBucketAccumulateOrOverwritePerMode()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        var t = new DateTime(2026, 1, 1, 3, 5, 0, DateTimeKind.Utc); // same 15-min bucket as t+5min

        await stats.RecordAtAsync("test.samebucket", 2, StatMode.Cumulative, t, CancellationToken.None);
        await stats.RecordAtAsync("test.samebucket", 3, StatMode.Cumulative, t.AddMinutes(5), CancellationToken.None);

        var series = await stats.GetAsync("test.samebucket", CancellationToken.None);
        Assert.Equal(5, Assert.Single(series!.Buckets).Value);
    }

    [Fact]
    public async Task WritesInDifferentBucketsCreateSeparateEntries()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        var t = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

        await stats.RecordAtAsync("test.rollover", 1, StatMode.Cumulative, t, CancellationToken.None);
        await stats.RecordAtAsync("test.rollover", 1, StatMode.Cumulative, t + StatsRecorder.BucketSize, CancellationToken.None);

        var series = await stats.GetAsync("test.rollover", CancellationToken.None);
        Assert.Equal(2, series!.Buckets.Count);
        Assert.Equal(t, series.Buckets[0].StartUtc);
        Assert.Equal(t + StatsRecorder.BucketSize, series.Buckets[1].StartUtc);
    }

    [Fact]
    public async Task FirstAndLastRecordedTimestampsAreTracked()
    {
        using var bot = new TestBot();
        var stats = bot.Services.GetRequiredService<StatsRecorder>();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddDays(1);

        await stats.RecordAtAsync("test.timestamps", 1, StatMode.Cumulative, t1, CancellationToken.None);
        await stats.RecordAtAsync("test.timestamps", 1, StatMode.Cumulative, t2, CancellationToken.None);

        var series = await stats.GetAsync("test.timestamps", CancellationToken.None);
        Assert.Equal(t1, series!.FirstRecordedUtc);
        Assert.Equal(t2, series.LastRecordedUtc);
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
