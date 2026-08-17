using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Roboto.Bot.Commands;
using Roboto.Bot.Stats;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Handles every button tap from XyzzySettingsCommand's menu - callback_data
/// "xy:se:&lt;chatId&gt;:menu:&lt;action&gt;" for the top-level choice, plus
/// "xy:se:&lt;chatId&gt;:kick:&lt;playerId&gt;" / "xy:se:&lt;chatId&gt;:score:&lt;playerId&gt;" for the two-step
/// "pick a player" follow-ups Kick and Score need (player identified by ID, not name-matching -
/// a correctness improvement over the free-text version, which had to do fuzzy name lookups).
/// </summary>
public sealed class XyzzySettingsCallbackHandler(
    IServiceProvider services, XyzzyGameRepository games, XyzzyRoundService rounds, StatsRecorder stats, ILogger<XyzzySettingsCallbackHandler> logger) : ICallbackQueryHandler
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
        if (game.Status is XyzzyStatus.Stopped)
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
            _ => "Not a valid choice.",
        };
    }

    private async Task<string> HandleMenuAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, string action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "cancel":
                await bot.SendMessage(userId, "Cancelled.", cancellationToken: cancellationToken);
                await rounds.RemindIfActionPendingAsync(bot, game, userId, cancellationToken);
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
                await bot.SendMessage(userId, "Who do you want to kick?", replyMarkup: BuildPlayerKeyboard(game, "kick"), cancellationToken: cancellationToken);
                return "Pick a player.";

            case "score":
                if (game.Players.Count == 0)
                {
                    return "No players to score.";
                }
                await bot.SendMessage(userId, "Whose score do you want to change?", replyMarkup: BuildPlayerKeyboard(game, "score"), cancellationToken: cancellationToken);
                return "Pick a player.";

            default:
                return "Not a valid choice.";
        }
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
        await rounds.RemindIfActionPendingAsync(bot, game, userId, cancellationToken);
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

    private static InlineKeyboardMarkup BuildPlayerKeyboard(XyzzyGameState game, string action)
    {
        var rows = game.Players
            .Select(p => new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(p.DisplayName, $"xy:se:{game.ChatId}:{action}:{p.PlayerId}") })
            .ToList();
        return new InlineKeyboardMarkup(rows);
    }
}
