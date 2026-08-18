using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Quotes.Commands;

/// <summary>
/// Ports legacy mod_quote's /quote_add - a 2-step DM flow (who, then what), matching phase 9's
/// design call: every multi-step flow goes through DmOutbox/ReplyRouter, not a group-posted
/// question the way legacy asked it. For a quote spanning several lines, see
/// QuoteConversationCommand (/quote_conv) instead.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class QuoteAddCommand(IServiceProvider services, QuotesRepository quotes) : IReplyHandler
{
    private const string AwaitWho = "await-who";
    private const string AwaitText = "await-text";

    public string Name => "quote_add";
    public string Description => "Adds a quote for this chat (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, AwaitWho, data: null,
            "Who is the quote by? Or enter 'cancel'", cancellationToken);

        if (!asked)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var text = reply.Text!.Trim();

        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(pending.UserId, "Cancelled adding a new quote", cancellationToken: cancellationToken);
            return;
        }

        if (pending.Step == AwaitWho)
        {
            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitText, data: text,
                $"What was the quote from {text}", cancellationToken);
            return;
        }

        var by = pending.Data!;
        var chat = await quotes.GetAsync(pending.TargetChatId, cancellationToken);
        chat.Quotes.Add(new Quote { Lines = [new QuoteLine { By = by, Text = text }] });
        await quotes.SaveAsync(chat, cancellationToken);
        await bot.SendMessage(pending.TargetChatId, $"Added {text} by {by} successfully", cancellationToken: cancellationToken);
    }
}
