namespace Roboto.Bot.Commands;

public sealed record DmButton(string Text, string CallbackData);

/// <summary>
/// One item in a user's DM outbox (see DmOutbox) - either a notice (no response needed), a
/// button question (Keyboard set), or a free-text question (HandlerCommand/Step/Data set, routed
/// back through ReplyRouter/IReplyHandler once answered).
/// </summary>
public sealed class DmOutboxEntry
{
    public string Text { get; set; } = "";
    public List<List<DmButton>>? Keyboard { get; set; }

    /// <summary>True if this entry expects the user to do something (tap a button or reply with
    /// text) before the next queued entry can be delivered. False for a pure notice, which is
    /// considered resolved the instant it's delivered.</summary>
    public bool ExpectsResponse { get; set; }

    // Only meaningful when ExpectsResponse is true and Keyboard is null (a free-text question) -
    // who handles the eventual reply. Mirrors PendingReply's old shape.
    public long TargetChatId { get; set; }
    public string HandlerCommand { get; set; } = "";
    public string Step { get; set; } = "";
    public string? Data { get; set; }

    /// <summary>Set once this entry has actually been sent - the real Telegram message ID, used to
    /// match an incoming button tap (and, defensively, an explicit reply-to) back to this specific
    /// entry rather than assuming any old button/text means this.</summary>
    public int? DeliveredMessageId { get; set; }

    public DateTime QueuedUtc { get; set; }
}
