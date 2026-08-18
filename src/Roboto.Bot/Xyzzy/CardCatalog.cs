namespace Roboto.Bot.Xyzzy;

/// <summary>AnswerCount > 1 ("Pick 2"+) only matters on question cards - a player must submit that
/// many cards before they're done for the round, and the judge sees them joined with " >> " rather
/// than substituted into the question's blank(s) (see XyzzyRoundService.CombinedAnswerText -
/// deliberately not reproducing legacy's regex-based per-blank interleaving).</summary>
public sealed record XyzzyCard(string Id, string Text, int AnswerCount = 1);

/// <summary>
/// The default (and, for v1, only) card pack: a modest hardcoded sample of the public Cards
/// Against Humanity base set (CC BY-NC-SA-licensed, hence safe to reproduce here). This is a
/// placeholder, not permanent content - no real card text exists anywhere in this repo (legacy
/// only ever held it in the live production XML), so this stands in until the phase-11 XML
/// migration importer brings across real data. CardCast/CRCast pack import (legacy's way of
/// getting more packs) is explicitly out of scope for v1 - see MIGRATION.md.
///
/// IDs are short, stable, human-debuggable strings (not GUIDs like legacy) - they get embedded
/// directly in inline-keyboard callback_data (format: xy:&lt;action&gt;:&lt;groupChatId&gt;:
/// &lt;round&gt;:&lt;cardId&gt;), and Telegram caps callback_data at 64 bytes.
/// </summary>
public static class CardCatalog
{
    public static IReadOnlyList<XyzzyCard> Questions { get; } =
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

    public static IReadOnlyList<XyzzyCard> Answers { get; } =
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
