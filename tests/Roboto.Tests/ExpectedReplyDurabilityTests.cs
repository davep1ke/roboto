using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;
using RobotoChatBot.Persistence;

namespace RobotoTests;

/// <summary>
/// expected_replies write-through (2026-08-24 addendum, closing a gap phase 3 deliberately left open -
/// see SqliteStateStore.cs's expected_replies notes and MIGRATION.md): a mutation to
/// Roboto.Settings.expectedReplies now lands in SQLite the moment it happens
/// (Messaging.addExpectedReply/removeExpectedReply, ExpectedReply.sendMessage()'s own
/// UpdateExpectedReply call for a reply that gets sent later than it was queued), not only at the next
/// periodic settings.save(). These tests open a second, independent SqliteStateStore against the same
/// on-disk file the harness's Roboto.Store already points at (TestHarness.DbPath) and read it back
/// directly - proving the row is really durable on disk, not just correct in the in-memory
/// Roboto.Settings.expectedReplies list - and never call settings.save() anywhere in them.
/// </summary>
public class ExpectedReplyDurabilityTests
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
    public void AQueuedQuestionIsOnDiskImmediatelyWithNoExplicitSave()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice");
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        // Bob now has a sent, outstanding "Question" ExpectedReply - open a completely separate
        // SqliteStateStore against the same file and confirm it's already there.
        var freshStore = new SqliteStateStore(bot.DbPath);
        var reloaded = freshStore.LoadExpectedReplies();

        var bobsReply = Assert.Single(reloaded, r => r.userID == Bob && r.messageData == "Question");
        Assert.True(bobsReply.outboundMessageID > 0, "the reply was sent immediately, so its outboundMessageID should already be persisted");
    }

    [Fact]
    public void AnAnsweredQuestionIsGoneFromDiskImmediatelyWithNoExplicitSave()
    {
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice");
        bot.TapButton(Alice, "Add Bots", "Alice");
        bot.TapButton(Alice, "Remove All Bots", "Alice");
        bot.SendGroupMessage(ChatId, Bob, "/xyzzy_join", "Bob");
        bot.SendGroupMessage(ChatId, Carol, "/xyzzy_join", "Carol");
        bot.TapButton(Alice, "Start", "Alice");

        string bobsAnswer = bot.LastKeyboardMessageTo(Bob).KeyboardRows![0][0].Text;
        bot.TapButton(Bob, bobsAnswer, "Bob");

        var freshStore = new SqliteStateStore(bot.DbPath);
        var reloaded = freshStore.LoadExpectedReplies();

        Assert.DoesNotContain(reloaded, r => r.userID == Bob && r.messageData == "Question");
    }

    [Fact]
    public void AReplyQueuedBehindAnOutstandingOneHasItsSentStatePersistedWithoutAnExplicitSaveOnceItsActuallySent()
    {
        // Reproduces the exact live shape MIGRATION.md's "/xyzzy_settings silently vanishing" fix
        // covers: Alice requests /xyzzy_settings while she still has an outstanding "Question" reply,
        // so it gets queued unsent (outboundMessageID still 0 on disk at that point) behind it.
        // Answering the Question frees the queue and sends the Settings menu - this test's actual
        // focus is that the persisted row for it picks up a real outboundMessageID immediately,
        // without a settings.save() in between.
        using var bot = new TestHarness();
        SeedCards();

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_start", "Alice");
        bot.TapButton(Alice, "Use Defaults", "Alice"); // auto-adds 2 bots
        bot.TapButton(Alice, "Start", "Alice");

        var chatData = (mod_xyzzy_chatdata)Chats.getChat(ChatId).getPluginData(typeof(mod_xyzzy_chatdata), true);
        var judgingKeyboard = bot.LastKeyboardMessageTo(Alice).KeyboardRows!;
        bot.TapButton(Alice, judgingKeyboard[0][0].Text, "Alice");
        Assert.Equal(1, chatData.lastPlayerAsked);

        bot.SendGroupMessage(ChatId, Alice, "/xyzzy_settings", "Alice");

        var freshStoreBeforeAnswering = new SqliteStateStore(bot.DbPath);
        var settingsReplyBeforeAnswering = freshStoreBeforeAnswering.LoadExpectedReplies().Single(r => r.userID == Alice && r.messageData == "Settings");
        Assert.Equal(0, settingsReplyBeforeAnswering.outboundMessageID);

        string alicesAnswer = bot.LastKeyboardMessageTo(Alice).KeyboardRows![0][0].Text;
        bot.TapButton(Alice, alicesAnswer, "Alice");

        var freshStoreAfterAnswering = new SqliteStateStore(bot.DbPath);
        var settingsReplyAfterAnswering = freshStoreAfterAnswering.LoadExpectedReplies().Single(r => r.userID == Alice && r.messageData == "Settings");
        Assert.True(settingsReplyAfterAnswering.outboundMessageID > 0, "the Settings reply was sent once the queue drained - its persisted row should reflect that immediately, not only after the next settings.save()");
    }
}
