using System.Text;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_status. Round-specific detail (current question, who hasn't
/// answered yet, time left before timeout) will show up once phase 8.2/8.3 land - for now, since
/// no round can be in progress, this only reports the game's phase and the player list.
/// </summary>
public sealed class XyzzyStatusCommand(XyzzyGameRepository games) : IBotCommand
{
    public string Name => "xyzzy_status";
    public string Description => "Shows the state of the Cards Against Humanity game in this chat.";

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
                "No game running here. Use /xyzzy_start to start one.", cancellationToken: cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Status: {game.Status}");

        if (game.Status is XyzzyStatus.Invites)
        {
            sb.AppendLine($"Waiting for players ({game.Players.Count}/3 minimum).");
        }

        sb.AppendLine("Players:");
        foreach (var player in game.Players.OrderByDescending(p => p.Wins))
        {
            var judgeTag = game.JudgePlayerId == player.PlayerId ? " (judge)" : "";
            sb.AppendLine($"- {player.DisplayName}: {player.Wins} win(s){judgeTag}");
        }

        await context.Bot.SendMessage(chatId, sb.ToString().TrimEnd(), cancellationToken: cancellationToken);
    }
}
