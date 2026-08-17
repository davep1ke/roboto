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
}
