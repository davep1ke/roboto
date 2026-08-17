using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>Handles the judge tapping their pick for the winning answer.</summary>
public sealed class XyzzyJudgeCallbackHandler(XyzzyGameRepository games, XyzzyRoundService rounds) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("xy:j:", StringComparison.Ordinal);

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

        return await rounds.PickWinnerAsync(bot, game, query.From.Id, parsed.CardId, cancellationToken);
    }
}
