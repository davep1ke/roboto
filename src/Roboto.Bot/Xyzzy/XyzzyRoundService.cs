using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Xyzzy;

/// <summary>
/// The actual round mechanics (dealing hands, asking a question, collecting answers, judging),
/// shared between XyzzyBeginCommand (kicks off the first round) and the two callback handlers
/// (answer submission can trigger judging; a judge's pick triggers the next round) - all three need
/// the same "deal/ask/advance" logic, so it lives here rather than being duplicated three times.
///
/// Deck refilling: a card is only ever "in play" while it's sitting in a player's Hand (or is the
/// current question) - once played/judged it's simply removed and never stored anywhere else, so
/// it's immediately free to be dealt again in a future refill. Refills explicitly exclude whatever
/// is currently in play (see TopUpHand/DrawQuestion) so no card can ever be dealt to two hands at
/// once - that uniqueness is what lets a card ID alone (via XyzzyCallbackData) unambiguously
/// identify "which submission is this" during judging, no extra bookkeeping needed.
/// </summary>
public sealed class XyzzyRoundService(XyzzyGameRepository games)
{
    public const int HandSize = 10;

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
        foreach (var player in game.Players)
        {
            if (player.PlayerId == game.JudgePlayerId)
            {
                await TrySendDmAsync(bot, player.PlayerId,
                    $"Round {game.RoundNumber}: you're judging! \"{question.Text}\"\nWaiting for everyone else to answer...",
                    null, cancellationToken);
                continue;
            }

            var keyboard = BuildHandKeyboard(game, player);
            await TrySendDmAsync(bot, player.PlayerId,
                $"Round {game.RoundNumber}: \"{question.Text}\"\nPick a card:", keyboard, cancellationToken);
        }

        var judgeName = game.FindPlayer(game.JudgePlayerId!.Value)!.DisplayName;
        await bot.SendMessage(game.ChatId,
            $"Round {game.RoundNumber}! {judgeName} is judging.\n\"{question.Text}\"\nCheck your DMs to play.",
            cancellationToken: cancellationToken);
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

        if (game.Players.Count < 2)
        {
            game.Status = XyzzyStatus.Stopped;
            await games.SaveAsync(game, cancellationToken);
            await bot.SendMessage(game.ChatId, "Not enough players left - game over.", cancellationToken: cancellationToken);
            return "Winner picked!";
        }

        await BeginQuestionAsync(bot, game, cancellationToken);
        return "Winner picked!";
    }

    private async Task BeginJudgingAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        game.Status = XyzzyStatus.Judging;
        game.StatusChangedUtc = DateTime.UtcNow;
        game.ReminderSent = false;
        await games.SaveAsync(game, cancellationToken);

        var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
        var entries = game.Submissions.Select(kvp => kvp.Value[0]).OrderBy(_ => Random.Shared.Next()).ToList();

        var rows = entries.Select(cardId =>
        {
            var card = CardCatalog.Answers.First(a => a.Id == cardId);
            var data = new XyzzyCallbackData("j", game.ChatId, game.RoundNumber, cardId).Encode();
            return new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(card.Text, data) };
        }).ToList();

        await TrySendDmAsync(bot, game.JudgePlayerId!.Value,
            $"Everyone's answered! Pick the winner for: \"{question.Text}\"", new InlineKeyboardMarkup(rows), cancellationToken);

        await bot.SendMessage(game.ChatId, "All answers are in - the judge is picking a winner.", cancellationToken: cancellationToken);
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
