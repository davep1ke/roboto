using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Handles the "Use Defaults" / "Configure Game" / "Cancel" keyboard XyzzyStartCommand sends -
/// callback_data "xy:su:&lt;chatId&gt;:&lt;choice&gt;" ("su" = setup). Kept separate from
/// XyzzyStartCommand itself since that class already implements IReplyHandler for the free-text
/// configure follow-ups (question limit/timeout/throttle) - this is the button half of the same
/// conversation, routed differently because it's a discrete choice rather than typed input.
/// </summary>
public sealed class XyzzySetupCallbackHandler(IServiceProvider services, XyzzyGameRepository games, XyzzyRoundService rounds, DmOutbox outbox) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:su:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var parts = query.Data!.Split(':', 4);
        if (parts.Length != 4 || !long.TryParse(parts[2], out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        if (game.Status is not XyzzyStatus.SettingUp)
        {
            return "Setup's already finished for this game.";
        }

        var userId = query.From.Id;

        switch (parts[3])
        {
            case "cancel":
                game.Status = XyzzyStatus.Stopped;
                game.Players = [];
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(chatId, "Game setup cancelled.", cancellationToken: cancellationToken);
                return "Cancelled.";

            case "defaults":
                await rounds.FinishSetupAsync(bot, game, userId, cancellationToken);
                return "Using default settings.";

            case "configure":
                var replies = services.GetRequiredService<ReplyRouter>();
                await replies.AskAsync(bot, chatId, userId, "xyzzy_start", XyzzyStartCommand.AskQuestionLimit, data: null,
                    "How many questions should the round last for? Enter a number, or -1 for unlimited.", cancellationToken);
                return "Let's configure it.";

            default:
                // Re-offers the same choice rather than just leaving the tapped keyboard dead - the
                // router already removed it as the resolved head (phase 11), so without this a
                // forged/malformed tap here would silently drop the user's DmOutbox queue's head with
                // nothing to replace it, leaving the whole flow stuck.
                await outbox.EnqueueButtonQuestionAsync(bot, userId, XyzzyStartCommand.ChoicePrompt,
                    XyzzyStartCommand.BuildChoiceKeyboard(chatId), cancellationToken);
                return "Not a valid choice.";
        }
    }
}
