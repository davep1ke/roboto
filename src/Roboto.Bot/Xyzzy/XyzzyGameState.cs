namespace Roboto.Bot.Xyzzy;

/// <summary>
/// Per-chat mod_xyzzy game state - its own key/POCO via IStateStore rather than a field on
/// ChatState, per ChatState's own doc comment about module-owned per-chat data. Collapses legacy's
/// setup-wizard-heavy status chain (Stopped -> useDefaults -> SetGameLength -> setPackFilter ->
/// ... -> Invites) down to a single jump straight to Invites with fixed defaults - see
/// MIGRATION.md's mod_xyzzy scope-cuts note for why (/xyzzy_settings, once built, covers adjusting
/// things after the fact instead of a pre-game wizard).
/// </summary>
public sealed class XyzzyGameState
{
    public long ChatId { get; set; }
    public XyzzyStatus Status { get; set; } = XyzzyStatus.Stopped;
    public List<XyzzyPlayer> Players { get; set; } = [];

    /// <summary>
    /// Telegram user ID of the current judge - a stable ID, not an array index. Legacy's
    /// equivalent (lastPlayerAsked) was an int index into the player list, which needed ~60 lines
    /// of reindexing bookkeeping in removePlayer whenever a player left mid-game (and was even
    /// self-flagged "//TODO - should be an ID!" in the legacy code). Removing a player here just
    /// removes them from Players - no reindexing needed.
    /// </summary>
    public long? JudgePlayerId { get; set; }

    public string? CurrentQuestionCardId { get; set; }
    public List<string> RemainingQuestionCardIds { get; set; } = [];
    public List<string> RemainingAnswerCardIds { get; set; } = [];

    /// <summary>Player ID -> the card(s) they've submitted this round. Cleared each new question.</summary>
    public Dictionary<long, List<string>> Submissions { get; set; } = [];

    /// <summary>
    /// Incremented every new question - lets a stale callback-query tap (from a message belonging
    /// to a round that's already moved on) be rejected with a clear "that round's over" answer
    /// instead of silently corrupting current state.
    /// </summary>
    public int RoundNumber { get; set; }

    public DateTime StatusChangedUtc { get; set; } = DateTime.UtcNow;
    public bool ReminderSent { get; set; }

    public double MaxWaitHours { get; set; } = 12;
    public double MinWaitHours { get; set; }

    /// <summary>Number of rounds to play before the game ends itself, or -1 for unlimited (legacy's
    /// enteredQuestionCount). Only settable via /xyzzy_start's "configure" path (phase 8.5) - -1
    /// otherwise, matching "use defaults".</summary>
    public int QuestionLimit { get; set; } = -1;

    /// <summary>Which CardCatalog packs this chat draws from - see XyzzyPackFilter for the actual
    /// semantics (a brand-new chat defaults to just the one base pack; "all packs" is the explicit
    /// XyzzyPackFilter.AllPacksId sentinel, matching legacy's packFilterIDs/AllPacksEnabledID
    /// exactly, not an empty-list convention). The field-initializer default here is only ever seen
    /// by a XyzzyGameState constructed directly (tests) - the real default for a chat with no stored
    /// game comes from XyzzyGameRepository.GetAsync, which needs CardCatalog already loaded.
    /// Populated via /xyzzy_settings' "Change Packs" menu (XyzzySettingsCallbackHandler) or import.</summary>
    public List<string> EnabledPackIds { get; set; } = [];

    public XyzzyPlayer? FindPlayer(long playerId) => Players.FirstOrDefault(p => p.PlayerId == playerId);
    public bool IsPlayer(long playerId) => FindPlayer(playerId) is not null;
}
