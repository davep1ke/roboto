using Microsoft.Extensions.Logging;
using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Xyzzy;

/// <summary>
/// The actual "what should happen to each active game right now" logic - pulled out of
/// XyzzyRoundSchedulerService (a BackgroundService, awkward to test directly) so it's directly
/// callable/testable, same reasoning MessageDispatcher was split out of TelegramPollingService.
/// Mirrors legacy's check() method: a reminder at 75% of MaxWaitHours, a force-advance at 100%, and
/// resuming a WaitingForNextHand game once the MinWaitHours throttle and any quiet-hours window
/// have both cleared.
/// </summary>
public sealed class XyzzyRoundReconciler(XyzzyGameRepository games, XyzzyRoundService rounds, QuietHoursQuery quietHours, ILogger<XyzzyRoundReconciler> logger)
{
    private const double ReminderThreshold = 0.75;

    public async Task ReconcileAllAsync(ITelegramBotClient bot, CancellationToken cancellationToken)
    {
        foreach (var game in await games.GetAllActiveAsync(cancellationToken))
        {
            try
            {
                await ReconcileAsync(bot, game, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciling mod_xyzzy game in chat {ChatId} failed", game.ChatId);
            }
        }
    }

    public Task ReconcileAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken) => game.Status switch
    {
        XyzzyStatus.Question or XyzzyStatus.Judging => ReconcileTimeoutAsync(bot, game, cancellationToken),
        XyzzyStatus.WaitingForNextHand => ReconcileWaitingAsync(bot, game, cancellationToken),
        _ => Task.CompletedTask, // Stopped/Invites have no clock running.
    };

    private async Task ReconcileTimeoutAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        var elapsed = DateTime.UtcNow - game.StatusChangedUtc;
        var maxWait = TimeSpan.FromHours(game.MaxWaitHours);

        if (elapsed >= maxWait)
        {
            await ForceAdvanceAsync(bot, game, cancellationToken);
        }
        else if (!game.ReminderSent && elapsed.Ticks >= (long)(maxWait.Ticks * ReminderThreshold))
        {
            await SendReminderAsync(bot, game, cancellationToken);
        }
    }

    private async Task ReconcileWaitingAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        var throttleElapsed = DateTime.UtcNow - game.StatusChangedUtc >= TimeSpan.FromHours(game.MinWaitHours);
        if (throttleElapsed && !await quietHours.IsQuietNowAsync(game.ChatId, cancellationToken))
        {
            await rounds.BeginQuestionAsync(bot, game, cancellationToken);
        }
    }

    private async Task SendReminderAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        game.ReminderSent = true;
        await games.SaveAsync(game, cancellationToken);

        if (game.Status is XyzzyStatus.Question)
        {
            var waitingOn = game.Players.Where(p => p.PlayerId != game.JudgePlayerId && !game.Submissions.ContainsKey(p.PlayerId));
            foreach (var player in waitingOn)
            {
                await TrySendDmAsync(bot, player.PlayerId, "Reminder: you still need to play a card!", cancellationToken);
            }
        }
        else
        {
            await TrySendDmAsync(bot, game.JudgePlayerId!.Value, "Reminder: you still need to pick a winner!", cancellationToken);
        }
    }

    private async Task ForceAdvanceAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        if (game.Status is XyzzyStatus.Question)
        {
            if (game.Submissions.Count > 0)
            {
                await bot.SendMessage(game.ChatId, "Time's up! Judging with whoever answered in time.", cancellationToken: cancellationToken);
                await rounds.BeginJudgingAsync(bot, game, cancellationToken);
            }
            else
            {
                await bot.SendMessage(game.ChatId, "Nobody answered in time - skipping to a new question.", cancellationToken: cancellationToken);
                await rounds.BeginQuestionAsync(bot, game, cancellationToken);
            }

            return;
        }

        // Judging timed out - auto-pick a random submission rather than replicating legacy's
        // "dock the judge a point" quirk, which isn't essential to keep the game moving.
        var cardId = game.Submissions.Values.Select(v => v[0]).OrderBy(_ => Random.Shared.Next()).First();
        await bot.SendMessage(game.ChatId, "The judge took too long - auto-picking a winner.", cancellationToken: cancellationToken);
        await rounds.PickWinnerAsync(bot, game, game.JudgePlayerId!.Value, cardId, cancellationToken);
    }

    private static async Task TrySendDmAsync(ITelegramBotClient bot, long userId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await bot.SendMessage(userId, text, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort, same reasoning as XyzzyRoundService.TrySendDmAsync.
        }
    }
}
