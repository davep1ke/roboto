using Roboto.Bot.Chats;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// Ports legacy mod_standard's /removeadmin - see AddAdminCommand's comment for why this uses
/// Telegram's "reply to their message" pattern instead of legacy's presence-tracked keyboard.
/// </summary>
public sealed class RemoveAdminCommand(ChatRepository chats) : IBotCommand
{
    public string Name => "removeadmin";
    public string Description => "Removes the person you reply to as a chat admin.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await chats.GetAsync(context.Message.Chat.Id, cancellationToken);

        if (chat.Admins.Count == 0)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This group doesn't have any admins!", cancellationToken: cancellationToken);
            return;
        }

        var caller = context.Message.From!;
        if (!chat.IsAdmin(caller.Id))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "https://www.youtube.com/watch?v=YEwlW5sHQ4Q", cancellationToken: cancellationToken);
            return;
        }

        var target = context.Message.ReplyToMessage?.From;
        if (target is null)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Reply to a message from the admin you want to remove.", cancellationToken: cancellationToken);
            return;
        }

        var removed = chat.Admins.Remove(target.Id);
        if (removed)
        {
            await chats.SaveAsync(chat, cancellationToken);
        }

        await context.Bot.SendMessage(context.Message.Chat.Id,
            removed ? $"Removed {target.FirstName} as admin." : $"{target.FirstName} wasn't an admin.",
            cancellationToken: cancellationToken);
    }
}
