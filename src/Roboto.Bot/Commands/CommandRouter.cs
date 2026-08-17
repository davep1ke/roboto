using Microsoft.Extensions.Logging;
using Roboto.Bot.Chats;
using Roboto.Bot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// Replaces the legacy per-module `if (text.StartsWith("/xyzzy_start"))` chains with a single
/// name-based lookup. No priority/collision flags needed - each command name maps to exactly one
/// handler.
/// </summary>
public sealed class CommandRouter
{
    public const string UsageStatsKey = "command-usage";

    // Always allowed through even in a muted group chat - matches the legacy mod_standard's own
    // per-command mute checks (chatIfMuted only got the *module* invoked at all; individual
    // commands like /stop and /start still worked regardless of mute state).
    private static readonly HashSet<string> AlwaysAllowedWhileMuted = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "stop",
    };

    private readonly Dictionary<string, IBotCommand> _commands;
    private readonly IStateStore _store;
    private readonly ChatRepository _chats;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IEnumerable<IBotCommand> commands, IStateStore store, ChatRepository chats, ILogger<CommandRouter> logger)
    {
        _store = store;
        _chats = chats;
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

        // Muting only applies to group chats - matches the legacy `chat` class not existing at all
        // for private chats.
        var isGroupChat = message.Chat.Type is ChatType.Group or ChatType.Supergroup;
        if (isGroupChat && !AlwaysAllowedWhileMuted.Contains(name))
        {
            var chat = await _chats.GetAsync(message.Chat.Id, cancellationToken);
            if (chat.Muted)
            {
                _logger.LogInformation("Ignoring /{Command} in muted chat {ChatId}", name, message.Chat.Id);
                return true;
            }
        }

        var args = parts.Skip(1).ToArray();
        var context = new CommandContext(bot, message, args);

        try
        {
            await command.ExecuteAsync(context, cancellationToken);
            await RecordUsageAsync(name, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command /{Command} threw", name);
        }

        return true;
    }

    /// <summary>
    /// Doubles as this project's first proof that SqliteStateStore actually persists across
    /// restarts, not just within a run - exposed via StatsCommand (/stats).
    /// </summary>
    private async Task RecordUsageAsync(string name, CancellationToken cancellationToken)
    {
        var counts = await _store.LoadAsync<Dictionary<string, int>>(UsageStatsKey, cancellationToken)
                     ?? new Dictionary<string, int>();

        counts[name] = counts.GetValueOrDefault(name) + 1;

        await _store.SaveAsync(UsageStatsKey, counts, cancellationToken);
    }
}
