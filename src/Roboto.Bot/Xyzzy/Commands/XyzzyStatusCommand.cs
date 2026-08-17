using System.Text;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>Ports legacy mod_xyzzy's /xyzzy_status.</summary>
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

        if (game.Status is XyzzyStatus.SettingUp)
        {
            sb.AppendLine("The starter is still finishing setup over DM.");
        }

        if (game.Status is XyzzyStatus.Invites)
        {
            sb.AppendLine($"{game.Players.Count} player(s) so far - the starter can tap Start any time " +
                           $"(I'll fill up to {XyzzyRoundService.MinPlayers} with bots if needed).");
        }

        if (game.Status is XyzzyStatus.Question or XyzzyStatus.Judging && game.CurrentQuestionCardId is not null)
        {
            var question = CardCatalog.Questions.First(q => q.Id == game.CurrentQuestionCardId);
            sb.AppendLine($"Round {game.RoundNumber}: \"{question.Text}\"");
        }

        if (game.Status is XyzzyStatus.Question)
        {
            var waitingOn = game.Players
                .Where(p => p.PlayerId != game.JudgePlayerId && !game.Submissions.ContainsKey(p.PlayerId))
                .Select(p => p.DisplayName);
            sb.AppendLine($"Still waiting on: {string.Join(", ", waitingOn)}");
        }
        else if (game.Status is XyzzyStatus.Judging)
        {
            sb.AppendLine("The judge is picking a winner.");
        }
        else if (game.Status is XyzzyStatus.WaitingForNextHand)
        {
            sb.AppendLine("Waiting to deal the next round (throttle and/or quiet hours).");
        }

        sb.AppendLine("Players:");
        foreach (var player in game.Players.OrderByDescending(p => p.Wins))
        {
            var tags = (player.IsBot ? " (bot)" : "") + (game.JudgePlayerId == player.PlayerId ? " (judge)" : "");
            sb.AppendLine($"- {player.DisplayName}: {player.Wins} win(s){tags}");
        }

        await context.Bot.SendMessage(chatId, sb.ToString().TrimEnd(), cancellationToken: cancellationToken);
    }
}
