using Roboto.Bot.Persistence;

namespace Roboto.Bot.Xyzzy;

/// <summary>AnswerCount > 1 ("Pick 2"+) only matters on question cards - a player must submit that
/// many cards before they're done for the round, and the judge sees them joined with " >> " rather
/// than substituted into the question's blank(s) (see XyzzyRoundService.CombinedAnswerText -
/// deliberately not reproducing legacy's regex-based per-blank interleaving). PackId is null for
/// the hardcoded placeholder set (no pack concept there); an imported card always has one - see
/// XyzzyGameState.EnabledPackIds for how it's used to filter a chat's deck.</summary>
public sealed record XyzzyCard(string Id, string Text, int AnswerCount = 1, string? PackId = null);

/// <summary>A pack a card can belong to (legacy's cardcast_pack, Roboto/Helpers/cardCast.cs) - just
/// enough to show a name in the "Change Packs" picker and filter a deck by selection. IsDefault
/// marks the one pack a brand-new chat starts enabled on (legacy's primaryPackID, the base CAH
/// set) - see XyzzyPackFilter.DefaultSelection. PackCode/NextSyncUtc are only set for a
/// crcast-imported pack (CrCastPackImportService) - null for the hardcoded placeholder set and for
/// anything imported via the XML migration importer, neither of which are crcast-sourced.</summary>
public sealed record XyzzyPack(string Id, string Name, bool IsDefault = false, string? PackCode = null, DateTime? NextSyncUtc = null);

/// <summary>
/// The default card pack: a modest hardcoded sample of the public Cards Against Humanity base set
/// (CC BY-NC-SA-licensed, hence safe to reproduce here) - what every fresh dev/test instance gets,
/// and every existing test still exercises unchanged (they never call LoadOverrideAsync, so these
/// hardcoded IDs/texts stay exactly what they were).
///
/// A real, imported instance overrides this once at startup via LoadOverrideAsync (phase 11's
/// XmlImporter writes the real catalog to IStateStore; Program.cs loads it before any game logic
/// runs) - a mutable-backing-field swap rather than making every call site across XyzzyRoundService/
/// XyzzyStartCommand load the catalog asynchronously from IStateStore on every access, which would
/// have meant touching most of the already-tested round-play engine for this. Deliberately replaces
/// the defaults outright when present, not merged - real content and the hardcoded placeholder
/// jokes have no business sharing one deck.
///
/// IDs are short, stable, human-debuggable strings (not GUIDs like legacy) - they get embedded
/// directly in inline-keyboard callback_data (format: xy:&lt;action&gt;:&lt;groupChatId&gt;:
/// &lt;round&gt;:&lt;cardId&gt;), and Telegram caps callback_data at 64 bytes. The importer assigns
/// its own new short IDs rather than reusing legacy's GUIDs for exactly this reason.
/// </summary>
public static class CardCatalog
{
    public const string QuestionsKey = "xyzzy:catalog:questions";
    public const string AnswersKey = "xyzzy:catalog:answers";
    public const string PacksKey = "xyzzy:catalog:packs";

    // Assigned in the static constructor below, not inline here - DefaultQuestions/DefaultAnswers
    // are declared later in this file, and static member initializers run in declaration order, so
    // an inline initializer here would read them before their own initializers had run. A static
    // constructor always runs after every static field/property initializer in the type, regardless
    // of declaration order, which sidesteps that.
    private static IReadOnlyList<XyzzyCard> _questions;
    private static IReadOnlyList<XyzzyCard> _answers;
    private static IReadOnlyList<XyzzyPack> _packs;
    private static Dictionary<string, XyzzyCard> _questionsById;
    private static Dictionary<string, XyzzyCard> _answersById;
    private static string? _defaultPackId;

    static CardCatalog()
    {
        _questions = DefaultQuestions;
        _answers = DefaultAnswers;
        _packs = [];
        _questionsById = BuildIndex(_questions);
        _answersById = BuildIndex(_answers);
        _defaultPackId = ComputeDefaultPackId(_packs);
    }

    public static IReadOnlyList<XyzzyCard> Questions => _questions;
    public static IReadOnlyList<XyzzyCard> Answers => _answers;
    public static IReadOnlyList<XyzzyPack> Packs => _packs;

    /// <summary>The pack a brand-new chat starts enabled on (see XyzzyPackFilter.DefaultSelection) -
    /// whichever pack is flagged IsDefault, else the first pack, else null if no packs are loaded at
    /// all (the hardcoded placeholder catalog has none).</summary>
    public static string? DefaultPackId => _defaultPackId;

    /// <summary>O(1) lookups - the real imported catalog is tens to hundreds of thousands of cards
    /// (72,441 questions / 229,734 answers in the largest real production export seen so far), and
    /// every round-play operation (dealing a hand, building a judge keyboard, matching a submission)
    /// needs at least one lookup. XyzzyRoundService used to do this via CardCatalog.Questions.First(
    /// predicate) - an O(n) scan that was fine against the ~30/90-card hardcoded placeholder set but
    /// became a real, measured cost once real catalogs were actually loaded (up to ~2.3M comparisons
    /// to build one 10-card hand keyboard). These dictionaries are rebuilt alongside Questions/Answers
    /// any time either changes (LoadOverrideAsync), so they're never stale.</summary>
    public static XyzzyCard? FindQuestion(string? id) => id is not null && _questionsById.TryGetValue(id, out var card) ? card : null;

    public static XyzzyCard? FindAnswer(string? id) => id is not null && _answersById.TryGetValue(id, out var card) ? card : null;

    /// <summary>Called once at startup (Program.cs, right after IStateStore.InitializeAsync,
    /// before any hosted service can touch a game) - swaps in an imported catalog if this instance
    /// has one, otherwise leaves the hardcoded defaults in place untouched.</summary>
    public static async Task LoadOverrideAsync(IStateStore store, CancellationToken cancellationToken)
    {
        var questions = await store.LoadAsync<List<XyzzyCard>>(QuestionsKey, cancellationToken);
        var answers = await store.LoadAsync<List<XyzzyCard>>(AnswersKey, cancellationToken);
        var packs = await store.LoadAsync<List<XyzzyPack>>(PacksKey, cancellationToken);

        if (questions is { Count: > 0 })
        {
            _questions = questions;
            _questionsById = BuildIndex(_questions);
        }

        if (answers is { Count: > 0 })
        {
            _answers = answers;
            _answersById = BuildIndex(_answers);
        }

        if (packs is { Count: > 0 })
        {
            _packs = packs;
        }

        _defaultPackId = ComputeDefaultPackId(_packs);
    }

    private static string? ComputeDefaultPackId(IReadOnlyList<XyzzyPack> packs) =>
        packs.FirstOrDefault(p => p.IsDefault)?.Id ?? packs.FirstOrDefault()?.Id;

    /// <summary>Test-only escape hatch: LoadOverrideAsync deliberately can't clear Packs back to
    /// empty (an empty stored list means "nothing to override", same convention as
    /// Questions/Answers) - but a test that seeds pack-tagged cards via LoadOverrideAsync needs a
    /// way to undo that afterward, since CardCatalog is a shared static across the whole test run.</summary>
    internal static void ResetPacksForTesting()
    {
        _packs = [];
        _defaultPackId = null;
    }

    private static Dictionary<string, XyzzyCard> BuildIndex(IReadOnlyList<XyzzyCard> cards) =>
        cards.ToDictionary(c => c.Id);

    private static IReadOnlyList<XyzzyCard> DefaultQuestions { get; } =
    [
        new("q01", "What's that smell?"),
        new("q02", "I got 99 problems but _ ain't one."),
        new("q03", "What ended my last relationship?"),
        new("q04", "What's a girl's best friend?"),
        new("q05", "It's a pity that kids these days are all getting involved with _."),
        new("q06", "What did I bring back from Mexico?"),
        new("q07", "Instead of coal, Santa now gives the bad children _."),
        new("q08", "What's the next Happy Meal toy?"),
        new("q09", "During sex, I like to think about _."),
        new("q10", "What never fails to liven up the party?"),
        new("q11", "What's that sound?"),
        new("q12", "TSA guidelines now require the standard pat-down to include _."),
        new("q13", "What don't you want to find in your Chinese food?"),
        new("q14", "What are my parents doing right now?"),
        new("q15", "What's the new fad diet?"),
        new("q16", "When I am President, I will create the Department of _."),
        new("q17", "What will always get you laid?"),
        new("q18", "What am I giving up for Lent?"),
        new("q19", "Why can't I sleep at night?"),
        new("q20", "What's the most emo?"),
        new("q21", "What ended the dinosaurs?"),
        new("q22", "War! What is it good for?"),
        new("q23", "What's my superpower?"),
        new("q24", "This is the way the world ends. Not with a bang but with _."),
        new("q25", "Alternative medicine is now embracing the use of _."),
        new("q26", "What's the secret ingredient?"),
        new("q27", "What gets better with age?"),
        new("q28", "What am I bringing to the office party?"),
        new("q29", "What's the most disappointing?"),
        new("q30", "What did the doctor say I have?"),
        new("q31", "Give me two things that go poorly together.", AnswerCount: 2),
    ];

    private static IReadOnlyList<XyzzyCard> DefaultAnswers { get; } =
    [
        new("a01", "Being on fire."),
        new("a02", "A windmill full of corpses."),
        new("a03", "Genghis Khan."),
        new("a04", "A resounding meh."),
        new("a05", "The Pope."),
        new("a06", "Passive-aggressive Post-it notes."),
        new("a07", "An erection that lasts longer than four hours."),
        new("a08", "The heat of a thousand suns."),
        new("a09", "A minor case of ebola."),
        new("a10", "My inner demons."),
        new("a11", "Sweet, sweet vindication."),
        new("a12", "A gassy antelope."),
        new("a13", "Racially-motivated gerrymandering."),
        new("a14", "Not giving a shit about the Third World."),
        new("a15", "A robust mongoloid."),
        new("a16", "Vigorous jazz hands."),
        new("a17", "The Kardashians."),
        new("a18", "An asymmetric boob job."),
        new("a19", "Nicolas Cage."),
        new("a20", "A moment of silence."),
        new("a21", "Full frontal nudity."),
        new("a22", "Old-people smell."),
        new("a23", "A bag of magic beans."),
        new("a24", "The invisible hand."),
        new("a25", "Poor life choices."),
        new("a26", "A tiny horse."),
        new("a27", "The Illuminati."),
        new("a28", "Repressed childhood memories."),
        new("a29", "Man-eating sharks."),
        new("a30", "An awkward silence."),
        new("a31", "The placebo effect."),
        new("a32", "A really cool hat."),
        new("a33", "Explosions."),
        new("a34", "A brand new pair of underpants."),
        new("a35", "Not paying attention."),
        new("a36", "Autocorrect."),
        new("a37", "A team of scientists."),
        new("a38", "Grandma's secret sauce."),
        new("a39", "The dark side."),
        new("a40", "A really cool hat and matching sunglasses."),
        new("a41", "Karma."),
        new("a42", "A pyramid scheme."),
        new("a43", "The sweet release of death."),
        new("a44", "Aggressively going through the motions."),
        new("a45", "An unstoppable wave of nostalgia."),
        new("a46", "My relationship with my father."),
        new("a47", "Winning the lottery."),
        new("a48", "A subtle hint of madness."),
        new("a49", "Two midgets sharing a strap-on."),
        new("a50", "Being a motherfucker."),
        new("a51", "Amateur surgery."),
        new("a52", "A disappointing birthday party."),
        new("a53", "Emotional baggage."),
        new("a54", "The audacity."),
        new("a55", "A tastefully-timed sneeze."),
        new("a56", "Committing tax fraud."),
        new("a57", "A well-regulated militia."),
        new("a58", "Flesh-eating bacteria."),
        new("a59", "Not understanding the assignment."),
        new("a60", "My imaginary friend."),
        new("a61", "A truly excellent sandwich."),
        new("a62", "Blind rage."),
        new("a63", "The government."),
        new("a64", "A really persistent salesman."),
        new("a65", "Existential dread."),
        new("a66", "A well-timed dad joke."),
        new("a67", "Uncontrollable sobbing."),
        new("a68", "The entire cast of a reality show."),
        new("a69", "A rogue Roomba."),
        new("a70", "Peer pressure."),
        new("a71", "A suspiciously specific denial."),
        new("a72", "Regret."),
        new("a73", "An extremely small horse."),
        new("a74", "Doing it for the Vine."),
        new("a75", "A stern talking-to."),
        new("a76", "The power of friendship."),
        new("a77", "A haunted Roomba."),
        new("a78", "Sheer, unbridled ambition."),
        new("a79", "A really weird flex."),
        new("a80", "Mild disappointment."),
        new("a81", "The last slice of pizza."),
        new("a82", "An overpriced coffee."),
        new("a83", "A questionable life decision."),
        new("a84", "Too much eye contact."),
        new("a85", "A single, solitary tear."),
        new("a86", "Group chat drama."),
        new("a87", "The last cookie."),
        new("a88", "A poorly-timed pun."),
        new("a89", "My browser history."),
        new("a90", "An awkward high five."),
    ];
}
