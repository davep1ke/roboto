using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

public sealed record CommandContext(ITelegramBotClient Bot, Message Message, string[] Args);
