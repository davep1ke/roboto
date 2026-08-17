using Roboto.Bot.Chats;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// Ports legacy mod_standard's /addadmin, deliberately re-designed rather than carried over as-is:
/// legacy asked "who?" via a keyboard built from presence-tracked recent chat members, which needs
/// a whole presence-tracking subsystem this codebase doesn't have yet and wasn't worth pulling in
/// just for this. Uses Telegram's standard "reply to their message" pattern instead - no
/// conversational flow needed at all, no presence data needed, and arguably more idiomatic for
/// group-management bots. Same bootstrap special case as legacy though: with no admins yet, a
/// bare (non-reply) /addadmin makes the caller the first admin.
/// </summary>
public sealed class AddAdminCommand(ChatRepository chats) : IBotCommand
{
    public string Name => "addadmin";
    public string Description => "Makes the person you reply to a chat admin (or, if there are no admins yet, makes you one).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await chats.GetAsync(context.Message.Chat.Id, cancellationToken);
        var caller = context.Message.From!;

        if (!chat.IsAdmin(caller.Id))
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "https://www.youtube.com/watch?v=YEwlW5sHQ4Q", cancellationToken: cancellationToken);
            return;
        }

        var target = context.Message.ReplyToMessage?.From;
        if (target is null && chat.Admins.Count == 0)
        {
            target = caller;
        }

        if (target is null)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "Reply to a message from the person you want to make admin.", cancellationToken: cancellationToken);
            return;
        }

        if (!chat.Admins.Contains(target.Id))
        {
            chat.Admins.Add(target.Id);
            await chats.SaveAsync(chat, cancellationToken);
        }

        await context.Bot.SendMessage(context.Message.Chat.Id,
            $"Added {target.FirstName} as admin.", cancellationToken: cancellationToken);
    }
}
