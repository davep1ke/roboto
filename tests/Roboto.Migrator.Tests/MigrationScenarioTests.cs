using Microsoft.Extensions.Options;
using Roboto.Bot;
using Roboto.Bot.Birthdays;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Quotes;
using Roboto.Bot.Steam;
using Roboto.Bot.Wordcraft;
using Roboto.Bot.Xyzzy;

namespace Roboto.Migrator.Tests;

public class MigrationScenarioTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("roboto-migrator-tests-").FullName;
    private readonly string _xmlPath = SyntheticXmlFixture.Write();

    public void Dispose()
    {
        Directory.Delete(_dataDir, recursive: true);
        File.Delete(_xmlPath);
    }

    private IStateStore OpenTargetStore(string instance)
    {
        var options = Options.Create(new BotOptions { DataDir = _dataDir, Instance = instance });
        return new SqliteStateStore(options);
    }

    [Fact]
    public async Task DurableDataImportsAcrossEveryModule()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "durable", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        Assert.Equal(3, result.Report.ChatsImported);
        Assert.Equal(1, result.Report.QuotesImported);
        Assert.Equal(1, result.Report.BirthdaysImported);
        Assert.Equal(2, result.Report.WordcraftWordsImported);
        Assert.Equal(1, result.Report.SteamPlayersImported);
        Assert.Equal(1, result.Report.SteamGamesImported);
        Assert.Equal(1, result.Report.QuietHoursChatsImported);
        Assert.True(result.Report.SteamApiKeyFound);

        var store = OpenTargetStore("durable");
        var chat = await new ChatRepository(store).GetAsync(SyntheticXmlFixture.DurableChatId, CancellationToken.None);
        Assert.Equal("Durable Chat", chat.Title);
        Assert.Contains(111, chat.Admins);

        var quotes = await new QuotesRepository(store).GetAsync(SyntheticXmlFixture.DurableChatId, CancellationToken.None);
        Assert.Single(quotes.Quotes);
        Assert.Equal("Bob", quotes.Quotes[0].Lines[0].By);

        var birthdays = await new BirthdaysRepository(store).GetAsync(SyntheticXmlFixture.DurableChatId, CancellationToken.None);
        Assert.Single(birthdays.Birthdays);
        Assert.Equal("Alice", birthdays.Birthdays[0].Name);

        var words = await new WordcraftStore(store).GetWordsAsync(CancellationToken.None);
        Assert.Contains("Foo", words);
        Assert.Contains("Bar", words);

        var steamChat = await new SteamRepository(store).GetChatAsync(SyntheticXmlFixture.DurableChatId, CancellationToken.None);
        Assert.Single(steamChat.Players);
        Assert.Equal("Gamer", steamChat.Players[0].PlayerName);

        var quietHours = await store.LoadAsync<QuietHours>(SetQuietHoursCommand.QuietHoursKey(SyntheticXmlFixture.DurableChatId), CancellationToken.None);
        Assert.Equal(TimeSpan.FromHours(22), quietHours!.Start);
        Assert.Equal(TimeSpan.FromHours(8), quietHours.End);
    }

    [Fact]
    public async Task TelegramTokenIsNeverWrittenAnywhereRealOrDryRun()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "notoken", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        // Confirms the negative directly against the report's own serialized form, not just "I
        // didn't write code that does this" - if telegramAPIKey ever leaked into ImportReport's
        // fields (a future edit mistake), this test would actually catch it.
        Assert.DoesNotContain(SyntheticXmlFixture.TelegramApiKey, result.Report.ToString());

        // And confirm nothing under the target instance directory contains it either - including
        // roboto.db itself, deliberately not excluded: SqliteStateStore stores every value as a
        // plain TEXT/JSON column, so a leaked value would actually show up as a readable substring
        // in the raw file bytes, not be hidden by SQLite's format.
        var tokenBytes = System.Text.Encoding.UTF8.GetBytes(SyntheticXmlFixture.TelegramApiKey);
        foreach (var file in Directory.GetFiles(Path.Combine(_dataDir, "notoken"), "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.True(bytes.AsSpan().IndexOf(tokenBytes) < 0, $"Found telegramAPIKey inside {file}");
        }
    }

    [Fact]
    public async Task SteamKeyIsOnlyCarriedWhenExplicitlyRequested()
    {
        var importer = new XmlImporter();

        var withoutFlag = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "nokey", DryRun: false, CarrySteamKey: false), CancellationToken.None);
        Assert.Null(withoutFlag.SteamApiKeyToCarry);
        Assert.False(withoutFlag.Report.SteamApiKeyCarried);

        var withFlag = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "withkey", DryRun: false, CarrySteamKey: true), CancellationToken.None);
        Assert.Equal(SyntheticXmlFixture.SteamApiKey, withFlag.SteamApiKeyToCarry);
        Assert.True(withFlag.Report.SteamApiKeyCarried);
    }

    [Fact]
    public async Task CatalogImportAssignsShortIdsAndTracksMultiAnswerCards()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "catalog", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        Assert.Equal(2, result.Report.QuestionCardsImported);
        Assert.Equal(3, result.Report.AnswerCardsImported);
        Assert.Equal(1, result.Report.MultiAnswerCardsImported);

        var store = OpenTargetStore("catalog");
        var questions = await store.LoadAsync<List<XyzzyCard>>(CardCatalog.QuestionsKey, CancellationToken.None);
        Assert.NotNull(questions);
        Assert.All(questions!, q => Assert.Matches("^q[0-9]+$", q.Id)); // short IDs, not legacy GUIDs
        Assert.Contains(questions!, q => q.AnswerCount == 2);
    }

    [Fact]
    public async Task UnmappableCardReferenceIsDroppedAndCountedNotFatal()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "unmappable", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        // Player One's hand had 3 legacy GUIDs, one of which ("MISSING-GUID") has no catalog entry.
        Assert.True(result.Report.UnmappableCardReferencesDropped >= 1);

        var store = OpenTargetStore("unmappable");
        var game = await new XyzzyGameRepository(store).GetAsync(SyntheticXmlFixture.QuestionChatId, CancellationToken.None);
        var player = game.FindPlayer(SyntheticXmlFixture.QuestionPlayerId)!;
        Assert.Equal(2, player.Hand.Count); // 3 in legacy, 1 dropped
    }

    [Fact]
    public async Task QuestionStatusGameResumesWithAPartialMultiAnswerSubmissionExcludingPickedCards()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "question", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        // 2 total across the whole import: this chat's "Question" plus the Judging chat's
        // "Judging" (see JudgingStatusGameResumesWithACombinedAnswerKeyboardForTheJudgeOnly) - the
        // report accumulates across every chat in one run, not just this one.
        Assert.Equal(2, result.Report.PendingRepliesResumed);

        var store = OpenTargetStore("question");
        var game = await new XyzzyGameRepository(store).GetAsync(SyntheticXmlFixture.QuestionChatId, CancellationToken.None);
        Assert.Equal(XyzzyStatus.Question, game.Status);
        Assert.Equal(SyntheticXmlFixture.QuestionJudgeId, game.JudgePlayerId); // lastPlayerAsked=1 -> Players[1]
        Assert.Single(game.Submissions[SyntheticXmlFixture.QuestionPlayerId]); // 1 of the question's 2 required cards

        var queue = await store.LoadAsync<List<DmOutboxEntry>>("dm-outbox:" + SyntheticXmlFixture.QuestionPlayerId, CancellationToken.None);
        Assert.NotNull(queue);
        var entry = Assert.Single(queue!);
        Assert.Null(entry.DeliveredMessageId); // never sent live during import itself
        Assert.NotNull(entry.Keyboard);

        // The already-picked card (mapped from A-GUID-1) must not be offered again.
        var pickedCardId = game.Submissions[SyntheticXmlFixture.QuestionPlayerId][0];
        Assert.DoesNotContain(entry.Keyboard!, row => row.Any(b => b.CallbackData.Contains($":{pickedCardId}", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task JudgingStatusGameResumesWithACombinedAnswerKeyboardForTheJudgeOnly()
    {
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "judging", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        var store = OpenTargetStore("judging");
        var game = await new XyzzyGameRepository(store).GetAsync(SyntheticXmlFixture.JudgingChatId, CancellationToken.None);
        Assert.Equal(XyzzyStatus.Judging, game.Status);
        Assert.Equal(SyntheticXmlFixture.JudgingJudgeId, game.JudgePlayerId);

        var judgeQueue = await store.LoadAsync<List<DmOutboxEntry>>("dm-outbox:" + SyntheticXmlFixture.JudgingJudgeId, CancellationToken.None);
        var judgeEntry = Assert.Single(judgeQueue!);
        Assert.Equal(2, judgeEntry.Keyboard!.Count); // one button per submitter (Answerer, KickTarget)

        // The "kick" pending reply for KickTarget has no equivalent flow - dropped, not resumed.
        var kickTargetQueue = await store.LoadAsync<List<DmOutboxEntry>>("dm-outbox:" + SyntheticXmlFixture.JudgingKickTargetId, CancellationToken.None);
        Assert.Null(kickTargetQueue);
        Assert.True(result.Report.PendingRepliesDroppedByReason.GetValueOrDefault("kick") >= 1);
    }

    [Fact]
    public async Task StatusChangedTimestampIsResetToImportTimeNotCarriedFromTheStaleExport()
    {
        var before = DateTime.UtcNow;
        var importer = new XmlImporter();
        await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "freshness", DryRun: false, CarrySteamKey: false), CancellationToken.None);
        var after = DateTime.UtcNow;

        var store = OpenTargetStore("freshness");
        var game = await new XyzzyGameRepository(store).GetAsync(SyntheticXmlFixture.QuestionChatId, CancellationToken.None);

        Assert.InRange(game.StatusChangedUtc, before, after);
        Assert.False(game.ReminderSent);
    }

    [Fact]
    public async Task DryRunProducesTheSameReportAsARealRunButWritesNothingToTheTargetInstance()
    {
        var importer = new XmlImporter();
        var dryRunResult = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "dryrun-target", DryRun: true, CarrySteamKey: false), CancellationToken.None);

        Assert.Equal(3, dryRunResult.Report.ChatsImported);
        Assert.False(Directory.Exists(Path.Combine(_dataDir, "dryrun-target")));

        var realResult = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "dryrun-target", DryRun: false, CarrySteamKey: false), CancellationToken.None);
        Assert.Equal(dryRunResult.Report.ChatsImported, realResult.Report.ChatsImported);
        Assert.Equal(dryRunResult.Report.XyzzyGamesImported, realResult.Report.XyzzyGamesImported);
        Assert.Equal(dryRunResult.Report.PendingRepliesResumed, realResult.Report.PendingRepliesResumed);
        Assert.True(Directory.Exists(Path.Combine(_dataDir, "dryrun-target")));
    }

    [Fact]
    public async Task EveryPendingReplyIsAccountedForResumedOrDroppedNeverSilentlyLost()
    {
        // Caught for real during a dry run against the actual production export, twice: (1)
        // resumed+dropped came back short because ResumePendingRepliesAsync was only ever called
        // for chats whose game was already Question/Judging - a chat that had *moved on* to
        // Stopped/SettingUp/etc. but still had a leftover stale reply meant that record was never
        // even looked at; (2) a further, smaller gap turned out to be replies referencing a chatID
        // that doesn't exist in chatData at all (a purged chat's stale leftover) - the chat-driven
        // iteration never visits those either. Locks in both fixes: every reply in the fixture (4
        // total - Question, Judging, kick, and one orphaned) must show up in the report one way or
        // the other.
        var importer = new XmlImporter();
        var result = await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "reconcile", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        var totalAccountedFor = result.Report.PendingRepliesResumed + result.Report.PendingRepliesDroppedByReason.Values.Sum();
        Assert.Equal(4, totalAccountedFor);
        Assert.Equal(1, result.Report.PendingRepliesDroppedByReason.GetValueOrDefault("orphaned - no matching chat"));
    }

    [Fact]
    public async Task SourceXmlFileIsNeverModified()
    {
        var originalBytes = await File.ReadAllBytesAsync(_xmlPath);
        var importer = new XmlImporter();
        await importer.RunAsync(new ImportOptions(_xmlPath, _dataDir, "copy-check", DryRun: false, CarrySteamKey: false), CancellationToken.None);

        var afterBytes = await File.ReadAllBytesAsync(_xmlPath);
        Assert.Equal(originalBytes, afterBytes);
    }
}
