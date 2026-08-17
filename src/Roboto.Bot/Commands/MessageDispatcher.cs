using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// The actual "what do we do with an incoming message" logic, pulled out of
/// TelegramPollingService (a BackgroundService) so it's directly callable/testable without
/// spinning up the real long-poll loop - feed it a fake ITelegramBotClient and a synthetic
/// Update/Message and assert on what the fake recorded as "sent", no network or BackgroundService
/// machinery involved. See tests/Roboto.Bot.Tests.
/// </summary>
public sealed class MessageDispatcher(ILogger<MessageDispatcher> logger, ReplyRouter replyRouter, CommandRouter commandRouter)
{
    public async Task DispatchAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } text } message)
        {
            return;
        }

        logger.LogInformation("Message from {User}: {Text}", message.From?.Username ?? message.From?.FirstName, text);

        // Checked first: if this user has a pending question (e.g. mid-way through /setquiethours),
        // treat whatever they typed as the answer rather than trying to parse it as a fresh command
        // - matches the legacy app checking parseExpectedReplies before general command dispatch.
        if (await replyRouter.TryHandleAsync(bot, message, cancellationToken))
        {
            return;
        }

        await commandRouter.TryDispatchAsync(bot, message, cancellationToken);
    }
}
