namespace RobotoTests;

/// <summary>
/// /statgraph (mod_standard.cs) renders via stats.generateImage - ScottPlot on legacy's own
/// stats.cs data shape (phase 6). Asserts on the produced PNG's magic bytes rather than pixel
/// content - a full visual-regression check isn't warranted here, just that a real image comes out
/// the other end of the pipeline for a matching series, and that a non-matching pattern falls back
/// to the "no statistics" message instead of crashing.
/// </summary>
public class StatGraphTests
{
    private const long ChatId = -600;
    private const long Alice = 50;
    private static readonly byte[] PngMagicBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void StatGraphWithNoArgsChartsEverythingAndSendsAPng()
    {
        using var bot = new TestHarness();

        // Roboto.Settings.stats.startup() (run by TestHarness's constructor) logs a "Startup" stat
        // against typeof(Roboto), so there's always at least one series with real data by the time
        // any test runs, with no game-specific setup needed.
        bot.SendGroupMessage(ChatId, Alice, "/statgraph", "Alice");

        var photo = Assert.Single(bot.BotClient.SentPhotos);
        Assert.Equal(ChatId, photo.ChatId);
        Assert.Equal(PngMagicBytes, photo.Content[..8]);
        Assert.True(photo.Content.Length > 1000, "Rendered chart should be a non-trivial PNG, not a near-empty stub image.");
    }

    [Fact]
    public void StatGraphWithAPatternThatMatchesNothingReportsNoStatistics()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/statgraph ThisStatDoesNotExist12345", "Alice");

        Assert.Empty(bot.BotClient.SentPhotos);
        Assert.Contains("No statistics were found", bot.BotClient.SentMessages[^1].Text);
    }

    [Fact]
    public void StatGraphWithAnExactSeriesNameCharts()
    {
        using var bot = new TestHarness();

        bot.SendGroupMessage(ChatId, Alice, "/statgraph Roboto>Startup", "Alice");

        var photo = Assert.Single(bot.BotClient.SentPhotos);
        Assert.Equal(PngMagicBytes, photo.Content[..8]);
    }
}
