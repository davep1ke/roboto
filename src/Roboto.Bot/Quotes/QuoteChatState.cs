namespace Roboto.Bot.Quotes;

/// <summary>
/// Per-chat mod_quote state - own key/POCO via IStateStore, per ChatState's own doc comment about
/// module-owned per-chat data (same pattern XyzzyGameState/BirthdayChatState follow).
/// </summary>
public sealed class QuoteChatState
{
    public long ChatId { get; set; }
    public List<Quote> Quotes { get; set; } = [];
    public DateTime NextAutoQuoteAfter { get; set; } = DateTime.MinValue;
    public int AutoQuoteHours { get; set; } = 24;
    public bool AutoQuoteEnabled { get; set; } = true;
}
