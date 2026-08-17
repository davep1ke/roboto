using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_settings admin menu (kick/abandon/timeout/throttle/score),
/// keyboard-ified (phase 8.7, per user feedback - matching /xyzzy_start's 8.6 treatment) rather than
/// the free-text-only version phase 8.4 shipped. Abandon/Kick/Score/Cancel are all button taps now
/// (XyzzySettingsCallbackHandler owns them, including the two-step "pick a player" keyboards Kick
/// and Score need). Timeout/Throttle (arbitrary hour values) and the final "what's their new score"
/// step stay free-text through ReplyRouter - no sensible keyboard for an arbitrary number, same
/// reasoning as /xyzzy_start's configure flow.
///
/// Every value set here ends by calling XyzzyRoundService.RemindIfActionPendingAsync for the admin
/// who ran this - added after user feedback that running /xyzzy_settings mid-round buried their own
/// still-outstanding "pick a card"/"pick a winner" prompt with no way to tell it was still waiting.
///
/// Resolves ReplyRouter/XyzzyRoundService lazily via IServiceProvider rather than as constructor
/// dependencies - see the warning in ReplyRouter's own doc comment for why a direct ReplyRouter
/// dependency here would be circular (XyzzyRoundService itself is safe as a constructor dependency,
/// but is resolved the same way here for consistency with the ReplyRouter lookups sitting right
/// next to it).
/// </summary>
public sealed class XyzzySettingsCommand(IServiceProvider services, XyzzyGameRepository games, ChatRepository chats) : IReplyHandler
{
    public const string AwaitTimeout = "timeout";
    public const string AwaitThrottle = "throttle";
    public const string AwaitScorePoints = "score-points";

    public string Name => "xyzzy_settings";
    public string Description => "Admin menu for the Cards Against Humanity game in this chat (kick/abandon/timeout/throttle/score).";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var chat = await chats.GetAsync(chatId, cancellationToken);
        var caller = context.Message.From!;

        if (!chat.IsAdmin(caller.Id))
        {
            await context.Bot.SendMessage(chatId, "Only a chat admin can change game settings.", cancellationToken: cancellationToken);
            return;
        }

        var game = await games.GetAsync(chatId, cancellationToken);
        if (game.Status is XyzzyStatus.Stopped)
        {
            await context.Bot.SendMessage(chatId, "No game running here.", cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(
        [
            [InlineKeyboardButton.WithCallbackData("Abandon", $"xy:se:{chatId}:menu:abandon")],
            [InlineKeyboardButton.WithCallbackData("Timeout", $"xy:se:{chatId}:menu:timeout")],
            [InlineKeyboardButton.WithCallbackData("Throttle", $"xy:se:{chatId}:menu:throttle")],
            [InlineKeyboardButton.WithCallbackData("Kick", $"xy:se:{chatId}:menu:kick")],
            [InlineKeyboardButton.WithCallbackData("Score", $"xy:se:{chatId}:menu:score")],
            [InlineKeyboardButton.WithCallbackData("Cancel", $"xy:se:{chatId}:menu:cancel")],
        ]);

        try
        {
            await context.Bot.SendMessage(caller.Id,
                "Cards Against Humanity settings:", replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var game = await games.GetAsync(pending.TargetChatId, cancellationToken);
        var replies = services.GetRequiredService<ReplyRouter>();
        var rounds = services.GetRequiredService<XyzzyRoundService>();
        var text = reply.Text!.Trim();

        switch (pending.Step)
        {
            case AwaitTimeout:
                if (!double.TryParse(text, out var maxHours) || maxHours <= 0)
                {
                    await replies.AskAsync(bot, game.ChatId, pending.UserId, Name, AwaitTimeout, data: null,
                        "Not a valid number. How many hours should I wait before auto-advancing an answer/judging round?", cancellationToken);
                    return;
                }

                game.MaxWaitHours = maxHours;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId, $"Timeout set to {maxHours}h.", cancellationToken: cancellationToken);
                await rounds.RemindIfActionPendingAsync(bot, game, pending.UserId, cancellationToken);
                break;

            case AwaitThrottle:
                if (!double.TryParse(text, out var minHours) || minHours < 0)
                {
                    await replies.AskAsync(bot, game.ChatId, pending.UserId, Name, AwaitThrottle, data: null,
                        "Not a valid number. Minimum hours between rounds (throttle)? Enter 0 for none.", cancellationToken);
                    return;
                }

                game.MinWaitHours = minHours;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId, $"Throttle set to {minHours}h.", cancellationToken: cancellationToken);
                await rounds.RemindIfActionPendingAsync(bot, game, pending.UserId, cancellationToken);
                break;

            case AwaitScorePoints:
                if (!int.TryParse(text, out var points))
                {
                    await replies.AskAsync(bot, game.ChatId, pending.UserId, Name, AwaitScorePoints, data: pending.Data,
                        "Not a valid number. What should their new score be?", cancellationToken);
                    return;
                }

                var target = long.TryParse(pending.Data, out var targetId) ? game.FindPlayer(targetId) : null;
                if (target is null)
                {
                    await bot.SendMessage(pending.UserId, "That player isn't in the game any more.", cancellationToken: cancellationToken);
                    break;
                }

                target.Wins = points;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId, $"{target.DisplayName}'s score is now {points}.", cancellationToken: cancellationToken);
                await rounds.RemindIfActionPendingAsync(bot, game, pending.UserId, cancellationToken);
                break;
        }
    }
}
