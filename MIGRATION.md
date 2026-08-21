# Roboto: legacy-structure Linux/Docker migration plan

Working document tracking the port of the legacy WinForms/.NET Framework bot (`Roboto/`) to modern
.NET on Linux/Docker, **on this branch (`rewrite/legacy-structure-port`)** - a from-legacy port that
deliberately keeps legacy's actual class/dispatch/game-logic structure intact, replacing only what
must change to run modern/cross-platform. Not a rewrite: see the full plan and its rationale at
`/home/davepike/.claude/plans/rustling-launching-cake.md` for how this branch's approach differs from
the abandoned `rewrite/dotnet-docker-port` branch (kept, untouched, as a reference/parity-check
source and an infra-pieces-to-copy-from source - see that plan's "What happens to
rewrite/dotnet-docker-port").

**This file is temporary** - a working plan/progress-tracker, not durable project context. See
`CLAUDE.md` for durable orientation (repo layout, safety rules, dev environment, architecture
decisions). Detailed "what we tried, what broke, how it got fixed" narratives live as **comments in
the code itself**, not here - check the file you're about to change before assuming its current shape
is arbitrary.

## Phase status

| Phase | Status | Commit |
|---|---|---|
| 0. Branch + skeleton: new branch off `master`/`legacy-winforms-baseline`, SDK-style `.csproj` (net10, `Exe`) | Done, verified | `3bcc6e9` |
| 1. Drop WPF/WinForms: `LogWindow` removed, `Color?`-threaded logging → Serilog, chart rendering stubbed | Done, verified | `4adcb18` |
| 2. Telegram transport swap (`Telegram.Bot` package, preserving `Messaging`/`ExpectedReply`/dispatch contracts exactly) | Done, verified | — |
| 3. Persistence swap (`IStateStore` blob rows + relational tables), `.env`/`ROBOTO_INSTANCE` config, `logs` table + DB sink | Not started | — |
| 4. Real periodic background scheduler + `ChatKeyedLock` | Not started | — |
| 5. Hybrid keyboards (`InlineKeyboardMarkup`/`CallbackQuery` bridged into `ExpectedReply`) | Not started | — |
| 6. Charting: ScottPlot on legacy's own `stats.cs` data shape | Not started | — |
| 7. Test harness + business-logic test suite | Not started | — |
| 8. Migrator retarget (`XmlImporter` → new decomposed store) | Not started | — |
| 9. Carry-forward deltas (multi-answer, bot self-de-admin, Add Bots, judge-kick-skip, bolded winner, real Abandon confirm, pack-default fix, pagination fix, kick-below-MinPlayers) | Not started | — |
| 10. Cutover prep | Not started | — |

"Verified" means actually built + run, not just "compiles" - see commit messages and in-code
comments for what was specifically tested and any bugs caught along the way.

## What's been found and fixed so far (phase 0/1)

Two **real, pre-existing legacy bugs** (not introduced by this port - both reproduce identically on
`master` given the same trigger), found by actually running the app rather than just getting it to
compile:

- `mod_xyzzy_card.TempCategory`'s back-compat `[XmlElement("category")]` shim collided with its own
  obsolete `category` field under modern .NET's stricter `XmlSerializer` duplicate-element-name
  checking. Fixed with `[XmlIgnore]` on the field - see `Modules/ModuleStorage/mod_xyzzy_classes.cs`'s
  comment.
- `Plugins.getPluginData(Type)` threw unconditionally on a miss instead of returning `null`, making
  `initPluginData()`'s "create fresh data for a module that doesn't have any yet" fallback dead code -
  crashed startup on a genuinely first-ever run with no pre-existing settings file. Never noticed in
  production because it's run continuously off one already-populated settings file since this was
  introduced. Fixed - see `Core/Plugins.cs`'s comment.

**Known issue, not yet fixed** (belongs to phase 3, would be throwaway work to patch now):
`settings.cs`'s `foldername` uses a literal `\Roboto\` path separator - harmless-but-wrong on Linux
today (produces flat oddly-named files instead of a real subdirectory tree, confirmed during phase 0's
smoke test) rather than a crash. Phase 3's `.env`/`ROBOTO_INSTANCE`/`{DataDir}/{Instance}/` config
swap replaces this path-resolution scheme entirely, so no separate fix needed before then.

## Phase 2 notes (Telegram transport swap)

`TelegramAPI.cs`'s hand-rolled `HttpWebRequest`+`JObject` layer is now the `Telegram.Bot` package
throughout (`postExpectedReplyToPlayer`, `getUpdates`, `createKeyboard`, `getChatMembersCount`), and
`Storage/message.cs` builds from a typed `Telegram.Bot.Types.Message` instead of a raw `JToken`.
Every module-facing call signature (`Messaging.SendMessage`/`SendQuestion`/`SendPhoto`,
`TelegramAPI.createKeyboard`) is unchanged in shape; only `createKeyboard`'s and
`ExpectedReply.keyboard`'s *type* changed (`string` → `List<List<string>>`, plain button-label rows -
**not** a real `ReplyKeyboardMarkup` directly, see below), which every call site just passed through
opaquely already, so this was a mechanical propagation across ~17 sites, not a redesign. Dispatch
itself - the per-plugin `chatEvent` loop, `Messaging.parseExpectedReplies`, the `ExpectedReply`
matching/queueing machinery - is untouched, just now driven from `Update`/`Message` objects instead
of `JToken`s.

**Real design finding, not just a mechanical port**: `ExpectedReply` (and therefore its `keyboard`
field) is part of the XML-serialized `Roboto.Settings.expectedReplies` graph - `XmlSerializer` cannot
serialize a real `Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup` at all (its `Keyboard` property
is `IEnumerable<...>`-typed, which throws `NotSupportedException` at `XmlSerializer` construction,
caught during phase 2 verification, not just a compile-time issue). Kept `ExpectedReply.keyboard` as
plain serializable `List<List<string>>` button-label rows instead, and added
`TelegramAPI.BuildReplyMarkup` to build the real typed keyboard only at the actual send boundary. This
decouples "what's persisted" from "what's sent" - a distinction legacy's own raw-JSON-string design
already implicitly had, that a naive typed port would have silently broken.

Verified via a real smoke test end-to-end: with the placeholder API key, `Client` construction
correctly rejects it (`ArgumentException: Bot token invalid`), logged and looped exactly like legacy's
own resilience model (no crash). With the real `beefy` test-bot token (from the abandoned rewrite
branch's leftover `data/beefy/bot.env`), the call reached the real Telegram API and got back a genuine
`409 Conflict: terminated by other getUpdates request` - proof of a real, successfully-authenticated
round trip (something else was already holding a long-poll on that same token; not a bug here).

## What's still open

Everything from phase 3 onward - see the phase table above and the full plan file for what each phase
actually involves, the four explicitly-confirmed architecture decisions (hybrid keyboards, real
background scheduler, decomposed persistence + relational tables for whole-bot lists, carry-forward
deltas), and the resolved/open sub-decisions (chatPriority sort - decided, implement; card/pack ID
scheme - open, needs a decision during phase 5; daily XML backup - decided, not needed, TrueNAS
snapshots instead; background-scheduler batching caps - decided, keep as legacy has them).
