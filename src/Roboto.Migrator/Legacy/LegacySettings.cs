using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

/// <summary>
/// Minimal, read-only reimplementation of legacy's serialized shape (Roboto/settings.cs,
/// Roboto/Storage/chat.cs, Roboto/Storage/ExpectedReply.cs) - just the fields this importer
/// actually reads, not a faithful full port of legacy's behavior. Deliberately mirrors legacy's
/// exact field names/casing (chatID, playerID, ...) rather than idiomatic C# naming, to minimize
/// the chance of an XmlSerializer name-mapping mistake - this is throwaway migration tooling, not
/// public API.
///
/// Not a dependency on the legacy Roboto.csproj (WinForms/.NET Framework, can't be referenced from
/// this net10.0 project) - a fresh, minimal read path instead.
/// </summary>
[XmlRoot("settings")]
public sealed class LegacySettings
{
    /// <summary>Read only to identify which bot this export is (logging) - never written anywhere
    /// by this importer. See CLAUDE.md's live-production safety rules.</summary>
    public string telegramAPIKey = "";

    public string botUserName = "";

    public List<LegacyChat> chatData = [];

    public List<LegacyModuleData> pluginData = [];

    public List<LegacyExpectedReply> expectedReplies = [];
}

public sealed class LegacyChat
{
    public long chatID;
    public string chatTitle = "";
    public bool muted;
    public List<long> chatAdmins = [];

    public List<LegacyModuleChatData> chatData = [];
}

/// <summary>Base for every module's global data blob (mod_quote_core_data, mod_xyzzy_coredata,
/// ...) - matches legacy's abstract RobotoModuleDataTemplate. The XmlSerializer constructed for
/// LegacySettings must be given every concrete subclass as an extraType (see XmlImporter) so
/// xsi:type-tagged elements deserialize to the right concrete class, same mechanism legacy's own
/// XmlSerializer(typeof(settings), Plugins.getPluginDataTypes()) call uses.</summary>
public abstract class LegacyModuleData;

/// <summary>Base for every module's per-chat data blob - matches legacy's abstract
/// RobotoModuleChatDataTemplate. See LegacyModuleData's doc comment for the extraTypes mechanism.</summary>
public abstract class LegacyModuleChatData
{
    public long chatID;
}

/// <summary>Only the fields this importer's resumption logic actually needs - chatID/userID to
/// know who it's for, pluginType/messageData to know what kind of question it was. Everything else
/// legacy tracked (text, keyboard, timestamps) is irrelevant since a resumed question is rebuilt
/// fresh from current game state, not replayed verbatim.</summary>
public sealed class LegacyExpectedReply
{
    public long chatID = -1;
    public long userID = -1;
    public string? pluginType;
    public string? messageData;
}
