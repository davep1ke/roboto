using System.Collections.Generic;

namespace RobotoChatBot.Modules
{
    /// <summary>
    /// Static content for the "ZZ Dummy Pack" (see mod_xyzzy_coredata.seedDummyPack()) - 10 real
    /// questions + 10 real answers, randomly sampled once from chat_against_humanity_bot's real
    /// migrated data (then hand-filtered for language/decency - the raw random draw pulled in
    /// several non-English cards and one referencing a real mass-shooting perpetrator, neither of
    /// which belong in a bootstrap pack every fresh instance ships with), not re-sampled at runtime.
    /// Replaces the old hardcoded 7-pack stub list, which assumed the official CAH decks could
    /// always be (re-)synced live from CrCast - no longer true, CrCast no longer lists them.
    /// </summary>
    internal static class mod_xyzzy_dummy_pack_seed
    {
        /// <summary>(uniqueID, text, nrAnswers) - nrAnswers matches the number of "__" blanks.</summary>
        public static readonly List<(string uniqueID, string text, int nrAnswers)> Questions = new List<(string, string, int)>
        {
            ("zzdummy-q1", "__ and __ are playing Cards Against Humanity. Meta!", 2),
            ("zzdummy-q2", "__ became invisible.", 1),
            ("zzdummy-q3", "I'm sorry, it's my first day as __. First day jitters.", 1),
            ("zzdummy-q4", "Surprisingly, Canterlot has a museum full of __.", 1),
            ("zzdummy-q5", "The biggest challenge for me today is __.", 1),
            ("zzdummy-q6", "The least inappropriate card I have is __.", 1),
            ("zzdummy-q7", "I love __ so much!", 1),
            ("zzdummy-q8", "If only faces could __!", 1),
            ("zzdummy-q9", "__ is the breakfast of champions.", 1),
            ("zzdummy-q10", "The reason my last relationship failed is because __", 1),
        };

        public static readonly List<(string uniqueID, string text)> Answers = new List<(string, string)>
        {
            ("zzdummy-a1", "beer"),
            ("zzdummy-a2", "Bruce Wayne"),
            ("zzdummy-a3", "My favorite video game."),
            ("zzdummy-a4", "Leggies!"),
            ("zzdummy-a5", "Small iron"),
            ("zzdummy-a6", "An Unaware Clefable"),
            ("zzdummy-a7", "A soulless ginger"),
            ("zzdummy-a8", "a man who is so cool that he smokes a pipe"),
            ("zzdummy-a9", "Squirming like a fish."),
            ("zzdummy-a10", "Sexy Grandpapa"),
        };
    }
}
