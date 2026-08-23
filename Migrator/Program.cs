using System;
using System.IO;
using System.Linq;
using RobotoChatBot;
using RobotoChatBot.Persistence;

namespace RobotoChatBot.Migrator
{
    /// <summary>
    /// Phase 8: imports a legacy XML export into this branch's own SQLite store, targeting a fresh
    /// {DataDir}/{Instance}/ folder (same layout InstanceBootstrapper/BotOptions already use for a
    /// real bot instance) rather than mutating anything in place. Defaults to dry-run - only a real
    /// write (--real) touches the target's roboto.db, and only into an instance folder with no
    /// existing data unless --force is also given. Never opens the source XML for anything but
    /// reading; never writes the Telegram token anywhere (settings.loadFromLegacyXml scrubs it from
    /// the parsed object, and InstanceBootstrapper's normal first-run stub bot.env is the only place
    /// a token can go, left for the operator to fill in by hand).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage();
                return args.Length < 2 ? 1 : 0;
            }

            var xmlPath = args[0];
            var instance = args[1];
            var dataDir = GetOption(args, "--datadir") ?? "data";
            var real = args.Contains("--real");
            var force = args.Contains("--force");

            if (!File.Exists(xmlPath))
            {
                Console.Error.WriteLine($"Source XML not found: {xmlPath}");
                return 1;
            }

            Console.WriteLine($"Loading plugins...");
            Plugins.initPluginAssemblies();

            Console.WriteLine($"Parsing {xmlPath}...");
            settings imported;
            try
            {
                imported = settings.loadFromLegacyXml(xmlPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to parse source XML: {ex.Message}");
                return 1;
            }

            var sourceReport = ImportReport.From(imported);
            Console.WriteLine();
            Console.WriteLine(sourceReport.Format($"Source ({xmlPath}):"));

            if (!real)
            {
                Console.WriteLine("Dry run only - nothing written. Pass --real to actually import into "
                    + $"{Path.Combine(dataDir, instance)}.");
                return 0;
            }

            var instanceDir = Path.Combine(dataDir, instance);
            var dbPath = Path.Combine(instanceDir, "roboto.db");
            Directory.CreateDirectory(instanceDir);

            if (File.Exists(dbPath) && !force)
            {
                Roboto.Options = new BotOptions { Instance = instance, DataDir = dataDir };
                Roboto.Store = new SqliteStateStore(dbPath);
                Roboto.Store.Initialize();
                var existing = settings.load();
                if (existing.chatData.Count > 0 || existing.pluginData.Count > 0)
                {
                    Console.Error.WriteLine($"{dbPath} already has data ({existing.chatData.Count} chats, "
                        + $"{existing.pluginData.Count} plugin-data rows) - refusing to overwrite. "
                        + "Pass --force to import anyway.");
                    return 1;
                }
            }

            // InstanceBootstrapper.TryLoad creates the stub bot.env on first run (leaving
            // TelegramToken blank) as a side effect and returns false for a blank token - expected
            // and fine here, the migrator never needs a real token itself.
            InstanceBootstrapper.TryLoad(dataDir, instance, out _, out _, out _, out _, out _);

            Roboto.Options = new BotOptions { Instance = instance, DataDir = dataDir };
            Roboto.Store = new SqliteStateStore(dbPath);
            Roboto.Store.Initialize();

            Console.WriteLine($"Writing to {dbPath}...");
            imported.save();

            Console.WriteLine("Reloading from the target store to verify round-trip fidelity...");
            var reloaded = settings.load();
            var afterReport = ImportReport.From(reloaded);
            Console.WriteLine();
            Console.WriteLine(afterReport.Format("After save() + reload:"));

            var diffs = ImportReport.Diff(sourceReport, afterReport);
            if (diffs.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("MISMATCH - the import did not round-trip cleanly:");
                foreach (var d in diffs) Console.Error.WriteLine($"  {d}");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Counts match. Fill in TelegramToken in " + Path.Combine(instanceDir, "bot.env")
                + " (a TEST bot token, never the production one) before running this instance live.");
            return 0;
        }

        private static string GetOption(string[] args, string name)
        {
            var idx = Array.IndexOf(args, name);
            return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("""
                Usage: Roboto.Migrator <source.xml> <targetInstance> [--datadir <dir>] [--real] [--force]

                  <source.xml>      Legacy XML export to import (read-only, never modified).
                  <targetInstance>  Instance name - data lands in <datadir>/<targetInstance>/.
                  --datadir <dir>   Defaults to "data" (relative to the current directory).
                  --real            Actually write. Without this, only a dry-run report is printed.
                  --force           Import even if the target instance already has data.

                Without --real, only parses the source XML and prints counts - nothing is written.
                """);
        }
    }
}
