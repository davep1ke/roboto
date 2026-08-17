using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_settings admin menu (kick/abandon/timeout/throttle/score) as a
/// single free-text DM command instead of legacy's keyboard-driven sub-flows - "abandon",
/// "timeout &lt;hours&gt;", "throttle &lt;hours&gt;", "kick", "score &lt;player&gt; &lt;points&gt;", "cancel".
/// Only "kick" needs a follow-up question (which player - can't use a reply-to-message here like
/// /addadmin does, there's no message to reply to inside a DM), everything else resolves in one
/// message. Admin-gated via ChatState.IsAdmin, same as /addadmin.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class XyzzySettingsCommand(IServiceProvider services, XyzzyGameRepository games, ChatRepository chats, ILogger<XyzzySettingsCommand> logger) : IReplyHandler
{
    private const string AwaitMenuChoice = "menu";
    private const string AwaitKickTarget = "kick-target";

    public string Name => "xyzzy_settings";
    public string Description => "Admin menu for the Cards Against Humanity game in this chat (kick/abandon/timeout/throttle/score).";

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
            await context.Bot.SendMessage(chatId, "Only a chat admin can change game settings.", cancellationToken: cancellationToken);
            return;
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        if (game.Status is XyzzyStatus.Stopped)
        {
            await context.Bot.SendMessage(chatId, "No game running here.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var asked = await replies.AskAsync(context.Bot, chatId, caller.Id, Name, AwaitMenuChoice, data: null,
            "Cards Against Humanity settings:\n" +
            "- abandon - stop the game entirely\n" +
            "- timeout <hours> - how long to wait before auto-advancing an answer/judging round\n" +
            "- throttle <hours> - minimum delay between hands\n" +
            "- kick - remove a player\n" +
            "- score <player name> <points> - override a player's win count\n" +
            "- cancel",
            cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var game = await games.GetAsync(pending.TargetChatId, cancellationToken);

        if (pending.Step == AwaitKickTarget)
        {
            await HandleKickTargetAsync(bot, game, reply.Text!.Trim(), cancellationToken);
            return;
        }

        var parts = reply.Text!.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var rest = parts.Length > 1 ? parts[1] : "";

        switch (action)
        {
            case "cancel":
                await bot.SendMessage(pending.UserId, "Cancelled.", cancellationToken: cancellationToken);
                break;

            case "abandon":
                game.Status = XyzzyStatus.Stopped;
                await games.SaveAsync(game, cancellationToken);
                logger.LogInformation("Admin {UserId} abandoned the mod_xyzzy game in chat {ChatId}", pending.UserId, pending.TargetChatId);
                await bot.SendMessage(pending.UserId, "Game abandoned.", cancellationToken: cancellationToken);
                await bot.SendMessage(pending.TargetChatId, "The game was abandoned by an admin.", cancellationToken: cancellationToken);
                break;

            case "timeout" when double.TryParse(rest, out var maxHours) && maxHours > 0:
                game.MaxWaitHours = maxHours;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId, $"Timeout set to {maxHours}h.", cancellationToken: cancellationToken);
                break;

            case "throttle" when double.TryParse(rest, out var minHours) && minHours >= 0:
                game.MinWaitHours = minHours;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId, $"Throttle set to {minHours}h.", cancellationToken: cancellationToken);
                break;

            case "kick" when game.Players.Count > 0:
                var replies = services.GetRequiredService<ReplyRouter>();
                await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitKickTarget, data: null,
                    $"Who? ({string.Join(", ", game.Players.Select(p => p.DisplayName))})", cancellationToken);
                break;

            case "kick":
                await bot.SendMessage(pending.UserId, "No players to kick.", cancellationToken: cancellationToken);
                break;

            case "score":
                await HandleScoreAsync(bot, game, rest, pending.UserId, cancellationToken);
                break;

            default:
                await bot.SendMessage(pending.UserId,
                    "I didn't understand that - use /xyzzy_settings again to see the options.", cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleKickTargetAsync(ITelegramBotClient bot, XyzzyGameState game, string targetName, CancellationToken cancellationToken)
    {
        var target = game.Players.FirstOrDefault(p => p.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            await bot.SendMessage(game.ChatId, $"No player called \"{targetName}\" in this game.", cancellationToken: cancellationToken);
            return;
        }

        game.Players.Remove(target);
        if (game.JudgePlayerId == target.PlayerId)
        {
            game.JudgePlayerId = null;
        }
        game.Submissions.Remove(target.PlayerId);
        await games.SaveAsync(game, cancellationToken);

        await bot.SendMessage(game.ChatId, $"{target.DisplayName} was kicked from the game.", cancellationToken: cancellationToken);
    }

    private async Task HandleScoreAsync(ITelegramBotClient bot, XyzzyGameState game, string rest, long userId, CancellationToken cancellationToken)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], out var points))
        {
            await bot.SendMessage(userId, "Usage: score <player name> <points>", cancellationToken: cancellationToken);
            return;
        }

        var playerName = string.Join(' ', parts[..^1]);
        var target = game.Players.FirstOrDefault(p => p.DisplayName.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            await bot.SendMessage(userId, $"No player called \"{playerName}\" in this game.", cancellationToken: cancellationToken);
            return;
        }

        target.Wins = points;
        await games.SaveAsync(game, cancellationToken);
        await bot.SendMessage(userId, $"{target.DisplayName}'s score is now {points}.", cancellationToken: cancellationToken);
    }
}
