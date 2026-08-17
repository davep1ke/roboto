namespace Roboto.Bot.Chats;

/// <summary>
/// Core, module-agnostic chat state - the equivalent of the fields that lived directly on the
/// legacy `chat` class itself (chatID, chatTitle, muted), not any one module's chat data. A
/// module's own per-chat state (once we have one, e.g. a ported mod_xyzzy) gets its own separate
/// key/POCO via IStateStore, not a field bolted on here - keeps modules from having to know about
/// each other, unlike the legacy `chat.chatData` list of every module's data in one object.
/// </summary>
public sealed class ChatState
{
    public long ChatId { get; set; }
    public string? Title { get; set; }
    public bool Muted { get; set; }

    /// <summary>Admin list lives here (like Muted) rather than in a mod_standard-specific blob -
    /// legacy put it directly on the core `chat` class too, since admin status is a plausible thing
    /// for other modules to gate on later, not something mod_standard's own commands solely own.</summary>
    public List<long> Admins { get; set; } = [];

    /// <summary>Matches legacy `chat.isChatAdmin()`: if the chat has no admins yet, everyone counts
    /// as one - lets the first person to try become the first admin with no separate bootstrap step.</summary>
    public bool IsAdmin(long userId) => Admins.Count == 0 || Admins.Contains(userId);
}
