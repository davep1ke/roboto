using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RobotoChatBot
{
    /// <summary>
    /// Per-key mutual exclusion, keyed by chat/user ID. Exists because of phase 4's real periodic
    /// background scheduler thread: legacy was safe with zero locking anywhere because it was
    /// structurally single-threaded (the message-poll loop, then background processing only ever
    /// ran after that loop had already exited - see MIGRATION.md's phase 4 notes for the actual
    /// trace confirming this). A second thread now genuinely running background checks concurrently
    /// with live message dispatch reintroduces the exact race class this locks against: two threads
    /// mutating the same chat's game state, or the shared Roboto.Settings lists, at the same time.
    ///
    /// Telegram's own ID namespace guarantees group/supergroup chat IDs are always negative and
    /// user/private-chat IDs always positive, so one shared lock table safely covers both chat-scoped
    /// and user-scoped keys with zero collision risk. GlobalListsKey (0) is reserved for
    /// Roboto.Settings' own top-level list operations (chatData/pluginData/expectedReplies/
    /// RecentChatMembers add/remove/snapshot-for-iteration) - never a real chat or user ID.
    ///
    /// Plain `lock` (Monitor.Enter/Exit), not an async-aware reentrant design: recursive
    /// AsyncLocal-based reentrancy tracking is specifically needed to handle an async continuation
    /// resuming on a different thread, which would otherwise break simple thread-based reentrancy.
    /// This codebase is fully synchronous/blocking throughout (see Messaging.processUpdates, every
    /// module's chatEvent/backgroundProcessing), so Monitor's own built-in per-thread reentrancy - a
    /// thread already holding a lock can re-enter it without blocking itself - is exactly what's
    /// needed already, with no extra machinery.
    /// </summary>
    public static class ChatKeyedLock
    {
        public const long GlobalListsKey = 0;

        private static readonly ConcurrentDictionary<long, object> Locks = new();

        public static IDisposable Acquire(long key)
        {
            var lockObj = Locks.GetOrAdd(key, static _ => new object());
            Monitor.Enter(lockObj);
            return new Releaser(lockObj);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly object _lockObj;
            private bool _released;

            public Releaser(object lockObj)
            {
                _lockObj = lockObj;
            }

            public void Dispose()
            {
                if (_released) { return; }
                _released = true;
                Monitor.Exit(_lockObj);
            }
        }
    }
}
