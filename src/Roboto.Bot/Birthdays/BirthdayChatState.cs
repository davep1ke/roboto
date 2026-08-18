namespace Roboto.Bot.Birthdays;

/// <summary>
/// Per-chat mod_birthdays state - own key/POCO via IStateStore, per ChatState's own doc comment
/// about module-owned per-chat data (same pattern XyzzyGameState follows).
/// </summary>
public sealed class BirthdayChatState
{
    public long ChatId { get; set; }
    public List<BirthdayEntry> Birthdays { get; set; } = [];

    /// <summary>Guards against announcing the same day's birthdays twice if the scheduler ticks
    /// more than once in a day - compared by date only, not time.</summary>
    public DateTime LastDayProcessed { get; set; } = DateTime.MinValue;
}
