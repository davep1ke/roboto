namespace Roboto.Bot.Commands;

/// <summary>
/// Replaces the legacy ExpectedReply/parseExpectedReplies mechanism (a single global List,
/// linearly scanned, matched by chat/user id + reply-to-message-id, keyed by an opaque
/// `messageData` string per handler). This version: one persisted-by-userId JSON blob per user
/// (ReplyRouter owns the key scheme), so a conversation survives a restart the same way everything
/// else in IStateStore does.
///
/// Deliberately simplified vs. legacy: at most **one** pending reply per user at a time, not a
/// full FIFO queue of several outstanding questions across different modules. Nothing needs more
/// than that yet (this only exists to support /setquiethours' two-step flow so far). If a second
/// real conversational flow needs genuine queueing later, that's the point to revisit - don't
/// build it speculatively now.
/// </summary>
public sealed class PendingReply
{
    /// <summary>The chat this conversation's answer applies to - not necessarily where the Q&amp;A
    /// itself is happening (e.g. /setquiethours is triggered in a group but answered over DM;
    /// TargetChatId is the group, the DM itself is just UserId's own private chat).</summary>
    public long TargetChatId { get; set; }

    public long UserId { get; set; }

    /// <summary>IBotCommand.Name of the handler that asked - ReplyRouter looks up the same command
    /// instance to deliver the answer to (a command must also implement IReplyHandler).</summary>
    public string HandlerCommand { get; set; } = "";

    /// <summary>Opaque, handler-owned state label, e.g. "await-start" / "await-end".</summary>
    public string Step { get; set; } = "";

    /// <summary>Opaque, handler-owned data carried forward between steps.</summary>
    public string? Data { get; set; }

    public DateTime AskedUtc { get; set; }
}
