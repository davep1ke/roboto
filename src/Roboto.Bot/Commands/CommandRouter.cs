using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// Replaces the legacy per-module `if (text.StartsWith("/xyzzy_start"))` chains with a single
/// name-based lookup. No priority/collision flags needed - each command name maps to exactly one
/// handler.
/// </summary>
public sealed class CommandRouter
{
    private readonly Dictionary<string, IBotCommand> _commands;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IEnumerable<IBotCommand> commands, ILogger<CommandRouter> logger)
    {
        _logger = logger;
        _commands = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        _logger.LogInformation("Registered commands: {Commands}", string.Join(", ", _commands.Keys));
    }

    public IReadOnlyCollection<IBotCommand> Commands => _commands.Values;

    public async Task<bool> TryDispatchAsync(ITelegramBotClient bot, Message message, CancellationToken cancellationToken)
    {
        if (message.Text is not { } text || !text.StartsWith('/'))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Group chats can have `/command@BotUsername` to disambiguate between multiple bots.
        var name = parts[0].TrimStart('/').Split('@')[0];

        if (!_commands.TryGetValue(name, out var command))
        {
            return false;
        }

        var args = parts.Skip(1).ToArray();
        var context = new CommandContext(bot, message, args);

        try
        {
            await command.ExecuteAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command /{Command} threw", name);
        }

        return true;
    }
}
