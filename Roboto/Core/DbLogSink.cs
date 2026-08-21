using System;
using Serilog.Core;
using Serilog.Events;

namespace RobotoChatBot
{
    /// <summary>
    /// Small custom Serilog sink writing to the `logs` table (SqliteStateStore.WriteLogEvent) -
    /// additive to the console sink, not a replacement. Console-only logging (since the phase 0/1
    /// WPF removal, which also dropped the old file-logging sink's equivalent) was a real
    /// functionality loss versus legacy, which logged to a file as well as its WPF window - this
    /// restores durable logging, just to SQLite instead of a flat file, consistent with the rest of
    /// this phase's persistence swap. A small hand-rolled sink rather than a third-party Serilog-
    /// SQLite package, matching this codebase's existing preference for small pieces written from
    /// scratch over pulling in extra dependencies (InstanceBootstrapper's hand-rolled .env parser,
    /// ChatKeyedLock written from scratch rather than sourced from a library, etc).
    ///
    /// Deliberately doesn't log its own write failures anywhere - a failure writing a log entry to
    /// the DB shouldn't itself become a new log entry that might also fail to write (avoids a
    /// recursive-failure spiral); silently drops the entry and moves on, exactly the same
    /// best-effort posture legacy's own file-logging sink already had for I/O errors.
    /// </summary>
    public sealed class DbLogSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            try
            {
                Roboto.Store?.WriteLogEvent(logEvent.Timestamp.UtcDateTime, logEvent.Level.ToString(), null, logEvent.RenderMessage());
            }
            catch
            {
                // best-effort - see class comment.
            }
        }
    }
}
