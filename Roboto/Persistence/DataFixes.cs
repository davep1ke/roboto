using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace RobotoChatBot.Persistence
{
    /// <summary>
    /// The changelog of one-time datafixes, run in order by SqliteStateStore.RunPendingDataFixes.
    /// Each entry runs at most once per DB, ever (tracked in the datafixes table) - append new
    /// fixes here rather than editing an already-shipped one. Kept separate from SqliteStateStore
    /// itself so this list can grow without bloating the class that owns the schema/connection.
    /// </summary>
    public static class DataFixes
    {
        public static readonly IReadOnlyList<(string Name, Action<SqliteConnection, SqliteTransaction> Apply)> All = new List<(string, Action<SqliteConnection, SqliteTransaction>)>
        {
            // The `logs` table (Serilog DbLogSink's target) was removed - nothing in the codebase
            // ever read it back out, and enableFileLogging's rotating text log already gives the
            // same crash-durability the DB sink existed for, without a synchronous SQLite
            // connection+INSERT on every single log() call. Drops the table on any DB that still
            // has it from before this change; IF EXISTS makes it a no-op everywhere else.
            ("0001_drop_logs_table", (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DROP TABLE IF EXISTS logs;";
                command.ExecuteNonQuery();
            }),
        };
    }
}
