using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Helpers;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Covers the "ZZ Dummy Pack" bootstrap (replacing the old hardcoded 7-pack stub list -
/// mod_xyzzy_coredata.seedDummyPack()/dropDummyPackIfNoLongerNeeded()) and the askQuestion empty-
/// card-pool crash guard, both found live: a brand-new instance with no persisted packs crashed the
/// moment a game actually started, since it had nothing to deal.
/// </summary>
public class XyzzyDummyPackTests
{
    private const long ChatId = -800;
    private const long Alice = 80;
    private const long Bob = 81;
    private const long Carol = 82;

    private static mod_xyzzy_coredata CoreData() =>
        (mod_xyzzy_coredata)Plugins.plugins.OfType<mod_xyzzy>().Single().getPluginData();

    [Fact]
    public void FreshInstanceGetsSeededWithTheDummyPack()
    {
        using var bot = new TestHarness();
        var coreData = CoreData();
        // TestHarness already ran startupChecks() once (seeding, then clearing packs back out for
        // test isolation) - clear everything back to a genuinely blank slate and re-run it directly,
        // simulating a real brand-new instance's very first boot.
        coreData.packs.Clear();
        coreData.questions.Clear();
        coreData.answers.Clear();

        coreData.startupChecks();

        cardcast_pack dummyPack = Assert.Single(coreData.packs);
        Assert.Equal("ZZ Dummy Pack", dummyPack.name);
        Assert.Equal(mod_xyzzy.dummyPackID, dummyPack.packID);
        Assert.Equal(10, coreData.questions.Count(q => q.packID == mod_xyzzy.dummyPackID));
        Assert.Equal(10, coreData.answers.Count(a => a.packID == mod_xyzzy.dummyPackID));
    }

    [Fact]
    public void DummyPackIsDroppedOnceMoreThanFiveRealPacksExist()
    {
        using var bot = new TestHarness();
        var coreData = CoreData();
        coreData.packs.Clear();
        coreData.questions.Clear();
        coreData.answers.Clear();
        coreData.startupChecks(); // seeds the dummy pack

        Assert.Contains(coreData.packs, p => p.packID == mod_xyzzy.dummyPackID);

        // Exactly 5 real packs alongside it - not yet enough to drop.
        for (int i = 0; i < 5; i++)
        {
            coreData.packs.Add(new cardcast_pack("Real Pack " + i, "REAL" + i, "desc"));
        }
        coreData.startupChecks();
        Assert.Contains(coreData.packs, p => p.packID == mod_xyzzy.dummyPackID);

        // A 6th real pack tips it over - dummy pack (and its now-orphaned cards) should be dropped.
        coreData.packs.Add(new cardcast_pack("Real Pack 5", "REAL5", "desc"));
        coreData.startupChecks();

        Assert.DoesNotContain(coreData.packs, p => p.packID == mod_xyzzy.dummyPackID);
        Assert.DoesNotContain(coreData.questions, q => q.packID == mod_xyzzy.dummyPackID);
        Assert.DoesNotContain(coreData.answers, a => a.packID == mod_xyzzy.dummyPackID);
    }

    [Fact]
    public void StartingAGameWithNoCardsAvailableSendsAMessageInsteadOfCrashing()
    {
        using var bot = new TestHarness();
        var coreData = CoreData();
        // Genuinely empty pool - no packs, no cards at all (not even the dummy pack's leftovers).
        coreData.packs.Clear();
        coreData.questions.Clear();
        coreData.answers.Clear();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice"); // auto-adds 2 bots now - harmless here, this test is about card availability, not player composition
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");

        // Used to throw ArgumentOutOfRangeException here (mod_xyzzy_chatdata.askQuestion indexing
        // an empty remainingQuestions after a refill attempt that found nothing to refill from).
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        Assert.Equal(xyzzy_Statuses.waitingForNextHand, chatData.status);
        Assert.Contains(bot.BotClient.SentMessages, m => m.ChatId == ChatId && m.Text.Contains("No cards are available"));
    }
}
