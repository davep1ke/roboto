using Roboto.Bot.Persistence;
using Telegram.Bot;

namespace Roboto.Bot.Commands;

public sealed class StatsCommand(IStateStore store) : IBotCommand
{
    public string Name => "stats";
    public string Description => "Shows how many times each command has been used.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var counts = await store.LoadAsync<Dictionary<string, int>>(CommandRouter.UsageStatsKey, cancellationToken)
                     ?? new Dictionary<string, int>();

        var text = counts.Count == 0
            ? "No commands used yet."
            : "Command usage:\n" + string.Join('\n',
                counts.OrderByDescending(kv => kv.Value).Select(kv => $"/{kv.Key}: {kv.Value}"));

        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
