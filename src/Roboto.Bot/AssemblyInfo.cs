using System.Runtime.CompilerServices;

// Roboto.Migrator (phase 11's XML importer) reuses XyzzyRoundService's internal hand/judge
// keyboard-building logic (BuildHandKeyboard/BuildJudgeKeyboard/CombinedAnswerText) so a resumed
// in-flight game's DmOutbox entries are built exactly the same way the live engine builds them -
// not a second, drift-prone reimplementation of the same formatting.
[assembly: InternalsVisibleTo("Roboto.Migrator")]
[assembly: InternalsVisibleTo("Roboto.Bot.Tests")]
