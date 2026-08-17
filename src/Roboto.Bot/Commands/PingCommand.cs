using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Roboto.Bot.Commands;

public sealed class PingCommand(ILogger<PingCommand> logger) : IBotCommand
{
    public string Name => "ping";
    public string Description => "Replies with pong - proves the bot is alive.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling /ping from {User}", context.Message.From?.Username);
        await context.Bot.SendMessage(context.Message.Chat.Id, "pong", cancellationToken: cancellationToken);
    }
}
