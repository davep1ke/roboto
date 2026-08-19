using Microsoft.Extensions.Logging;
using Roboto.Bot.Commands;
using Roboto.Bot.Stats;
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
public sealed class XyzzyRoundReconciler(XyzzyGameRepository games, XyzzyRoundService rounds, QuietHoursQuery quietHours, StatsRecorder stats, ILogger<XyzzyRoundReconciler> logger)
{
    private const double ReminderThreshold = 0.75;

    public async Task ReconcileAllAsync(ITelegramBotClient bot, CancellationToken cancellationToken)
    {
        var activeGames = await games.GetAllActiveAsync(cancellationToken);

        // Free snapshot - GetAllActiveAsync already loaded every active game for this tick, no
        // extra query needed to record how many games/players are currently active right now.
        await stats.RecordAsync(XyzzyStatNames.ActiveGames, activeGames.Count, StatMode.Snapshot, cancellationToken);
        await stats.RecordAsync(XyzzyStatNames.ActivePlayers, activeGames.Sum(g => g.Players.Count(p => !p.IsBot)), StatMode.Snapshot, cancellationToken);

        foreach (var game in activeGames)
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
        XyzzyStatus.SettingUp => ReconcileAbandonedSetupAsync(bot, game, cancellationToken),
        _ => Task.CompletedTask, // Stopped/Invites have no clock running.
    };

    private async Task ReconcileTimeoutAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        // The judge left after judging had already begun (XyzzyLeaveCommand only resolves this
        // proactively when the departure itself triggers it - a judge leaving isn't the only way
        // this can happen, e.g. a kick via /xyzzy_settings). Don't wait for the normal reminder/
        // timeout thresholds with nobody to actually judge - resolve it on the very next tick.
        if (game.Status is XyzzyStatus.Judging && game.JudgePlayerId is null)
        {
            if (!await rounds.TryEndGameAsync(bot, game, cancellationToken))
            {
                await rounds.BeginQuestionAsync(bot, game, cancellationToken);
            }
            return;
        }

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

    /// <summary>A game abandoned mid-setup (starter asked "defaults or configure?" and never
    /// replied) shouldn't squat the chat's one-game slot forever - mirrors legacy's "idle >24h in
    /// any setup status auto-resets to Stopped".</summary>
    private async Task ReconcileAbandonedSetupAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - game.StatusChangedUtc < TimeSpan.FromHours(24))
        {
            return;
        }

        game.Status = XyzzyStatus.Stopped;
        game.Players = [];
        await games.SaveAsync(game, cancellationToken);
        await bot.SendMessage(game.ChatId, "Game setup timed out - use /xyzzy_start to try again.", cancellationToken: cancellationToken);
    }

    /// <summary>Internal, not private - also the mechanics behind /xyzzy_settings' "Force Question"
    /// admin action (XyzzySettingsCallbackHandler), an on-demand version of the same timeout logic
    /// this class applies automatically. Callers outside this class are expected to have already
    /// gated on Status being Question or Judging - this method assumes it (the Judging branch below
    /// doesn't check, matching the one call site inside this class that already guarantees it).</summary>
    internal async Task ForceAdvanceAsync(ITelegramBotClient bot, XyzzyGameState game, CancellationToken cancellationToken)
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
                if (!await rounds.TryEndGameAsync(bot, game, cancellationToken))
                {
                    await rounds.BeginQuestionAsync(bot, game, cancellationToken);
                }
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
