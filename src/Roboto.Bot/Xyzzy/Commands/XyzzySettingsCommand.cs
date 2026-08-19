using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Chats;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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
/// The menu itself goes through DmOutbox (phase 8.9) like everything else that DMs a player - if the
/// admin already has something else outstanding (a card to play in another game, say), the menu
/// simply won't appear until they've cleared it. That structurally replaces phase 8.7's
/// RemindIfActionPendingAsync nudge: a still-pending game question can no longer get buried by
/// opening this menu in the first place, since the menu can't even be shown until the question's
/// answered.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class XyzzySettingsCommand(IServiceProvider services, XyzzyGameRepository games, ChatRepository chats, DmOutbox outbox) : IReplyHandler
{
    public const string AwaitTimeout = "timeout";
    public const string AwaitThrottle = "throttle";
    public const string AwaitScorePoints = "score-points";
    public const string AwaitQuestionLimit = "question-limit";

    public string Name => "xyzzy_settings";
    public string Description => "Admin menu for the Cards Against Humanity game in this chat.";

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
        if (game.Status is XyzzyStatus.Stopped && game.Players.Count == 0)
        {
            await context.Bot.SendMessage(chatId, "No game running here.", cancellationToken: cancellationToken);
            return;
        }

        List<List<DmButton>> keyboard =
        [
            [new DmButton("Abandon", $"xy:se:{chatId}:menu:abandon")],
            [new DmButton("Timeout", $"xy:se:{chatId}:menu:timeout")],
            [new DmButton("Throttle", $"xy:se:{chatId}:menu:throttle")],
            [new DmButton("Kick", $"xy:se:{chatId}:menu:kick")],
            [new DmButton("Score", $"xy:se:{chatId}:menu:score")],
            [new DmButton("Reset Scores", $"xy:se:{chatId}:menu:reset")],
            [new DmButton("Game Length", $"xy:se:{chatId}:menu:gamelength")],
            [new DmButton("Re-deal", $"xy:se:{chatId}:menu:redeal")],
            [new DmButton("Extend", $"xy:se:{chatId}:menu:extend")],
            [new DmButton("Force Question", $"xy:se:{chatId}:menu:force")],
            [new DmButton("Change Packs", $"xy:se:{chatId}:menu:packs")],
            [new DmButton("Cancel", $"xy:se:{chatId}:menu:cancel")],
        ];

        var asked = await outbox.EnqueueButtonQuestionAsync(context.Bot, caller.Id, "Cards Against Humanity settings:", keyboard, cancellationToken);
        if (!asked)
        {
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first.", cancellationToken: cancellationToken);
        }
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var game = await games.GetAsync(pending.TargetChatId, cancellationToken);
        var replies = services.GetRequiredService<ReplyRouter>();
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
                break;

            case AwaitQuestionLimit:
                if (!int.TryParse(text, out var limit) || limit < -1)
                {
                    await replies.AskAsync(bot, game.ChatId, pending.UserId, Name, AwaitQuestionLimit, data: null,
                        "Not a valid number. How many questions should the round last for? Enter a number, or -1 for unlimited.", cancellationToken);
                    return;
                }

                game.QuestionLimit = limit;
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(pending.UserId,
                    $"Game length set to {(limit == -1 ? "unlimited" : $"{limit} questions")}.", cancellationToken: cancellationToken);
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
                break;
        }
    }
}
