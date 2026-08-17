using Roboto.Bot.Chats;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// Always let through by CommandRouter's mute gate, even in a muted chat - has to be, it's the
/// only way to unmute. Private chats skip ChatState entirely (mirrors the legacy `chat` class not
/// existing at all for PM chats, only groups).
/// </summary>
public sealed class StartCommand(ChatRepository chats) : IBotCommand
{
    public string Name => "start";
    public string Description => "Starts (or resumes) listening to this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Hi! Type /help for a list of commands.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await chats.GetAsync(context.Message.Chat.Id, cancellationToken);
        var wasMuted = chat.Muted;
        chat.Muted = false;
        chat.Title = context.Message.Chat.Title;
        await chats.SaveAsync(chat, cancellationToken);

        var text = wasMuted
            ? "I am listening for messages again. Type /help for a list of commands."
            : "Hello! Type /help for a list of commands.";
        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
