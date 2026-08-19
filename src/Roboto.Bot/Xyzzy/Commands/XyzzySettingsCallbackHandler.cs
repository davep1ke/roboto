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
            "packs" => await HandlePackPageAsync(bot, game, userId, value, cancellationToken),
            "packtoggle" => await HandlePackToggleAsync(bot, game, userId, value, cancellationToken),
            "packsall" => await HandlePackEnableAllAsync(bot, game, userId, value, cancellationToken),
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
                game.Status = XyzzyStatus.Stopped;
                await games.SaveAsync(game, cancellationToken);
                logger.LogInformation("Admin {UserId} abandoned the mod_xyzzy game in chat {ChatId}", userId, game.ChatId);
                await bot.SendMessage(userId, "Game abandoned.", cancellationToken: cancellationToken);
                await bot.SendMessage(game.ChatId, "The game was abandoned by an admin.", cancellationToken: cancellationToken);
                await stats.RecordAsync(XyzzyStatNames.GamesEnded, 1, StatMode.Cumulative, cancellationToken);
                return "Game abandoned.";

            case "timeout":
                await services.GetRequiredService<ReplyRouter>().AskAsync(bot, game.ChatId, userId, "xyzzy_settings", XyzzySettingsCommand.AwaitTimeout,
                    data: null, "How many hours should I wait before auto-advancing an answer/judging round?", cancellationToken);
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
                await outbox.EnqueueButtonQuestionAsync(bot, userId, PackFiltersPrompt, BuildPacksKeyboard(game, 0), cancellationToken);
                return "Let's pick packs.";

            default:
                return "Not a valid choice.";
        }
    }

    /// <summary>Ports legacy's /xyzzy_settings pack filter, keyboard-ified with paging (legacy's own
    /// maxPacksPerPage was 30 - real catalogs run up to ~1,285 packs, so pagination is load-bearing,
    /// not decorative). EnabledPackIds empty means "all packs" (see its own doc comment) - the very
    /// first toggle tap against that state has to materialize the full list minus the one just
    /// turned off, since there's nothing else recorded yet to remove it from.</summary>
    private const int PacksPerPage = 30;
    private const string PackFiltersPrompt = "Cards Against Humanity - pack filters:";

    private async Task<string> HandlePackPageAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        var page = int.TryParse(pageText, out var parsed) ? parsed : 0;
        await outbox.EnqueueButtonQuestionAsync(bot, userId, PackFiltersPrompt, BuildPacksKeyboard(game, page), cancellationToken);
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
        if (game.EnabledPackIds.Count == 0)
        {
            game.EnabledPackIds = CardCatalog.Packs.Select(p => p.Id).Where(id => id != packId).ToList();
        }
        else if (!game.EnabledPackIds.Remove(packId))
        {
            game.EnabledPackIds.Add(packId);
        }

        await games.SaveAsync(game, cancellationToken);
        await outbox.EnqueueButtonQuestionAsync(bot, userId, PackFiltersPrompt, BuildPacksKeyboard(game, page), cancellationToken);
        return "Updated.";
    }

    private async Task<string> HandlePackEnableAllAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string pageText, CancellationToken cancellationToken)
    {
        game.EnabledPackIds = [];
        await games.SaveAsync(game, cancellationToken);
        var page = int.TryParse(pageText, out var parsed) ? parsed : 0;
        await outbox.EnqueueButtonQuestionAsync(bot, userId, PackFiltersPrompt, BuildPacksKeyboard(game, page), cancellationToken);
        return "All packs enabled.";
    }

    private static List<List<DmButton>> BuildPacksKeyboard(XyzzyGameState game, int page)
    {
        var packs = CardCatalog.Packs;
        var totalPages = Math.Max(1, (packs.Count + PacksPerPage - 1) / PacksPerPage);
        page = Math.Clamp(page, 0, totalPages - 1);

        var keyboard = new List<List<DmButton>>();
        foreach (var pack in packs.Skip(page * PacksPerPage).Take(PacksPerPage))
        {
            var enabled = game.EnabledPackIds.Count == 0 || game.EnabledPackIds.Contains(pack.Id);
            var label = (enabled ? "✓ " : "") + pack.Name;
            keyboard.Add([new DmButton(label, $"xy:se:{game.ChatId}:packtoggle:{page}|{pack.Id}")]);
        }

        var navRow = new List<DmButton>();
        if (page > 0)
        {
            navRow.Add(new DmButton("< Prev", $"xy:se:{game.ChatId}:packs:{page - 1}"));
        }
        if (page < totalPages - 1)
        {
            navRow.Add(new DmButton("Next >", $"xy:se:{game.ChatId}:packs:{page + 1}"));
        }
        if (navRow.Count > 0)
        {
            keyboard.Add(navRow);
        }

        keyboard.Add([new DmButton("Enable All Packs", $"xy:se:{game.ChatId}:packsall:{page}")]);
        keyboard.Add([new DmButton("Done", $"xy:se:{game.ChatId}:menu:cancel")]);
        return keyboard;
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
