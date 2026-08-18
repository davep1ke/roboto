namespace Roboto.Bot.Steam;

/// <summary>Records that a tracked player has earned a specific achievement in a specific game -
/// the fact of having it, not the achievement's own description (see SteamAchievementSchema).</summary>
public sealed class SteamChiev
{
    public string ChievCode { get; set; } = "";
    public string AppId { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
