namespace Roboto.Bot.Steam;

/// <summary>A Steam profile being tracked for achievement announcements in one chat.</summary>
public sealed class SteamPlayer
{
    public string SteamId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public List<SteamChiev> Chievs { get; set; } = [];
}
