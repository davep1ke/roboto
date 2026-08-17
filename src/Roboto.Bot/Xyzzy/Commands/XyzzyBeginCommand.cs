using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy's admin "Start"/"Override" buttons from the Invites screen as a plain command
/// instead - deals the first hand and asks the first question. Admin-gated via ChatState.IsAdmin
/// (same as /addadmin etc.), needs 3+ players like legacy, "/xyzzy_begin force" allows 2 (legacy's
/// "Override").
/// </summary>
public sealed class XyzzyBeginCommand(XyzzyGameRepository games, ChatRepository chats, XyzzyRoundService rounds) : IBotCommand
{
    public string Name => "xyzzy_begin";
    public string Description => "Deals the first round and starts play (admin only; needs 3+ players, or \"force\" for 2).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var chat = await chats.GetAsync(chatId, cancellationToken);
        var caller = context.Message.From!;

        if (!chat.IsAdmin(caller.Id))
        {
            await context.Bot.SendMessage(chatId,
                "Only a chat admin can begin the round.", cancellationToken: cancellationToken);
            return;
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        if (game.Status is not XyzzyStatus.Invites)
        {
            var text = game.Status is XyzzyStatus.Stopped
                ? "No game waiting to begin. Use /xyzzy_start first."
                : "This game's already underway.";
            await context.Bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
            return;
        }

        var force = context.Args is [var arg, ..] && arg.Equals("force", StringComparison.OrdinalIgnoreCase);
        var minimum = force ? 2 : 3;
        if (game.Players.Count < minimum)
        {
            var text = $"Need at least {minimum} players to begin (currently {game.Players.Count})." +
                       (force ? "" : " Use \"/xyzzy_begin force\" to start with just 2.");
            await context.Bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
            return;
        }

        await rounds.BeginQuestionAsync(context.Bot, game, cancellationToken);
    }
}
