using Roboto.Bot.Stats;

namespace Roboto.Bot.Steam;

/// <summary>Ports legacy mod_steam.getStats() - tracked-player count and known-achievement count.</summary>
public sealed class SteamStatsProvider(SteamRepository steam) : IModuleStatsProvider
{
    public string ModuleName => "mod_steam";
    public int Order => 20;

    public async Task<string> GetStatsAsync(CancellationToken cancellationToken)
    {
        var chats = await steam.GetAllChatsAsync(cancellationToken);
        var players = chats.Sum(c => c.Players.Count);
        var achievements = chats.Sum(c => c.Players.Sum(p => p.Chievs.Count));
        return $"Tracking {players} players\n{achievements} player achievements known";
    }
}
