using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Covers the grand-total stats feature (MIGRATION.md phase 9 addendum) - statType.total/
/// .totalSince, tracked separately from the 48h rolling window statSlices get pruned to. Not a
/// legacy feature at all; legacy's stats were always a pure rolling window. Surfaced via
/// mod_xyzzy.getStats() ("/stats" in mod_standard.cs), which every plugin already contributes a
/// line to.
/// </summary>
public class StatsGrandTotalTests
{
    private const long ChatId = -800;
    private const long Alice = 80;
    private const long Bob = 81;
    private const long Carol = 82;

    private static void SeedCards(int questionCount = 5, int answerCount = 40)
    {
        var xyzzy = Plugins.plugins.OfType<mod_xyzzy>().Single();
        var coreData = (mod_xyzzy_coredata)xyzzy.getPluginData();
        coreData.questions.Clear();
        coreData.answers.Clear();
        for (int i = 0; i < questionCount; i++)
        {
            coreData.questions.Add(new mod_xyzzy_card($"Question {i} ___?", mod_xyzzy.dummyPackID, 1));
        }
        for (int i = 0; i < answerCount; i++)
        {
            coreData.answers.Add(new mod_xyzzy_card($"Answer {i}", mod_xyzzy.dummyPackID));
        }
    }

    [Fact]
    public void StartingAGameIncrementsTheGrandTotalSurvivingBeyondTheStatsSlice()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");

        var total = Roboto.Settings.stats.getGrandTotal("New Games Started", typeof(mod_xyzzy));
        Assert.True(total.HasValue);
        Assert.Equal(1, total.Value.total);
        Assert.Equal(System.DateTime.Now.Date, total.Value.since.Date);
    }

    [Fact]
    public void GrandTotalKeepsAccumulatingAcrossMultipleGamesUnlikeTheRollingWindow()
    {
        using var bot = new TestHarness();
        SeedCards();

        // Full 3-player start, not just the Invites screen - Alice (the starter/tzar) then has no
        // outstanding DM reply pending, so /xyzzy_settings -> Abandon isn't racing an earlier
        // unresolved prompt for the same user (see XyzzyCarriedForwardDeltasTests' own note on this).
        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");
        bot.TapButton(Alice, "Abandon", "Alice");
        bot.TapButton(Alice, "Yes", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");

        var total = Roboto.Settings.stats.getGrandTotal("New Games Started", typeof(mod_xyzzy));
        Assert.Equal(2, total.Value.total);
    }

    [Fact]
    public void StatsCommandReportsTheGrandTotalLines()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice"); // Use Defaults now auto-adds 2 bots - clear them for a clean human-only baseline
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        bot.SendGroupMessage(ChatId, Alice, "/stats", "Alice");

        string statsText = bot.BotClient.SentMessages[^1].Text;
        Assert.Contains("1 games started since " + System.DateTime.Now.ToString("yyyy-MM-dd"), statsText);
        Assert.Contains("1 hands played since " + System.DateTime.Now.ToString("yyyy-MM-dd"), statsText);
    }
}
