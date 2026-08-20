using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>Ports legacy mod_xyzzy's /xyzzy_get_settings - posts the settings summary directly to
/// the group chat (no DM, no keyboard), distinct from /xyzzy_settings (the admin-only DM menu for
/// actually changing anything - anyone can run this one to just see where things stand).</summary>
public sealed class XyzzyGetSettingsCommand(XyzzyGameRepository games) : IBotCommand
{
    private const int PackListCap = 30;

    public string Name => "xyzzy_get_settings";
    public string Description => "Shows the current Cards Against Humanity game settings in this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var game = await games.GetAsync(chatId, cancellationToken);

        if (game.Status is XyzzyStatus.Stopped)
        {
            var text = "No game is running. You should probably start a new game by typing /xyzzy_start in your group chat.";
            if (game.Players.Count > 1)
            {
                text += " You can also continue from the last game by typing /xyzzy_settings, and selecting Extend.";
            }

            await context.Bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
            return;
        }

        var message =
            "Current settings are below. You can change with /xyzzy_settings, or use /xyzzy_status to get the current state of the game.\n" +
            $"- {game.RemainingQuestionCardIds.Count} questions and {game.RemainingAnswerCardIds.Count} answers remain in the deck\n" +
            $"- {game.MaxWaitHours} hour timeouts before the game skips slow players.\n" +
            $"- Wait at least {game.MinWaitHours} hours between hands starting.\n" +
            $"- {EnabledPackCount(game)} packs currently enabled.\n\n" +
            $"Enabled Packs:\n{EnabledPackNames(game)}";

        await context.Bot.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private static int EnabledPackCount(XyzzyGameState game) =>
        XyzzyPackFilter.AllEnabled(game) ? CardCatalog.Packs.Count : game.EnabledPackIds.Count;

    private static string EnabledPackNames(XyzzyGameState game)
    {
        var enabled = CardCatalog.Packs.Where(p => XyzzyPackFilter.IsEnabled(game, p.Id)).Select(p => p.Name).ToList();
        if (enabled.Count == 0)
        {
            return "(none)";
        }

        var shown = string.Join('\n', enabled.Take(PackListCap));
        return enabled.Count > PackListCap ? $"{shown}\n.. plus {enabled.Count - PackListCap} more." : shown;
    }
}
