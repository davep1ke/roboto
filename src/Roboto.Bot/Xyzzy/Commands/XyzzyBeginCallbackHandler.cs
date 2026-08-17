using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Handles the "Start" button XyzzyRoundService.FinishSetupAsync DMs to the starter -
/// callback_data "xy:sb:&lt;chatId&gt;" ("sb" = start begin). Replaces the earlier group-chat
/// /xyzzy_begin command entirely per user feedback (2026-08-17): only the starter ever sees this
/// button (it's in their own DM), so there's no separate admin check needed the way the old group
/// command had - having the button at all *is* the access control. No player-count gate either -
/// XyzzyRoundService.BeginRoundAsync tops up with bots if short, so there's nothing to refuse.
/// </summary>
public sealed class XyzzyBeginCallbackHandler(XyzzyGameRepository games, XyzzyRoundService rounds) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:sb:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var parts = query.Data!.Split(':', 3);
        if (parts.Length != 3 || !long.TryParse(parts[2], out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        if (game.Status is not XyzzyStatus.Invites)
        {
            return "This game isn't waiting to begin any more.";
        }

        await rounds.BeginRoundAsync(bot, game, cancellationToken);
        return "Starting the game!";
    }
}
