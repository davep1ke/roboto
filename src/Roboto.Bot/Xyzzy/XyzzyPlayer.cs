namespace Roboto.Bot.Xyzzy;

public sealed class XyzzyPlayer
{
    public long PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
    public List<string> Hand { get; set; } = [];
    public int Wins { get; set; }
}
