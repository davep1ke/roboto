using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Xyzzy;

/// <summary>
/// The actual round mechanics (setup completion, dealing hands, asking a question, collecting
/// answers, judging), shared between XyzzyStartCommand/the setup and begin callback handlers (kick
/// off a game) and the answer/judge callback handlers (answering can trigger judging; a judge's pick
/// triggers the next round) - all of them need the same "deal/ask/advance" logic, so it lives here
/// rather than being duplicated across every caller.
///
/// Deck refilling: a card is only ever "in play" while it's sitting in a player's Hand (or is the
/// current question) - once played/judged it's simply removed and never stored anywhere else, so
/// it's immediately free to be dealt again in a future refill. Refills explicitly exclude whatever
/// is currently in play (see TopUpHand/DrawQuestion) so no card can ever be dealt to two hands at
/// once - that uniqueness is what lets a card ID alone (via XyzzyCallbackData) unambiguously
/// identify "which submission is this" during judging, no extra bookkeeping needed.
/// </summary>
public sealed class XyzzyRoundService(XyzzyGameRepository games, QuietHoursQuery quietHours)
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

        var keyboard = new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("Start", $"xy:sb:{game.ChatId}")]]);
        await TrySendDmAsync(bot, starterId,
            "Setup's done! Use /xyzzy_join in the group to gather players, then tap Start whenever you're ready " +
            $"- I'll fill any empty slots (below {MinPlayers} players) with bots.",
            keyboard, cancellationToken);

        await bot.SendMessage(game.ChatId,
            "Setup's done! Use /xyzzy_join to play - the starter can begin whenever they're ready.",
            cancellationToken: cancellationToken);
    }

    /// <summary>Entry point for the "Start" DM button (XyzzyBeginCallbackHandler) - tops the game up
    /// with bots if it's short of MinPlayers, then deals the first hand.</summary>
    public async Task BeginRoundAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        FillBotSlots(game);
        await BeginQuestionAsync(bot, game, cancellationToken);
    }

    public async Task BeginQuestionAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
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

        var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
        var judge = game.FindPlayer(game.JudgePlayerId!.Value)!;

        if (!judge.IsBot)
        {
            await TrySendDmAsync(bot, judge.PlayerId,
                $"Round {game.RoundNumber}: you're judging! \"{question.Text}\"\nWaiting for everyone else to answer...",
                null, cancellationToken);
        }

        foreach (var player in game.Players.Where(p => p.PlayerId != game.JudgePlayerId && !p.IsBot))
        {
            var keyboard = BuildHandKeyboard(game, player);
            await TrySendDmAsync(bot, player.PlayerId,
                $"Round {game.RoundNumber}: \"{question.Text}\"\nPick a card:", keyboard, cancellationToken);
        }

        await bot.SendMessage(game.ChatId,
            $"Round {game.RoundNumber}! {judge.DisplayName} is judging.\n\"{question.Text}\"\nCheck your DMs to play.",
            cancellationToken: cancellationToken);

        // Bots answer immediately rather than waiting on a callback that'll never come - "pick
        // randomly for now" per the initial ask. Re-checks Status each time: an earlier bot's
        // submission may already have completed the round (SubmitAnswerAsync triggers judging once
        // everyone's in) and moved it past Question, including - if the judge is also a bot - all
        // the way through to a new Question entirely.
        foreach (var botPlayer in game.Players.Where(p => p.PlayerId != game.JudgePlayerId && p.IsBot).ToList())
        {
            if (game.Status is not XyzzyStatus.Question)
            {
                break;
            }

            var randomCardId = botPlayer.Hand[Random.Shared.Next(botPlayer.Hand.Count)];
            await SubmitAnswerAsync(bot, game, botPlayer.PlayerId, randomCardId, cancellationToken);
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

        if (game.Submissions.ContainsKey(playerId))
        {
            return "You've already answered this round.";
        }

        if (!player.Hand.Remove(cardId))
        {
            return "That card isn't in your hand any more.";
        }

        game.Submissions[playerId] = [cardId];
        await games.SaveAsync(game, cancellationToken);

        var nonJudgePlayers = game.Players.Count(p => p.PlayerId != game.JudgePlayerId);
        if (game.Submissions.Count >= nonJudgePlayers)
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

        var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
        var answer = CardCatalog.Answers.First(a => a.Id == cardId);
        var filled = question.Text.Contains('_') ? question.Text.Replace("_", answer.Text) : $"{question.Text} {answer.Text}";

        await bot.SendMessage(game.ChatId,
            $"{winner.DisplayName} wins the round with: {filled}\n({winner.DisplayName} now has {winner.Wins} win(s))",
            cancellationToken: cancellationToken);

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
            return true;
        }

        if (game.QuestionLimit >= 0 && game.RoundNumber >= game.QuestionLimit)
        {
            game.Status = XyzzyStatus.Stopped;
            await games.SaveAsync(game, cancellationToken);
            var scoreboard = string.Join('\n', game.Players.OrderByDescending(p => p.Wins).Select(p => $"{p.DisplayName}: {p.Wins} win(s)"));
            await bot.SendMessage(game.ChatId, $"That's the end of the game! Final scores:\n{scoreboard}", cancellationToken: cancellationToken);
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

        var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
        await TrySendDmAsync(bot, judge.PlayerId,
            $"Everyone's answered! Pick the winner for: \"{question.Text}\"", BuildJudgeKeyboard(game), cancellationToken);

        await bot.SendMessage(game.ChatId, "All answers are in - the judge is picking a winner.", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Re-sends whichever keyboard (hand or judge) a player still hasn't acted on, with a short
    /// reminder - added after user feedback that running /xyzzy_settings mid-round buried the
    /// original "pick a card"/"pick a winner" message with no way to tell it was still waiting on
    /// them. A no-op if this player doesn't currently have anything outstanding (not in the game,
    /// already answered, not the judge, game isn't even mid-round, etc.) - safe to call
    /// unconditionally after any settings interaction finishes.
    /// </summary>
    public async Task RemindIfActionPendingAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, CancellationToken cancellationToken)
    {
        var player = game.FindPlayer(userId);
        if (player is null || player.IsBot)
        {
            return;
        }

        if (game.Status is XyzzyStatus.Question && userId != game.JudgePlayerId && !game.Submissions.ContainsKey(userId))
        {
            var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
            await TrySendDmAsync(bot, userId,
                $"Reminder: I'm still waiting on your answer for \"{question.Text}\".", BuildHandKeyboard(game, player), cancellationToken);
        }
        else if (game.Status is XyzzyStatus.Judging && userId == game.JudgePlayerId)
        {
            var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
            await TrySendDmAsync(bot, userId,
                $"Reminder: I'm still waiting on you to pick a winner for \"{question.Text}\".", BuildJudgeKeyboard(game), cancellationToken);
        }
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

    private static void DrawQuestion(XyzzyGameState game)
    {
        if (game.RemainingQuestionCardIds.Count == 0)
        {
            game.RemainingQuestionCardIds = CardCatalog.Questions
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
                game.RemainingAnswerCardIds = CardCatalog.Answers
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

    private static InlineKeyboardMarkup BuildHandKeyboard(XyzzyGameState game, XyzzyPlayer player)
    {
        var rows = player.Hand.Select(cardId =>
        {
            var card = CardCatalog.Answers.First(a => a.Id == cardId);
            var data = new XyzzyCallbackData("a", game.ChatId, game.RoundNumber, cardId).Encode();
            return new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(card.Text, data) };
        }).ToList();

        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildJudgeKeyboard(XyzzyGameState game)
    {
        var entries = game.Submissions.Select(kvp => kvp.Value[0]).OrderBy(_ => Random.Shared.Next()).ToList();
        var rows = entries.Select(cardId =>
        {
            var card = CardCatalog.Answers.First(a => a.Id == cardId);
            var data = new XyzzyCallbackData("j", game.ChatId, game.RoundNumber, cardId).Encode();
            return new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(card.Text, data) };
        }).ToList();

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>Swallows DM failures (blocked/never-opened chat) so one unreachable player doesn't
    /// crash the whole round-start - a deliberate v1 simplification of legacy's fuller dormant-player
    /// bookkeeping (removal, chat notices). Revisit if this turns out to matter in practice.</summary>
    private static async Task TrySendDmAsync(ITelegramBotClient bot, long userId, string text, InlineKeyboardMarkup? keyboard, CancellationToken cancellationToken)
    {
        try
        {
            await bot.SendMessage(userId, text, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort - see doc comment above.
        }
    }
}
