using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobotoChatBot.Persistence
{
    /// <summary>
    /// Snapshots the live SQLite DB before anything touches it, on every startup - added after a
    /// real incident (2026-08-24, see MIGRATION.md): a bug in startupChecks() deleted 457 real answer
    /// cards and 90 real question cards from robotolive's production data, discovered only after the
    /// fact via the application log. A cheap timestamped copy taken before Plugins.startupChecks()
    /// ever runs means the previous good state is always one file away, regardless of what a future
    /// startup-time bug does to it.
    /// </summary>
    public static class DbBackup
    {
        private const int KeepCount = 10;

        /// <summary>Copies dbPath to "roboto.&lt;yyyyMMdd-HHmmss&gt;.db" alongside it, then trims
        /// older backups in that same directory down to the most recent KeepCount. No-ops if dbPath
        /// doesn't exist yet (a genuinely fresh instance's very first boot - nothing to back up).</summary>
        public static void RunBeforeOpen(string dbPath)
        {
            if (!File.Exists(dbPath)) { return; }

            string dir = Path.GetDirectoryName(dbPath) ?? ".";
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = Path.Combine(dir, $"roboto.{timestamp}.db");

            // Same timestamp already backed up this second (unlikely, but harmless) - don't clobber it.
            if (!File.Exists(backupPath))
            {
                File.Copy(dbPath, backupPath);
            }

            List<string> backups = Directory.GetFiles(dir, "roboto.*.db")
                .Where(f => Path.GetFileName(f) != "roboto.db")
                .OrderByDescending(f => f)
                .ToList();

            foreach (string old in backups.Skip(KeepCount))
            {
                File.Delete(old);
            }
        }
    }
}
