using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>Handles a player tapping a card from their hand to answer the current question.</summary>
public sealed class XyzzyAnswerCallbackHandler(XyzzyGameRepository games, XyzzyRoundService rounds) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:a:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        if (!XyzzyCallbackData.TryParse(query.Data!, out var parsed))
        {
            return "That button isn't valid any more.";
        }

        var game = await games.GetAsync(parsed.ChatId, cancellationToken);
        if (parsed.Round != game.RoundNumber)
        {
            return "That round's already over.";
        }

        return await rounds.SubmitAnswerAsync(bot, game, query.From.Id, parsed.CardId, cancellationToken);
    }
}
