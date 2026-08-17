using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Opt-in for commands that ask a follow-up question via ReplyRouter.AskAsync and need to receive
/// the answer. Extends IBotCommand rather than being a separate registration - Program.cs's
/// reflection discovery already registers anything assignable to IBotCommand, so an
/// IReplyHandler is picked up automatically as both.
/// </summary>
public interface IReplyHandler : IBotCommand
{
    Task HandleReplyAsync(ITelegramBotClient bot, PendingReply pending, Message reply, CancellationToken cancellationToken);
}
