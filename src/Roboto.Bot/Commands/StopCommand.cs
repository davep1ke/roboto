using Roboto.Bot.Chats;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

public sealed class StopCommand(ChatRepository chats) : IBotCommand
{
    public string Name => "stop";
    public string Description => "Stops listening to this chat until /start is sent again.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await chats.GetAsync(context.Message.Chat.Id, cancellationToken);
        chat.Muted = true;
        chat.Title = context.Message.Chat.Title;
        await chats.SaveAsync(chat, cancellationToken);

        await context.Bot.SendMessage(context.Message.Chat.Id,
            "I am now ignoring all messages in this chat until I get a /start command.",
            cancellationToken: cancellationToken);
    }
}
