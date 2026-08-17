using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_join. Checks the player has an open DM with the bot up front
/// (same as legacy) - once round-play lands (phase 8.2), hands get dealt and cards offered over
/// DM, so a player who can't be DMed can't actually play.
/// </summary>
public sealed class XyzzyJoinCommand(XyzzyGameRepository games) : IBotCommand
{
    public string Name => "xyzzy_join";
    public string Description => "Joins the Cards Against Humanity game in this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var game = await games.GetAsync(chatId, cancellationToken);

        if (game.Status is XyzzyStatus.Stopped)
        {
            await context.Bot.SendMessage(chatId,
                "No game's running here yet. Use /xyzzy_start to start one.", cancellationToken: cancellationToken);
            return;
        }

        var caller = context.Message.From!;
        if (game.IsPlayer(caller.Id))
        {
            await context.Bot.SendMessage(chatId, $"{caller.FirstName} is already in this game.",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            await context.Bot.SendMessage(caller.Id,
                $"You joined the Cards Against Humanity game in {context.Message.Chat.Title}. " +
                "I'll message you here when it's your turn to play or judge.",
                cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first before joining - I couldn't DM them.",
                cancellationToken: cancellationToken);
            return;
        }

        game.Players.Add(new XyzzyPlayer { PlayerId = caller.Id, DisplayName = caller.FirstName });
        await games.SaveAsync(game, cancellationToken);

        await context.Bot.SendMessage(chatId, $"{caller.FirstName} joined! ({game.Players.Count} players)",
            cancellationToken: cancellationToken);
    }
}
