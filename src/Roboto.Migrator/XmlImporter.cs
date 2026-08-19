using System.Xml.Serialization;
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
using Roboto.Migrator.Legacy;

namespace Roboto.Migrator;

/// <summary>
/// Orchestrates a full import: copy the source XML first (never touch the original), deserialize,
/// write every module's data into a target instance's IStateStore via the exact same repositories
/// Roboto.Bot itself uses. A dry run and a real write share this entire code path - dry run just
/// points at a throwaway temp store instead of the real target, so its report is a trustworthy
/// preview, not a separate approximation that could drift from what a real run actually does.
/// </summary>
public sealed class XmlImporter
{
    public async Task<ImportResult> RunAsync(ImportOptions options, CancellationToken cancellationToken)
    {
        var workingCopy = Path.Combine(Path.GetTempPath(), $"roboto-import-{Guid.NewGuid():N}.xml");
        File.Copy(options.XmlPath, workingCopy);

        var targetDataDir = options.DryRun
            ? Path.Combine(Path.GetTempPath(), $"roboto-import-dryrun-{Guid.NewGuid():N}")
            : options.DataDir;

        try
        {
            var settings = Deserialize(workingCopy);
            var report = new ImportReport { BotUserName = settings.botUserName };

            Directory.CreateDirectory(Path.Combine(targetDataDir, options.Instance));
            var storeOptions = Options.Create(new BotOptions { DataDir = targetDataDir, Instance = options.Instance });
            var store = new SqliteStateStore(storeOptions);
            await store.InitializeAsync(cancellationToken);

            var importTimeUtc = DateTime.UtcNow;
            var cardIdMap = new Dictionary<string, string>();
            var packIdMap = new Dictionary<Guid, string>();
            string? steamApiKeyFound = null;

            // Global data first - the xyzzy catalog needs to be written and loaded into this
            // process's CardCatalog before any per-chat game mapping below, since resuming a
            // Question/Judging pending reply looks card text up via CardCatalog.Questions/Answers.
            foreach (var global in settings.pluginData)
            {
                switch (global)
                {
                    case LegacyWordcraftData wordcraft:
                        await new WordcraftStore(store).SaveWordsAsync(wordcraft.words, cancellationToken);
                        report.WordcraftWordsImported = wordcraft.words.Count;
                        break;

                    case LegacySteamCoreData steamCore:
                        var core = new SteamCoreState();
                        foreach (var g in steamCore.games)
                        {
                            core.Games.Add(new SteamGame
                            {
                                GameId = g.gameID,
                                DisplayName = g.displayName,
                                Achievements = g.chievs.Select(a => new SteamAchievementSchema
                                {
                                    Code = a.achievement_code,
                                    DisplayName = a.displayName,
                                    Description = a.description,
                                }).ToList(),
                            });
                        }

                        await new SteamRepository(store).SaveCoreAsync(core, cancellationToken);
                        report.SteamGamesImported = core.Games.Count;

                        if (!string.IsNullOrWhiteSpace(steamCore.steamAPIKey))
                        {
                            steamApiKeyFound = steamCore.steamAPIKey;
                            report.SteamApiKeyFound = true;
                        }

                        break;

                    case LegacyXyzzyCoreData xyzzyCore:
                        var (questions, answers, packs, map, pMap) = XyzzyImportMapper.BuildCatalog(xyzzyCore, report);
                        cardIdMap = map;
                        packIdMap = pMap;
                        await store.SaveAsync(CardCatalog.QuestionsKey, questions, cancellationToken);
                        await store.SaveAsync(CardCatalog.AnswersKey, answers, cancellationToken);
                        await store.SaveAsync(CardCatalog.PacksKey, packs, cancellationToken);

                        // Loads into *this process's* CardCatalog statics (Roboto.Migrator's own
                        // process, separate from any Roboto.Bot process) so the resumption logic
                        // below resolves card text against the real imported catalog, not the
                        // hardcoded placeholder set.
                        await CardCatalog.LoadOverrideAsync(store, cancellationToken);
                        break;
                }
            }

            var chats = new ChatRepository(store);
            var quotes = new QuotesRepository(store);
            var birthdays = new BirthdaysRepository(store);
            var steam = new SteamRepository(store);
            var xyzzyGames = new XyzzyGameRepository(store);

            foreach (var legacyChat in settings.chatData)
            {
                var chatState = new ChatState
                {
                    ChatId = legacyChat.chatID,
                    Title = legacyChat.chatTitle,
                    Muted = legacyChat.muted,
                    Admins = legacyChat.chatAdmins,
                };
                await chats.SaveAsync(chatState, cancellationToken);
                report.ChatsImported++;

                foreach (var blob in legacyChat.chatData)
                {
                    switch (blob)
                    {
                        case LegacyQuoteChatData legacyQuote:
                            await ImportQuoteAsync(legacyChat.chatID, legacyQuote, quotes, importTimeUtc, report, cancellationToken);
                            break;

                        case LegacyBirthdayChatData legacyBirthday:
                            await ImportBirthdayAsync(legacyChat.chatID, legacyBirthday, birthdays, report, cancellationToken);
                            break;

                        case LegacySteamChatData legacySteamChat:
                            await ImportSteamChatAsync(legacyChat.chatID, legacySteamChat, steam, report, cancellationToken);
                            break;

                        case LegacyStandardChatData legacyStandard:
                            await ImportQuietHoursAsync(legacyChat.chatID, legacyStandard, store, report, cancellationToken);
                            break;

                        case LegacyXyzzyChatData legacyXyzzy:
                            var game = XyzzyImportMapper.MapGame(legacyChat.chatID, legacyXyzzy, cardIdMap, packIdMap, importTimeUtc, report);
                            await xyzzyGames.SaveAsync(game, cancellationToken);
                            report.XyzzyGamesImported++;
                            var statusKey = game.Status.ToString();
                            report.XyzzyGamesByStatus[statusKey] = report.XyzzyGamesByStatus.GetValueOrDefault(statusKey) + 1;

                            // Unconditional - not gated on game.Status being Question/Judging.
                            // Found the hard way (a real dry run's resumed+dropped counts not
                            // summing to the file's true ExpectedReply total): a chat whose game had
                            // already moved on to Stopped/SettingUp/etc. could still have a leftover
                            // stale reply, and gating this call meant that record was never even
                            // looked at, let alone counted - ResumePendingRepliesAsync's own
                            // per-reply status match already has a "game state no longer matches"
                            // drop path for exactly this; every reply for every xyzzy chat now goes
                            // through it, so the report is a complete accounting, not an
                            // approximation with a silent gap.
                            var repliesForChat = settings.expectedReplies.Where(r => r.chatID == legacyChat.chatID).ToList();
                            await XyzzyImportMapper.ResumePendingRepliesAsync(repliesForChat, game, store, report, cancellationToken);

                            break;
                    }
                }
            }

            // A reply whose chatID matches no chat in chatData at all (a chat that was later
            // purged/removed, its stale reply left behind) is never visited by the per-chat loop
            // above, which only ever iterates *subsets* of expectedReplies keyed by a real chat -
            // found for real (3 such records) while reconciling a dry run's counts against a real
            // export's true ExpectedReply total. Counted explicitly here so the report has no
            // silent gap, matching every other "drop, don't just vanish" path in this importer.
            var knownChatIds = settings.chatData.Select(c => c.chatID).ToHashSet();
            var orphanedReplies = settings.expectedReplies.Count(r => !knownChatIds.Contains(r.chatID));
            if (orphanedReplies > 0)
            {
                report.PendingRepliesDroppedByReason["orphaned - no matching chat"] = orphanedReplies;
            }

            var carriedKey = options.CarrySteamKey ? steamApiKeyFound : null;
            report.SteamApiKeyCarried = carriedKey is not null;

            return new ImportResult(report, carriedKey, settings.botUserName);
        }
        finally
        {
            File.Delete(workingCopy);
            if (options.DryRun && Directory.Exists(targetDataDir))
            {
                Directory.Delete(targetDataDir, recursive: true);
            }
        }
    }

    private static async Task ImportQuoteAsync(
        long chatId, LegacyQuoteChatData legacy, QuotesRepository quotes, DateTime importTimeUtc, ImportReport report, CancellationToken cancellationToken)
    {
        var state = await quotes.GetAsync(chatId, cancellationToken);
        state.AutoQuoteEnabled = legacy.autoQuoteEnabled;
        state.AutoQuoteHours = legacy.autoQuoteHours;

        // Stale-data safety - the original nextAutoQuoteAfter is however old this export is;
        // recompute from import time instead of carrying a value that's probably already passed.
        state.NextAutoQuoteAfter = importTimeUtc.AddHours(legacy.autoQuoteHours);

        foreach (var mq in legacy.multiquotes)
        {
            state.Quotes.Add(new Quote
            {
                On = mq.on,
                Lines = mq.lines.Select(l => new QuoteLine { By = l.by, Text = l.text }).ToList(),
            });
        }

        await quotes.SaveAsync(state, cancellationToken);
        report.QuotesImported += state.Quotes.Count;
    }

    private static async Task ImportBirthdayAsync(
        long chatId, LegacyBirthdayChatData legacy, BirthdaysRepository birthdays, ImportReport report, CancellationToken cancellationToken)
    {
        var state = await birthdays.GetAsync(chatId, cancellationToken);

        // Stale-data safety - MinValue means "not checked today yet", which is always correct
        // regardless of import time (a birthday landing on the actual import day still deserves a
        // real, on-time announcement - this isn't a "reminder", it's a genuine live trigger).
        state.LastDayProcessed = DateTime.MinValue;

        foreach (var b in legacy.birthdays)
        {
            state.Birthdays.Add(new BirthdayEntry { Name = b.name, Birthday = b.birthday });
        }

        await birthdays.SaveAsync(state, cancellationToken);
        report.BirthdaysImported += state.Birthdays.Count;
    }

    private static async Task ImportSteamChatAsync(
        long chatId, LegacySteamChatData legacy, SteamRepository steam, ImportReport report, CancellationToken cancellationToken)
    {
        var state = await steam.GetChatAsync(chatId, cancellationToken);
        foreach (var p in legacy.players)
        {
            state.Players.Add(new SteamPlayer
            {
                SteamId = p.playerID,
                PlayerName = p.playerName,
                Chievs = p.chievs.Select(c => new SteamChiev { ChievCode = c.chievName, AppId = c.appID }).ToList(),
            });
        }

        await steam.SaveChatAsync(state, cancellationToken);
        report.SteamPlayersImported += state.Players.Count;
    }

    private static async Task ImportQuietHoursAsync(
        long chatId, LegacyStandardChatData legacy, IStateStore store, ImportReport report, CancellationToken cancellationToken)
    {
        if (legacy.x_quietHoursStartTime == TimeSpan.MinValue.Ticks && legacy.x_quietHoursEndTime == TimeSpan.MinValue.Ticks)
        {
            return; // never configured - nothing to import
        }

        await store.SaveAsync(
            SetQuietHoursCommand.QuietHoursKey(chatId),
            new QuietHours(new TimeSpan(legacy.x_quietHoursStartTime), new TimeSpan(legacy.x_quietHoursEndTime)),
            cancellationToken);
        report.QuietHoursChatsImported++;
    }

    private static LegacySettings Deserialize(string path)
    {
        var extraTypes = new[]
        {
            typeof(LegacyQuoteChatData), typeof(LegacyQuoteCoreData),
            typeof(LegacyBirthdayChatData), typeof(LegacyBirthdayCoreData),
            typeof(LegacyWordcraftData),
            typeof(LegacySteamChatData), typeof(LegacySteamCoreData),
            typeof(LegacyStandardChatData), typeof(LegacyStandardData),
            typeof(LegacyXyzzyChatData), typeof(LegacyXyzzyCoreData),
        };

        var serializer = new XmlSerializer(typeof(LegacySettings), extraTypes);
        using var stream = File.OpenRead(path);
        return (LegacySettings)serializer.Deserialize(stream)!;
    }
}
