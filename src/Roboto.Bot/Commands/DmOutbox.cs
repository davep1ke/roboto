using Microsoft.Extensions.Logging;
using Roboto.Bot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Roboto.Bot.Commands;

/// <summary>
/// Every DM the bot sends a user that's part of a conversation - a question expecting a button tap
/// or a typed reply, or a plain notice - goes through here instead of being sent directly. User's
/// explicit design call (2026-08-18), replacing the reply-to-disambiguation approach ReplyRouter
/// used before this: with several games/flows able to want a player's attention at once, sending
/// everything immediately meant a still-unanswered question could scroll off screen and be
/// forgotten, or a user could genuinely lose track of which reply applied to which game. Now:
/// only one thing is ever visible and outstanding in a user's DM at a time - everything else, from
/// any game or command, waits in an ordered per-user queue and gets delivered once the current
/// thing is resolved.
///
/// Deliberately doesn't distinguish "kind of question" beyond ExpectsResponse - a keyboard tap and
/// a typed reply are the same thing from the user's perspective (both "the bot is waiting on me"),
/// which is exactly the framing that was missing before this.
///
/// Reachability: if a user's queue is empty, a new entry is sent immediately and its real
/// send-or-fail result is reported back (matching the old "DM failed, tell the group" contract
/// exactly). If the queue is non-empty, the existing head was already successfully delivered at
/// some point, which already proves this user is reachable - so a new entry is just appended and
/// optimistically reported as queued (true), with no fresh reachability check needed. If a queued
/// entry's turn eventually comes and the send fails anyway (e.g. they blocked the bot in the
/// meantime), it's dropped rather than left stuck blocking everything behind it - see PumpNextAsync.
/// </summary>
public sealed class DmOutbox(IStateStore store, ILogger<DmOutbox> logger)
{
    /// <summary>Tracks, per user, the insertion point for entries enqueued while a router is
    /// mid-resolution (between RemoveCurrentHeadAsync and PumpNextAsync) - see AddAsync. In-memory
    /// only: the window it describes never outlives a single request.</summary>
    private readonly Dictionary<long, int> _resolvingInsertIndex = [];


    public Task<bool> EnqueueNoticeAsync(ITelegramBotClient bot, long userId, string text, CancellationToken cancellationToken) =>
        AddAsync(bot, userId, new DmOutboxEntry { Text = text, ExpectsResponse = false }, cancellationToken);

    public Task<bool> EnqueueButtonQuestionAsync(
        ITelegramBotClient bot, long userId, string text, List<List<DmButton>> keyboard, CancellationToken cancellationToken) =>
        AddAsync(bot, userId, new DmOutboxEntry { Text = text, Keyboard = keyboard, ExpectsResponse = true }, cancellationToken);

    public Task<bool> EnqueueTextQuestionAsync(
        ITelegramBotClient bot, long userId, long targetChatId, string handlerCommand, string step, string? data, string text,
        CancellationToken cancellationToken) =>
        AddAsync(bot, userId, new DmOutboxEntry
        {
            Text = text,
            ExpectsResponse = true,
            TargetChatId = targetChatId,
            HandlerCommand = handlerCommand,
            Step = step,
            Data = data,
        }, cancellationToken);

    /// <summary>Whether callbackMessageId is the currently-blocking button question for this user -
    /// callers should treat a false result as "that button isn't valid any more" and not dispatch
    /// to any handler at all.</summary>
    public async Task<bool> IsCurrentHeadAsync(long userId, int callbackMessageId, CancellationToken cancellationToken)
    {
        var queue = await LoadAsync(userId, cancellationToken);
        return queue.Count > 0 && queue[0].ExpectsResponse && queue[0].Keyboard is not null && queue[0].DeliveredMessageId == callbackMessageId;
    }

    /// <summary>The current head, if it's a free-text question - null otherwise (nothing pending,
    /// or the head is a button question instead). replyToMessageId, if the incoming message was an
    /// explicit Telegram reply, must match the head's delivered message when both are present -
    /// an explicit reply to something else is deliberately not treated as an answer, even though
    /// there's only ever one thing to answer at a time.</summary>
    public async Task<DmOutboxEntry?> TryGetHeadTextQuestionAsync(long userId, int? replyToMessageId, CancellationToken cancellationToken)
    {
        var queue = await LoadAsync(userId, cancellationToken);
        if (queue.Count == 0)
        {
            return null;
        }

        var head = queue[0];
        if (!head.ExpectsResponse || head.Keyboard is not null)
        {
            return null;
        }

        if (replyToMessageId is not null && head.DeliveredMessageId is not null && replyToMessageId != head.DeliveredMessageId)
        {
            return null;
        }

        return head;
    }

    /// <summary>Removes the current head (the user just answered/tapped it) - deliberately doesn't
    /// pump the next item yet. Callers (ReplyRouter, CallbackQueryRouter) remove the head *before*
    /// invoking the answering handler, same reasoning as always (avoid reprocessing on throw, let
    /// the handler ask a fresh follow-up without it looking stale) - and that handler may itself
    /// send its own immediate follow-ups (a confirmation, the next step of its own flow) into the
    /// now-empty queue. Only call PumpNextAsync once the handler is completely done, so anything
    /// *else* that was queued from an unrelated game surfaces after the current flow's own output,
    /// not interleaved before it.</summary>
    public async Task RemoveCurrentHeadAsync(long userId, CancellationToken cancellationToken)
    {
        var queue = await LoadAsync(userId, cancellationToken);
        if (queue.Count > 0)
        {
            queue.RemoveAt(0);
            await SaveAsync(userId, queue, cancellationToken);
        }

        // Opens the front-insertion window for this user - see AddAsync's use of this dictionary.
        _resolvingInsertIndex[userId] = 0;
    }

    /// <summary>Delivers as much of the queue as it currently can: every leading notice
    /// immediately (they don't block), stopping at (and delivering) the first not-yet-delivered
    /// question, which then blocks further delivery until it's resolved. A delivery failure (user
    /// went unreachable) drops that entry and moves on rather than leaving everything behind it
    /// stuck forever.</summary>
    public async Task PumpNextAsync(ITelegramBotClient bot, long userId, CancellationToken cancellationToken)
    {
        // Closes the front-insertion window opened by RemoveCurrentHeadAsync - anything enqueued
        // from here on is a genuinely new request, not a continuation of the flow that just resolved.
        _resolvingInsertIndex.Remove(userId);

        var queue = await LoadAsync(userId, cancellationToken);
        var changed = false;

        while (queue.Count > 0 && queue[0].DeliveredMessageId is null)
        {
            var head = queue[0];
            var sent = await TrySendAsync(bot, userId, head, cancellationToken);
            if (sent is null)
            {
                logger.LogWarning("Dropping undeliverable DM outbox entry for user {UserId} - they may have blocked the bot", userId);
                queue.RemoveAt(0);
                changed = true;
                continue;
            }

            if (!head.ExpectsResponse)
            {
                queue.RemoveAt(0);
                changed = true;
                continue;
            }

            head.DeliveredMessageId = sent.Id;
            changed = true;
            break;
        }

        if (changed)
        {
            await SaveAsync(userId, queue, cancellationToken);
        }
    }

    private async Task<bool> AddAsync(ITelegramBotClient bot, long userId, DmOutboxEntry entry, CancellationToken cancellationToken)
    {
        entry.QueuedUtc = DateTime.UtcNow;
        var queue = await LoadAsync(userId, cancellationToken);

        if (_resolvingInsertIndex.TryGetValue(userId, out var insertIndex))
        {
            // A router is actively resolving this user's just-answered head (we're running inside
            // that handler's own dispatch, before PumpNextAsync). This entry is that same flow's own
            // continuation - a re-prompt, the next step, a confirmation - and belongs immediately
            // ahead of anything a *different* game already queued behind the head that just resolved,
            // not behind it. Not sent here: the router's PumpNextAsync call right after delivers it
            // for real once the handler is done. Multiple entries in the same resolving window keep
            // their call order via the advancing index, rather than each pushing the last to the back.
            queue.Insert(insertIndex, entry);
            _resolvingInsertIndex[userId] = insertIndex + 1;
            await SaveAsync(userId, queue, cancellationToken);
            return true;
        }

        if (queue.Count > 0)
        {
            // Something's already blocking - its presence already proves this user is reachable,
            // so just take our place in line.
            queue.Add(entry);
            await SaveAsync(userId, queue, cancellationToken);
            return true;
        }

        var sent = await TrySendAsync(bot, userId, entry, cancellationToken);
        if (sent is null)
        {
            return false;
        }

        entry.DeliveredMessageId = sent.Id;
        if (entry.ExpectsResponse)
        {
            queue.Add(entry);
            await SaveAsync(userId, queue, cancellationToken);
        }
        // A notice that sent successfully with nothing ahead of it needs no tracking - it's
        // already resolved by definition (nothing to wait for).

        return true;
    }

    private static async Task<Message?> TrySendAsync(ITelegramBotClient bot, long userId, DmOutboxEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            ReplyMarkup? keyboard = entry.Keyboard is { } rows
                ? new InlineKeyboardMarkup(rows.Select(row => row.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData))))
                : null;
            return await bot.SendMessage(userId, entry.Text, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<List<DmOutboxEntry>> LoadAsync(long userId, CancellationToken cancellationToken) =>
        await store.LoadAsync<List<DmOutboxEntry>>(Key(userId), cancellationToken) ?? [];

    private Task SaveAsync(long userId, List<DmOutboxEntry> queue, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(userId), queue, cancellationToken);

    private static string Key(long userId) => $"dm-outbox:{userId}";
}
