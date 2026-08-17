using Roboto.Bot.Persistence;
using Telegram.Bot;

namespace Roboto.Bot.Commands;

public sealed class StatsCommand(IStateStore store, AppClock clock) : IBotCommand
{
    public string Name => "stats";
    public string Description => "Shows uptime and how many times each command has been used.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var counts = await store.LoadAsync<Dictionary<string, int>>(CommandRouter.UsageStatsKey, cancellationToken)
                     ?? new Dictionary<string, int>();

        var uptime = DateTime.UtcNow - clock.StartedUtc;
        var usageText = counts.Count == 0
            ? "No commands used yet."
            : string.Join('\n', counts.OrderByDescending(kv => kv.Value).Select(kv => $"/{kv.Key}: {kv.Value}"));

        var text = $"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n\nCommand usage:\n{usageText}";

        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
