using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

[XmlType("mod_steam_chat_data")]
public sealed class LegacySteamChatData : LegacyModuleChatData
{
    public List<LegacySteamPlayer> players = [];
}

[XmlType("mod_steam_core_data")]
public sealed class LegacySteamCoreData : LegacyModuleData
{
    /// <summary>Read only to optionally carry into the new instance's bot.env - never logged or
    /// echoed anywhere. See CLAUDE.md/MIGRATION.md's phase 11 credential-handling notes.</summary>
    public string steamAPIKey = "";

    public List<LegacySteamGame> games = [];
}

[XmlType("mod_steam_player")]
public sealed class LegacySteamPlayer
{
    public string playerID = "";
    public string playerName = "";
    public List<LegacySteamChiev> chievs = [];
}

[XmlType("mod_steam_chiev")]
public sealed class LegacySteamChiev
{
    public string chievName = "";
    public string appID = "";
}

[XmlType("mod_steam_game")]
public sealed class LegacySteamGame
{
    public string gameID = "";
    public string displayName = "";
    public List<LegacySteamAchievement> chievs = [];
}

[XmlType("mod_steam_achievement")]
public sealed class LegacySteamAchievement
{
    public string achievement_code = "";
    public string displayName = "";
    public string description = "";
}
