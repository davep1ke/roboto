namespace Roboto.Bot.Commands;

/// <summary>
/// Replaces the legacy ExpectedReply/parseExpectedReplies mechanism (a single global List,
/// linearly scanned, matched by chat/user id + reply-to-message-id, keyed by an opaque
/// `messageData` string per handler). This version: one persisted-by-userId JSON blob per user
/// (ReplyRouter owns the key scheme), so a conversation survives a restart the same way everything
/// else in IStateStore does.
///
/// A user can have **several** of these outstanding at once (phase 9, per user feedback,
/// 2026-08-17: "I need to be able to handle this... not uncommon for users to be in multiple
/// groups" - e.g. two different mod_xyzzy games' settings flows, or a settings flow in one chat
/// while /setquiethours is outstanding in another) - ReplyRouter stores a list per user, not one
/// slot, and disambiguates by requiring the DM reply to be a Telegram "reply" to the specific
/// QuestionMessageId below whenever more than one is outstanding (matches legacy's own
/// reply-to-message-id matching). With exactly one outstanding, a plain (non-reply) text message
/// still works, same as before this changed - only ambiguous cases require an explicit reply-to.
///
/// Card answering/judging (mod_xyzzy's round-play) never uses this mechanism at all - it's
/// inline-keyboard/CallbackQuery-based (XyzzyCallbackData encodes the chat+round+card directly in
/// each button), so it was never subject to this limitation in the first place, before or after.
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

    /// <summary>The bot's own question message ID (the DM AskAsync just sent) - lets an incoming
    /// reply be matched unambiguously via Telegram's native "reply to this message" feature when
    /// more than one of these is outstanding for the same user.</summary>
    public int QuestionMessageId { get; set; }
}
