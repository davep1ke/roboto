namespace Roboto.Migrator;

public sealed record ImportOptions(string XmlPath, string DataDir, string Instance, bool DryRun, bool CarrySteamKey);

/// <summary>SteamApiKeyToCarry is deliberately kept separate from Report - a real credential that
/// must never end up in a printed/logged summary, only ever written directly into bot.env by the
/// caller (Program.cs) if CarrySteamKey was set. Report itself only ever records whether a key was
/// found/carried (a bool), never the value.</summary>
public sealed record ImportResult(ImportReport Report, string? SteamApiKeyToCarry, string? LegacyBotUserName);
