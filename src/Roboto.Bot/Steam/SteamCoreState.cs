namespace Roboto.Bot.Steam;

/// <summary>Global (not per-chat) cache of every game's achievement schema seen so far across all
/// tracked players in every chat - mirrors legacy mod_steam_core_data's games cache.</summary>
public sealed class SteamCoreState
{
    public List<SteamGame> Games { get; set; } = [];

    public SteamGame? FindGame(string gameId) => Games.FirstOrDefault(g => g.GameId == gameId);

    /// <summary>Adds a game to the cache if it's not already known - returns whether it was newly
    /// added, matching legacy's tryAddGame.</summary>
    public bool TryAddGame(SteamGame game)
    {
        if (FindGame(game.GameId) is not null)
        {
            return false;
        }

        Games.Add(game);
        return true;
    }
}
