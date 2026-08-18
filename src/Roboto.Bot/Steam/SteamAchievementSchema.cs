namespace Roboto.Bot.Steam;

/// <summary>One achievement a game defines - the description/display text, not any one player's
/// progress toward it (see SteamChiev for that).</summary>
public sealed class SteamAchievementSchema
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";

    public override string ToString() => $"*{DisplayName}* - {Description}";
}
