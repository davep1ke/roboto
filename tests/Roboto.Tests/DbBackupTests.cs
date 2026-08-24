using System.IO;
using RobotoChatBot.Persistence;

namespace RobotoTests;

/// <summary>
/// Covers DbBackup.RunBeforeOpen - added after a real incident (2026-08-24, see MIGRATION.md) where
/// a startupChecks() bug deleted real production cards with no prior snapshot to recover from.
/// </summary>
public class DbBackupTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"dbbackup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DoesNothingWhenTheDatabaseDoesNotExistYet()
    {
        string dir = NewTempDir();
        string dbPath = Path.Combine(dir, "roboto.db");

        DbBackup.RunBeforeOpen(dbPath);

        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void CreatesATimestampedCopyOfAnExistingDatabase()
    {
        string dir = NewTempDir();
        string dbPath = Path.Combine(dir, "roboto.db");
        File.WriteAllText(dbPath, "fake sqlite content");

        DbBackup.RunBeforeOpen(dbPath);

        string[] backups = Directory.GetFiles(dir, "roboto.*.db");
        string backup = Assert.Single(backups);
        Assert.NotEqual("roboto.db", Path.GetFileName(backup));
        Assert.Equal("fake sqlite content", File.ReadAllText(backup));
        // The live file itself must be untouched, not moved.
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void KeepsOnlyTheTenMostRecentBackups()
    {
        string dir = NewTempDir();
        string dbPath = Path.Combine(dir, "roboto.db");
        File.WriteAllText(dbPath, "fake sqlite content");

        // Pre-seed 10 older backups with fabricated, already-sortable timestamps - avoids the test
        // depending on real wall-clock time to produce 10 distinct per-second filenames.
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(dir, $"roboto.2020010{i}-000000.db"), "old");
        }

        // One more backup - now 11 exist before trimming - the oldest of the 10 pre-seeded ones
        // should be the one removed to get back down to 10.
        DbBackup.RunBeforeOpen(dbPath);

        string[] backups = Directory.GetFiles(dir, "roboto.*.db");
        Assert.Equal(10, backups.Length);
        Assert.DoesNotContain(backups, f => Path.GetFileName(f) == "roboto.20200100-000000.db");
    }
}
