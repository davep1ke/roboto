namespace Roboto.Bot.Steam;

/// <summary>A game's cached achievement schema - shared across every chat/player tracking it, so
/// the schema only needs fetching once per game rather than once per player.</summary>
public sealed class SteamGame
{
    public string GameId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<SteamAchievementSchema> Achievements { get; set; } = [];

    public SteamAchievementSchema? FindAchievement(string code) => Achievements.FirstOrDefault(a => a.Code == code);
}
