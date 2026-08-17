namespace Roboto.Bot.Xyzzy;

/// <summary>Centralized stat-name constants so the various call sites recording mod_xyzzy events
/// (StatsRecorder) can't drift out of sync with typos.</summary>
internal static class XyzzyStatNames
{
    public const string GamesStarted = "xyzzy.games-started";
    public const string GamesEnded = "xyzzy.games-ended";
    public const string HandsPlayed = "xyzzy.hands-played";
    public const string ActiveGames = "xyzzy.active-games";
    public const string ActivePlayers = "xyzzy.active-players";
}
