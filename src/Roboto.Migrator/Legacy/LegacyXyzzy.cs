using System.Xml.Serialization;

namespace Roboto.Migrator.Legacy;

[XmlType("mod_xyzzy_data")]
public sealed class LegacyXyzzyChatData : LegacyModuleChatData
{
    public List<LegacyXyzzyPlayer> players = [];

    /// <summary>Position in players, not a stable ID - see XyzzyGameState.JudgePlayerId's own doc
    /// comment on why the rewrite uses a stable ID instead. -1 = unset.</summary>
    public int lastPlayerAsked = -1;

    /// <summary>Enum name (xyzzy_Statuses) - see XyzzyImportMapper for the mapping onto the
    /// rewrite's collapsed XyzzyStatus.</summary>
    public string status = "Stopped";

    public DateTime statusChangedTime = DateTime.UtcNow;

    /// <summary>GUID string - remapped to a new short card ID via the catalog import's ID map
    /// (see XyzzyImportMapper), never used directly.</summary>
    public string? currentQuestion;

    public List<string> remainingQuestions = [];
    public List<string> remainingAnswers = [];

    public int maxWaitTimeHours;
    public int minWaitTimeHours;
    public int enteredQuestionCount = -1;

    /// <summary>GUIDs - remapped via the catalog import's pack ID map (see
    /// XyzzyImportMapper.MapEnabledPackIds). Legacy's own semantics are the inverse of what the
    /// field name suggests: this does NOT default to empty ("all enabled") - it defaults to
    /// [primaryPackID] (one specific pack), and "all packs" is instead represented by the sentinel
    /// mod_xyzzy.AllPacksEnabledID (Guid.Empty) appearing *inside* this list. See MapEnabledPackIds
    /// for the translation onto XyzzyGameState.EnabledPackIds' own (simpler) "empty = all" scheme.</summary>
    public List<Guid> packFilterIDs = [];
}

/// <summary>Global - the real card catalog (packs/questions/answers) and background-scan
/// bookkeeping.</summary>
[XmlType("mod_xyzzy_coredata")]
public sealed class LegacyXyzzyCoreData : LegacyModuleData
{
    public List<LegacyXyzzyCard> questions = [];
    public List<LegacyXyzzyCard> answers = [];
    public List<LegacyCardcastPack> packs = [];
}

/// <summary>Roboto/Helpers/cardCast.cs's cardcast_pack - only the fields the importer actually
/// needs (packID for remapping/filter translation, name for the "Change Packs" picker label).</summary>
[XmlType("cardcast_pack")]
public sealed class LegacyCardcastPack
{
    public Guid packID = Guid.NewGuid();
    public string name = "";
    public string? packCode;
}

[XmlType("mod_xyzzy_player")]
public sealed class LegacyXyzzyPlayer
{
    public string name = "";
    public long playerID;
    public int wins;
    public List<string> cardsInHand = [];
    public List<string> selectedCards = [];
}

[XmlType("mod_xyzzy_card")]
public sealed class LegacyXyzzyCard
{
    public string uniqueID = Guid.NewGuid().ToString();
    public string text = "";

    /// <summary>Only meaningful on question cards - legacy defaults answer cards to -1, where the
    /// field is meaningless (see XyzzyImportMapper's defensive "<= 0 treated as 1" handling).</summary>
    public int nrAnswers = -1;

    /// <summary>Guid.Empty on a card that predates pack filtering (or was never assigned one) -
    /// treated as "no pack" (XyzzyCard.PackId stays null), same as the rewrite's own hardcoded
    /// placeholder set.</summary>
    public Guid packID = Guid.Empty;
}
