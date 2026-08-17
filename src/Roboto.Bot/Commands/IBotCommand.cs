namespace Roboto.Bot.Commands;

/// <summary>
/// One slash command. Implementations are discovered by reflection (harmless, same idea the
/// legacy module scan used) and resolved through DI - inject whatever a command needs instead of
/// reaching into static globals. One class = one command for now; a module that needs several
/// commands just registers several IBotCommand classes. Revisit if/when a bigger module (e.g. a
/// ported mod_standard) needs an explicit "these commands belong together" grouping.
/// </summary>
public interface IBotCommand
{
    /// <summary>Command name without the leading slash, e.g. "ping" for /ping.</summary>
    string Name { get; }

    /// <summary>One-line description, shown by /help.</summary>
    string Description { get; }

    Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
