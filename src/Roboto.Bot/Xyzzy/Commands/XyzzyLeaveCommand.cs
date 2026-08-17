using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_leave, group-context only - legacy also has a DM variant (typed
/// with no chat context, scans every chat you're playing in and disambiguates if you're in
/// several) which is deliberately dropped for v1, see MIGRATION.md's scope-cuts note. Removing a
/// player who happens to be the current judge just clears JudgePlayerId - no index-reshuffling
/// needed, unlike legacy's array-index judge pointer (see XyzzyGameState.JudgePlayerId). Mid-round
/// consequences of a judge/player leaving (e.g. re-picking a judge) belong to the round-loop logic
/// landing in phase 8.2 - no round is active yet for this command to worry about.
/// </summary>
public sealed class XyzzyLeaveCommand(XyzzyGameRepository games) : IBotCommand
{
    public string Name => "xyzzy_leave";
    public string Description => "Leaves the Cards Against Humanity game in this chat.";

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
    }
}
