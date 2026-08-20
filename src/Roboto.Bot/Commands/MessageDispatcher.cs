using Microsoft.Extensions.Logging;
using Roboto.Bot.Chats;
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
public sealed class MessageDispatcher(
    ILogger<MessageDispatcher> logger, ReplyRouter replyRouter, CommandRouter commandRouter,
    CallbackQueryRouter callbackRouter, ChatRepository chats)
{
    public async Task DispatchAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            logger.LogInformation("Callback query from {User}: {Data}",
                callbackQuery.From.Username ?? callbackQuery.From.FirstName, callbackQuery.Data);

            // Bumps whichever chat the tapped message lives in - almost always the caller's own DM
            // with the bot, not the group a game is running in (see ChatPurgeReconciler's own doc
            // comment on why that's fine: playing a game is itself group-chat activity through its
            // /xyzzy_* commands, which already touches the group chat separately).
            if (callbackQuery.Message is { } callbackMessage)
            {
                await chats.TouchAsync(callbackMessage.Chat.Id, cancellationToken);
            }

            await callbackRouter.HandleAsync(bot, callbackQuery, cancellationToken);
            return;
        }

        if (update.Message is not { Text: { } text } message)
        {
            return;
        }

        logger.LogInformation("Message from {User}: {Text}", message.From?.Username ?? message.From?.FirstName, text);
        await chats.TouchAsync(message.Chat.Id, cancellationToken, message.Chat.Title);

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
