// dotnet run --project src/Roboto.Migrator -- <xmlPath> <dataDir> <instance> [--dry-run]
//
// A separate console tool, not a Telegram command and not part of Roboto.Bot's always-running
// host - structurally impossible to trigger via a chat message. See MIGRATION.md's phase 11
// section for the full safety rationale (never touches the source XML in place, never writes
// telegramAPIKey anywhere, dry-run/checksum validation before any real write).

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Roboto.Migrator <xmlPath> <dataDir> <instance> [--dry-run] [--carry-steam-key]");
    return 1;
}

var xmlPath = args[0];
var dataDir = args[1];
var instance = args[2];
var dryRun = args.Contains("--dry-run");
var carrySteamKey = args.Contains("--carry-steam-key");

if (!File.Exists(xmlPath))
{
    Console.Error.WriteLine($"No such file: {xmlPath}");
    return 1;
}

Console.WriteLine($"Roboto.Migrator: importing {xmlPath} -> {dataDir}/{instance} (dryRun={dryRun}, carrySteamKey={carrySteamKey})");
Console.WriteLine("Not yet implemented - importer logic lands in the next commit.");
return 0;
