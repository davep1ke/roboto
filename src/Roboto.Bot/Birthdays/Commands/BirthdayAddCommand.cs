using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Birthdays.Commands;

/// <summary>
/// Ports legacy mod_birthdays' /birthday_add - a 2-step DM flow (name, then date), matching phase
/// 9's design call: every multi-step flow in this codebase goes through DmOutbox/ReplyRouter, not
/// a group-posted question the way legacy asked it.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class BirthdayAddCommand(IServiceProvider services, BirthdaysRepository birthdays) : IReplyHandler
{
    private const string AwaitName = "await-name";
    private const string AwaitDate = "await-date";

    public string Name => "birthday_add";
    public string Description => "Adds a birthday reminder for this chat (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, AwaitName, data: null,
            "Whose birthday do you want to add?", cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        if (pending.Step == AwaitName)
        {
            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitDate, data: reply.Text!.Trim(),
                "What is their Birthday? (DD-MON-YYYY format, e.g. 01-JAN-1900)", cancellationToken);
            return;
        }

        var personName = pending.Data!;
        if (!DateTime.TryParse(reply.Text, out var birthday))
        {
            await bot.SendMessage(pending.TargetChatId, "Failed to add birthday - couldn't understand that date.", cancellationToken: cancellationToken);
            return;
        }

        var chat = await birthdays.GetAsync(pending.TargetChatId, cancellationToken);
        chat.Birthdays.Add(new BirthdayEntry { Name = personName, Birthday = birthday });
        await birthdays.SaveAsync(chat, cancellationToken);

        await bot.SendMessage(pending.TargetChatId, $"Added {personName}'s Birthday ({birthday:yyyy-MM-dd})", cancellationToken: cancellationToken);
    }
}
