using Roboto.Bot.Commands;
using Telegram.Bot;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_get_settings - a read-only dump, no admin gate (unlike
/// /xyzzy_settings itself, which will be admin-only once it lands in phase 8.4). Reports the
/// hardcoded catalog counts in place of legacy's per-chat pack-filter list, since v1 only has the
/// one built-in pack - see CardCatalog's doc comment.
/// </summary>
public sealed class XyzzyGetSettingsCommand(XyzzyGameRepository games) : IBotCommand
{
    public string Name => "xyzzy_get_settings";
    public string Description => "Shows the Cards Against Humanity settings for this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var chatId = context.Message.Chat.Id;
        var game = await games.GetAsync(chatId, cancellationToken);

        var text =
            $"Card pack: default sample ({CardCatalog.Questions.Count} questions, {CardCatalog.Answers.Count} answers)\n" +
            $"Max wait per round: {game.MaxWaitHours}h\n" +
            $"Min delay between rounds: {game.MinWaitHours}h";

        await context.Bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }
}
