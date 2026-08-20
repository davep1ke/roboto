using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_leave, both the group-context version (leave the game right
/// here) and the DM version (typed with no chat context - scans every active game you're in and
/// shows a picker, XyzzyLeavePickerCallbackHandler owns the actual leave once one's chosen).
/// Removing a player who happens to be the current judge just clears JudgePlayerId - no index-
/// reshuffling needed, unlike legacy's array-index judge pointer (see XyzzyGameState.JudgePlayerId).
///
/// If the departure leaves a round active with no judge or too few real players, resolves it
/// immediately (TryEndGameAsync, or re-dealing with a freshly-rotated judge) rather than leaving it
/// broken until the next scheduler tick - see XyzzyRoundService.BeginJudgingAsync's own null-judge
/// guard for the other half of this (a judge leaving mid-Question, before judging even starts).
/// </summary>
public sealed class XyzzyLeaveCommand(XyzzyGameRepository games, XyzzyRoundService rounds, ChatRepository chats, DmOutbox outbox) : IBotCommand
{
    public string Name => "xyzzy_leave";
    public string Description => "Leaves the Cards Against Humanity game in this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await ExecuteDmPickerAsync(context, cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var game = await games.GetAsync(chatId, cancellationToken);
        var caller = context.Message.From!;

        var removed = game.Players.RemoveAll(p => p.PlayerId == caller.Id) > 0;
        if (!removed)
        {
            await context.Bot.SendMessage(chatId, $"{caller.FirstName} isn't in this game.",
                cancellationToken: cancellationToken);
            return;
        }

        if (game.JudgePlayerId == caller.Id)
        {
            game.JudgePlayerId = null;
        }

        await games.SaveAsync(game, cancellationToken);
        await context.Bot.SendMessage(chatId, $"{caller.FirstName} left the game. ({game.Players.Count} players)",
            cancellationToken: cancellationToken);

        if (game.Status is XyzzyStatus.Question or XyzzyStatus.Judging or XyzzyStatus.WaitingForNextHand)
        {
            var ended = await rounds.TryEndGameAsync(context.Bot, game, cancellationToken);
            if (!ended && game.JudgePlayerId is null)
            {
                await rounds.BeginQuestionAsync(context.Bot, game, cancellationToken);
            }
        }
    }

    private async Task ExecuteDmPickerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var callerId = context.Message.From!.Id;
        var active = await games.GetAllActiveAsync(cancellationToken);
        var myGames = active.Where(g => g.Players.Any(p => p.PlayerId == callerId)).ToList();

        if (myGames.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "You are not in any active games.", cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new List<List<DmButton>>();
        foreach (var game in myGames)
        {
            var chat = await chats.GetAsync(game.ChatId, cancellationToken);
            var title = string.IsNullOrEmpty(chat.Title) ? game.ChatId.ToString() : chat.Title;
            keyboard.Add([new DmButton($"{title} ({game.ChatId})", $"xy:lv:{game.ChatId}")]);
        }
        keyboard.Add([new DmButton("Cancel", "xy:lv:cancel")]);

        await outbox.EnqueueButtonQuestionAsync(context.Bot, callerId, "Which game would you like to leave?", keyboard, cancellationToken);
    }
}
