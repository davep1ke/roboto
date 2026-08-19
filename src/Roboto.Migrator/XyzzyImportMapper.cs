using Roboto.Bot.Commands;
using Roboto.Bot.Persistence;
using Roboto.Bot.Xyzzy;
using Roboto.Migrator.Legacy;

namespace Roboto.Migrator;

/// <summary>
/// All the mod_xyzzy-specific mapping logic - by far the trickiest module, since it's the one the
/// user asked to genuinely *resume* in-flight rather than reset. Kept separate from XmlImporter's
/// orchestration since this is where nearly all the real complexity lives.
/// </summary>
public static class XyzzyImportMapper
{
    /// <summary>Legacy's magic Guid.Empty sentinel meaning "all packs" when it appears inside a
    /// chat's packFilterIDs - Roboto/Modules/mod_xyzzy.cs's mod_xyzzy.AllPacksEnabledID.</summary>
    private static readonly Guid AllPacksEnabledId = Guid.Empty;

    /// <summary>Legacy IDs are GUIDs - too long to reuse directly (risks blowing Telegram's 64-byte
    /// callback_data limit for large supergroup chat IDs, exactly why CardCatalog chose short IDs
    /// over GUIDs originally). Assigns new sequential short IDs while building a GUID -> new-ID map
    /// used to translate every other card/pack reference in the same file.</summary>
    public static (List<XyzzyCard> Questions, List<XyzzyCard> Answers, List<XyzzyPack> Packs,
        Dictionary<string, string> CardIdMap, Dictionary<Guid, string> PackIdMap) BuildCatalog(
        LegacyXyzzyCoreData core, ImportReport report)
    {
        var packIdMap = new Dictionary<Guid, string>();
        var packs = new List<XyzzyPack>();

        var pIndex = 1;
        foreach (var p in core.packs)
        {
            var newId = $"p{pIndex++}";
            packIdMap[p.packID] = newId;
            packs.Add(new XyzzyPack(newId, p.name));
        }

        report.PacksImported = packs.Count;

        var cardIdMap = new Dictionary<string, string>();
        var questions = new List<XyzzyCard>();
        var answers = new List<XyzzyCard>();

        var qIndex = 1;
        foreach (var q in core.questions)
        {
            var newId = $"q{qIndex++}";
            cardIdMap[q.uniqueID] = newId;

            // Legacy defaults answer cards to -1 (meaningless there); defensively treat any
            // non-positive value as a plain single-answer question rather than trusting it blindly.
            var answerCount = q.nrAnswers <= 0 ? 1 : q.nrAnswers;
            var packId = packIdMap.GetValueOrDefault(q.packID);
            questions.Add(new XyzzyCard(newId, q.text, answerCount, packId));
            if (answerCount > 1)
            {
                report.MultiAnswerCardsImported++;
            }
        }

        var aIndex = 1;
        foreach (var a in core.answers)
        {
            var newId = $"a{aIndex++}";
            cardIdMap[a.uniqueID] = newId;
            var packId = packIdMap.GetValueOrDefault(a.packID);
            answers.Add(new XyzzyCard(newId, a.text, PackId: packId));
        }

        report.QuestionCardsImported = questions.Count;
        report.AnswerCardsImported = answers.Count;
        return (questions, answers, packs, cardIdMap, packIdMap);
    }

    /// <summary>Translates a chat's legacy packFilterIDs onto XyzzyGameState.EnabledPackIds.
    /// Legacy's scheme is the inverse of the rewrite's: packFilterIDs defaults to one specific pack
    /// (never starts empty), and "all packs" is the explicit AllPacksEnabledId sentinel appearing
    /// *inside* the list - the rewrite instead uses an empty list to mean "all packs" (see
    /// XyzzyGameState.EnabledPackIds' own doc comment), so both of those map onto []. A
    /// packFilterIDs list that's empty with no sentinel (legacy's own "nothing enabled" edge case -
    /// only reachable by explicitly picking "None" in the legacy UI) collapses onto the same []
    /// result; XyzzyRoundService's own empty-after-filter fallback then treats it as "all packs"
    /// rather than leaving the game unable to draw a card, which is a safer outcome for a resumed
    /// game than reproducing a genuinely broken legacy state.</summary>
    public static List<string> MapEnabledPackIds(IReadOnlyList<Guid> legacyPackFilterIds, IReadOnlyDictionary<Guid, string> packIdMap, ImportReport report)
    {
        if (legacyPackFilterIds.Count == 0 || legacyPackFilterIds.Contains(AllPacksEnabledId))
        {
            return [];
        }

        var mapped = new List<string>();
        foreach (var guid in legacyPackFilterIds)
        {
            if (packIdMap.TryGetValue(guid, out var shortId))
            {
                mapped.Add(shortId);
            }
            else
            {
                report.UnmappablePackReferencesDropped++;
            }
        }

        return mapped;
    }

    /// <summary>Legacy's setup-wizard sub-statuses all collapse into SettingUp - v1's own wizard
    /// already does the same collapse for newly-started games (see XyzzyGameState's doc comment),
    /// so this isn't a new loss of fidelity, just applying the same collapse on the way in.
    /// Anything unrecognised falls back to Stopped, the safest possible state, rather than guessing.</summary>
    public static XyzzyStatus MapStatus(string? legacyStatus) => legacyStatus switch
    {
        "Stopped" => XyzzyStatus.Stopped,
        "useDefaults" or "SetGameLength" or "setPackFilter" or "setMinHours" or "setMaxHours" or "cardCastImport" => XyzzyStatus.SettingUp,
        "Invites" => XyzzyStatus.Invites,
        "Question" => XyzzyStatus.Question,
        "Judging" => XyzzyStatus.Judging,
        "waitingForNextHand" => XyzzyStatus.WaitingForNextHand,
        _ => XyzzyStatus.Stopped,
    };

    private static string? MapCardId(string? legacyGuid, IReadOnlyDictionary<string, string> cardIdMap, ImportReport report)
    {
        if (string.IsNullOrEmpty(legacyGuid))
        {
            return null;
        }

        if (cardIdMap.TryGetValue(legacyGuid, out var mapped))
        {
            return mapped;
        }

        report.UnmappableCardReferencesDropped++;
        return null;
    }

    public static XyzzyGameState MapGame(
        long chatId, LegacyXyzzyChatData legacy, IReadOnlyDictionary<string, string> cardIdMap,
        IReadOnlyDictionary<Guid, string> packIdMap, DateTime importTimeUtc, ImportReport report)
    {
        var game = new XyzzyGameState
        {
            ChatId = chatId,
            Status = MapStatus(legacy.status),
            MaxWaitHours = legacy.maxWaitTimeHours,
            MinWaitHours = legacy.minWaitTimeHours,
            QuestionLimit = legacy.enteredQuestionCount,
            RoundNumber = 1,
            EnabledPackIds = MapEnabledPackIds(legacy.packFilterIDs, packIdMap, report),

            // Stale-data safety (explicit user concern) - never carry the original timestamp
            // forward. A resumed game's clock starts at the moment it's actually imported, not
            // silently inherited as already-overdue - see MIGRATION.md's phase 11 notes.
            StatusChangedUtc = importTimeUtc,
            ReminderSent = false,
        };

        foreach (var legacyPlayer in legacy.players)
        {
            var player = new XyzzyPlayer
            {
                PlayerId = legacyPlayer.playerID,
                DisplayName = legacyPlayer.name,
                Wins = legacyPlayer.wins,
                IsBot = false, // legacy never had bots - a purely new-engine concept (phase 8.6)
            };

            foreach (var cardGuid in legacyPlayer.cardsInHand)
            {
                if (MapCardId(cardGuid, cardIdMap, report) is { } mapped)
                {
                    player.Hand.Add(mapped);
                }
            }

            game.Players.Add(player);

            var mappedSelected = legacyPlayer.selectedCards
                .Select(id => MapCardId(id, cardIdMap, report))
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList();
            if (mappedSelected.Count > 0)
            {
                game.Submissions[legacyPlayer.playerID] = mappedSelected;
            }
        }

        // Judge: lastPlayerAsked is an array index, not a stable ID (legacy's own
        // "//TODO - should be an ID!"). Same defensive bounds-check legacy itself uses
        // (mod_xyzzy_chatdata.check()) rather than trusting a possibly-stale index blindly.
        if (legacy.lastPlayerAsked >= 0 && legacy.lastPlayerAsked < game.Players.Count)
        {
            game.JudgePlayerId = game.Players[legacy.lastPlayerAsked].PlayerId;
        }
        else if (game.Players.Count > 0)
        {
            game.JudgePlayerId = game.Players[0].PlayerId;
        }

        game.CurrentQuestionCardId = MapCardId(legacy.currentQuestion, cardIdMap, report);

        foreach (var guid in legacy.remainingQuestions)
        {
            if (MapCardId(guid, cardIdMap, report) is { } mapped)
            {
                game.RemainingQuestionCardIds.Add(mapped);
            }
        }

        foreach (var guid in legacy.remainingAnswers)
        {
            if (MapCardId(guid, cardIdMap, report) is { } mapped)
            {
                game.RemainingAnswerCardIds.Add(mapped);
            }
        }

        return game;
    }

    /// <summary>Only "Question" (answer a card) and "Judging" (pick a winner) resume as real,
    /// tappable DmOutbox entries - confirmed scope with the user. Everything else legacy used for
    /// mod_xyzzy pending replies (kick/changescore/Settings/SetGameLength/setMaxHours/setMinHours/
    /// setPackFilter/cardCastImport/fuckwith/leaveGamePickGroup - all admin sub-flows or v1-cut
    /// features with no equivalent shape in the rewrite) is dropped, counted by reason, not
    /// attempted. Reuses XyzzyRoundService's own internal keyboard-building (BuildHandKeyboard/
    /// BuildJudgeKeyboard) so a resumed entry is built exactly the way the live engine builds one -
    /// requires CardCatalog to already reflect this file's imported catalog (see XmlImporter, which
    /// calls CardCatalog.LoadOverrideAsync in its own process right after writing it).
    ///
    /// Written as *undelivered* (no DeliveredMessageId) - never sent live during import itself.
    /// Only actually delivered once the real bot process starts (DmOutbox.PumpAllOutstandingAsync,
    /// the startup safety net) - see MIGRATION.md's stale-data-safety notes for why that split
    /// matters.</summary>
    public static async Task ResumePendingRepliesAsync(
        IReadOnlyList<LegacyExpectedReply> repliesForChat, XyzzyGameState game, IStateStore store, ImportReport report, CancellationToken cancellationToken)
    {
        foreach (var reply in repliesForChat)
        {
            if (reply.pluginType is null || !reply.pluginType.Contains("mod_xyzzy", StringComparison.Ordinal))
            {
                // A different module's pending reply, for a chat that also happens to have an
                // xyzzy game - counted, not silently skipped, even though this method has no
                // resumption logic for other modules (a real, deliberate gap - see MIGRATION.md's
                // phase 11 notes on why only mod_xyzzy's Question/Judging are worth resuming).
                // Silently continuing here was the exact bug class caught during a real dry run:
                // resumed+dropped counts not summing to the file's true total.
                Drop(report, "different module (not mod_xyzzy)");
                continue;
            }

            if (reply.messageData is not ("Question" or "Judging"))
            {
                Drop(report, reply.messageData ?? "(none)");
                continue;
            }

            var player = game.FindPlayer(reply.userID);
            if (player is null)
            {
                Drop(report, "player no longer in game");
                continue;
            }

            DmOutboxEntry? entry = (reply.messageData, game.Status) switch
            {
                ("Question", XyzzyStatus.Question) when player.PlayerId != game.JudgePlayerId => BuildQuestionEntry(game, player),
                ("Judging", XyzzyStatus.Judging) when player.PlayerId == game.JudgePlayerId => BuildJudgingEntry(game),
                _ => null,
            };

            if (entry is null)
            {
                Drop(report, "game state no longer matches (already advanced past this)");
                continue;
            }

            var key = $"dm-outbox:{player.PlayerId}";
            var queue = await store.LoadAsync<List<DmOutboxEntry>>(key, cancellationToken) ?? [];
            queue.Add(entry);
            await store.SaveAsync(key, queue, cancellationToken);
            report.PendingRepliesResumed++;
        }
    }

    private static DmOutboxEntry? BuildQuestionEntry(XyzzyGameState game, XyzzyPlayer player)
    {
        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId);
        if (question is null)
        {
            return null;
        }

        return new DmOutboxEntry
        {
            Text = $"Round (resumed): \"{question.Text}\"\nPick a card:",
            ExpectsResponse = true,
            Keyboard = XyzzyRoundService.BuildHandKeyboard(game, player),
        };
    }

    private static DmOutboxEntry? BuildJudgingEntry(XyzzyGameState game)
    {
        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId);
        if (question is null || game.Submissions.Count == 0)
        {
            return null;
        }

        return new DmOutboxEntry
        {
            Text = $"Everyone's answered! Pick the winner for: \"{question.Text}\"",
            ExpectsResponse = true,
            Keyboard = XyzzyRoundService.BuildJudgeKeyboard(game),
        };
    }

    private static void Drop(ImportReport report, string reason) =>
        report.PendingRepliesDroppedByReason[reason] = report.PendingRepliesDroppedByReason.GetValueOrDefault(reason) + 1;
}
