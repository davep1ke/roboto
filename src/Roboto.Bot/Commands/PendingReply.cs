namespace Roboto.Bot.Commands;

/// <summary>
/// The shape ReplyRouter hands an IReplyHandler once its free-text question has been answered.
/// Superseded the legacy ExpectedReply mechanism (a single global List, matched by chat/user id +
/// reply-to-message-id), then went through two more designs of its own before landing here: first
/// one-slot-per-user (nothing else could be outstanding), then several-slots-with-reply-to-
/// disambiguation (phase 8.8). Both got replaced by DmOutbox (phase 11, user's explicit design
/// call): only one thing - a button question, a text question, or a notice, from any game or
/// command - is ever visible/outstanding in a user's DM at a time; everything else queues and
/// waits its turn rather than being sent immediately and disambiguated after the fact. ReplyRouter
/// itself is now just a thin adapter over DmOutbox that preserves this exact shape for handlers.
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
