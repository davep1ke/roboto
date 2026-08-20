using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>Handles the "which game?" picker XyzzyLeaveCommand's DM variant sends - callback_data
/// "xy:lv:&lt;chatId&gt;" (leave that game) or "xy:lv:cancel". Mirrors the group-context /xyzzy_leave's
/// own leave logic (XyzzyLeaveCommand) since both need the same judge-clearing/round-resolution
/// handling - kept here rather than shared via a helper since the two call sites differ (a Message
/// vs a resolved chatId+caller) enough that inlining is clearer than a shared private method would
/// be across two different classes.</summary>
public sealed class XyzzyLeavePickerCallbackHandler(XyzzyGameRepository games, XyzzyRoundService rounds) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:lv:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var value = query.Data!["xy:lv:".Length..];
        if (value == "cancel")
        {
            return "Cancelled.";
        }

        if (!long.TryParse(value, out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        var callerId = query.From.Id;
        var player = game.FindPlayer(callerId);
        if (player is null)
        {
            return "You're not in that game any more.";
        }

        game.Players.Remove(player);
        if (game.JudgePlayerId == callerId)
        {
            game.JudgePlayerId = null;
        }

        await games.SaveAsync(game, cancellationToken);
        await bot.SendMessage(chatId, $"{player.DisplayName} left the game. ({game.Players.Count} players)", cancellationToken: cancellationToken);

        if (game.Status is XyzzyStatus.Question or XyzzyStatus.Judging or XyzzyStatus.WaitingForNextHand)
        {
            var ended = await rounds.TryEndGameAsync(bot, game, cancellationToken);
            if (!ended && game.JudgePlayerId is null)
            {
                await rounds.BeginQuestionAsync(bot, game, cancellationToken);
            }
        }

        return "You left the game.";
    }
}
