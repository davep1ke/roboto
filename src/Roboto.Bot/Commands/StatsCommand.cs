using Microsoft.Extensions.Options;
using Roboto.Bot.Chats;
using Roboto.Bot.Persistence;
using Roboto.Bot.Stats;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Roboto.Bot.Commands;

/// <summary>
/// Ports legacy mod_standard's /stats (Roboto/Modules/mod_standard.cs:206-223) - a hybrid of bot
/// identity/uptime/chat-count plus each module's own live-computed snapshot line (getStats()),
/// *not* a dump of the stats-engine's time-series registry (that's /statgraph). IModuleStatsProvider
/// is the rewrite's equivalent of getStats() overrides, reflection-discovered the same way
/// IBotCommand/ICallbackQueryHandler are.
/// </summary>
public sealed class StatsCommand(
    IOptions<BotOptions> options, AppClock clock, ChatRepository chats, IStateStore store, IEnumerable<IModuleStatsProvider> providers) : IBotCommand
{
    public string Name => "stats";
    public string Description => "Shows uptime, chat count, and per-module stats.";

    public async Task ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var uptime = DateTime.UtcNow - clock.StartedUtc;
        var chatCount = (await chats.GetAllAsync(cancellationToken)).Count;

        var lines = new List<string>
        {
            $"I is *@{options.Value.BotUsername}*",
            $"Uptime: {uptime.Days} days, {uptime.Hours} hours and {uptime.Minutes} minutes.",
            $"I currently know about {chatCount} chats.",
            "The following plugins are currently loaded:",
        };

        foreach (var provider in providers.OrderBy(p => p.Order))
        {
            lines.Add($"*{provider.ModuleName}*");
            lines.Add(await provider.GetStatsAsync(cancellationToken));
        }

        var counts = await store.LoadAsync<Dictionary<string, int>>(CommandRouter.UsageStatsKey, cancellationToken)
                     ?? new Dictionary<string, int>();
        if (counts.Count > 0)
        {
            lines.Add("Top commands:");
            lines.AddRange(counts.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"/{kv.Key}: {kv.Value}"));
        }

        await context.Bot.SendMessage(context.Message.Chat.Id, string.Join('\n', lines), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
    }
}
