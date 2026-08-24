using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using RobotoChatBot;
using RobotoChatBot.Modules;
using RobotoChatBot.Persistence;

namespace RobotoTests;

/// <summary>
/// Covers whole-bot background housekeeping that lives outside any one module's own
/// backgroundProcessing: SqliteStateStore's datafix runner and Chats.removeDormantChats (phase 4's
/// per-chat lock applied to the same dormant-chat sweep legacy always had).
/// </summary>
public class BackgroundReconcilersTests
{
    [Fact]
    public void DropLogsTableDataFixRemovesAPreExistingLogsTableAndIsIdempotent()
    {
        // Standalone SqliteStateStore rather than TestHarness - TestHarness already runs
        // RunPendingDataFixes as part of its own startup, before this test gets control, so a fresh
        // harness DB never has a `logs` table to drop in the first place. Simulates a DB from before
        // the `logs` table (Serilog DbLogSink's target) was removed.
        string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"roboto-test-{Guid.NewGuid():N}.db");
        var store = new SqliteStateStore(dbPath);
        store.Initialize();

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE logs (id INTEGER PRIMARY KEY, message TEXT);";
            command.ExecuteNonQuery();
        }

        store.RunPendingDataFixes(DataFixes.All);

        Assert.False(TableExists(store, "logs"));

        // Idempotent: running again (e.g. next startup) doesn't error and doesn't reapply.
        store.RunPendingDataFixes(DataFixes.All);
    }

    [Fact]
    public void RunPendingDataFixesNoOpsWhenTheTargetTableNeverExisted()
    {
        // A fresh instance created after the `logs` table was removed from Initialize() never has
        // one - "0001_drop_logs_table"'s DROP TABLE IF EXISTS must not throw here.
        using var bot = new TestHarness();

        Roboto.Store.RunPendingDataFixes(DataFixes.All);

        Assert.False(TableExists(Roboto.Store, "logs"));
    }

    private static bool TableExists(SqliteStateStore store, string tableName)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() != null;
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
