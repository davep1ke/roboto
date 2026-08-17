namespace Roboto.Bot.Commands;

/// <summary>
/// Replaces the legacy ExpectedReply/parseExpectedReplies mechanism (a single global List,
/// linearly scanned, matched by chat/user id + reply-to-message-id, keyed by an opaque
/// `messageData` string per handler). This version: one persisted-by-userId JSON blob per user
/// (ReplyRouter owns the key scheme), so a conversation survives a restart the same way everything
/// else in IStateStore does.
///
/// Deliberately simplified vs. legacy: at most **one** pending reply per user at a time, not a
/// full FIFO queue of several outstanding questions across different modules/chats. Legacy's
/// ExpectedReply was explicitly built to let one user hold several of these at once - e.g. being in
/// two different mod_xyzzy games at the same time, or a settings flow in one chat while a game
/// question is outstanding in another - without one clobbering the other (user's own framing,
/// 2026-08-17: "the whole 'expected replies' thing was so a user could be in multiple groups/games
/// at the same time, and they wouldn't all overlap").
///
/// Not yet hit for real here: card answering/judging (mod_xyzzy's round-play) deliberately avoids
/// this mechanism entirely - it's inline-keyboard/CallbackQuery-based (XyzzyCallbackData encodes the
/// chat+round+card directly in each button), so those never contend for this one slot no matter how
/// many games a player is in. Only free-text follow-ups still route through here (e.g.
/// /xyzzy_settings' timeout/throttle/score-points, /setquiethours) - a real collision needs two of
/// *those* outstanding at once for the same user. If/when that happens, this is the point to build
/// genuine per-context queueing rather than one shared slot - don't build it speculatively before
/// then.
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
