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
}

/// <summary>Global - the real card catalog (packs/questions/answers) and background-scan
/// bookkeeping. Only questions/answers matter to this importer; pack metadata is out of scope (v1
/// never built pack filtering - see MIGRATION.md's scope-cuts note).</summary>
[XmlType("mod_xyzzy_coredata")]
public sealed class LegacyXyzzyCoreData : LegacyModuleData
{
    public List<LegacyXyzzyCard> questions = [];
    public List<LegacyXyzzyCard> answers = [];
}

public sealed class LegacyXyzzyPlayer
{
    public string name = "";
    public long playerID;
    public int wins;
    public List<string> cardsInHand = [];
    public List<string> selectedCards = [];
}

public sealed class LegacyXyzzyCard
{
    public string uniqueID = Guid.NewGuid().ToString();
    public string text = "";

    /// <summary>Only meaningful on question cards - legacy defaults answer cards to -1, where the
    /// field is meaningless (see XyzzyImportMapper's defensive "<= 0 treated as 1" handling).</summary>
    public int nrAnswers = -1;
}
