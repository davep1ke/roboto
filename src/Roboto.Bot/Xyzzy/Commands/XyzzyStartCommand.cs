using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_start, minus the multi-step setup wizard (defaults-vs-custom
/// chain, pack-filter pager, timeout/throttle prompts) - v1 jumps straight to Invites with fixed
/// defaults (matching legacy's own default values). /xyzzy_settings (phase 8.4) will cover
/// adjusting things after the fact. Actually beginning the round (dealing hands, asking the first
/// question) isn't wired up yet either - that needs the inline-keyboard/callback-query
/// infrastructure being built in phase 8.2, so for now the game just sits in Invites once enough
/// players have joined.
/// </summary>
public sealed class XyzzyStartCommand(XyzzyGameRepository games) : IBotCommand
{
    public string Name => "xyzzy_start";
    public string Description => "Starts a new Cards Against Humanity game in this chat.";

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

        if (game.Status is not XyzzyStatus.Stopped)
        {
            await context.Bot.SendMessage(chatId,
                "A game's already in progress here. Use /xyzzy_join to join it or /xyzzy_status to see where it's at.",
                cancellationToken: cancellationToken);
            return;
        }

        var caller = context.Message.From!;
        game.Players = [new XyzzyPlayer { PlayerId = caller.Id, DisplayName = caller.FirstName }];
        game.Status = XyzzyStatus.Invites;
        game.StatusChangedUtc = DateTime.UtcNow;
        await games.SaveAsync(game, cancellationToken);

        await context.Bot.SendMessage(chatId,
            $"{caller.FirstName} started a game of Cards Against Humanity! Use /xyzzy_join to play " +
            "(need at least 3 players).",
            cancellationToken: cancellationToken);
    }
}
