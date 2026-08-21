using System;
using System.Threading;

namespace RobotoChatBot
{
    /// <summary>
    /// Runs Plugins.backgroundProcessing(false) on a real, live interval, on its own thread -
    /// genuinely concurrent with Messaging.processUpdates()'s main poll loop, not embedded in it and
    /// not just "runs at shutdown" the way legacy actually did (confirmed by tracing
    /// Roboto.cs/Core/Plugins.cs/Core/Messaging.cs: Plugins.backgroundProcessing was only ever
    /// invoked once, after processUpdates()'s loop had already exited, or manually via /background -
    /// ChatKeyedLock.cs's own comment has the full trace). Explicitly requested this way rather than
    /// blocking the message loop while a pass runs - mod_xyzzy's own background pass "does take a
    /// chunk of time to run, even across a small number of games" (the batching caps in
    /// mod_xyzzy_coredata that bound how long one pass takes are being kept exactly as legacy had
    /// them, not simplified now that a live timer exists - see MIGRATION.md).
    ///
    /// Interval matches mod_xyzzy's own already-declared backgroundMins=1 (RobotoModuleTemplate's
    /// per-module throttle) - a shorter scheduler tick than any module actually wants doesn't cost
    /// anything, since callBackgroundProcessing's own per-module throttle just no-ops until that
    /// module's own interval has actually elapsed.
    /// </summary>
    public static class BackgroundScheduler
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
        private static Thread _thread;
        private static volatile bool _stop;

        public static void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "BackgroundScheduler" };
            _thread.Start();
        }

        public static void Stop()
        {
            _stop = true;
        }

        private static void Run()
        {
            while (!_stop)
            {
                Thread.Sleep(Interval);
                if (_stop) { return; }

                try
                {
                    Plugins.backgroundProcessing(false);
                }
                catch (Exception e)
                {
                    // Plugins.backgroundProcessing already catches+logs per-plugin so one module's
                    // failure doesn't stop the others - this is a last-resort catch-all so a truly
                    // unexpected exception can't silently kill the whole scheduler thread instead.
                    Roboto.log.log("Unhandled exception in background scheduler: " + e.ToString(), logging.loglevel.critical);
                }
            }
        }
    }
}
