using System;
using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Modules;

namespace RobotoTests;

/// <summary>
/// Covers whole-bot background housekeeping that lives outside any one module's own
/// backgroundProcessing: SqliteStateStore's logs-table 30-day purge (phase 3c) and
/// Chats.removeDormantChats (phase 4's per-chat lock applied to the same dormant-chat sweep legacy
/// always had).
/// </summary>
public class BackgroundReconcilersTests
{
    [Fact]
    public void PurgeLogsOlderThanRemovesOnlyRowsPastTheCutoff()
    {
        using var bot = new TestHarness();
        Roboto.Store.WriteLogEvent(DateTime.UtcNow.AddDays(-40), "Information", "Test", "old message 1");
        Roboto.Store.WriteLogEvent(DateTime.UtcNow.AddDays(-40), "Information", "Test", "old message 2");
        Roboto.Store.WriteLogEvent(DateTime.UtcNow.AddDays(-5), "Information", "Test", "recent message");

        int purged = Roboto.Store.PurgeLogsOlderThan(DateTime.UtcNow.AddDays(-30));

        Assert.Equal(2, purged);
    }

    [Fact]
    public void PurgeLogsOlderThanIsIdempotentOnceRowsAreAlreadyGone()
    {
        using var bot = new TestHarness();
        Roboto.Store.WriteLogEvent(DateTime.UtcNow.AddDays(-40), "Information", "Test", "old message");
        Roboto.Store.PurgeLogsOlderThan(DateTime.UtcNow.AddDays(-30));

        int purgedAgain = Roboto.Store.PurgeLogsOlderThan(DateTime.UtcNow.AddDays(-30));

        Assert.Equal(0, purgedAgain);
    }

    [Fact]
    public void ChatDataThatIsAllPurgableGetsRemovedByTryPurgeData()
    {
        // Unit-level: exercises chat.tryPurgeData()/removeDormantChats' actual purge mechanism in
        // isolation, with only purgable module data present. See the next test for why this never
        // actually happens for a real chat that's exchanged any message at all.
        using var bot = new TestHarness();
        const long chatId = -1600;
        bot.SendGroupMessage(chatId, 160, "/xyzzy_status", "Alice");

        var chat = Chats.getChat(chatId);
        var xyzzyData = (mod_xyzzy_chatdata)chat.getPluginData(typeof(mod_xyzzy_chatdata), true);
        chat.lastupdate = DateTime.Now.AddDays(-150);
        xyzzyData.statusChangedTime = DateTime.Now.AddDays(-150);
        // Strip every other module's auto-created chatdata (mod_birthday_data, mod_quote_data,
        // mod_standard_chatdata, mod_steam_chat_data - all created unconditionally by dispatch
        // regardless of which command matched, see the next test) so mod_xyzzy is the only thing
        // tryPurgeData has to evaluate.
        chat.chatData.RemoveAll(d => d is not mod_xyzzy_chatdata);

        Chats.removeDormantChats();

        Assert.Null(Roboto.Settings.chatData.FirstOrDefault(c => c.chatID == chatId));
    }

    [Fact]
    public void RealChatsCanNeverActuallyBePurgedBecauseBirthdayDataAlwaysBlocksIt()
    {
        // A real, confirmed-in-legacy interaction (byte-for-byte identical in legacy-winforms-
        // baseline) worth documenting via a test, not a bug to fix: mod_birthdays.chatEvent fetches
        // c.getPluginData<mod_birthday_data>() unconditionally at the top, for every message
        // regardless of which command matched - so any chat that has ever exchanged a single
        // message already has a mod_birthday_data entry. mod_birthday_data.isPurgable() is
        // unconditionally false ("Never purge chats with birthday data"), and chat.tryPurgeData()
        // only purges when *every* module's data reports purgable. Net effect: removeDormantChats'
        // purge is, in practice, permanently unreachable for any chat that has ever done anything -
        // not because of a mistake (no TODO or "doesn't handle X" comment marks this one, unlike the
        // group-question bug), just an emergent interaction between two independently-reasonable
        // module decisions.
        using var bot = new TestHarness();
        const long chatId = -1601;
        bot.SendGroupMessage(chatId, 161, "/xyzzy_status", "Alice");

        var chat = Chats.getChat(chatId);
        var xyzzyData = (mod_xyzzy_chatdata)chat.getPluginData(typeof(mod_xyzzy_chatdata), true);
        chat.lastupdate = DateTime.Now.AddDays(-150);
        xyzzyData.statusChangedTime = DateTime.Now.AddDays(-150);

        Chats.removeDormantChats();

        Assert.NotNull(Roboto.Settings.chatData.FirstOrDefault(c => c.chatID == chatId));
    }
}
