using System.IO;
using System.Xml.Serialization;
using RobotoChatBot;
using RobotoChatBot.Modules;
using RobotoChatBot.Persistence;

namespace RobotoTests;

/// <summary>
/// Phase 8: settings.loadFromLegacyXml/ImportReport, proven against a synthetic fixture built by
/// serializing this branch's own live types with the exact same XmlSerializer(typeof(settings),
/// extraTypes) shape real legacy XML was written with - not hand-authored XML strings, so it stays
/// correct as the schema evolves. This only proves the import->save()->reload mechanism itself is
/// sound; a real copy of production XML still needs its own dry-run pass before any real cutover,
/// per CLAUDE.md's migration safety rules.
/// </summary>
public class LegacyXmlImportTests
{
    private static string BuildSyntheticFixture(settings source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"roboto-legacy-fixture-{System.Guid.NewGuid():N}.xml");
        var serializer = new XmlSerializer(typeof(settings), Plugins.getPluginDataTypes());
        using (var writer = new StreamWriter(path))
        {
            serializer.Serialize(writer, source);
        }
        return path;
    }

    private static settings BuildSourceSettings()
    {
        var source = new settings();
        source.telegramAPIKey = "a-real-looking-production-token";
        source.telegramAPIURL = "https://api.telegram.org/bot";
        source.botUserName = "RealProdBot";

        var xyzzyCore = new mod_xyzzy_coredata();
        xyzzyCore.questions.Add(new mod_xyzzy_card("Question ___?", mod_xyzzy.primaryPackID, 1));
        xyzzyCore.answers.Add(new mod_xyzzy_card("An answer", mod_xyzzy.primaryPackID));
        source.pluginData.Add(xyzzyCore);

        var chat = new chat(-900, "Fixture Chat");
        chat.chatData.Clear();

        var xyzzyChat = new mod_xyzzy_chatdata();
        xyzzyChat.chatID = -900;
        xyzzyChat.players.Add(new mod_xyzzy_player { name = "Alice", playerID = 1 });
        xyzzyChat.players.Add(new mod_xyzzy_player { name = "Bob", playerID = 2 });
        chat.chatData.Add(xyzzyChat);

        var quoteChat = new mod_quote_data();
        quoteChat.chatID = -900;
        quoteChat.quotes.Add(new mod_quote_quote { by = "Alice", text = "a fixture quote" });
        chat.chatData.Add(quoteChat);

        var birthdayChat = new mod_birthday_data();
        birthdayChat.chatID = -900;
        birthdayChat.birthdays.Add(new mod_birthday_birthday("Alice", new System.DateTime(1990, 1, 1)));
        chat.chatData.Add(birthdayChat);

        var steamChat = new mod_steam_chat_data();
        steamChat.chatID = -900;
        steamChat.players.Add(new mod_steam_player(-900, "12345", "Gaben", false));
        chat.chatData.Add(steamChat);

        // Every registered module needs a chat-data entry here, or the round-trip test below sees a
        // false "gained a stub" diff - that's real, correct behavior (chat.initPlugins() legitimately
        // stub-fills any module the source didn't have, e.g. one added to the codebase after a real
        // export was taken), not something this synthetic fixture should be exercising.
        var standardChat = new mod_standard_chatdata();
        standardChat.chatID = -900;
        chat.chatData.Add(standardChat);

        source.chatData.Add(chat);

        source.expectedReplies.Add(new ExpectedReply { chatID = -900, userID = 1, text = "pending" });
        source.RecentChatMembers.Add(new chatPresence(1, -900, "Alice"));

        var stat = new statType();
        stat.name = "Fixture Stat";
        stat.moduleType = "mod_xyzzy";
        stat.statSlices.Add(new statSlice());
        source.stats.statsList.Add(stat);

        return source;
    }

    [Fact]
    public void ParsingScrubsTheTelegramTokenEvenThoughTheSourceXmlCarriesARealOne()
    {
        using var bot = new TestHarness();
        var fixturePath = BuildSyntheticFixture(BuildSourceSettings());

        var imported = settings.loadFromLegacyXml(fixturePath);

        Assert.Equal("ENTERYOURAPIKEYHERE", imported.telegramAPIKey);
        Assert.Equal("Roboto_bot_name", imported.botUserName);
        File.Delete(fixturePath);
    }

    [Fact]
    public void ImportReportCountsMatchWhatWasActuallySerialized()
    {
        using var bot = new TestHarness();
        var fixturePath = BuildSyntheticFixture(BuildSourceSettings());

        var imported = settings.loadFromLegacyXml(fixturePath);
        var report = ImportReport.From(imported);

        Assert.Equal(1, report.ChatCount);
        Assert.Equal(1, report.PluginDataModuleCount);
        Assert.Equal(1, report.ExpectedReplyCount);
        Assert.Equal(1, report.RecentChatMemberCount);
        Assert.Equal(1, report.StatTypeCount);
        Assert.Equal(1, report.StatSliceCount);
        Assert.Equal(1, report.XyzzyQuestionCount);
        Assert.Equal(1, report.XyzzyAnswerCount);
        Assert.Equal(2, report.XyzzyTotalPlayersAcrossChats);
        Assert.Equal(1, report.QuoteCount);
        File.Delete(fixturePath);
    }

    [Fact]
    public void SaveThenReloadRoundTripsWithNoCountMismatches()
    {
        // TestHarness already points Roboto.Store/Roboto.Options at a fresh, isolated temp SQLite
        // DB - exactly the "target instance" a real migrator run writes into, just test-scoped.
        using var bot = new TestHarness();
        var fixturePath = BuildSyntheticFixture(BuildSourceSettings());

        var imported = settings.loadFromLegacyXml(fixturePath);
        var beforeReport = ImportReport.From(imported);

        imported.save();
        var reloaded = settings.load();
        var afterReport = ImportReport.From(reloaded);

        var diffs = ImportReport.Diff(beforeReport, afterReport);
        Assert.Empty(diffs);
        File.Delete(fixturePath);
    }

    [Fact]
    public void StatTypesWithNoRecordedSlicesDontCountAsAMismatchAfterRoundTrip()
    {
        // The `stats` table only stores per-slice rows (SqliteStateStore.SaveStats/LoadStats) - a
        // registered stat type with zero slices has nothing to persist, so it genuinely doesn't
        // survive save()+reload. Confirmed real against data/robotolive.xml (a real 2021 production
        // export): 10 of its 23 stat types had never recorded a single slice - not a bug, each
        // module re-registers its own stat types fresh on every startup regardless of persistence,
        // so an empty type just reappears from code, not data, next boot. ImportReport.StatTypeCount
        // only counts types that actually carry data for exactly this reason.
        using var bot = new TestHarness();
        var source = BuildSourceSettings();
        source.stats.statsList.Add(new statType { name = "Never Fired", moduleType = "mod_xyzzy" });
        var fixturePath = BuildSyntheticFixture(source);

        var imported = settings.loadFromLegacyXml(fixturePath);
        var beforeReport = ImportReport.From(imported);
        Assert.Equal(1, beforeReport.StatTypeCount);
        Assert.Equal(1, beforeReport.StatTypesWithNoData);

        imported.save();
        var afterReport = ImportReport.From(settings.load());

        Assert.Empty(ImportReport.Diff(beforeReport, afterReport));
        Assert.Equal(0, afterReport.StatTypesWithNoData);
        File.Delete(fixturePath);
    }

    [Fact]
    public void DiffCatchesARealMismatch()
    {
        using var bot = new TestHarness();
        var before = ImportReport.From(BuildSourceSettings());
        var afterSource = BuildSourceSettings();
        afterSource.chatData.Clear();
        var after = ImportReport.From(afterSource);

        var diffs = ImportReport.Diff(before, after);

        Assert.Contains(diffs, d => d.StartsWith("Chats:"));
    }

    [Fact]
    public void ChatGainingAStubForAModuleItNeverTouchedIsNotAMismatch()
    {
        // Confirmed real against data/chat_mangler_bot.xml: a chat that never played xyzzy or used
        // Steam tracking has no mod_xyzzy_chatdata/mod_steam_chat_data in the source, but
        // chat.initPlugins() correctly stub-fills every registered module on reload - so the "after"
        // report always shows one *more* chat carrying that module's data than the source did. That's
        // expected, not data loss - Diff should only flag a drop, never a gain, for this metric.
        var beforeSource = BuildSourceSettings();
        beforeSource.chatData.Single().chatData.RemoveAll(cd => cd is mod_steam_chat_data);
        var before = ImportReport.From(beforeSource);
        var after = ImportReport.From(BuildSourceSettings());

        var diffs = ImportReport.Diff(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void ChatLosingAModuleItReallyHadIsStillCaughtAsAMismatch()
    {
        var before = ImportReport.From(BuildSourceSettings());
        var afterSource = BuildSourceSettings();
        afterSource.chatData.Single().chatData.RemoveAll(cd => cd is mod_quote_data);
        var after = ImportReport.From(afterSource);

        var diffs = ImportReport.Diff(before, after);

        Assert.Contains(diffs, d => d.StartsWith("Chats with mod_quote_data:"));
    }
}
