using System.Text.RegularExpressions;
using Roboto.Bot.Persistence;

namespace Roboto.Bot.Xyzzy;

public sealed record CrCastImportOutcome(bool Success, string Message, string? PackId);

/// <summary>
/// Live crcast pack import/sync - ports legacy's mod_xyzzy_coredata.importCardCastPack. A fresh
/// pack code adds every card plus the pack itself to the live catalog. A pack code that's already
/// known is treated as a *sync*: cards are matched by text, unchanged ones keep their ID, new text
/// gets a new card, and text that's gone is "removed" - with every game state reference to a
/// removed card remapped onto a surviving card of the same kind (question/answer), preferring one
/// from the same pack, rather than left dangling. Mirrors legacy's own replacement-GUID remap in
/// spirit, adapted to the rewrite's short sequential IDs and per-game state (hands/submissions/
/// remaining decks) instead of a single global reference table.
/// </summary>
public sealed class CrCastPackImportService(CrCastClient client, IStateStore store, XyzzyGameRepository games)
{
    private static readonly Regex PackCodePattern = new("^[A-Z0-9]*$", RegexOptions.Compiled);

    public async Task<CrCastImportOutcome> ImportOrSyncAsync(string packCodeRaw, CancellationToken cancellationToken)
    {
        var packCode = packCodeRaw.Trim().ToUpperInvariant();
        if (!PackCodePattern.IsMatch(packCode) || packCode.Length == 0)
        {
            return new CrCastImportOutcome(false, "That doesn't look like a valid pack code.", null);
        }

        var fetched = await client.FetchPackAsync(packCode, cancellationToken);
        if (fetched is null)
        {
            return new CrCastImportOutcome(false, "Failed to import pack from CRCAST. Check that the code is valid", null);
        }

        var existingPack = CardCatalog.Packs.FirstOrDefault(p => p.PackCode == packCode);
        return existingPack is null
            ? await ImportFreshAsync(packCode, fetched, cancellationToken)
            : await SyncAsync(existingPack, fetched, cancellationToken);
    }

    /// <summary>For the background sync tick (CrCastSyncReconciler) - a failed fetch still needs to
    /// push the pack's NextSyncUtc out, or it would retry every single tick forever. Legacy's own
    /// syncFailed() does the same ("failures still get rescheduled, not hammered").</summary>
    public async Task RescheduleAfterFailureAsync(string packId, CancellationToken cancellationToken)
    {
        var packs = CardCatalog.Packs.Select(p => p.Id == packId ? p with { NextSyncUtc = NextSyncTime() } : p).ToList();
        await store.SaveAsync(CardCatalog.PacksKey, packs, cancellationToken);
        await CardCatalog.LoadOverrideAsync(store, cancellationToken);
    }

    /// <summary>Ports legacy's mod_xyzzy_coredata.removeDormantPacks() - deliberately NOT called
    /// from anywhere in this codebase, matching legacy's own current disablement ("//TODO DISABLE
    /// AS CARDCAST DEAD"). Left as the obvious integration point if dormant-pack cleanup is ever
    /// wanted - see MIGRATION.md's phase 14.5 notes.</summary>
    public Task RemoveDormantPacksAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<CrCastImportOutcome> ImportFreshAsync(string packCode, CrCastFetchedPack fetched, CancellationToken cancellationToken)
    {
        var newPackId = $"p{NextIndex(CardCatalog.Packs.Select(p => p.Id), "p")}";
        var pack = new XyzzyPack(newPackId, fetched.Name, PackCode: packCode, NextSyncUtc: NextSyncTime());

        var qIndex = NextIndex(CardCatalog.Questions.Select(q => q.Id), "q");
        var newQuestions = fetched.Questions.Select(q => new XyzzyCard($"q{qIndex++}", q.Text, Math.Max(1, q.AnswerCount), newPackId)).ToList();

        var aIndex = NextIndex(CardCatalog.Answers.Select(a => a.Id), "a");
        var newAnswers = fetched.Answers.Select(a => new XyzzyCard($"a{aIndex++}", a.Text, PackId: newPackId)).ToList();

        await PersistCatalogAsync(
            CardCatalog.Questions.Concat(newQuestions).ToList(),
            CardCatalog.Answers.Concat(newAnswers).ToList(),
            CardCatalog.Packs.Append(pack).ToList(),
            cancellationToken);

        var message = $"Importing fresh pack {packCode} - {fetched.Name} - {fetched.Description}\n" +
                       $"Added {newQuestions.Count} questions and {newAnswers.Count} answers.";
        return new CrCastImportOutcome(true, message, newPackId);
    }

    private async Task<CrCastImportOutcome> SyncAsync(XyzzyPack existingPack, CrCastFetchedPack fetched, CancellationToken cancellationToken)
    {
        var existingQuestions = CardCatalog.Questions.Where(q => q.PackId == existingPack.Id).ToList();
        var existingAnswers = CardCatalog.Answers.Where(a => a.PackId == existingPack.Id).ToList();

        var questionDiff = DiffAndReplace(existingQuestions, fetched.Questions, existingPack.Id, "q", CardCatalog.Questions);
        var answerDiff = DiffAndReplace(existingAnswers, fetched.Answers, existingPack.Id, "a", CardCatalog.Answers);

        var syncedPack = existingPack with { Name = fetched.Name, NextSyncUtc = NextSyncTime() };
        var questions = CardCatalog.Questions.Where(q => q.PackId != existingPack.Id).Concat(questionDiff.Result).ToList();
        var answers = CardCatalog.Answers.Where(a => a.PackId != existingPack.Id).Concat(answerDiff.Result).ToList();
        var packs = CardCatalog.Packs.Select(p => p.Id == existingPack.Id ? syncedPack : p).ToList();

        await PersistCatalogAsync(questions, answers, packs, cancellationToken);
        await RemapRemovedCardsAsync(questionDiff.Replacements, answerDiff.Replacements, cancellationToken);

        var message = $"Pack {fetched.Name} ({existingPack.PackCode}) exists, syncing cards - " +
                       $"added {questionDiff.Added + answerDiff.Added}, removed {questionDiff.Removed + answerDiff.Removed}.";
        return new CrCastImportOutcome(true, message, existingPack.Id);
    }

    private sealed record DiffResult(List<XyzzyCard> Result, Dictionary<string, string> Replacements, int Added, int Removed);

    /// <summary>Matches existing cards to freshly-fetched ones by exact text - cards whose text
    /// still exists keep their ID (so in-flight hands/decks referencing them are unaffected); new
    /// text gets a new sequential ID; text that's gone is "removed" and mapped onto a surviving
    /// card of the same kind, preferring one still in this pack, falling back to any remaining card
    /// of that kind anywhere in the catalog (only left unmapped if literally none exist, which would
    /// mean the whole catalog just ran out of that card type entirely).</summary>
    private static DiffResult DiffAndReplace(
        List<XyzzyCard> existing, List<CrCastFetchedCard> fetched, string packId, string prefix, IReadOnlyList<XyzzyCard> wholeCatalog)
    {
        var fetchedTexts = fetched.Select(f => f.Text).ToHashSet(StringComparer.Ordinal);
        var existingTexts = existing.Select(c => c.Text).ToHashSet(StringComparer.Ordinal);

        var kept = existing.Where(c => fetchedTexts.Contains(c.Text)).ToList();
        var removed = existing.Where(c => !fetchedTexts.Contains(c.Text)).ToList();
        var newCards = fetched.Where(f => !existingTexts.Contains(f.Text)).ToList();

        var nextIndex = NextIndex(wholeCatalog.Select(c => c.Id), prefix);
        var added = newCards.Select(f => new XyzzyCard($"{prefix}{nextIndex++}", f.Text, Math.Max(1, f.AnswerCount), packId)).ToList();

        var result = kept.Concat(added).ToList();

        var replacements = new Dictionary<string, string>();
        if (removed.Count > 0)
        {
            var survivor = result.FirstOrDefault()?.Id
                            ?? wholeCatalog.FirstOrDefault(c => c.PackId != packId)?.Id;
            if (survivor is not null)
            {
                foreach (var r in removed)
                {
                    replacements[r.Id] = survivor;
                }
            }
        }

        return new DiffResult(result, replacements, added.Count, removed.Count);
    }

    /// <summary>Applies a removed-card replacement map across every active game's live state -
    /// hands, submissions, remaining decks, and the current question - so a mid-sync card removal
    /// never leaves a dangling reference a later CardCatalog.FindQuestion/FindAnswer would fail on.</summary>
    private async Task RemapRemovedCardsAsync(
        IReadOnlyDictionary<string, string> questionReplacements, IReadOnlyDictionary<string, string> answerReplacements, CancellationToken cancellationToken)
    {
        if (questionReplacements.Count == 0 && answerReplacements.Count == 0)
        {
            return;
        }

        foreach (var game in await games.GetAllActiveAsync(cancellationToken))
        {
            var changed = false;

            if (game.CurrentQuestionCardId is not null && questionReplacements.TryGetValue(game.CurrentQuestionCardId, out var newQuestion))
            {
                game.CurrentQuestionCardId = newQuestion;
                changed = true;
            }

            changed |= RemapList(game.RemainingQuestionCardIds, questionReplacements);
            changed |= RemapList(game.RemainingAnswerCardIds, answerReplacements);

            foreach (var player in game.Players)
            {
                changed |= RemapList(player.Hand, answerReplacements);
            }

            foreach (var submission in game.Submissions.Values)
            {
                changed |= RemapList(submission, answerReplacements);
            }

            if (changed)
            {
                await games.SaveAsync(game, cancellationToken);
            }
        }
    }

    private static bool RemapList(List<string> ids, IReadOnlyDictionary<string, string> replacements)
    {
        var changed = false;
        for (var i = 0; i < ids.Count; i++)
        {
            if (replacements.TryGetValue(ids[i], out var replacement))
            {
                ids[i] = replacement;
                changed = true;
            }
        }

        return changed;
    }

    private async Task PersistCatalogAsync(List<XyzzyCard> questions, List<XyzzyCard> answers, List<XyzzyPack> packs, CancellationToken cancellationToken)
    {
        await store.SaveAsync(CardCatalog.QuestionsKey, questions, cancellationToken);
        await store.SaveAsync(CardCatalog.AnswersKey, answers, cancellationToken);
        await store.SaveAsync(CardCatalog.PacksKey, packs, cancellationToken);
        await CardCatalog.LoadOverrideAsync(store, cancellationToken);
    }

    /// <summary>Legacy's cardcast_pack.setNextSync(): 3-9 days out, at a random hour - jittered per
    /// pack so a large catalog's syncs spread out instead of all landing at once.</summary>
    private static DateTime NextSyncTime() =>
        DateTime.UtcNow.AddDays(3 + Random.Shared.Next(7)).AddHours(Random.Shared.Next(24));

    private static int NextIndex(IEnumerable<string> ids, string prefix)
    {
        var max = 0;
        foreach (var id in ids)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(prefix.Length), out var n))
            {
                max = Math.Max(max, n);
            }
        }

        return max + 1;
    }
}
