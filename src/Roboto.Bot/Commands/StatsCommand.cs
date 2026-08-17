using Roboto.Bot.Persistence;
using Roboto.Bot.Stats;
using Telegram.Bot;

namespace Roboto.Bot.Commands;

public sealed class StatsCommand(IStateStore store, AppClock clock, StatsRecorder stats) : IBotCommand
{
    public string Name => "stats";
    public string Description => "Shows uptime, command usage, and recorded stats.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var counts = await store.LoadAsync<Dictionary<string, int>>(CommandRouter.UsageStatsKey, cancellationToken)
                     ?? new Dictionary<string, int>();

        var uptime = DateTime.UtcNow - clock.StartedUtc;
        var usageText = counts.Count == 0
            ? "No commands used yet."
            : string.Join('\n', counts.OrderByDescending(kv => kv.Value).Select(kv => $"/{kv.Key}: {kv.Value}"));

        var series = await stats.GetAllAsync(cancellationToken);
        var statsText = series.Count == 0
            ? "No stats recorded yet."
            : string.Join('\n', series.OrderBy(s => s.Name).Select(s => $"{s.Name}: {s.Total:0.##}"));

        var text = $"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n\nCommand usage:\n{usageText}\n\nStats:\n{statsText}";

        await context.Bot.SendMessage(context.Message.Chat.Id, text, cancellationToken: cancellationToken);
    }
}
