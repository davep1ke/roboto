namespace Roboto.Bot.Xyzzy;

/// <summary>
/// Legacy semantics for XyzzyGameState.EnabledPackIds, restored to match mod_xyzzy_chatdata's real
/// behavior (Phase 11's original "empty list = all packs" convention was inverted from legacy and
/// has been reverted - see MIGRATION.md). A brand-new chat starts with exactly one pack enabled
/// (DefaultSelection, legacy's packFilterIDs = [primaryPackID]) - "all packs" is an explicit opt-in,
/// represented by adding the AllPacksId sentinel into the list (legacy's AllPacksEnabledID/
/// Guid.Empty), not by leaving the list empty.
/// </summary>
public static class XyzzyPackFilter
{
    /// <summary>Reserved pack ID meaning "every pack is enabled" when present in EnabledPackIds -
    /// legacy's AllPacksEnabledID (Guid.Empty), translated to a short-ID string. Can never collide
    /// with a real pack ID: the importer only ever assigns sequential "p1", "p2", ... IDs.</summary>
    public const string AllPacksId = "*";

    public static bool AllEnabled(XyzzyGameState game) => game.EnabledPackIds.Contains(AllPacksId);

    public static bool IsEnabled(XyzzyGameState game, string? packId) =>
        AllEnabled(game) || (packId is not null && game.EnabledPackIds.Contains(packId));

    /// <summary>What a brand-new chat gets. Falls back to the "all packs" sentinel when the catalog
    /// has no packs loaded at all (the hardcoded placeholder dev/test set) - there's no "base pack"
    /// concept to default to there, and CardCatalog.Questions/Answers already have no PackId on any
    /// card in that set, so the sentinel is a no-op in practice (every filter check short-circuits
    /// to the full unfiltered catalog regardless).</summary>
    public static List<string> DefaultSelection() =>
        CardCatalog.DefaultPackId is { } id ? [id] : [AllPacksId];
}
