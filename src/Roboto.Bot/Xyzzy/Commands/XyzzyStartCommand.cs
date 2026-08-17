using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_start, including the setup wizard (phase 8.5): "use defaults" vs
/// "configure" (question limit, timeout, throttle) over DM, same free-text-reply shape as
/// /xyzzy_settings rather than legacy's keyboard-driven chain. Pack-filter selection specifically
/// stays cut - v1 only has the one hardcoded pack (CardCatalog), nothing to filter yet.
///
/// The game exists (status SettingUp, starter already added as a player) for the whole setup
/// conversation, not just once it's finished - matches legacy adding the starter and setting a
/// setup status before the first question is even answered. If the starter has no open DM at all,
/// the whole thing is rolled back to Stopped rather than leaving a game stuck in SettingUp nobody
/// can ever finish.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class XyzzyStartCommand(IServiceProvider services, XyzzyGameRepository games) : IReplyHandler
{
    private const string ChooseSetup = "choose-setup";
    private const string AskQuestionLimit = "question-limit";
    private const string AskTimeout = "timeout";
    private const string AskThrottle = "throttle";

    public string Name => "xyzzy_start";
    public string Description => "Starts a new Cards Against Humanity game in this chat.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Message.Chat.Type is ChatType.Private)
        {
            await context.Bot.SendMessage(context.Message.Chat.Id,
                "This only applies to group chats.", cancellationToken: cancellationToken);
            return;
        }

        var chatId = context.Message.Chat.Id;
        var game = await games.GetAsync(chatId, cancellationToken);

        if (game.Status is not XyzzyStatus.Stopped)
        {
            await context.Bot.SendMessage(chatId,
                "A game's already in progress here. Use /xyzzy_join to join it or /xyzzy_status to see where it's at.",
                cancellationToken: cancellationToken);
            return;
        }

        var caller = context.Message.From!;
        game.Players = [new XyzzyPlayer { PlayerId = caller.Id, DisplayName = caller.FirstName }];
        game.Status = XyzzyStatus.SettingUp;
        game.QuestionLimit = -1;
        game.MaxWaitHours = 12;
        game.MinWaitHours = 0;
        game.StatusChangedUtc = DateTime.UtcNow;
        await games.SaveAsync(game, cancellationToken);

        var replies = services.GetRequiredService<ReplyRouter>();
        var asked = await replies.AskAsync(context.Bot, chatId, caller.Id, Name, ChooseSetup, data: null,
            "Start the game with default settings, or configure it first (round length/timeout/throttle)? " +
            "Reply \"defaults\", \"configure\", or \"cancel\".",
            cancellationToken);

        if (!asked)
        {
            await games.SaveAsync(new XyzzyGameState { ChatId = chatId }, cancellationToken);
            await context.Bot.SendMessage(chatId,
                $"{caller.FirstName} needs to open a private chat with me first to start a game.",
                cancellationToken: cancellationToken);
            return;
        }

        await context.Bot.SendMessage(chatId,
            $"{caller.FirstName} is starting a new game of Cards Against Humanity! Check your DMs to finish setup, " +
            "then use /xyzzy_join to play (need at least 3 players).",
            cancellationToken: cancellationToken);
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var game = await games.GetAsync(pending.TargetChatId, cancellationToken);
        var replies = services.GetRequiredService<ReplyRouter>();
        var text = reply.Text!.Trim();

        switch (pending.Step)
        {
            case ChooseSetup:
                await HandleChooseSetupAsync(bot, replies, game, pending.UserId, text, cancellationToken);
                break;
            case AskQuestionLimit:
                await HandleQuestionLimitAsync(bot, replies, game, pending.UserId, text, cancellationToken);
                break;
            case AskTimeout:
                await HandleTimeoutAsync(bot, replies, game, pending.UserId, text, cancellationToken);
                break;
            case AskThrottle:
                await HandleThrottleAsync(bot, replies, game, pending.UserId, text, cancellationToken);
                break;
        }
    }

    private async Task HandleChooseSetupAsync(ITelegramBotClient bot, ReplyRouter replies, XyzzyGameState game, long userId, string text, CancellationToken cancellationToken)
    {
        switch (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant())
        {
            case "cancel":
                game.Status = XyzzyStatus.Stopped;
                game.Players = [];
                await games.SaveAsync(game, cancellationToken);
                await bot.SendMessage(userId, "Cancelled.", cancellationToken: cancellationToken);
                await bot.SendMessage(game.ChatId, "Game setup cancelled.", cancellationToken: cancellationToken);
                break;

            case "defaults":
                await FinishSetupAsync(bot, game, userId, cancellationToken);
                break;

            case "configure":
                await replies.AskAsync(bot, game.ChatId, userId, Name, AskQuestionLimit, data: null,
                    "How many questions should the round last for? Enter a number, or -1 for unlimited.", cancellationToken);
                break;

            default:
                await replies.AskAsync(bot, game.ChatId, userId, Name, ChooseSetup, data: null,
                    "Not a valid answer. Reply \"defaults\", \"configure\", or \"cancel\".", cancellationToken);
                break;
        }
    }

    private async Task HandleQuestionLimitAsync(ITelegramBotClient bot, ReplyRouter replies, XyzzyGameState game, long userId, string text, CancellationToken cancellationToken)
    {
        if (!int.TryParse(text, out var limit) || limit < -1)
        {
            await replies.AskAsync(bot, game.ChatId, userId, Name, AskQuestionLimit, data: null,
                "Not a valid number. How many questions should the round last for? Enter a number, or -1 for unlimited.", cancellationToken);
            return;
        }

        game.QuestionLimit = limit;
        await games.SaveAsync(game, cancellationToken);
        await replies.AskAsync(bot, game.ChatId, userId, Name, AskTimeout, data: null,
            "How many hours should I wait for answers/judging before auto-advancing?", cancellationToken);
    }

    private async Task HandleTimeoutAsync(ITelegramBotClient bot, ReplyRouter replies, XyzzyGameState game, long userId, string text, CancellationToken cancellationToken)
    {
        if (!double.TryParse(text, out var hours) || hours <= 0)
        {
            await replies.AskAsync(bot, game.ChatId, userId, Name, AskTimeout, data: null,
                "Not a valid number. How many hours should I wait for answers/judging before auto-advancing?", cancellationToken);
            return;
        }

        game.MaxWaitHours = hours;
        await games.SaveAsync(game, cancellationToken);
        await replies.AskAsync(bot, game.ChatId, userId, Name, AskThrottle, data: null,
            "Minimum hours between rounds (throttle)? Enter 0 for none.", cancellationToken);
    }

    private async Task HandleThrottleAsync(ITelegramBotClient bot, ReplyRouter replies, XyzzyGameState game, long userId, string text, CancellationToken cancellationToken)
    {
        if (!double.TryParse(text, out var hours) || hours < 0)
        {
            await replies.AskAsync(bot, game.ChatId, userId, Name, AskThrottle, data: null,
                "Not a valid number. Minimum hours between rounds (throttle)? Enter 0 for none.", cancellationToken);
            return;
        }

        game.MinWaitHours = hours;
        await games.SaveAsync(game, cancellationToken);
        await FinishSetupAsync(bot, game, userId, cancellationToken);
    }

    private async Task FinishSetupAsync(ITelegramBotClient bot, XyzzyGameState game, long userId, CancellationToken cancellationToken)
    {
        game.Status = XyzzyStatus.Invites;
        game.StatusChangedUtc = DateTime.UtcNow;
        await games.SaveAsync(game, cancellationToken);

        await bot.SendMessage(userId, "Setup complete!", cancellationToken: cancellationToken);
        await bot.SendMessage(game.ChatId,
            "Setup's done! Use /xyzzy_join to play (need at least 3 players), then an admin can /xyzzy_begin.",
            cancellationToken: cancellationToken);
    }
}
