using Microsoft.Extensions.DependencyInjection;
using Roboto.Bot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Xyzzy.Commands;

/// <summary>
/// Ports legacy mod_xyzzy's /xyzzy_start, including the setup wizard (phase 8.5, keyboard-ified in
/// 8.6 per user feedback): the initial "use defaults / configure / cancel" choice is an inline
/// keyboard (XyzzySetupCallbackHandler owns the taps), matching legacy's own keyboard for that exact
/// decision. The three "configure" follow-ups (question limit/timeout/throttle) stay free-text DM
/// questions through ReplyRouter, same as legacy - those were always plain number prompts even in
/// the original app, not keyboard-driven. Pack-filter selection specifically stays cut - v1 only has
/// the one hardcoded pack (CardCatalog), nothing to filter yet.
///
/// The game exists (status SettingUp, starter already added as a player) for the whole setup
/// conversation, not just once it's finished - matches legacy adding the starter and setting a setup
/// status before the first question is even answered. If the starter has no open DM at all, the
/// whole thing is rolled back to Stopped rather than leaving a game stuck in SettingUp nobody can
/// ever finish.
///
/// Resolves ReplyRouter lazily via IServiceProvider rather than as a constructor dependency - see
/// the warning in ReplyRouter's own doc comment for why a direct dependency here would be circular.
/// </summary>
public sealed class XyzzyStartCommand(IServiceProvider services, XyzzyGameRepository games, XyzzyRoundService rounds) : IReplyHandler
{
    public const string AskQuestionLimit = "question-limit";
    public const string AskTimeout = "timeout";
    public const string AskThrottle = "throttle";

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

        var keyboard = new InlineKeyboardMarkup(
        [
            [InlineKeyboardButton.WithCallbackData("Use Defaults", $"xy:su:{chatId}:defaults")],
            [InlineKeyboardButton.WithCallbackData("Configure Game", $"xy:su:{chatId}:configure")],
            [InlineKeyboardButton.WithCallbackData("Cancel", $"xy:su:{chatId}:cancel")],
        ]);

        bool asked;
        try
        {
            await context.Bot.SendMessage(caller.Id,
                "Do you want to start the game with default settings, or set advanced options first?",
                replyMarkup: keyboard, cancellationToken: cancellationToken);
            asked = true;
        }
        catch (Exception)
        {
            asked = false;
        }

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
            "then use /xyzzy_join to play.",
            cancellationToken: cancellationToken);
    }

    public async Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken)
    {
        var game = await games.GetAsync(pending.TargetChatId, cancellationToken);
        var replies = services.GetRequiredService<ReplyRouter>();
        var text = reply.Text!.Trim();

        switch (pending.Step)
        {
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
        await rounds.FinishSetupAsync(bot, game, userId, cancellationToken);
    }
}
