using Roboto.Bot.Commands;

namespace Roboto.Bot.Stats;

/// <summary>Ports legacy mod_standard.getStats() - "N messages awaiting reply" (Roboto.Settings.
/// expectedReplies.Count()), the rewrite's equivalent being DmOutbox's own per-user queues. Ordered
/// last, matching legacy showing this after the more interesting per-module lines.</summary>
public sealed class StandardStatsProvider(DmOutbox outbox) : IModuleStatsProvider
{
    public string ModuleName => "mod_standard";
    public int Order => 100;

    public async Task<string> GetStatsAsync(CancellationToken cancellationToken)
    {
        var awaiting = await outbox.CountAwaitingReplyAsync(cancellationToken);
        return $"There are {awaiting} messages awaiting reply.";
    }
}
