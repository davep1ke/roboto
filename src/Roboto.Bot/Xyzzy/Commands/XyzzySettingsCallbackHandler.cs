using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Roboto.Bot.Commands;
using Roboto.Bot.Stats;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Handles every button tap from XyzzySettingsCommand's menu - callback_data
/// "xy:se:&lt;chatId&gt;:menu:&lt;action&gt;" for the top-level choice, plus
/// "xy:se:&lt;chatId&gt;:kick:&lt;playerId&gt;" / "xy:se:&lt;chatId&gt;:score:&lt;playerId&gt;" for the two-step
/// "pick a player" follow-ups Kick and Score need (player identified by ID, not name-matching -
/// a correctness improvement over the free-text version, which had to do fuzzy name lookups).
/// </summary>
public sealed class XyzzySettingsCallbackHandler(
    IServiceProvider services, XyzzyGameRepository games, XyzzyRoundService rounds, XyzzyRoundReconciler reconciler,
    DmOutbox outbox, StatsRecorder stats, ILogger<XyzzySettingsCallbackHandler> logger) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:se:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var parts = query.Data!.Split(':', 5);
        if (parts.Length != 5 || !long.TryParse(parts[2], out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var game = await games.GetAsync(chatId, cancellationToken);

        // "Extend" is the one action meant to work on a Stopped game (resuming with the same
        // roster/scores) - every other action needs an actual game in progress. Matches
        // XyzzySettingsCommand's own gate on whether to show the menu at all.
        if (game.Status is XyzzyStatus.Stopped && game.Players.Count == 0)
        {
            return "No game running here any more.";
        }

        var userId = query.From.Id;
        var value = parts[4];

        return parts[3] switch
        {
            "menu" => await HandleMenuAsync(bot, game, userId, value, cancellationToken),
            "kick" => await HandleKickAsync(bot, game, userId, value, cancellationToken),
            "score" => await HandleScoreTargetAsync(bot, game, userId, value, cancellationToken),
            "messwith" => await HandleMessWithTargetAsync(bot, game, userId, value, cancellationToken),
            "packs" => await HandlePackPageAsync(bot, game, userId, value, cancellationToken),
            "packtoggle" => await HandlePackToggleAsync(bot, game, userId, value, cancellationToken),
            "packsall" => await HandlePackEnableAllAsync(bot, game, userId, value, cancellationToken),
            "packsreset" => await HandlePackResetAsync(bot, game, userId, value, cancellationToken),
            "packsimport" => await HandlePackImportPromptAsync(bot, game, userId, value, cancellationToken),
            "packsdone" => await HandlePackDoneAsync(bot, game, userId, cancellationToken),
            "abandonconfirm" => await HandleAbandonConfirmAsync(bot, game, userId, value, cancellationToken),
            _ => "Not a valid choice.",
        };
    }

    private async Task<string> HandleMenuAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "cancel":
                await bot.SendMessage(userId, "Cancelled.", cancellationToken: cancellationToken);
                return "Cancelled.";

            case "abandon":
                // Legacy's own Yes/No confirm here is cosmetic-only - its reply handler never
                // actually checks which button was tapped, so any reply abandons the game. Fixed
                // here rather than reproduced: a real confirm that only abandons on "Yes".
                await outbox.EnqueueButtonQuestionAsync(bot, userId, "Are you sure you want to abandon the game?",
                    [[new DmButton("Yes", $"xy:se:{game.ChatId}:abandonconfirm:yes")], [new DmButton("No", $"xy:se:{game.ChatId}:abandonconfirm:no")]],
                    cancellationToken);
                return "Are you sure?";

            case "timeout":
                await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitTimeout,
                    data: null, XyzzyStartCommand.TimeoutPrompt, cancellationToken);
                return "Let's set the timeout.";

            case "throttle":
                await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitThrottle,
                    data: null, "Minimum hours between rounds (throttle)? Enter 0 for none.", cancellationToken);
                return "Let's set the throttle.";

            case "kick":
                if (game.Players.Count == 0)
                {
                    return "No players to kick.";
                }
                await outbox.EnqueueButtonQuestionAsync(bot, userId, "Who do you want to kick?", BuildPlayerKeyboard(game, "kick"), cancellationToken);
                return "Pick a player.";

            case "score":
                if (game.Players.Count == 0)
                {
                    return "No players to score.";
                }
                await outbox.EnqueueButtonQuestionAsync(bot, userId, "Whose score do you want to change?", BuildPlayerKeyboard(game, "score"), cancellationToken);
                return "Pick a player.";

            case "messwith":
                if (game.Players.Count == 0)
                {
                    return "No players to mess with.";
                }
                await outbox.EnqueueButtonQuestionAsync(bot, userId, "Pick a player to toggle the Mess-With flag", BuildPlayerKeyboard(game, "messwith"), cancellationToken);
                return "Pick a player.";

            case "reset":
                foreach (var player in game.Players)
                {
                    player.Wins = 0;
                }
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(game.ChatId, "Scores have been reset!", cancellationToken: cancellationToken);
                return "Scores reset.";

            case "gamelength":
                await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitQuestionLimit,
                    data: null, "How many questions should the round last for? Enter a number, or -1 for unlimited.", cancellationToken);
                return "Let's set the game length.";

            case "redeal":
                if (game.Status is not (XyzzyStatus.Question or XyzzyStatus.Judging or XyzzyStatus.WaitingForNextHand))
                {
                    return "Nothing to reshuffle yet - the game hasn't started a round.";
                }
                await rounds.RedealAsync(bot, game, cancellationToken);
                return "Redealt.";

            case "extend":
                var extended = await rounds.TryExtendAsync(bot, game, cancellationToken);
                return extended ? "Extended!" : "Nothing to extend - the game needs at least 2 players still on the roster.";

            case "force":
                if (game.Status is not (XyzzyStatus.Question or XyzzyStatus.Judging))
                {
                    return "Nothing stuck to force right now.";
                }
                await reconciler.ForceAdvanceAsync(bot, game, cancellationToken);
                return "Forced!";

            case "packs":
                if (CardCatalog.Packs.Count == 0)
                {
                    return "No card packs are loaded - nothing to filter.";
                }
                await XyzzyPackPickerUi.SendPageAsync(outbox, bot, game, userId, 0, cancellationToken);
                return "Let's pick packs.";

            default:
                return "Not a valid choice.";
        }
    }

    /// <summary>Ports legacy's /xyzzy_settings pack filter (sendPackFilterMessage/
    /// processPackFilterMessage). Message/keyboard building lives in XyzzyPackPickerUi, shared with
    /// XyzzyStartCommand's setup-wizard pack step - only the "packsdone" exit differs per caller
    /// (see HandlePackDoneAsync).</summary>
    private async Task<string> HandlePackPageAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        var page = int.TryParse(pageText, out var parsed) ? parsed : 0;
        await XyzzyPackPickerUi.SendPageAsync(outbox, bot, game, userId, page, cancellationToken);
        return "Page updated.";
    }

    private async Task<string> HandlePackToggleAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string data, CancellationToken cancellationToken)
    {
        var pieces = data.Split('|', 2);
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out var page))
        {
            return "That button isn't valid any more.";
        }

        var packId = pieces[1];
        if (XyzzyPackFilter.AllEnabled(game))
        {
            // First toggle against the "all packs" sentinel materializes the full catalog minus the
            // one just turned off - there's nothing else recorded yet to remove it from.
            game.EnabledPackIds = CardCatalog.Packs.Select(p => p.Id).Where(id => id != packId).ToList();
        }
        else if (game.EnabledPackIds.Contains(packId))
        {
            if (game.EnabledPackIds.Count == 1)
            {
                return "At least one pack has to stay enabled.";
            }

            game.EnabledPackIds.Remove(packId);
        }
        else
        {
            game.EnabledPackIds.Add(packId);
        }

        await games.SaveAsync(game, cancellationToken);
        await XyzzyPackPickerUi.SendPageAsync(outbox, bot, game, userId, page, cancellationToken);
        return "Updated.";
    }

    private async Task<string> HandlePackEnableAllAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        game.EnabledPackIds = [XyzzyPackFilter.AllPacksId];
        await games.SaveAsync(game, cancellationToken);
        var page = int.TryParse(pageText, out var parsed) ? parsed : 0;
        await XyzzyPackPickerUi.SendPageAsync(outbox, bot, game, userId, page, cancellationToken);
        return "All packs enabled.";
    }

    private async Task<string> HandlePackResetAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        game.EnabledPackIds = XyzzyPackFilter.DefaultSelection();
        await games.SaveAsync(game, cancellationToken);
        var page = int.TryParse(pageText, out var parsed) ? parsed : 0;
        await XyzzyPackPickerUi.SendPageAsync(outbox, bot, game, userId, page, cancellationToken);
        return "Reset to the base pack.";
    }

    /// <summary>Ports legacy's "Import Pack" prompt exactly (boilerplate text + free-text pack
    /// code) - routed through ReplyRouter/XyzzySettingsCommand.HandleReplyAsync since it needs
    /// arbitrary typed text, not a button tap. The page number rides along as pending.Data so the
    /// picker can return to the same page once the import/sync finishes.</summary>
    private async Task<string> HandlePackImportPromptAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitPackCode,
            data: pageText,
            "Custom packs are grabbed from cast.clrtd.com - you should search for new deck codes (or create your own) there.\n\n" +
            "To import a pack, enter the pack code. To cancel, type 'Cancel'",
            cancellationToken);
        return "Let's import a pack.";
    }

    /// <summary>The picker's "Continue" button. During the setup wizard's own pack step
    /// (game.Status == SettingUp), advances straight into the next setup step (Timeout) rather than
    /// just closing the menu - XyzzyStartCommand's setup chain owns that step, referenced here only
    /// by its public step-name constant, no runtime dependency on that class.</summary>
    private async Task<string> HandlePackDoneAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, CancellationToken cancellationToken)
    {
        if (game.Status is XyzzyStatus.SettingUp)
        {
            await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_start", XyzzyStartCommand.AskTimeout,
                data: null, XyzzyStartCommand.TimeoutPrompt, cancellationToken);
            return "Let's set the timeout.";
        }

        await bot.SendMessage(userId, "Done.", cancellationToken: cancellationToken);
        return "Done.";
    }

    private async Task<string> HandleAbandonConfirmAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string value, CancellationToken cancellationToken)
    {
        if (value != "yes")
        {
            await bot.SendMessage(userId, "Not abandoned.", cancellationToken: cancellationToken);
            return "Not abandoned.";
        }

        game.Status = XyzzyStatus.Stopped;
        await games.SaveAsync(game, cancellationToken);
        logger.LogInformation("Admin {UserId} abandoned the mod_xyzzy game in chat {ChatId}", userId, game.ChatId);
        await bot.SendMessage(userId, "Game abandoned.", cancellationToken: cancellationToken);
        await bot.SendMessage(game.ChatId, "The game was abandoned by an admin.", cancellationToken: cancellationToken);
        await stats.RecordAsync(XyzzyStatNames.GamesEnded, 1, StatMode.Cumulative, cancellationToken);
        return "Game abandoned.";
    }

    private async Task<string> HandleKickAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string targetIdText, CancellationToken cancellationToken)
    {
        if (!long.TryParse(targetIdText, out var targetId) || game.FindPlayer(targetId) is not { } target)
        {
            return "That player isn't in the game any more.";
        }

        game.Players.Remove(target);
        if (game.JudgePlayerId == target.PlayerId)
        {
            game.JudgePlayerId = null;
        }
        game.Submissions.Remove(target.PlayerId);
        await games.SaveAsync(game, cancellationToken);

        await bot.SendMessage(game.ChatId, $"{target.DisplayName} was kicked from the game.", cancellationToken: cancellationToken);
        return $"Kicked {target.DisplayName}.";
    }

    private async Task<string> HandleMessWithTargetAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string targetIdText, CancellationToken cancellationToken)
    {
        if (!long.TryParse(targetIdText, out var targetId) || game.FindPlayer(targetId) is not { } target)
        {
            return "That player isn't in the game any more.";
        }

        target.MessedWith = !target.MessedWith;
        await games.SaveAsync(game, cancellationToken);

        var state = target.MessedWith ? "now" : "no longer";
        await bot.SendMessage(userId, $"{target.DisplayName}'s score is {state} being messed with.", cancellationToken: cancellationToken);
        return $"Toggled {target.DisplayName}.";
    }

    private async Task<string> HandleScoreTargetAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string targetIdText, CancellationToken cancellationToken)
    {
        if (!long.TryParse(targetIdText, out var targetId) || game.FindPlayer(targetId) is not { } target)
        {
            return "That player isn't in the game any more.";
        }

        await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitScorePoints,
            data: targetId.ToString(), $"What should {target.DisplayName}'s new score be?", cancellationToken);
        return $"Picked {target.DisplayName}.";
    }

    private static List<List<DmButton>> BuildPlayerKeyboard(XyzzyGameState game, string action) =>
        game.Players.Select(p => new List<DmButton> { new(p.DisplayName, $"xy:se:{game.ChatId}:{action}:{p.PlayerId}") }).ToList();
}
