using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Quotes.Commands;

/// <summary>
/// Ports legacy mod_quote's /quote_conv - an open-ended DM loop, repeatedly asking for
/// "Name\text" lines until "done" or "cancel". Unlike every other multi-step flow in this codebase
/// (a fixed number of steps), the number of lines isn't known up front, so there's no separate
/// step name per line - it just re-asks the same AwaitLine step, accumulating lines into
/// PendingReply.Data as it goes (same approach legacy took, flattening the growing list into a
/// single delimited string since ExpectedReply/PendingReply only carries one opaque string
/// forward between steps - just with a control character separator instead of legacy's
/// "&lt;&lt;#::#&gt;&gt;" marker text, which could theoretically collide with real quote content).
///
/// A malformed line (no backslash) cancels the whole flow rather than re-prompting - ported as-is
/// from legacy, which has the same behavior (no SendQuestion call on that path, so there's nothing
/// left to answer).
/// </summary>
public sealed class QuoteConversationCommand(IServiceProvider services, QuotesRepository quotes) : IReplyHandler
{
    private const char Separator = '\u001f'; // unit separator - won't appear in normal chat text
    private const string AwaitLine = "await-line";

    public string Name => "quote_conv";
    public string Description => "Adds a quote containing multiple lines (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;
        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, AwaitLine, data: null,
            "Enter the first speaker's name, a \\, then the text (e.g. Bob\\I like Bees).\nOr enter 'cancel' or 'done' to finish.",
            cancellationToken);

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

        if (text.Equals("done", StringComparison.OrdinalIgnoreCase))
        {
            var lines = Decode(pending.Data);
            if (lines.Count == 0)
            {
                await bot.SendMessage(pending.UserId, "Couldn't add quote - no lines to add?", cancellationToken: cancellationToken);
                return;
            }

            var quote = new Quote { Lines = lines };
            var chat = await quotes.GetAsync(pending.TargetChatId, cancellationToken);
            chat.Quotes.Add(quote);
            await quotes.SaveAsync(chat, cancellationToken);
            await bot.SendMessage(pending.TargetChatId, $"Added quote \n{quote.GetText()}", cancellationToken: cancellationToken);
            return;
        }

        var separatorPos = text.IndexOf('\\');
        if (separatorPos == -1)
        {
            await bot.SendMessage(pending.UserId,
                "Couldn't work out where the name and text were. Cancelled adding a new quote", cancellationToken: cancellationToken);
            return;
        }

        var by = text[..separatorPos];
        var lineText = text[(separatorPos + 1)..];

        var replies = services.GetRequiredService<ReplyRouter>();
        await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitLine, data: Encode(pending.Data, by, lineText),
            "Enter the next line, 'cancel' or 'done'", cancellationToken);
    }

    private static string Encode(string? existing, string by, string text)
    {
        var prefix = string.IsNullOrEmpty(existing) ? "" : existing + Separator;
        return prefix + by + Separator + text;
    }

    private static List<QuoteLine> Decode(string? data)
    {
        var lines = new List<QuoteLine>();
        if (string.IsNullOrEmpty(data))
        {
            return lines;
        }

        var tokens = data.Split(Separator);
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            lines.Add(new QuoteLine { By = tokens[i], Text = tokens[i + 1] });
        }

        return lines;
    }
}
