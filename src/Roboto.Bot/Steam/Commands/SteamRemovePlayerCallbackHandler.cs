using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Steam.Commands;

/// <summary>Handles taps on SteamRemovePlayerCommand's picker - callback_data
/// "steam:rm:&lt;chatId&gt;:&lt;steamId|cancel&gt;".</summary>
public sealed class SteamRemovePlayerCallbackHandler(SteamRepository steam) : ICallbackQueryHandler
{
    public bool CanHandle(string callbackData) => callbackData.StartsWith("steam:rm:", StringComparison.Ordinal);

    public async Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        var parts = query.Data!.Split(':', 4);
        if (parts.Length != 4 || !long.TryParse(parts[2], out var chatId))
        {
            return "That button isn't valid any more.";
        }

        var target = parts[3];
        if (target == "cancel")
        {
            return "Cancelled.";
        }

        var chat = await steam.GetChatAsync(chatId, cancellationToken);
        var removed = chat.Players.RemoveAll(p => p.SteamId == target) > 0;
        if (removed)
        {
            await steam.SaveChatAsync(chat, cancellationToken);
            await bot.SendMessage(chatId, "Player removed.", cancellationToken: cancellationToken);
            return "Removed.";
        }

        await bot.SendMessage(chatId, "Sorry, something went wrong removing that player.", cancellationToken: cancellationToken);
        return "Something went wrong.";
    }
}
