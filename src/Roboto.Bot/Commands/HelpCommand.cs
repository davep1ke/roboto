using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Roboto.Bot.Commands;

/// <summary>
/// Resolves CommandRouter lazily via IServiceProvider rather than taking it as a constructor
/// dependency - CommandRouter needs every IBotCommand built first (including this one), so a
/// direct constructor dependency here would be circular.
/// </summary>
public sealed class HelpCommand(IServiceProvider services) : IBotCommand
{
    public string Name => "help";
    public string Description => "Lists available commands.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var router = services.GetRequiredService<CommandRouter>();

        var lines = router.Commands
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => $"/{c.Name} - {c.Description}");

        var text = "Available commands:\n" + string.Join('\n', lines);
        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
