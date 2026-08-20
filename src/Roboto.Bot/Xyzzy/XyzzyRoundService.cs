using Roboto.Bot.Commands;
using Roboto.Bot.Stats;
using Telegram.Bot;

namespace Roboto.Bot.Xyzzy;

/// <summary>
/// The actual round mechanics (setup completion, dealing hands, asking a question, collecting
/// answers, judging), shared between XyzzyStartCommand/the setup and begin callback handlers (kick
/// off a game) and the answer/judge callback handlers (answering can trigger judging; a judge's pick
/// triggers the next round) - all of them need the same "deal/ask/advance" logic, so it lives here
/// rather than being duplicated across every caller.
///
/// Every DM to a player (hand keyboards, judge keyboards, "you're judging" notices) goes through
/// DmOutbox rather than being sent directly - phase 8.9, user's explicit design call: only one
/// thing (from any game) is ever outstanding in a player's DM at a time, so a player mid-round in
/// two games gets dealt their second hand only once they've resolved the first.
///
/// Deck refilling: a card is only ever "in play" while it's sitting in a player's Hand (or is the
/// current question) - once played/judged it's simply removed and never stored anywhere else, so
/// it's immediately free to be dealt again in a future refill. Refills explicitly exclude whatever
/// is currently in play (see TopUpHand/DrawQuestion) so no card can ever be dealt to two hands at
/// once - that uniqueness is what lets a card ID alone (via XyzzyCallbackData) unambiguously
/// identify "which submission is this" during judging, no extra bookkeeping needed.
/// </summary>
public sealed class XyzzyRoundService(XyzzyGameRepository games, QuietHoursQuery quietHours, DmOutbox outbox, StatsRecorder stats)
{
    public const int HandSize = 10;

    /// <summary>Below this many real (non-bot) players, FillBotSlots tops the game up with bots
    /// rather than refusing to start - see the doc comment on FillBotSlots for why "force starting
    /// with too few players" isn't a thing any more.</summary>
    public const int MinPlayers = 3;

    /// <summary>
    /// Finishes the setup wizard (XyzzyStartCommand/XyzzySetupCallbackHandler, phase 8.5/8.6):
    /// moves the game to Invites and DMs the starter a "Start" button. Starting the actual round is
    /// a separate step (BeginRoundAsync, triggered by that button) so other players still get a
    /// window to /xyzzy_join before bots fill any empty slots.
    /// </summary>
    public async Task FinishSetupAsync(ITelegramBotClient bot, XyzzyGameState game, long starterId, CancellationToken cancellationToken)
    {
        game.Status = XyzzyStatus.Invites;
        game.StatusChangedUtc = DateTime.UtcNow;
        await games.SaveAsync(game, cancellationToken);

        await outbox.EnqueueButtonQuestionAsync(bot, starterId,
            "Setup's done! Use /xyzzy_join in the group to gather players, then tap Start whenever you're ready " +
            $"- I'll fill any empty slots (below {MinPlayers} players) with bots.",
            [[new DmButton("Start", $"xy:sb:{game.ChatId}")]], cancellationToken);

        await bot.SendMessage(game.ChatId,
            "Setup's done! Use /xyzzy_join to play - the starter can begin whenever they're ready.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Entry point for the "Start" DM button (XyzzyBeginCallbackHandler) - deals the first
    /// hand. Bot top-up now happens inside BeginQuestionAsync itself, not here specifically.</summary>
    public async Task BeginRoundAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken) =>
        await BeginQuestionAsync(bot, game, cancellationToken);

    public async Task BeginQuestionAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        // Re-tops-up to MinPlayers with bots at the start of *every* round, not just the first -
        // closes a real gap (logged in MIGRATION.md before this fix): a kick (XyzzySettingsCallbackHandler)
        // never rechecked anything, and a leave (XyzzyLeaveCommand) only ever checked whether the
        // game should *end*, not whether it should top back up. FillBotSlots is a no-op once the
        // real player count is already at or above MinPlayers, so this is free on every ordinary
        // round where nothing dropped below threshold.
        FillBotSlots(game);

        RotateJudge(game);
        DrawQuestion(game);
        game.RoundNumber++;
        game.Submissions = [];
        game.ReminderSent = false;
        game.StatusChangedUtc = DateTime.UtcNow;
        game.Status = XyzzyStatus.Question;

        foreach (var player in game.Players)
        {
            TopUpHand(game, player);
        }

        await games.SaveAsync(game, cancellationToken);

        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId)!;
        var judge = game.FindPlayer(game.JudgePlayerId!.Value)!;

        if (!judge.IsBot)
        {
            // allowFrontInsert: false - a brand-new round is an independent event, not "the same
            // flow's own continuation" for whoever's action happened to trigger it via a bot-cascade
            // (see DmOutbox.EnqueueButtonQuestionAsync's own doc comment) - it must never preempt
            // something that player already has queued, like an /xyzzy_settings menu.
            await outbox.EnqueueNoticeAsync(bot, judge.PlayerId,
                $"Round {game.RoundNumber}: you're judging! \"{question.Text}\"\nWaiting for everyone else to answer...",
                cancellationToken, allowFrontInsert: false);
        }

        foreach (var player in game.Players.Where(p => p.PlayerId != game.JudgePlayerId && !p.IsBot))
        {
            await outbox.EnqueueButtonQuestionAsync(bot, player.PlayerId,
                $"Round {game.RoundNumber}: \"{question.Text}\"\nPick a card:", BuildHandKeyboard(game, player), cancellationToken, allowFrontInsert: false);
        }

        await bot.SendMessage(game.ChatId,
            $"Round {game.RoundNumber}! {judge.DisplayName} is judging.\n\"{question.Text}\"\nCheck your DMs to play.",
            cancellationToken: cancellationToken);

        // Bots answer immediately rather than waiting on a callback that'll never come - "pick
        // randomly for now" per the initial ask. Re-checks Status each time: an earlier bot's
        // submission may already have completed the round (SubmitAnswerAsync triggers judging once
        // everyone's in) and moved it past Question, including - if the judge is also a bot - all
        // the way through to a new Question entirely. Loops per bot until it's submitted the
        // question's full AnswerCount, same as a real player would across several taps.
        foreach (var botPlayer in game.Players.Where(p => p.PlayerId != game.JudgePlayerId && p.IsBot).ToList())
        {
            while (game.Status is XyzzyStatus.Question
                   && game.Submissions.GetValueOrDefault(botPlayer.PlayerId, []).Count < question.AnswerCount
                   && botPlayer.Hand.Count > 0)
            {
                var randomCardId = botPlayer.Hand[Random.Shared.Next(botPlayer.Hand.Count)];
                await SubmitAnswerAsync(bot, game, botPlayer.PlayerId, randomCardId, cancellationToken);
            }
        }
    }

    public async Task<string> SubmitAnswerAsync(ITelegramBotClient bot, XyzzyGameState game, long playerId, string cardId, CancellationToken cancellationToken)
    {
        if (game.Status is not XyzzyStatus.Question)
        {
            return "That round's already over.";
        }

        if (playerId == game.JudgePlayerId)
        {
            return "The judge doesn't submit an answer.";
        }

        var player = game.FindPlayer(playerId);
        if (player is null)
        {
            return "You're not in this game any more.";
        }

        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId)!;
        var picked = game.Submissions.GetValueOrDefault(playerId, []);
        if (picked.Count >= question.AnswerCount)
        {
            return "You've already answered this round.";
        }

        if (!player.Hand.Remove(cardId))
        {
            return "That card isn't in your hand any more.";
        }

        picked = [.. picked, cardId];
        game.Submissions[playerId] = picked;
        await games.SaveAsync(game, cancellationToken);

        // Multi-answer question, not yet done - ask for the next card rather than checking for
        // judging. BuildHandKeyboard excludes what's already been picked this round.
        if (picked.Count < question.AnswerCount)
        {
            if (!player.IsBot)
            {
                await outbox.EnqueueButtonQuestionAsync(bot, playerId,
                    $"Pick your next card ({picked.Count}/{question.AnswerCount}):", BuildHandKeyboard(game, player), cancellationToken);
            }

            return "Answer submitted! Pick your next card.";
        }

        // Not just "has everyone submitted something" any more - each non-judge player needs to
        // have reached the question's full AnswerCount, not just have an entry in Submissions.
        var allDone = game.Players.Where(p => p.PlayerId != game.JudgePlayerId)
            .All(p => game.Submissions.GetValueOrDefault(p.PlayerId, []).Count >= question.AnswerCount);
        if (allDone)
        {
            await BeginJudgingAsync(bot, game, cancellationToken);
        }

        return "Answer submitted!";
    }

    public async Task<string> PickWinnerAsync(ITelegramBotClient bot, XyzzyGameState game, long judgeId, string cardId, CancellationToken cancellationToken)
    {
        if (game.Status is not XyzzyStatus.Judging)
        {
            return "Judging's already finished for this round.";
        }

        if (judgeId != game.JudgePlayerId)
        {
            return "You're not the judge this round.";
        }

        var winnerId = game.Submissions.Where(kvp => kvp.Value.Contains(cardId)).Select(kvp => (long?)kvp.Key).FirstOrDefault();
        var winner = winnerId is { } id ? game.FindPlayer(id) : null;
        if (winner is null)
        {
            return "That answer isn't valid any more.";
        }

        winner.Wins++;

        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId)!;
        var winningCards = game.Submissions[winner.PlayerId];

        // Single-answer questions substitute directly into the blank, unchanged from before
        // multi-answer support existed. A multi-answer submission falls back to showing the
        // question and the joined answer separately - see BuildJudgeKeyboard's doc comment on why
        // per-blank interleaving isn't reproduced here.
        string filled;
        if (winningCards.Count == 1)
        {
            var answer = CardCatalog.FindAnswer(winningCards[0])!;
            filled = question.Text.Contains('_') ? question.Text.Replace("_", answer.Text) : $"{question.Text} {answer.Text}";
        }
        else
        {
            filled = $"{question.Text}\nAnswer: {CombinedAnswerText(winningCards)}";
        }

        await bot.SendMessage(game.ChatId,
            $"{winner.DisplayName} wins the round with: {filled}\n({winner.DisplayName} now has {ScoreDisplayText(winner)})",
            cancellationToken: cancellationToken);

        await stats.RecordAsync(XyzzyStatNames.HandsPlayed, 1, StatMode.Cumulative, cancellationToken);

        if (!await TryEndGameAsync(bot, game, cancellationToken))
        {
            await AdvanceToNextHandAsync(bot, game, cancellationToken);
        }

        return "Winner picked!";
    }

    /// <summary>Shared "should the game stop here" check - not enough real players left (either too
    /// few total, or the only ones remaining are bots, which would otherwise let a fully-bot game
    /// grind through rounds forever with nobody watching), or the configured question limit's been
    /// reached. Called after a round completes, whether that's the normal judged-a-winner path or
    /// the reconciler force-advancing an empty round. Returns true (and stops the game, with an
    /// appropriate message) if it should; false if play should continue.</summary>
    public async Task<bool> TryEndGameAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        if (game.Players.Count < 2 || game.Players.All(p => p.IsBot))
        {
            game.Status = XyzzyStatus.Stopped;
            await games.SaveAsync(game, cancellationToken);
            await bot.SendMessage(game.ChatId, "Not enough real players left - game over.", cancellationToken: cancellationToken);
            await stats.RecordAsync(XyzzyStatNames.GamesEnded, 1, StatMode.Cumulative, cancellationToken);
            return true;
        }

        if (game.QuestionLimit >= 0 && game.RoundNumber >= game.QuestionLimit)
        {
            game.Status = XyzzyStatus.Stopped;
            await games.SaveAsync(game, cancellationToken);
            var scoreboard = string.Join('\n', game.Players.OrderByDescending(p => p.Wins).Select(p => $"{p.DisplayName}: {p.Wins} win(s)"));
            await bot.SendMessage(game.ChatId, $"That's the end of the game! Final scores:\n{scoreboard}", cancellationToken: cancellationToken);
            await stats.RecordAsync(XyzzyStatNames.GamesEnded, 1, StatMode.Cumulative, cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>Also called directly by XyzzyRoundReconciler when a round times out with at least
    /// one submission already in (force-advance to judging with whatever's there) - public for
    /// that, not just the "everyone answered normally" path.</summary>
    public async Task BeginJudgingAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        if (game.JudgePlayerId is null)
        {
            // The judge left mid-Question, before enough answers came in to trigger judging at all -
            // rather than judge with nobody to pick, just re-deal with a freshly-rotated judge
            // (RotateJudge picks Players[0] when JudgePlayerId is null, same as a brand new game).
            await BeginQuestionAsync(bot, game, cancellationToken);
            return;
        }

        game.Status = XyzzyStatus.Judging;
        game.StatusChangedUtc = DateTime.UtcNow;
        game.ReminderSent = false;
        await games.SaveAsync(game, cancellationToken);

        var judge = game.FindPlayer(game.JudgePlayerId!.Value)!;

        if (judge.IsBot)
        {
            var randomCardId = game.Submissions.Values.Select(v => v[0]).OrderBy(_ => Random.Shared.Next()).First();
            await PickWinnerAsync(bot, game, judge.PlayerId, randomCardId, cancellationToken);
            return;
        }

        var question = CardCatalog.FindQuestion(game.CurrentQuestionCardId)!;
        // allowFrontInsert: false - same reasoning as BeginQuestionAsync's own calls: judging a new
        // round is an independent event, not a continuation of whatever flow the judge might
        // currently be resolving of their own.
        await outbox.EnqueueButtonQuestionAsync(bot, judge.PlayerId,
            $"Everyone's answered! Pick the winner for: \"{question.Text}\"", BuildJudgeKeyboard(game), cancellationToken, allowFrontInsert: false);

        await bot.SendMessage(game.ChatId, "All answers are in - the judge is picking a winner.", cancellationToken: cancellationToken);
    }

    /// <summary>Ports legacy's /xyzzy_settings "Re-deal" - clears every player's hand and this
    /// round's submissions, and empties the chat's draw piles so the next draw naturally reshuffles
    /// fresh from the full catalog (same self-refill DrawQuestion/TopUpHand already use, not a
    /// separate code path), then deals a brand new question. Doesn't touch scores (see the "Reset"
    /// menu action for that) or the player roster. Only meaningful for a round actually in
    /// progress - the caller gates on Status being Question/Judging/WaitingForNextHand.</summary>
    public async Task RedealAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        foreach (var player in game.Players)
        {
            player.Hand.Clear();
        }

        game.Submissions = [];
        game.RemainingQuestionCardIds = [];
        game.RemainingAnswerCardIds = [];
        game.CurrentQuestionCardId = null;

        await bot.SendMessage(game.ChatId, "Reshuffled the decks and dealt everyone a fresh hand!", cancellationToken: cancellationToken);
        await BeginQuestionAsync(bot, game, cancellationToken);
    }

    /// <summary>Ports legacy's /xyzzy_settings "Extend" in full, not just its Stopped-game path:
    /// legacy's extend() always adds more cards to the deck (addQuestions/addAllAnswers) regardless
    /// of status, and *additionally* resumes play if the game was Stopped. On a Stopped game with
    /// players still on the roster (TryEndGameAsync leaves Players intact when it stops a game -
    /// only the explicit setup-time "Cancel" path clears it), resumes with the same roster/scores.
    /// On a game still in progress, tops up the draw piles instead - clearing
    /// RemainingQuestionCardIds/RemainingAnswerCardIds so the next natural draw reshuffles fresh
    /// from FilteredQuestions/FilteredAnswers, picking up any packs enabled since the piles were
    /// last built - without touching hands, the current question, or the round in progress (that's
    /// Re-deal's job, a deliberately more disruptive action). Returns false only when there's
    /// nothing to extend (a Stopped game with too few players to resume).</summary>
    public async Task<bool> TryExtendAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        if (game.Status is XyzzyStatus.Stopped)
        {
            if (game.Players.Count < 2)
            {
                return false;
            }

            await bot.SendMessage(game.ChatId, "Extending the game with the same players and scores!", cancellationToken: cancellationToken);
            await BeginQuestionAsync(bot, game, cancellationToken);
            return true;
        }

        game.RemainingQuestionCardIds = [];
        game.RemainingAnswerCardIds = [];
        await games.SaveAsync(game, cancellationToken);
        await bot.SendMessage(game.ChatId, "Added additional cards to the game!", cancellationToken: cancellationToken);
        return true;
    }

    /// <summary>Throttle/quiet-hours gate between hands (phase 8.3) - mirrors legacy's
    /// waitingForNextHand status. If neither MinWaitHours nor quiet hours apply, the next question
    /// starts immediately (the only path that existed before 8.3, and still the default - both are
    /// 0/unset for a chat that's never touched them). Otherwise parks the game in
    /// WaitingForNextHand; XyzzyRoundReconciler picks it back up once both clear.</summary>
    private async Task AdvanceToNextHandAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        var throttled = game.MinWaitHours > 0;
        var quiet = await quietHours.IsQuietNowAsync(game.ChatId, cancellationToken);

        if (!throttled && !quiet)
        {
            await BeginQuestionAsync(bot, game, cancellationToken);
            return;
        }

        game.Status = XyzzyStatus.WaitingForNextHand;
        game.StatusChangedUtc = DateTime.UtcNow;
        await games.SaveAsync(game, cancellationToken);

        var text = quiet
            ? "It's quiet hours here - I'll ask the next question once they're over."
            : $"Next question in at least {game.MinWaitHours}h.";
        await bot.SendMessage(game.ChatId, text, cancellationToken: cancellationToken);
    }

    /// <summary>Tops the game up to MinPlayers with bots if it's short - replaces the earlier
    /// "/xyzzy_begin force" escape hatch entirely (user's explicit ask: testing solo/with one other
    /// person meant always having to force-start with too few players, which "I've always struggled
    /// with"). Bot IDs are negative (real Telegram user IDs are always positive) so they can never
    /// collide with a real player and are an unambiguous "don't try to DM this" signal everywhere
    /// else in this class.</summary>
    private static void FillBotSlots(XyzzyGameState game)
    {
        var needed = MinPlayers - game.Players.Count;
        if (needed <= 0)
        {
            return;
        }

        var nextBotId = game.Players.Where(p => p.IsBot).Select(p => p.PlayerId).DefaultIfEmpty(0).Min() - 1;
        for (var i = 0; i < needed; i++)
        {
            game.Players.Add(new XyzzyPlayer { PlayerId = nextBotId, DisplayName = $"Bot {-nextBotId}", IsBot = true });
            nextBotId--;
        }
    }

    /// <summary>Legacy's equivalent (lastPlayerAsked) was an int index into the player list, needing
    /// ~60 lines of reindexing bookkeeping in removePlayer whenever a player left. This just walks
    /// the current Players order by stable ID - removing a player never invalidates anything here.</summary>
    private static void RotateJudge(XyzzyGameState game)
    {
        if (game.JudgePlayerId is null)
        {
            game.JudgePlayerId = game.Players[0].PlayerId;
            return;
        }

        var index = game.Players.FindIndex(p => p.PlayerId == game.JudgePlayerId);
        var nextIndex = index < 0 ? 0 : (index + 1) % game.Players.Count;
        game.JudgePlayerId = game.Players[nextIndex].PlayerId;
    }

    /// <summary>Cards belonging to one of the chat's EnabledPackIds, or every card if the
    /// XyzzyPackFilter.AllPacksId sentinel is present. Falls back to the full unfiltered catalog if
    /// the filter would otherwise empty the deck entirely (e.g. a chat's enabled packs got removed
    /// from the catalog on a later import) - an empty deck would hang DrawQuestion/TopUpHand
    /// outright, and a stale filter shouldn't be able to brick a game. This fallback is orthogonal
    /// to the sentinel check above it - it only ever fires for a genuinely stale/mismatched filter.</summary>
    private static IReadOnlyList<XyzzyCard> FilteredQuestions(XyzzyGameState game)
    {
        if (XyzzyPackFilter.AllEnabled(game))
        {
            return CardCatalog.Questions;
        }

        var filtered = CardCatalog.Questions.Where(q => XyzzyPackFilter.IsEnabled(game, q.PackId)).ToList();
        return filtered.Count > 0 ? filtered : CardCatalog.Questions;
    }

    private static IReadOnlyList<XyzzyCard> FilteredAnswers(XyzzyGameState game)
    {
        if (XyzzyPackFilter.AllEnabled(game))
        {
            return CardCatalog.Answers;
        }

        var filtered = CardCatalog.Answers.Where(a => XyzzyPackFilter.IsEnabled(game, a.PackId)).ToList();
        return filtered.Count > 0 ? filtered : CardCatalog.Answers;
    }

    private static void DrawQuestion(XyzzyGameState game)
    {
        if (game.RemainingQuestionCardIds.Count == 0)
        {
            game.RemainingQuestionCardIds = FilteredQuestions(game)
                .Select(q => q.Id)
                .Where(id => id != game.CurrentQuestionCardId)
                .OrderBy(_ => Random.Shared.Next())
                .ToList();
        }

        var next = game.RemainingQuestionCardIds[^1];
        game.RemainingQuestionCardIds.RemoveAt(game.RemainingQuestionCardIds.Count - 1);
        game.CurrentQuestionCardId = next;
    }

    private static void TopUpHand(XyzzyGameState game, XyzzyPlayer player)
    {
        while (player.Hand.Count < HandSize)
        {
            if (game.RemainingAnswerCardIds.Count == 0)
            {
                var inPlay = game.Players.SelectMany(p => p.Hand).ToHashSet();
                game.RemainingAnswerCardIds = FilteredAnswers(game)
                    .Select(a => a.Id)
                    .Where(id => !inPlay.Contains(id))
                    .OrderBy(_ => Random.Shared.Next())
                    .ToList();

                if (game.RemainingAnswerCardIds.Count == 0)
                {
                    break; // whole catalog is already dealt out - shouldn't happen at this catalog size.
                }
            }

            var drawn = game.RemainingAnswerCardIds[^1];
            game.RemainingAnswerCardIds.RemoveAt(game.RemainingAnswerCardIds.Count - 1);
            player.Hand.Add(drawn);
        }
    }

    /// <summary>Excludes cards the player has already picked *this round* (Submissions), so a
    /// multi-answer question can't have the same card played into more than one of its slots.</summary>
    internal static List<List<DmButton>> BuildHandKeyboard(XyzzyGameState game, XyzzyPlayer player)
    {
        var alreadyPicked = game.Submissions.GetValueOrDefault(player.PlayerId, []);
        return player.Hand.Where(cardId => !alreadyPicked.Contains(cardId)).Select(cardId =>
        {
            var card = CardCatalog.FindAnswer(cardId)!;
            var data = new XyzzyCallbackData("a", game.ChatId, game.RoundNumber, cardId).Encode();
            return new List<DmButton> { new(card.Text, data) };
        }).ToList();
    }

    /// <summary>One button per submitter (not per card) - callback_data keys on the submission's
    /// first card, same as before multi-answer support existed. That's still enough to uniquely
    /// identify the submission: PickWinnerAsync matches via Contains(cardId), and a card only ever
    /// belongs to one player's hand/submission at a time (see this class's own doc comment on deck
    /// uniqueness), so no extra bookkeeping is needed just because a submission can now hold more
    /// than one card.</summary>
    internal static List<List<DmButton>> BuildJudgeKeyboard(XyzzyGameState game) =>
        game.Submissions.OrderBy(_ => Random.Shared.Next()).Select(kvp =>
        {
            var data = new XyzzyCallbackData("j", game.ChatId, game.RoundNumber, kvp.Value[0]).Encode();
            return new List<DmButton> { new(CombinedAnswerText(kvp.Value), data) };
        }).ToList();

    /// <summary>Joins a multi-card submission with " >> " - legacy's own fallback format, used here
    /// deliberately instead of reproducing its primary regex-based per-blank interleaving (matching
    /// each answer into its own "_" in the question text). A single-card submission is just that
    /// one card's text, unaffected.</summary>
    internal static string CombinedAnswerText(IReadOnlyList<string> cardIds) =>
        string.Join(" >> ", cardIds.Select(id => CardCatalog.FindAnswer(id)!.Text));

    private static readonly string[] MessedWithUnits =
    [
        "INT", "XP", "Points", "Sq. Ft.", "ft", "6 inches", "mm", "out of 10. Must try harder.", "Buzzards", "Buzzards/m/s²", "m/s²",
    ];

    /// <summary>Legacy's mod_xyzzy_player.getPointsMessage() - normally just "{wins} win(s)", but
    /// once a player's MessedWith flag is set (the /xyzzy_settings "Mess With" toggle), substitutes
    /// a randomized number and nonsense unit instead, re-randomized on every call - purely cosmetic,
    /// the real Wins value is never touched. Used by /xyzzy_status and the round-win announcement;
    /// deliberately NOT used by TryEndGameAsync's final game-over scoreboard, which always shows the
    /// real score - preserving a legacy asymmetry (ambiguous in the source whether it was
    /// deliberate) rather than "fixing" something nobody asked to change.</summary>
    public static string ScoreDisplayText(XyzzyPlayer player)
    {
        if (!player.MessedWith)
        {
            return $"{player.Wins} win(s)";
        }

        var multiplier = (50 - Random.Shared.Next(150)) / 100.0;
        var messedScore = (int)(player.Wins * multiplier);
        var unit = MessedWithUnits[Random.Shared.Next(MessedWithUnits.Length)];
        return $"{messedScore} {unit}";
    }
}
