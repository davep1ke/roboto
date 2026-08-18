using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Quotes.Commands;

/// <summary>
/// Ports legacy mod_quote's /quote_config - DM button menu ("Set Duration" / "Toggle automatic
/// quotes"), same shape as XyzzySettingsCommand's menu. QuoteConfigCallbackHandler owns the button
/// taps; this class also handles the one free-text follow-up ("Set Duration" asks for a number).
///
/// BuildKeyboard/BuildStatusText are shared statics rather than private, so
/// QuoteConfigCallbackHandler can re-offer the identical menu if a tap comes in with an
/// unrecognised choice - see XyzzyStartCommand.BuildChoiceKeyboard for why that matters (the
/// router already removed the tapped keyboard as the resolved head before the handler runs, so an
/// unhandled choice needs a real replacement, not just an error toast, or the flow gets stuck).
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class QuoteConfigCommand(IServiceProvider services, QuotesRepository quotes, DmOutbox outbox) : IReplyHandler
{
    public const string AwaitDuration = "await-duration";

    public string Name => "quote_config";
    public string Description => "Configures how often to post quotes automatically (asks over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id, "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var chat = await quotes.GetAsync(chatId, cancellationToken);
        var caller = context.Message.From!;

        var asked = await outbox.EnqueueButtonQuestionAsync(context.Bot, caller.Id, BuildStatusText(chat), BuildKeyboard(chatId), cancellationToken);
        if (!asked)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var text = reply.Text!.Trim();

        if (!int.TryParse(text, out var hours) || hours < -1)
        {
            if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                await bot.SendMessage(pending.UserId, "Cancelled.", cancellationToken: cancellationToken);
                return;
            }

            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitDuration, data: null,
                "Not a number. How many hours between updates, or 'Cancel' to cancel", cancellationToken);
            return;
        }

        var chat = await quotes.GetAsync(pending.TargetChatId, cancellationToken);
        chat.AutoQuoteHours = hours;
        await quotes.SaveAsync(chat, cancellationToken);
        await bot.SendMessage(pending.TargetChatId, $"Quote schedule set to every {hours} hours.", cancellationToken: cancellationToken);
    }

    public static List<List<DmButton>> BuildKeyboard(long chatId) =>
    [
        [new DmButton("Set Duration", $"quote:cfg:{chatId}:duration")],
        [new DmButton("Toggle automatic quotes", $"quote:cfg:{chatId}:toggle")],
    ];

    public static string BuildStatusText(QuoteChatState chat) =>
        $"Quotes are currently {(chat.AutoQuoteEnabled ? "enabled" : "disabled")} and set to announce every {chat.AutoQuoteHours} hours";
}
