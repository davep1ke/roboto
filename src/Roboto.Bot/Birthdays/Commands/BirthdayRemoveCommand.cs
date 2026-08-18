using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Birthdays.Commands;

/// <summary>Ports legacy mod_birthdays' /birthday_remove - single DM question, same shape as
/// BirthdayAddCommand, see its doc comment.</summary>
public sealed class BirthdayRemoveCommand(IServiceProvider services, BirthdaysRepository birthdays) : IReplyHandler
{
    public string Name => "birthday_remove";
    public string Description => "Removes a birthday reminder for this chat (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, "await-name", data: null,
            "Whose birthday do you want to remove?", cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var personName = reply.Text!.Trim();
        var chat = await birthdays.GetAsync(pending.TargetChatId, cancellationToken);
        var removed = chat.Birthdays.RemoveAll(b => b.Name == personName) > 0;
        if (removed)
        {
            await birthdays.SaveAsync(chat, cancellationToken);
        }

        await bot.SendMessage(pending.TargetChatId,
            $"Removed birthday for {personName} " + (removed ? "successfully" : "but fell on my ass"),
            cancellationToken: cancellationToken);
    }
}
