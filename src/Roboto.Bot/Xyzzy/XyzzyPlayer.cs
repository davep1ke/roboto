namespace Roboto.Bot.Xyzzy;

public sealed class XyzzyPlayer
{
    public long PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
    public List<string> Hand { get; set; } = [];
    public int Wins { get; set; }

    /// <summary>Auto-filled slot (XyzzyRoundService.FillBotSlots), not a real Telegram user - gets
    /// a normal hand like anyone else but answers/judges by picking randomly, with no DM ever sent
    /// (PlayerId is a synthetic negative value, not a real chat to message). See MIGRATION.md for
    /// why: manual testing kept needing "force" to start with too few real players.</summary>
    public bool IsBot { get; set; }
}
