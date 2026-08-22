using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RobotoChatBot;

namespace RobotoTests;

/// <summary>
/// Covers ChatKeyedLock directly rather than only indirectly through the xyzzy game-flow tests -
/// it exists specifically because phase 4's real background scheduler thread now runs genuinely
/// concurrently with live message dispatch, closing the exact race class legacy never had to worry
/// about (see ChatKeyedLock's own doc comment and MIGRATION.md's phase 4 notes). Two behaviors
/// matter enough to verify directly: real mutual exclusion between two different threads sharing a
/// key, and that a thread re-acquiring a key it already holds doesn't deadlock itself (the whole
/// reason this uses plain Monitor rather than a naive semaphore-per-key scheme).
/// </summary>
public class ChatKeyedLockTests
{
    [Fact]
    public void TwoThreadsOnTheSameKeyGenuinelySerialize()
    {
        var order = new List<string>();
        var orderLock = new object();
        var threadAHasLock = new ManualResetEventSlim(false);
        var releaseThreadA = new ManualResetEventSlim(false);

        var threadA = Task.Run(() =>
        {
            using (ChatKeyedLock.Acquire(1))
            {
                lock (orderLock) { order.Add("A-acquired"); }
                threadAHasLock.Set();
                releaseThreadA.Wait(TimeSpan.FromSeconds(5));
                lock (orderLock) { order.Add("A-releasing"); }
            }
        });

        // Only start thread B once we know thread A genuinely holds the lock, so a B-first race
        // can't make this test pass by accident.
        Assert.True(threadAHasLock.Wait(TimeSpan.FromSeconds(5)), "Thread A never acquired the lock.");

        var threadB = Task.Run(() =>
        {
            using (ChatKeyedLock.Acquire(1))
            {
                lock (orderLock) { order.Add("B-acquired"); }
            }
        });

        // Thread B should still be blocked on key 1 right now - thread A hasn't released it yet.
        Thread.Sleep(100);
        lock (orderLock) { Assert.DoesNotContain("B-acquired", order); }

        releaseThreadA.Set();
        Task.WaitAll(threadA, threadB);

        Assert.Equal(new[] { "A-acquired", "A-releasing", "B-acquired" }, order);
    }

    [Fact]
    public void TwoThreadsOnDifferentKeysRunConcurrentlyNotSerialized()
    {
        var bothHoldingTheirLocks = new CountdownEvent(2);
        var release = new ManualResetEventSlim(false);

        var threadA = Task.Run(() =>
        {
            using (ChatKeyedLock.Acquire(101))
            {
                bothHoldingTheirLocks.Signal();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        });

        var threadB = Task.Run(() =>
        {
            using (ChatKeyedLock.Acquire(102))
            {
                bothHoldingTheirLocks.Signal();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        });

        // If different keys contended on one shared lock, this would time out - both threads would
        // never be inside their `using` block at the same time.
        bool bothEntered = bothHoldingTheirLocks.Wait(TimeSpan.FromSeconds(5));
        release.Set();
        Task.WaitAll(threadA, threadB);

        Assert.True(bothEntered, "Different keys contended on the same lock instead of running concurrently.");
    }

    [Fact]
    public void SameThreadReacquiringItsOwnKeyDoesNotDeadlock()
    {
        var completed = Task.Run(() =>
        {
            using (ChatKeyedLock.Acquire(555))
            {
                // Same key, same thread - Monitor's own built-in per-thread reentrancy (the whole
                // reason this class uses plain Monitor.Enter/Exit rather than a semaphore) should
                // let this through without blocking.
                using (ChatKeyedLock.Acquire(555))
                {
                }
            }
        }).Wait(TimeSpan.FromSeconds(5));

        Assert.True(completed, "Re-acquiring the same key on the same thread deadlocked.");
    }
}
