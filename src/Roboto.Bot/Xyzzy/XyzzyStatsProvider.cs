using Roboto.Bot.Stats;

namespace Roboto.Bot.Xyzzy;

/// <summary>Ports legacy mod_xyzzy.getStats() (active players/games, packs+cards loaded), plus two
/// new all-time lines the user asked for - legacy never had a persisted lifetime total for
/// anything (its stats engine is a pure rolling window), so these counters only cover time since
/// the rewrite started recording them, not truly "all-time" - the "since" date makes that explicit
/// rather than implying a false continuity with the legacy bot's own history.</summary>
public sealed class XyzzyStatsProvider(XyzzyGameRepository games, StatsRecorder stats) : IModuleStatsProvider
{
    public string ModuleName => "mod_xyzzy";
    public int Order => 10;

    public async Task<string> GetStatsAsync(CancellationToken cancellationToken)
    {
        var active = await games.GetAllActiveAsync(cancellationToken);
        var activeGames = active.Count;
        var activePlayers = active.Sum(g => g.Players.Count(p => !p.IsBot));

        var packs = CardCatalog.Packs.Count;
        var cards = CardCatalog.Questions.Count + CardCatalog.Answers.Count;

        var lines = new List<string>
        {
            $"{activePlayers} players in {activeGames} active games",
            $"{packs} packs loaded containing {cards} cards",
        };

        var gamesStarted = await stats.GetAsync(XyzzyStatNames.GamesStarted, cancellationToken);
        var handsPlayed = await stats.GetAsync(XyzzyStatNames.HandsPlayed, cancellationToken);
        var since = gamesStarted?.FirstRecordedUtc ?? handsPlayed?.FirstRecordedUtc;
        if (since is { } sinceDate)
        {
            lines.Add($"{gamesStarted?.Total ?? 0:N0} games and {handsPlayed?.Total ?? 0:N0} hands played since {sinceDate:d MMM yyyy}");
        }

        return string.Join('\n', lines);
    }
}
