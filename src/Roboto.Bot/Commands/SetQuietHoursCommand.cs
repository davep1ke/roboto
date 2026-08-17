using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Roboto.Bot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

public sealed record QuietHours(TimeSpan Start, TimeSpan End);

/// <summary>
/// Ports legacy mod_standard's /setquiethours: a two-step DM conversation (start time, then end
/// time), "cancel" aborts, "disable" clears any existing setting - same shape as the legacy
/// replyReceived's "setQuietHours"/"setWakeHours" messageData branches. Triggered from a group
/// (the setting applies to that chat) but the back-and-forth happens over DM, same as legacy.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
///
/// Logs each step transition explicitly (not just "message received" at the TelegramPollingService
/// level) - a multi-step conversation is otherwise very hard to verify from server logs alone,
/// since nothing else distinguishes "fresh /setquiethours" from "this text was actually an answer
/// to a pending question" or shows which branch (cancel/disable/invalid/advance/save) was taken.
/// </summary>
public sealed class SetQuietHoursCommand(IServiceProvider services, IStateStore store, ILogger<SetQuietHoursCommand> logger) : IReplyHandler
{
    private const string AwaitStart = "await-start";
    private const string AwaitEnd = "await-end";

    public string Name => "setquiethours";
    public string Description => "Sets quiet hours for this chat (asks the time over DM).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var replies = services.GetRequiredService<ReplyRouter>();
        var userId = context.Message.From!.Id;

        var asked = await replies.AskAsync(context.Bot, context.Message.Chat.Id, userId, Name, AwaitStart, data: null,
            "Enter the start time for quiet hours (hh:mm:ss), \"disable\", or \"cancel\".", cancellationToken);

        if (!asked)
        {
            logger.LogInformation("Couldn't start /setquiethours for user {UserId} in chat {ChatId} - no open DM", userId, context.Message.Chat.Id);
            await context.Bot.SendMessage(context.Message.Chat.Id,
                $"{context.Message.From.FirstName} needs to open a private chat with me first.",
                cancellationToken: cancellationToken);
            return;
        }

        logger.LogInformation("Asked user {UserId} for quiet-hours start time (chat {ChatId})", userId, context.Message.Chat.Id);
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var text = reply.Text!.Trim();

        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("User {UserId} cancelled /setquiethours at step {Step}", pending.UserId, pending.Step);
            await bot.SendMessage(pending.UserId, "Cancelled.", cancellationToken: cancellationToken);
            return;
        }

        if (text.Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("User {UserId} disabled quiet hours for chat {ChatId}", pending.UserId, pending.TargetChatId);
            await store.DeleteAsync(QuietHoursKey(pending.TargetChatId), cancellationToken);
            await bot.SendMessage(pending.UserId, "Quiet hours disabled.", cancellationToken: cancellationToken);
            return;
        }

        if (!TimeSpan.TryParse(text, out var time) || time <= TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            logger.LogInformation("User {UserId} sent an invalid quiet-hours value {Value} at step {Step}", pending.UserId, text, pending.Step);
            var prompt = pending.Step == AwaitStart
                ? "Invalid value. Enter the start time for quiet hours (hh:mm:ss), \"disable\", or \"cancel\"."
                : "Invalid value. Enter the end time for quiet hours (hh:mm:ss), \"disable\", or \"cancel\".";

            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, pending.Step, pending.Data, prompt, cancellationToken);
            return;
        }

        if (pending.Step == AwaitStart)
        {
            logger.LogInformation("User {UserId} set quiet-hours start to {Start}, asking for end time", pending.UserId, time);
            var replies = services.GetRequiredService<ReplyRouter>();
            await replies.AskAsync(bot, pending.TargetChatId, pending.UserId, Name, AwaitEnd, data: text,
                "Enter the end time for quiet hours (hh:mm:ss), \"disable\", or \"cancel\".", cancellationToken);
            return;
        }

        // AwaitEnd - pending.Data holds the already-validated start time from the previous step.
        var start = TimeSpan.Parse(pending.Data!);
        logger.LogInformation("User {UserId} set quiet hours for chat {ChatId}: {Start}-{End}", pending.UserId, pending.TargetChatId, start, time);
        await store.SaveAsync(QuietHoursKey(pending.TargetChatId), new QuietHours(start, time), cancellationToken);
        await bot.SendMessage(pending.UserId, $"Quiet hours set from {start:c} to {time:c}.", cancellationToken: cancellationToken);
    }

    private static string QuietHoursKey(long chatId) => $"chat:{chatId}:quiet-hours";
}
