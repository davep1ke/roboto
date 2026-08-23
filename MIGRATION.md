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
| 2. Telegram transport swap (`Telegram.Bot` package, preserving `Messaging`/`ExpectedReply`/dispatch contracts exactly) | Done, verified | `4afcef9` |
| 3. Persistence swap (`IStateStore` blob rows + relational tables), `.env`/`ROBOTO_INSTANCE` config | Done, verified | `3ed15cb` |
| 3b. Split the xyzzy card/pack catalog out of its blob into the `xyzzy_cards`/`xyzzy_packs` tables | Done, verified | `0d8e1c3` |
| 3c. `logs` table + custom Serilog DB sink + 30-day purge task | Done, verified | `0d8e1c3` |
| 4. Real periodic background scheduler + `ChatKeyedLock` | Done, verified | `521b9eb` |
| 5. Hybrid keyboards (`InlineKeyboardMarkup`/`CallbackQuery` bridged into `ExpectedReply`) | Deferred - user call, see notes | — |
| 6. Charting: ScottPlot on legacy's own `stats.cs` data shape | Done, verified | `a98f277` |
| 7. Test harness + business-logic test suite | Done, verified (partial coverage - see notes) | `28d4714` |
| 8. Migrator retarget (`XmlImporter` → new decomposed store) | Done, verified against a real copy of production XML | — |
| 9. Carry-forward deltas (multi-answer, bot self-de-admin, Add Bots, judge-kick-skip, bolded winner, real Abandon confirm, pack-default fix, pagination fix, kick-below-MinPlayers) | Done, verified - see notes (most items were already-true-by-construction, not actual deltas) | `abe3dea` |
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

**Known issue at the time, since fixed in phase 3** (see phase 3 notes below): `settings.cs`'s
`foldername` used a literal `\Roboto\` path separator - harmless-but-wrong on Linux (produced flat
oddly-named files instead of a real subdirectory tree, confirmed during phase 0's smoke test) rather
than a crash. Deliberately left unfixed in phase 0/1 since patching it then would have been throwaway
work - phase 3's `.env`/`ROBOTO_INSTANCE`/`{DataDir}/{Instance}/` config swap replaced the whole
path-resolution scheme it lived in.

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

## Phase 3 notes (persistence + config swap)

**Scope decision, flagged explicitly**: the plan's "real relational tables for whole-bot growing
lists" was narrowed on implementation to what the user's own wording actually called for -
`expected_replies`, `stats`, and `chat_presence` (all genuinely whole-bot-scoped) are real tables;
`quotes`/`birthdays` are **not** split out into their own tables, since they're per-chat-scoped lists
nested inside `mod_quote_data`/`mod_birthday_data`, not whole-bot lists, and stay inside those
modules' own blob rows. The xyzzy card/pack catalog (`mod_xyzzy_coredata.questions`/`.answers`/
`.packs`) has a real scale argument for its own tables independent of that framing (up to 72k/230k
cards in the largest real production export seen on the abandoned rewrite branch - one blob holding
that would be a multi-MB read/write on every save) - split out into `xyzzy_cards`/`xyzzy_packs` in
phase 3b (see its own notes below).

**Timing model, flagged explicitly**: every table (including the new `expected_replies`/`stats`/
`chat_presence` ones) is flushed via a full delete+reinsert at `settings.save()`, matching the exact
same "mutate in memory, periodically flush everything" timing legacy's XmlSerializer round-trip
always had - **not** per-mutation write-through. Real durability of in-flight `ExpectedReply` state
(what a user is mid-conversation about) across an unclean shutdown is therefore not actually delivered
by "it's a table now" alone - that's a separately-scoped, real follow-up, called out so it isn't
assumed solved.

**Design**: `Persistence/SqliteStateStore.cs` - one `state` blob table (key→JSON, `IncludeFields =
true` is load-bearing, see its own comment and the sanity-check below) for small/bounded per-item
state (a chat's own scalars + each `(chat, module)` pair's data, each module's own global core-data,
top-level `settings` config), plus the three real tables. `chat.chatData`/`settings.pluginData`/
`settings.chatData` are all `[JsonIgnore]`'d on their containing object and reconstructed from their
own per-item blob rows in `settings.load()`/written in `settings.save()` - sidesteps
`System.Text.Json` polymorphic-list serialization entirely (each blob row holds exactly one concrete
type, known at the call site via `plugin.pluginDataType`/`plugin.pluginChatDataType`/`item.GetType()`
- the non-generic `IStateStore.Load(Type, string)`/`Save(Type, string, object)` overloads exist
specifically for this, mirroring `Plugins.cs`'s own existing `Activator.CreateInstance(pluginDataType)`
reflection pattern). `InstanceBootstrapper.cs`/`BotOptions.cs` (adapted from the abandoned rewrite
branch, dropped the `IOptions<T>`/DI wrapping) replace `-context`/`%appdata%\Roboto\<context>.xml`
with `ROBOTO_INSTANCE`/`ROBOTO_DATADIR` env vars + `{DataDir}/{Instance}/bot.env` - `-plugin` (module
allow-list) is kept as a CLI flag as-is, unrelated/low-blast-radius. `Roboto.cs`'s `startBackground()`
bootstraps the instance/store before `Plugins.initPluginAssemblies()`/`settings.load()`, same ordering
constraint `Plugins.getPluginDataTypes()` always had for `XmlSerializer`'s `extraTypes`.

**Bug fixed as a side effect**: `logging.cs`'s log-file path used the same literal-backslash bug
flagged (not fixed) in phase 0 - fixed properly here now that `Roboto.Options.InstanceDir` exists to
build a real path from via `Path.Combine`.

**Real, load-bearing catch, confirmed by deliberately breaking it**: `System.Text.Json` only
serializes properties by default, not fields - and every data model class in this codebase (`chat`,
`ExpectedReply`, every module data type, ...) uses public fields throughout. Without
`JsonSerializerOptions.IncludeFields = true`, every blob would silently round-trip as `{}` - no
exception anywhere, the most dangerous kind of bug. Verified this is genuinely load-bearing (not just
theoretical) by toggling it off, confirming the `settings:config` blob really did come back as `{}`,
then reverting.

**Verified** via real smoke tests, not just compiling: (1) first-ever run with no `bot.env` yet -
correctly creates the stub file and exits cleanly, no crash; (2) with the real `beefy` test-bot token,
full startup completes (all 6 modules' blobs written, `chats:index` written, `stats` table populated)
and the real 409-Conflict round trip from phase 2 reproduces identically; (3) **a genuine restart**
against the same on-disk `roboto.db` - `stats` rows with real recorded data (`Critical Errors`/`High
Errors`, both hit during startup's own critical-level log lines) correctly load and match as "already
exists" on the second run, proving actual round-trip persistence, not just successful writes. (Two
xyzzy-specific stat types show "added" again on restart rather than "already exists" - expected, not a
bug: they were registered but never actually measured in the test window, so no row existed to
reconstruct in the first place; a `statType` with zero recorded slices behaves identically either way.)

## Phase 3b notes (xyzzy card/pack catalog tables)

`mod_xyzzy_coredata.questions`/`.answers`/`.packs` are `[JsonIgnore]`'d out of that module's own blob
row and persisted via the real `xyzzy_cards`/`xyzzy_packs` tables instead (`SqliteStateStore.
LoadXyzzyCards`/`SaveXyzzyCards`/`LoadXyzzyPacks`/`SaveXyzzyPacks`), wired into `settings.load()`/
`save()` as a special case for that one module type (found via `pluginData.OfType<mod_xyzzy_coredata>
().FirstOrDefault()`). A genuinely fresh instance (no rows in `xyzzy_packs` yet) keeps whatever
`mod_xyzzy_coredata`'s own field initializers already supply (the 7 default CAH packs, empty
questions/answers) rather than the load path overwriting them with an empty load result - same
fresh-instance-fallback convention every other module already had via `Plugins.initPluginData()`.

**Verified**: a fresh instance seeds and persists the 7 default packs correctly; a genuine restart
proof (not just "doesn't crash") - hand-edited one pack's `total_picks` directly in the DB via
`sqlite3`, restarted, and confirmed the edited value (not the field-initializer default) survived,
proving real load-from-table rather than silent re-seeding.

## Phase 3c notes (DB logging + purge)

`Core/DbLogSink.cs` - a small custom `Serilog.Core.ILogEventSink` writing to the new `logs` table
(`SqliteStateStore.WriteLogEvent`), added to `logging.cs`'s `LoggerConfiguration` alongside (not
instead of) the console sink. Fully additive/best-effort: wrapped in a bare `try/catch` that silently
drops a failed write rather than risking a recursive failure-logging-a-failure spiral, and safely
no-ops via `Roboto.Store?.` for the handful of very-early log lines (the "ROBOTO" startup banner, etc)
that happen before `Roboto.Store` is constructed (this codebase's static `Roboto.log = new logging()`
field initializer runs before `startBackground()`'s instance bootstrap). Genuinely write-through per
log line (not batched to `settings.save()`'s timing model like the rest of this phase) - a log line
capturing what happened is the whole point, so it needs to survive a crash on its own.

30-day purge lives in `mod_standard.backgroundProcessing()` (`SqliteStateStore.PurgeLogsOlderThan`),
throttled to once/day via a new `mod_standard_data.lastLogPurgeDateTime` field, the same pattern
`lastSaveToDiskDateTime` already used for throttling `settings.save()`.

**Verified**: real startup produces real rows in `logs` with correct level/timestamp/message content,
confirmed by direct SQLite inspection. The purge path itself (correct SQL, follows the exact same
delete-with-cutoff pattern already verified for `expected_replies`/`chat_presence`/`stats`) wasn't yet
exercised for real, since `Plugins.backgroundProcessing()` doesn't fire on a live timer until phase 4 -
flagged to confirm once phase 4's scheduler is running and actually driving it repeatedly.

## Phase 4 notes (real background scheduler + `ChatKeyedLock`)

**`Core/BackgroundScheduler.cs`**: a genuine dedicated `Thread`, not a timer embedded in the message
loop - calls `Plugins.backgroundProcessing(false)` every 60s (matching `mod_xyzzy`'s own already-
declared `backgroundMins=1`), started from `Roboto.cs`'s `startBackground()` right before
`Messaging.processUpdates()`.

**Correction (found later, prompted by the user noticing real production log volume looked higher
than they remembered)**: the original phase-4 research here concluded legacy never ran this live on
a timer *in any version checked* - true for every commit from `a7e2f79` (2020-05-16) onward, but that
research didn't trace back far enough to catch that this was itself a regression, not the original
design. Before `a7e2f79` (confirmed present as late as `e8ca7d2`, 2020-04-14), `Settings.
backgroundProcessing(false)` was called at the end of *every* iteration of the main message loop -
`a7e2f79`'s own diff shows the exact moment it moved from inside `while (!endLoop)` to after it,
during a plugin-system refactor whose commit message doesn't mention background processing at all
(an accidental casualty, not a deliberate redesign). Since `getUpdates` long-polls with a 60s timeout
(`waitDuration`, still in `settings.cs` today), that loop naturally cycled about once every 60s in a
quiet chat - and every module's own `backgroundMins` throttle (`mod_xyzzy`'s own comment: `backgroundMins
= 1; //every 1 min, check the latest 20 chats`) only makes sense against a live cadence roughly that
frequent. So this scheduler isn't new behavior after all - it's restoring a ~6-year-old regression to
close to its original cadence, coincidentally landing on almost exactly the same 60s figure
independently (matched to `mod_xyzzy`'s declared throttle, not to this history - confirmed correct
by accident, not by tracing it correctly the first time).

The batching caps in `mod_xyzzy_coredata` (5 full-checks + 50 mini-checks per pass) are kept exactly
as legacy had them, not simplified now that a live timer exists - confirmed still load-bearing (a
real background pass genuinely takes measurable time even across a small number of games).

**`Core/ChatKeyedLock.cs`**: per-key (chat/user ID) mutual exclusion, needed because a second thread
now genuinely runs concurrently with live message dispatch for the first time in this codebase's
history - legacy was safe with zero locking anywhere specifically because it was structurally single-
threaded (confirmed by the same trace above). Deliberately **not** the AsyncLocal-based reentrant
design an equivalent primitive needed on the abandoned rewrite branch - that complexity was specific
to async code (a continuation can resume on a different thread, breaking simple thread-based
reentrancy). This codebase is fully synchronous throughout, so a plain `lock` (`Monitor.Enter`/`Exit`)
gives correct per-thread reentrancy for free, with no extra machinery - confirmed directly (not just
reasoned about) with a standalone 3-part test: (1) two different threads acquiring the *same* key
genuinely serialize (one measurably waits for the other), (2) two threads acquiring *different* keys
run genuinely concurrently rather than contending on one global lock (proving this actually delivers
concurrent throughput, not just "on a separate thread but still fully blocking"), (3) the same thread
re-acquiring the same key it already holds doesn't deadlock.

`GlobalListsKey` (reserved as `0`, never a real chat/user ID under Telegram's own ID namespace rules)
protects `Roboto.Settings`' own top-level lists (`chatData`, `pluginData`, `expectedReplies`,
`RecentChatMembers`, `stats.statsList`) - **every** direct read/write of these across the whole
codebase was audited and locked: `Core/Messaging.cs` (`expectedReplies` - the highest-traffic one,
touched on every message), `Core/Presence.cs` (`RecentChatMembers`), `Core/Plugins.cs` (`pluginData`),
`Core/stats.cs` (`statsList`), `Core/Chats.cs` (`chatData`), `settings.cs`'s own `save()` (snapshots
every list under the lock before the actual - potentially slow - DB writes, then holds the lock again
specifically around the three real-table writes since those touch each item's own nested fields, not
just the outer list), and every module's own `backgroundProcessing()` that iterates `chatData`
directly (`mod_xyzzy`, `mod_quote`, `mod_birthdays`, `mod_steam` - `mod_wordcraft`/`mod_standard` don't
touch it directly). One direct unlocked mutation was also found and fixed outside these
(`mod_xyzzy_chatdata.cs`'s player-removal cleanup was calling `Roboto.Settings.expectedReplies.Remove`
directly instead of the now-locked `Messaging.removeReply`). Two methods in
`mod_xyzzy_coredata.cs` (`removeACard`/`removeQCard`, reachable from a live pack import/sync, which
touch *every* chat's game state to remap removed-card references) got the same snapshot-then-per-chat-
lock treatment `mod_xyzzy.backgroundProcessing` uses. `removeDormantPacks`/`removePack` were left
unlocked with a comment - confirmed genuinely dead code (legacy's own call site is commented out) -
and `removeDupeCards`/`replaceCardReferences` needed no changes at all, turning out to already be
entirely inside a `/* ... */` block comment, not real compiled code.

**Lock granularity, deliberately**: never held across a Telegram/network API call - every fix above
locks only the actual in-memory list touch (a snapshot, an Add/Remove, or - for the three real-table
saves in `settings.save()`, and `stats.cs`'s in-memory-only methods - a bounded, fast, purely-local
operation), releasing before any `sendMessage()`-shaped call. Holding `GlobalListsKey` across a slow
network call would have meant one chat's slow outbound message blocking every other chat's live
dispatch - exactly the "background processing blocks everything" problem this whole phase exists to
avoid, just relocated to a different call site.

**Known, deliberately accepted limitation, not solved by this pass**: locking makes the shared list
*structures* safe (no more concurrent-modification exceptions/corruption), but doesn't make every
check-then-act sequence built on top of them fully atomic - e.g. `Messaging.processNewExpectedReply`'s
"does this user already have an outstanding message" check and its subsequent decision to send-or-
queue are two separate lock acquisitions, so a genuinely concurrent add for the same user landing in
between is a real, narrow, deliberately-accepted residual race (worst case: a queueing-order hiccup,
not data corruption or a crash). Fully closing this would need either a bigger queueing-algorithm
redesign or holding a lock across the send call itself (ruled out above) - flagged rather than silently
assumed away.

**Verified**: build clean; a real 75-second live run against the `beefy` test bot showed the scheduler
firing exactly on its 60s interval (`Messaging.backgroundProcessing`, `Dormant Chat Check`, and
`XYZZY - background` all completing with zero exceptions) *while* the message loop kept polling and
handling the real 409-Conflict round trip from phase 2 concurrently - genuine concurrent execution, not
serialized-behind-a-big-lock. The `ChatKeyedLock` standalone test above gives direct evidence for the
mechanism itself, not just "the live run didn't crash."

## Phase 5 - deferred (hybrid keyboards)

Revisited with the user before starting phase 6. The plan's original "hybrid keyboards" decision
flagged a genuinely open sub-decision it hadn't resolved ("card/pack ID scheme... needs a closer
look during phase 5, not assumed up front"), and working through the actual tradeoff surfaced a
bigger question the plan hadn't asked: does this bot need `InlineKeyboardMarkup`/`CallbackQuery` at
all, given `ReplyKeyboardMarkup` already works, is fully tested (phase 7), and legacy's own
single-outstanding-question queue in `Messaging` exists specifically as a workaround for
`ReplyKeyboardMarkup`'s "only one active keyboard per chat" limitation - i.e. the one concrete
problem inline keyboards would actually solve already has a working mitigation in place.

Discussed directly with the user: for a bot with a small, familiar player base, the benefit (cleaner
chat history, per-message-attached buttons instead of one shared keyboard) is a real but modest UX
nicety, not a fix for something broken - and pursuing it now would mean either touching most modules'
keyboard call sites and rewriting all of phase 7's `TapButton`-based tests, or building bridge
infrastructure with only one demo call site actually using it. **Decided: skip phase 5 entirely for
now.** `ReplyKeyboardMarkup` stays exactly as legacy had it everywhere. Revisit only if the
single-outstanding-question queueing behavior actually causes real friction in practice - at that
point the plan file's original design (`CallbackQuery` bridged into the *same* `ExpectedReply`
matching path, not a parallel dispatch mechanism) is still the right shape to build.

## Phase 6 notes (charting: ScottPlot on legacy's own `stats.cs` data shape)

**`Core/stats.cs`'s `generateImage`**: was a stub since phase 1 (chart rendering data-gathering
logic - `statType.getSeries`, `getMatchingSeries`'s exact+regex series selection - was already
chart-library-agnostic and kept as-is; only the actual image rendering was missing, per that
method's own doc comment). Now builds a `ScottPlot.Plot` directly from the existing
`List<statSeriesData>` (one `Scatter` per matched series, ordered oldest-to-newest since
`statType.getSeries` produces points newest-first), returns PNG bytes via `GetImageBytes(1200, 600,
...)` wrapped in a `MemoryStream` - same `Stream generateImage(List<string> series)` signature
`mod_standard.cs`'s `/statgraph` handler already called, so that call site needed only a one-line
filename fix (`.jpg` → `.png`, since the output format actually changed - not a legacy behavior to
preserve, just a filename that stopped matching reality).

**Rendering model reused from the abandoned rewrite branch's `StatGraphCommand.BuildPlot`/`Densify`**
(`src/Roboto.Bot/Commands/StatGraphCommand.cs` on `rewrite/dotnet-docker-port`), per the plan's own
direction - filled-area for cumulative (`statmode.increment`) series, plain line for gauge-like
(`statmode.absolute`) ones, legend, title/axis labels. `Densify` itself wasn't needed: legacy's own
`statType.getSeries` already produces a dense, evenly-spaced array (it calls `getSlice(point)` for
every one of `graphYAxisCount` (192) points regardless of whether real data exists there, unlike the
rewrite's sparse per-bucket dictionary), so there's no gap-filling step to port - confirmed this is
legacy's actual existing behavior, not something newly introduced. Legacy's `granularity`/
`graphYAxisCount` constants (15 min × 192 = 48h) already matched the window the rewrite's renderer
was independently modeled on, so no window-size decision was needed either.

**Deliberate simplification, not a legacy-fidelity loss**: `statType`'s separate `displaymode.bar`/
`.line` flag (set per-stat-type, e.g. "Startup"/"BotAPI Timeouts"/"Chats Purged" are registered as
`bar`, most others `line`) is not honoured in the rendered output - every series renders as a
line/filled-area regardless. The reused rewrite model never distinguished them either, and mixing
literal bar charts with line charts on one overlaid multi-series plot (recall `/statgraph` can chart
several regex-matched series at once) gets visually messy for little payoff. `statSeriesData` still
carries `displayMode` (now also `statMode`, added so `generateImage` doesn't need to re-look-up each
series' mode from its title) for a future pass to pick back up if it turns out to matter.

**`statSeriesData`** gained a `statMode` field (previously only `displayMode`) - `generateImage`
needs to know cumulative-vs-gauge per series to decide the fill, and looking that back up from the
matched `statType` list via title-string-parsing was fragile; carrying it on the DTO directly (like
`displayMode` already was) is simpler and matches the existing shape.

**`Roboto.csproj`**: added `ScottPlot` `5.1.59` (same version the abandoned rewrite branch used).
`System.Drawing.Common` stays - `statType`/`statSeriesData` still store colors as
`System.Drawing.Color` throughout the codebase (every module's `registerStatType` call), and
`generateImage` just converts to `ScottPlot.Color` at the render boundary rather than threading a
new color type through every call site - smaller, more contained change.

**Sanity-checked by breaking something**: forced `generateImage`'s "no matching series" branch to
always trigger (`if (matches.Count == 0)` → `if (true)`) and confirmed both
`StatGraphWithNoArgsChartsEverythingAndSendsAPng` and `StatGraphWithAnExactSeriesNameCharts` failed
as expected (no photo sent), then reverted and confirmed all 15 tests passed again.

**Verified**: build clean; `dotnet test` green across repeated runs (15/15, up from 12 after phase
7). Not yet verified live against the `beefy` test bot - `/statgraph` is a read-only, deterministic
command (no persisted state, no player-facing game logic), so the automated PNG-magic-bytes test
plus a build was judged sufficient before a live round-trip; still worth a live check whenever the
next live-bot session for a different phase happens anyway.

## Phase 7 notes (test harness + business-logic test suite)

Explicitly scoped by the user as a partial-coverage "yardstick," not a re-derivation of the abandoned
branch's full ~198-test suite: "get as many of those tests working as makes sense - obviously some
will fail as elements haven't been implemented." 12 tests added, all green, stable across repeated
runs.

**`tests/Roboto.Tests/TestHarness.cs`**: this codebase is 100% static-global state by design
(`Roboto.Settings`/`Roboto.Store`/`Roboto.Options`/`Plugins.plugins`/`TelegramAPI`'s cached client) -
no DI, so there's no way to give each test its own isolated instance the way the abandoned rewrite
branch's DI-per-test `TestBot` could. `TestHarness`'s constructor instead repoints every static at
fresh state before each test (new temp SQLite file, fresh in-memory `settings.load()`, a fresh
`FakeTelegramBotClient`) and disables xUnit parallelization assembly-wide (`AssemblyInfo.cs`) since
shared statics can't tolerate concurrent tests. `Plugins.plugins` itself (the module *objects*, not
their data) is scanned once per process, not once per test - see the `pluginExists` bug below.

**`Roboto/APIs/TelegramAPI.cs`**: `Client`'s declared type changed from concrete `TelegramBotClient`
to the `ITelegramBotClient` interface it already implements (Telegram.Bot's own extension methods are
defined against the interface anyway), plus a test-only `SetClientForTesting` hook - this is the only
production-code change this phase needed. The per-update dispatch body that lived inline in
`getUpdates()`'s foreach loop was extracted verbatim into a new public `DispatchUpdate(Update)` method
so tests can drive one update through the exact same logic without a real long-poll; `getUpdates()`
now just calls it per update. Pure code-motion, no behavior change.

**`tests/Roboto.Tests/FakeTelegramBotClient.cs`**: single `SendRequest<TResponse>` chokepoint
(matches how `ITelegramBotClient`'s own extension methods work), adapted from the abandoned rewrite
branch's fake of the same name. Deliberately keeps full per-row keyboard structure rather than
flattening it - "tapping a button" here means sending the button's exact label as a new message
(legacy's actual `ReplyKeyboardMarkup` behavior; hybrid inline keyboards are phase 5, not built yet),
so a test needs the real row/label shape to find the right button to tap.
`TestHarness.TapButton(userId, text)` is literally `SendPrivateMessage(userId, text)` - validated as
sufficient because `ExpectedReply`'s match predicate for DM-based flows (`m.chatID == e.userID`) is
satisfied by any private message from that user, no reply-to-message-id wiring needed.

**Bug found, not fixed (flagged in-code, not production-critical)**: `Plugins.pluginExists`/
`typeDataExists` both compare `t.GetType() == existing.GetType()` where `t` is already a `Type`
object - `t.GetType()` returns `System.RuntimeType` (the type of the `Type` object itself), never
`t`, so the comparison is always `false`. Harmless in production only because
`initPluginAssemblies()`/`registerData()` each run exactly once per process there; would silently
duplicate entries if that invariant ever broke. `Plugins.ResetPluginDataForTesting()` (new, test-only)
works around it by never re-scanning plugins after the first `TestHarness`, only clearing each
plugin's cached `localData` between tests.

**Namespace collision hit while setting up the test project**: `namespace Roboto.Tests;` made C#
resolve `Roboto.Options`/`Roboto.Store`/`Roboto.Settings` as a namespace-path lookup (treating
`Roboto` as an enclosing-namespace prefix) instead of the `RobotoChatBot.Roboto` class's static
members, throwing `CS0234`. Fixed by using `namespace RobotoTests;` (no dot) for the C# namespace,
while keeping the project/folder/assembly name as `Roboto.Tests` (unrelated, no conflict there).

**Coverage added**: `AdminCommandsTests` (mute/unmute, mute suppresses non-exempt modules, first-
admin bootstrap), `XyzzyGameFlowTests` (full round: start → "Use Defaults" → join → "Start" → deal
hands → both non-judge players answer → judging triggers automatically → judge picks a winner → point
awarded and next round auto-starts; plus under-`MinPlayers` rejection and idempotent re-join) -
seeded via synthetic cards written directly into `mod_xyzzy_coredata.questions`/`.answers` under
`mod_xyzzy.primaryPackID`, since the real catalog only populates via a live CardCast/CrCast network
import. `QuoteTests` (add → retrieve, cancel-aborts-cleanly), `BirthdayTests` (add → list data,
remove), `SteamTests` (the two network-free commands only - see below).

**Sanity-checked by breaking something** (matching this project's established convention): commented
out `winner.wins++` in `judgesResponse` and confirmed `XyzzyGameFlowTests.
FullRoundAwardsAPointAndStartsTheNextRound` failed as expected, then reverted and confirmed all tests
passed again.

**Deliberately not covered this phase** (the "some will fail/some are left out" part of the ask):
- `mod_steam`'s `/steam_addplayer` and `/steam_check` flows - both call the real Steam Web API
  synchronously (`mod_steam_steamapi.cs`'s `WebClient` calls), no fake HTTP client exists yet. Only
  the two network-free commands (`/steam_help`, `/steam_stats` with no players) are tested.
- `mod_wordcraft` - not touched at all this pass, purely a time/priority call (breadth across the
  four other modules was judged more valuable than five-module completeness).
- Anything depending on not-yet-built phases: hybrid inline keyboards/`CallbackQuery` (phase 5),
  ScottPlot charting (phase 6), migrator retarget (phase 8).
- `ChatKeyedLockTests` re-derivation (flagged in the original plan as still worth porting from the
  abandoned branch) - not done this pass; phase 4's own live-bot verification already gave direct
  evidence the mechanism works, so this was judged lower priority than breadth across modules.

## Phase 9 notes (carry-forward deltas)

The plan's one-line list of 9 "rewrite improvements" turned out to conflate several genuinely
different situations, only clear after checking each against the abandoned rewrite branch's actual
commits (not just its own terse phase-table wording) and against real legacy (`legacy-winforms-
baseline`) directly. Surfaced to the user before writing any code (a genuine fork the plan hadn't
resolved), who made two calls: keep legacy's real judge-kick/leave behavior rather than the rewrite's
simplification, and build "Add Bots" as a real feature now rather than deferring it.

**Already true here, no code needed** - these were only ever *rewrite* gaps (closed there to reach
parity with legacy), not things legacy itself lacked, so a from-legacy port already has them:
- **Multi-answer questions** - `mod_xyzzy_card.nrAnswers`, `logAnswer`'s multi-blank gating,
  `judgesResponse`'s regex-based multi-blank substitution are all legacy-native, confirmed present in
  `legacy-winforms-baseline` verbatim. Already covered by phase 7's `XyzzyGameFlowTests`.
- **Bolded winning answer** - `judgesResponse` already wraps the winning card(s) in `*...*` and sends
  via `Messaging.SendMessage(chatID, message, null, true)` (markdown on). Legacy-native.
- **Kick-below-MinPlayers stops the game** - `removePlayer`'s existing `players.Count <= 2` check
  already calls `wrapUp()` regardless of whether the removed player is a bot or human. The rewrite
  only needed a fix here because of its own bot-auto-fill feature masking the check for bot-specific
  kicks - a problem that doesn't exist on this branch (see "Add Bots" below - no auto-fill was built).
- **Pack-default-semantics** - the rewrite's own commit for this (`d24a007`) is titled "revert pack
  default-semantics to match legacy exactly" - it was fixing the *rewrite's own* inverted convention,
  not adding anything legacy lacked. A from-legacy port never had the divergence to begin with.

**Genuine legacy bugs, fixed and documented (not silently reproduced)** - matches this project's
established "fix it and comment why" convention (`CLAUDE.md`'s two phase-0 examples):
- **Pack-list pagination off-by-one** (`mod_xyzzy_chatdata.sendPackFilterMessage`) -
  `totalPageCount = (count / maxPacksPerPage) + 1` produced a phantom empty trailing page whenever
  the pack count was an exact multiple of `maxPacksPerPage` (30). Confirmed present in real legacy;
  the rewrite's own commit explicitly called this "a bug worth just fixing, not parity-worthy" rather
  than reproducing it. Fixed to ceiling division, `Math.Max(1, (count + maxPacksPerPage - 1) /
  maxPacksPerPage)`. Covered by `PackListPaginationHasNoPhantomTrailingPageOnAnExactMultiple`.
- **Abandon confirm didn't check which button was tapped** (`mod_xyzzy.cs`'s `"Abandon"` reply
  handler) - the Yes/No confirmation dialog is legacy-real, but the handler abandoned the game on
  *any* reply, including "No". Confirmed present in real legacy. Fixed: only abandons on "Yes", sends
  "Not abandoned." otherwise. Covered by `AbandonConfirmOnlyAbandonsOnYes`/
  `AbandonConfirmAbandonsOnYes`.

**A deliberate no-op, not a bug fix** - **judge-kick/leave**: legacy's real behavior when the judge
is kicked mid-round (`removePlayer`) is to re-pick a new judge and resume judging on the *same*
round's already-collected answers (calling `beginJudging(true)` again) - this already works, is
already ported verbatim, and needed no changes. The rewrite had replaced this with "just deal a fresh
round" as a deliberate simplification in its own context; asked directly, the user chose to keep
legacy's real behavior here rather than adopt that simplification, matching this branch's whole
reason for existing (preserve legacy nuance, don't re-derive/simplify it away). Covered by
`KickingTheJudgeMidJudgingReassignsTheSameRoundRatherThanDealingAFreshOne`.

**Genuinely new features, not legacy behavior at all:**

- **Bot self-de-admin** (`TelegramAPI.DispatchUpdate`) - if the bot is promoted to admin in a group,
  immediately strips its own rights back off (`PromoteChatMember` with every permission left at its
  default `false` - there's no separate "demote" API) and explains why
  (`TelegramAPI.BotSelfDeAdminExplanation`). Wired off `Update.MyChatMember`, which Telegram includes
  in `getUpdates()`'s default update set with no explicit `allowedUpdates` opt-in needed (unlike
  `chat_member`, which covers *other* members and isn't included by default) - so no change was
  needed to the polling call itself, only to `DispatchUpdate`'s handling. Only reacts to a fresh
  promotion (old status not already admin), not every no-op `MyChatMember` update. Legacy had no
  admin-only functionality and never reacted to this update type at all.
- **"Add Bots"** (`mod_xyzzy_player.isBot`, `mod_xyzzy_chatdata.addBots`/`nextRobotName`,
  `askAddBotsCount`) - lets a player deliberately pad a game out with computer-controlled players,
  reachable from both the Invites setup screen and the `/xyzzy_settings` menu (blocked mid-round -
  `Question`/`Judging` - since a bot added then would have no dealt hand and nothing would ever
  prompt it to answer). Bot playerIDs are synthetic negative values (real Telegram user IDs are
  always positive), an unambiguous "never DM this" signal in `askQuestion` (bots skip the DM, answer
  immediately with a random card from their dealt hand instead, looping per the question's
  `nrAnswers` same as a real player across several taps) and `beginJudging` (a bot judge picks a
  random submitted answer immediately rather than being sent a keyboard). A same-process safety stop
  (top of `askQuestion`) ends the game if every remaining player is a bot - ported from the rewrite's
  own equivalent guard, which it confirmed load-bearing there via a real `StackOverflowException`
  during development, not just a failed assertion; ours is untested against that specific failure
  mode but exists for the identical reason (auto-answer -> auto-judge -> next round chaining forever
  with nobody watching once no human remains). No `MinPlayers` auto-fill was built (unlike the
  rewrite) - the existing `players.Count > 2` gate on the "Start" button is untouched; "Add Bots" is
  purely opt-in padding a player chooses, not an automatic top-up. Robot-themed names ported verbatim
  from the rewrite's own list. Covered by
  `AddBotsLetsASoloStarterReachThreePlayersAndPlayARound` (full round: solo starter adds 2 bots,
  starts, both bots auto-answer without ever being DMed, tzar judges and awards a bot the point).

**Sanity-checked by breaking something**: disabled the Abandon "Yes" check (always abandon) and
confirmed `AbandonConfirmOnlyAbandonsOnYes` failed; disabled the bot auto-answer loop entirely (`&&
false`) and confirmed `AddBotsLetsASoloStarterReachThreePlayersAndPlayARound` failed (status stuck on
`Question`, judging never triggered) - both reverted, full suite green again afterward.

**Verified**: build clean; `dotnet test` green across repeated runs (21/21, up from 15 after phase
6). Not yet verified live against the `beefy` test bot - flagged as still worth doing before this
phase is considered fully done, particularly for "Add Bots" (the highest-risk new logic this phase
added) and bot self-de-admin (depends on a real Telegram group promoting the bot, not easily
exercised any other way).

## Post-phase-9 addenda: grand-total stats, and a broad test-coverage push

With the port itself functionally complete (phases 5-9 done or deliberately deferred; only phase 8
(migrator) and phase 10 (cutover) remain, both squarely "data migration" work rather than porting),
two follow-up requests: a stats feature the user recalled from the abandoned branch that hadn't
landed here, and a push to close as much of the phase-7/9 test-coverage gap as reasonably possible
before moving on to migration.

**Grand-total stats** (`Core/stats.cs`): legacy's stats were always a pure 15-min/48h rolling window
(`statSlices`, pruned by `removeOldData`) - there was never a genuine all-time counter. Confirmed via
the abandoned branch's own "Phase 14.2: stats engine dual-track" commit message: *"a genuine all-time
Total (the rewrite's own addition, legacy never had this)"*. Added the same idea onto legacy's
`statType` shape: a `total` field that accumulates forever for `statmode.increment` stats (mirrors
the latest value for `statmode.absolute` gauges, where summing wouldn't mean anything), plus
`totalSince`, which self-initializes on first `logStat` call so it comes out right for both a
brand-new stat type and one that already existed with `total=0` before this field existed. Surfaced
via `mod_xyzzy.getStats()` (already `/stats`' per-module contributor) as two new lines: total games
started and total hands played, "since &lt;date&gt;" - necessarily starting from zero at deploy time,
since the pre-existing rolling window has nothing further back to backfill from. 3 tests.

**Broader test coverage** (24 -> 53 tests): `ChatKeyedLockTests` (3, direct coverage of phase 4's
primitive - couldn't reuse the abandoned branch's own version, which tests a different AsyncLocal-
based reentrant design this branch deliberately didn't port); `WordcraftTests` (3, mod_wordcraft had
zero coverage before this); `XyzzySettingsTests` (9: Kick, Change Score, Mess With, Reset, Extend,
Re-deal, Force Question, pack filtering All/None/toggle); `XyzzyMoreCoverageTests` (6: Timeout/Delay
settings, the background `check()` timeout-skip path via a backdated `statusChangedTime` rather than
real wall-clock time, pack-pagination Next/Prev, both `/xyzzy_leave` variants);
`QuoteMoreCoverageTests` (4: `/quote_conv` multi-line conversations, `/quote_config`, the auto-quote
background announcement); `SteamTests` gained 2 more; `StatsGrandTotalTests` (3) and
`MessagingGuardTests` (2) cover the two items above.

**A real bug found and fixed along the way, not just tested around**: `Messaging.
processNewExpectedReply`'s group-targeted-question branch (`isPrivateMessage:false`) sent the message
but never registered the `ExpectedReply` for matching - any reply to it was silently lost. Confirmed
byte-for-byte present in `legacy-winforms-baseline` (including the original author's own `//TODO -
doesnt handle group PMs` comment marking it incomplete), so this predates the port entirely - a live
production bug, not something introduced here. Affected 5 call sites, leaving `mod_quote`'s
`/quote_config` (Set Duration + its retry) and `mod_steam`'s `/steam_addplayer` **completely
non-functional end-to-end** - including `/steam_addplayer`'s very first prompt, not just its retry,
meaning nobody had ever been able to successfully add a Steam player through this command. Discussed
directly with the user (this is a real behavior change, not just a test addition) - all 5 call sites
migrated to `isPrivateMessage:true`, and a `NotImplementedException` guard added to the broken branch
itself (fires only when `expectsReply:true` - ordinary fire-and-forget group messages, the
overwhelming majority of traffic, are unaffected) so the same mistake can't be silently reintroduced.
Sanity-checked by disabling the guard and confirming `MessagingGuardTests` caught it, then reverted.

**Sanity-checked by breaking something** (this addendum's own new logic, beyond what's noted per-item
above): disabled the grand-total `total +=` line and confirmed all 3 `StatsGrandTotalTests` failed;
disabled the `Messaging.cs` guard and confirmed `MessagingGuardTests` caught it. Both reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (53/53). Not yet verified live
against the `beefy` test bot.

### Second coverage pass: birthdays background, admin/quiet-hours, multi-answer round, background reconcilers

Continuing the same push, working through the remaining named gaps one at a time (53 -> 70 tests):

- **`mod_birthdays` background announcement** (3 tests): the day-of `Happy Birthday` message,
  confirmed silent on a non-matching day, and confirmed it doesn't double-announce if
  `backgroundProcessing` runs twice the same day (`lastDayProcessed` gating).
- **`mod_standard` admin/quiet-hours** (9 tests, `AdminCommandsTests`): `/help`'s own explicit mute
  check (separate from `chatIfMuted`'s chatEvent-level exemption); the default `/start` welcome
  message; full quiet-hours set/disable/invalid-retry flow; `/addadmin`'s existing-admins keyboard
  picker; `/removeadmin` (successful pick, and the no-admins-yet case).
- **Multi-answer ("Pick 2") round** (1 test, `XyzzyGameFlowTests`): a full round with a 2-answer
  question - each non-judge player prompted for a second card, judging combines both cards per
  player with `" >> "`, matching `logAnswer`'s existing (legacy-native) multi-answer gating that
  only had single-answer coverage until now.
- **CardCast import cancel** (1 test, `XyzzySettingsTests`): the network-free half of "Import Pack" -
  actually importing needs the real CardCast/CrCast API with no local validation before the network
  call, so (matching the same constraint already noted for `mod_steam`) only Cancel is covered.
- **Background reconcilers** (4 tests, new `BackgroundReconcilersTests`): `SqliteStateStore`'s
  `logs`-table 30-day purge (`PurgeLogsOlderThan`, both "removes only rows past the cutoff" and
  idempotency once they're already gone) and `Chats.removeDormantChats`.

**A second real, notable finding** (not a bug to fix, just worth documenting - and now covered by a
test rather than only living in a comment): `mod_birthdays.chatEvent` unconditionally fetches
`c.getPluginData<mod_birthday_data>()` at the top, for *every* message regardless of which command
matched - confirmed byte-for-byte identical in `legacy-winforms-baseline`. Since
`mod_birthday_data.isPurgable()` is unconditionally `false` ("Never purge chats with birthday data")
and `chat.tryPurgeData()` only purges when *every* module's chat data reports purgable, this means
**any chat that has ever exchanged a single message can never actually be dormant-purged** - the
`removeDormantChats` feature is reachable in the unit-test sense (confirmed via
`ChatDataThatIsAllPurgableGetsRemovedByTryPurgeData`, which strips the auto-created birthday data to
test the mechanism in isolation) but not in practice for any real chat. No `TODO` or similar comment
marks this one (unlike the group-question bug) - it reads as an emergent interaction between two
independently-reasonable module decisions, not a known-incomplete path. Flagged, not changed.

**Sanity-checked by breaking something**: disabled the multi-answer "ask for next card" gate
(`player.selectedCards.Count != question.nrAnswers` → `false`) in `logAnswer` and confirmed
`MultiAnswerQuestionRequiresTwoCardsPerPlayerAndCombinesThemForJudging` failed (judge never saw a
combined `" >> "` answer), then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (70/70, up from 53). Not yet
verified live against the `beefy` test bot.

### Third pass: fake HTTP seam for Steam/CardCast, and a real cross-platform crash found by using it

Closes the last named test gap - `mod_steam`'s network-calling paths and actually importing a
CardCast pack (not just cancelling), both previously deferred for lack of a fake HTTP client.

**`mod_steam_steamapi.HttpGetOverride` / `cardCast.HttpGetOverride`** (both `internal static
Func<string, string>`, `null` in production): both modules' real HTTP calls already funnelled
through one `sendPOST` chokepoint each (same shape in both files - build a URL, GET it, parse JSON),
so the fix mirrors `TelegramAPI.SetClientForTesting`'s existing pattern exactly - when set, `sendPOST`
calls the override with the fully-built request URL and treats the return value as the raw JSON
response body, skipping the real `HttpWebRequest`/`HttpWebResponse` path entirely. `TestHarness`
resets both to `null` before each test (same "process-global static, reset per test" convention as
`Plugins.ResetPluginDataForTesting`), so a test that doesn't touch either never sees a previous test's
override leak in.

**A real, confirmed production bug found while wiring this up, not a test-only issue**: both
`sendPOST` methods compute `Encoding enc = Encoding.GetEncoding(1252)` unconditionally, verbatim from
legacy. The very first attempt at a fake-backed Steam test threw
`NotSupportedException: No data is available for encoding 1252` immediately - confirmed this isn't a
test artifact by checking `legacy-winforms-baseline` directly (byte-for-byte identical code): it only
ever worked because legacy ran on .NET Framework on Windows, where code page 1252 is registered by
default. Modern .NET on Linux - this branch's actual deployment target - doesn't have it registered,
so **every real call to either the Steam Web API or the CardCast/CrCast API would crash on first
contact in production**, encoding-provider-missing, before even reaching the parsing logic. Neither
integration had ever been exercised end-to-end before this pass, so this had never surfaced. Fixed to
`Encoding.UTF8` (what both APIs actually encode JSON responses as, and what virtually every modern
REST API uses) in both files - a genuine cross-platform port gap being closed, not a legacy behavior
worth preserving. Also removed a dead, never-referenced `WebClient client = new WebClient();` line in
`mod_steam_steamapi.cs` while in the same spot (legacy-verbatim leftover from before
`HttpWebRequest` was the real mechanism there).

**Coverage added**: `SteamTests` +2 (`/steam_addplayer` with a public profile - added, achievement
tracking confirmed; with a private profile - rejected, not added); `XyzzySettingsTests` +2
(CardCast import of a genuinely new pack - pack/questions/answers all land in
`mod_xyzzy_coredata`, its filter auto-enabled for the importing chat; an API-level failure - reprompts
cleanly rather than crashing).

**Sanity-checked by breaking something**: reverted `mod_steam_steamapi.cs`'s encoding fix back to
`Encoding.GetEncoding(1252)` and confirmed both new `SteamTests` failed with the exact
`NotSupportedException` this fix closes, then reverted back to `Encoding.UTF8`.

**Verified**: build clean; `dotnet test` green across repeated runs (74/74, up from 70).

### Docker: `Dockerfile` / `docker-compose.yml` / `.dockerignore`

No Docker setup existed on this branch at all until now, despite it being part of this project's own
established phase-verification checklist. Adapted from the abandoned rewrite branch's own
`Dockerfile`/`docker-compose.yml` (reusing the *idiom* - multi-stage build, non-root `$APP_UID`,
`/data` bind mount, `ROBOTO_INSTANCE` env var - not its architecture), retargeted at this branch's
actual layout: `Roboto/Roboto.csproj` (not `src/Roboto.Bot/`), assembly `Roboto.dll`.

**Multi-stage build**: `mcr.microsoft.com/dotnet/sdk:10.0` to restore/publish, `mcr.microsoft.com/
dotnet/runtime:10.0` (Debian-based, not `-alpine` - musl + SkiaSharp native deps is a known pain
point there) for the final image. Restore is its own layer (copies just the `.csproj` first) so
dependency layers cache across code-only changes.

**Carried over directly because it applies here too, not just copied for convenience**: the
abandoned branch's runtime-image fontconfig/`fonts-dejavu-core` install. ScottPlot (phase 6's
`/statgraph`) renders through SkiaSharp - confirmed a direct transitive dependency here
(`~/.nuget/packages/skiasharp*`) - and the bare runtime image ships no fonts at all, so without this
every chart's title/axis labels/legend would silently render blank in a real container (SkiaSharp
doesn't error when it can't find a font, it just draws no text) even though local dev testing always
looks fine (a normal desktop Linux already has system fonts installed). This exact failure mode is
why the abandoned branch added the fix in the first place, and it applies identically here since
this branch's phase 6 landed on the same ScottPlot/SkiaSharp stack independently.

**`docker-compose.yml`**: single `roboto-bot` service, `ROBOTO_INSTANCE` env var (default `default`,
matching `Roboto.cs`'s own `Environment.GetEnvironmentVariable("ROBOTO_INSTANCE") ?? "default"`),
`./data:/data` bind mount (matching `BotOptions.DataDir`'s own `/data` default and
`InstanceBootstrapper`'s `{DataDir}/{Instance}/bot.env` layout), and a `user:` override
(`${DOCKER_UID:-1000}:${DOCKER_GID:-1000}`) so the container can actually write into the bind-mounted
directory - the Dockerfile's baked-in non-root user owns `/data` *inside the image*, but the bind
mount overlays that with the host directory's own ownership instead.

**Verified for real, not just "should work"**: `docker compose build` succeeds cleanly (confirmed
twice, including after the unreachable-code cleanup below). `docker compose run --rm -e
ROBOTO_INSTANCE=smoketest -v <scratch-dir>:/data roboto-bot` against a throwaway host directory (not
`./data`) actually starts the container, resolves the instance/data-dir env vars, and exercises
`InstanceBootstrapper.TryLoad`'s real first-run path end-to-end: creates `smoketest/bot.env` with the
expected stub content, logs `"No config found for instance 'smoketest'..."`, and exits cleanly (no
crash, no hang) - and the created file is owned by the host user (1000:1000), confirming the
bind-mount permission handling actually works, not just builds. Scratch directory discarded after.

**Incidental cleanup found via the build**: `docker compose build`'s own warning output surfaced two
`CS0162: Unreachable code detected` warnings - a trailing `return null;` after a try/catch that
always either returns or throws, left over in both `cardCast.sendPOST` and `mod_steam_steamapi.
sendPOST` by the `HttpGetOverride` refactor earlier in this same session. Removed from both; rebuilt
and reran the full test suite (74/74 still green) to confirm the cleanup was inert.

### Live round-trip against `beefy` - two real bugs found and fixed

Ran the actual current build live, for real, against Telegram (`ROBOTO_INSTANCE=beefy-livetest`, a
fresh local instance so this branch's SQLite schema never touched the abandoned rewrite branch's own
stale `data/beefy/roboto.db`). Had to stop two leftover processes first - a bare `dotnet Roboto.Bot.dll`
process (running since Aug 20) and a `docker compose`-managed container (`roboto-roboto-bot-1`, up 2
days), both from the abandoned branch and both still holding the one active long-poll slot Telegram
allows per bot token (409 Conflict until cleared). Confirmed with the user before stopping either,
since a memory note from a prior session says not to proactively stop a running test bot - that
guidance was about not tearing down a session's *own* deliberately-left-running instance, not a
blanket rule blocking all future live testing; the user confirmed stopping the stale ones was fine.
Startup itself came up clean (real Telegram auth, no errors, background scheduler + main thread both
started) with no code changes needed - the first genuinely-live verification since phase 4.

The user then drove real interactive testing (game start, join, "Add Bots", answer, judge) directly
against the running bot and found two real bugs neither the automated suite nor the author had caught:

**1. "Skipped these chumps" for a bot that never got a turn.** Root cause: `logAnswer`'s "is
everyone done, start judging" check used `outstandingResponses()` - which counts `ExpectedReply`
objects - but bot players (`mod_xyzzy_player.isBot`, "Add Bots", phase 9) never get one at all, since
they're deliberately never DMed. In a solo-player-plus-bots game (no other humans), that meant the
check read "everyone's answered" the instant the *first* bot in the auto-answer loop submitted -
before later bots in that same loop had gone - because there were zero real ExpectedReplies left to
be "outstanding" regardless of what the bots had actually done. `allPlayersAnswered()` (used
elsewhere, e.g. `check()`) already does this correctly, checking each non-judge player's real
`selectedCards` count instead - bot-agnostic by construction. Swapped `logAnswer` to use it too.
While fixing this, found and fixed a second, latent instance of the identical root cause a few lines
above it: the "pick your next card" prompt for a multi-answer question was sent unconditionally,
without an `isBot` guard (unlike `askQuestion`'s own dealing loop, which does guard it) - harmless
for the single-answer questions exercised so far, but would have tried to DM a bot's synthetic
negative player ID the first time a multi-answer question came up with bots in play. Both fixed
together; `AddBotsLetsASoloStarterReachThreePlayersAndPlayARound` (previously a false-negative - it
only asserted *a* winner could be picked, not that both bots actually got to answer) strengthened to
assert both bots' answers appear in the judging keyboard and no one is listed as skipped. Sanity-
checked by reverting to the old `outstandingResponses()`-based check and confirming the strengthened
test failed with exactly the shape reported live (1 judging option instead of 2), then reverted.

**2. Missing spacing around a blank glued directly to its neighbouring words.** `judgesResponse`'s
win-message formatting (`Regex "_+"` substitution) pasted the winning answer straight into the
question text with no added spacing - confirmed byte-for-byte identical in `legacy-winforms-baseline`,
so this predates the port, but the user asked for a genuine improvement here, not preservation. Real
imported pack data is inconsistent about whether a blank already has spaces around it
("I found ___ when I was cleaning" vs "I found_when I was cleaning", the shape the user's own live
pack actually produced) - a blind space-insertion would have double-spaced the already-correct case.
Fixed with a targeted heuristic per the user's explicit caution ("pack data is very hit and miss," "if
the `_` is followed by a full stop, we don't want to space that out"): only insert a space where the
character immediately adjacent to the blank is a letter/digit glued on with nothing in between -
already-spaced cards are untouched (the neighbour is already a space), and a blank immediately before
punctuation (e.g. "I like to ___.") never gets a space wrongly inserted before it, since punctuation
isn't a letter/digit. 3 new tests: the glued case (spaces added both sides), the already-spaced case
(no double-space - narrowly asserted around the substitution itself, not the whole message, since the
winner-name line has its own unrelated pre-existing quirk, a trailing space from `userFullName`'s
`firstName + " " + emptySurname` construction colliding with the literal `" wins a point!"` that
follows it), and the trailing-punctuation case (no space before the period). Sanity-checked by
disabling both space-insertion branches and confirming the two spacing-sensitive tests failed (the
punctuation test correctly did not, since nothing about that path changed), then reverted.

Both fixes verified against the live bot itself, not just the test suite: killed and restarted the
`beefy-livetest` instance with the fixes applied so the user could re-verify against the real thing
rather than trusting the automated tests alone.

**Verified**: build clean; `dotnet test` green across repeated runs (77/77, up from 74). Live
`beefy-livetest` instance restarted with both fixes and left running (per this project's own
convention - never stopped on a guessed schedule) for the user's continued live verification.

### Final sweep against legacy and the abandoned rewrite branch, before moving to migration

With phases 5-9 done/deliberately deferred and the two live-bot bugs fixed, did one last systematic
pass looking for anything still missing before starting phase 8 (migrator): every business-logic-
relevant commit on the abandoned `rewrite/dotnet-docker-port` branch (all ~65 commits after its own
"Begin major rewrite" branch point - everything before that is legacy's own history, already
inherited here by construction) checked against real `master` and this branch's current code, split
across two passes (recent xyzzy-specific phases 14.x-19, and module-port/earlier phases 8.x-9).

**Recent-phases pass: nothing missing.** Every item checked (infinite-timeout bug, duplicated-answer
blanks, pack selection in the setup wizard, `/xyzzy_get_settings` content, round-flow wording, chat-
name stamping, the "3 discrepancy fixes" bundled into phase 16) turned out to be either a bug the
*rewrite* introduced in its own from-scratch reimplementation (and legacy's real code, carried
forward here verbatim, was already correct), or specific to the rewrite's own architecture (DmOutbox,
its own audit-log/keyboard-column design) with no equivalent gap on this branch.

**Module-ports pass: one real bug found and fixed.** `mod_steam_steamapi.getAchievements()` returned
every achievement *defined for a game*, not just ones the player had actually unlocked - the Steam
`GetUserStatsForGame` response carries an `achieved` 0/1 flag per entry that was never read. Confirmed
byte-for-byte present in `legacy-winforms-baseline` (a real, pre-existing legacy bug, not introduced
by this port), and confirmed the abandoned rewrite branch (`ccbf2852`) had explicitly found and fixed
this exact issue rather than reproducing it. Effect: `mod_steam_player.checkAchievements()` treats
anything not yet in a player's local `chievs` list as "new," so a player's very first check announced
*every* achievement in the game as just-earned, including ones never unlocked. Fixed by filtering to
`achieved == 1` before adding to the result list, matching this project's established "fix it and
comment why" convention. Covered by
`SteamTests.GetAchievementsOnlyReturnsOnesThePlayerHasActuallyUnlocked` (mixed achieved/locked
fixture, asserts only the unlocked one comes back).

Everything else checked in this pass (enum-by-name persistence, quiet-hours interaction with xyzzy
timeouts, settings-menu completeness, mod_quote/mod_birthdays/mod_wordcraft command surfaces) was
already correct/covered. Two migrator-relevant data-shape gotchas surfaced along the way, worth
remembering once phase 8 starts (not code changes now, just noted): (1) any XML-deserialization
shadow/mirror class needs explicit `[XmlType]` attributes matching legacy's real element names -
without it `XmlSerializer` silently defaults to the C# class name and every count comes back as
**zero, not an exception** (the exact bug the abandoned branch's own migrator hit); (2) reconciling
`ExpectedReply` counts during import needs to account for stale replies attached to chats not in
`Question`/`Judging` state, and replies referencing an already-purged chat ID with no matching chat
record.

**Sanity-checked by breaking something**: reverted the `achieved == 1` filter (made the branch
unconditionally `false`) and confirmed the new test failed exactly as expected (both the locked and
unlocked achievement came back), then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (78/78, up from 77).

### Tech-debt sweep (checked-in binaries, dead project files)

User asked directly ("why do we have the Newtonsoft DLLs still there?") - checked the whole tree for
similar leftovers rather than just that one file.

**Removed** (unreferenced, unambiguous, confirmed by a clean build after removal):
- `Roboto/Newtonsoft.Json.dll` / `Newtonsoft.Json.xml` (~1MB) - legacy's pre-NuGet checked-in
  reference DLL, present since the repo's very first commit. `Roboto.csproj` already pulls
  Newtonsoft via `PackageReference` (per CLAUDE.md's own description of the intended shape) - these
  were dead weight nobody deleted when that switch happened.
- `Roboto/Properties/app.manifest` - a WinForms/.NET-Framework UAC manifest, not referenced by the
  SDK-style `Roboto.csproj` (no `<ApplicationManifest>` property) at all. Should have gone with the
  rest of WPF/WinForms in phase 1; missed.

**Checked, kept as-is by explicit user choice**: `Roboto/icons/*` (~660KB, unreferenced in code -
possibly the bot's real uploaded Telegram profile picture / brand assets) and `Roboto/CallGraphs/*.dgml`
(2 VS diagram exports, excluded from compile, original author's own commit flagged them "not sure if
current" - kept anyway as a rough legacy-flow reference).

**Checked, confirmed already-known/deliberately-accepted, not new debt** - not touched:
`Plugins.pluginExists`/`typeDataExists`'s `Type`-comparison bug (phase 7 notes - flagged in-code,
harmless in production, has a documented test-only workaround); `Roboto.sln`'s many stale
per-instance build configurations from legacy's WinForms multi-context setup (already flagged in
CLAUDE.md as non-load-bearing for the actual `dotnet build`/`dotnet test` workflow); the scattered
`//TODO` comments throughout `Core/`/`Modules/` (all pre-existing in real legacy, not introduced by
this port - left alone per this project's "don't restructure adjacent working code" convention).

**Verified**: build clean after removal; `dotnet test` green (78/78, unchanged - confirms nothing
depended on the removed files).

## Phase 8 notes (migrator retarget)

The plan file's own prediction held up in practice: "the importer can most likely deserialize
straight into the real live types with `XmlSerializer(typeof(settings), Plugins.getPluginDataTypes())`
- the exact mechanism `settings.load()` already used [before phase 3] - eliminating the shadow-class
layer [the abandoned rewrite branch] needed." Since this branch's classes are still legacy's own
classes (not a redesigned shape), that's exactly what `settings.loadFromLegacyXml(xmlPath)`
(`settings.cs`) does - deserializes a legacy XML export straight into the live `settings`/`chat`/
module-data object graph, then the *already-existing, already-tested* `settings.save()` writes it to
the target SQLite store. No shadow classes, no separate mapping layer - the whole importer is a few
dozen lines plus a thin CLI (`Migrator/Roboto.Migrator.csproj`, new top-level project alongside
`Roboto/`/`tests/`, referencing `Roboto.csproj`; `Roboto/AssemblyInfo.cs` extended with
`InternalsVisibleTo("Roboto.Migrator")` for the same reason `Roboto.Tests` already needed it -
driving `Plugins.initPluginAssemblies()`/`getPluginDataTypes()` from a separate entry point).

**Safety**: read-only against the source XML (only ever opened via `StreamReader`). Real legacy XML
persisted the live Telegram token in `telegramAPIKey` (confirmed - `data/robotolive.xml`, a real 2021
production export, has one) - `loadFromLegacyXml` scrubs `telegramAPIKey`/`telegramAPIURL`/
`botUserName` back to their unconfigured defaults immediately after deserializing, on top of
`save()` already never persisting those three fields (`JsonIgnore`'d from the SQLite blob) - two
independent layers, not relying on either alone. The CLI (`Migrator/Program.cs`) defaults to a dry
run (parses + reports counts, writes nothing); `--real` is required to actually write, and refuses to
touch a target instance directory that already has data unless `--force`. Never writes a
`TelegramToken` anywhere - the target's `bot.env` is `InstanceBootstrapper`'s normal first-run blank
stub, left for a human to fill in with a **test** bot token by hand.

**Validation**: `Roboto/Persistence/ImportReport.cs` - counts (chats, plugin-data modules, expected
replies, recent chat members, stat types/slices, xyzzy catalog, xyzzy players, quotes, birthdays, plus
a per-module "how many chats have this module's data" breakdown), computed identically from the
parsed source and from a fresh `settings.load()` after a real write - `ImportReport.Diff` is what
actually proves round-trip fidelity (CLAUDE.md's "validate with counts/checksums... rather than
eyeballing it"), not either report alone. A real `--real` run without `--force` also refuses to
proceed if the target already has data.

**Two real, serious, previously-undetected bugs found while building this** - both in phase 3's
SqliteStateStore persistence layer itself, not the migrator's own logic, and both invisible until now
because no existing test had ever exercised a full `save()`-then-reload round trip with real populated
per-chat module data (phase 7's tests all operate on in-memory state within one `TestHarness` instance
and never call `.save()`; phase 3's own verification restarted a real process, but only checked the
`stats` *table* specifically, which doesn't go through either of the code paths below):

1. **`mod_xyzzy_player` couldn't be deserialized by `System.Text.Json` at all.** Two public
   parameterized constructors, no public parameterless one - STJ's default reflection-based converter
   only auto-selects a constructor when there's a public parameterless one or *exactly one* public
   parameterized one; with two and no `[JsonConstructor]` disambiguation, it has no usable candidate
   and throws `NotSupportedException` on every attempt. Since `mod_xyzzy_player` lives inside
   `mod_xyzzy_chatdata.players` (a per-chat blob field), this meant **any chat with real players in
   its xyzzy game would crash `settings.load()` entirely** on any restart - not just lose that chat's
   data, the exception propagates past the whole method, taking startup down. Fixed with
   `[JsonConstructor]` on the internal parameterless constructor
   (`Modules/ModuleStorage/mod_xyzzy_classes.cs`). Audited every other class with the same
   "internal parameterless ctor + public parameterized ctor(s)" shape across `Modules/`/`Storage/`/
   `Core/`/`Helpers/` (17 candidates) - `mod_xyzzy_player` was the only one with *multiple* public
   parameterized constructors; everything else has exactly one, which STJ's automatic single-ctor
   parameter-matching already handles (independently confirmed working for `mod_xyzzy_card` by phase
   3b's own real restart test).
2. **Every restart silently reset every chat's per-module state to fresh defaults, for that entire
   run.** `chat`'s only public constructor - the one STJ picks automatically, being the sole public
   parameterized one - calls `initPlugins()`, which stub-populates `chatData` with one fresh entry per
   registered module *before* `settings.load()`'s own per-module loop runs (`chatData` is empty at
   that point, so `initPlugins()`'s "do we already have this module's data?" check finds nothing and
   adds a stub for every module, real data or not). `settings.load()`'s loop then **appended** the
   real loaded row instead of replacing the stub, leaving both in the list with the stub first - and
   `chat.getPluginData<T>()`/`getPluginData(Type)` both return the *first* match. Net effect: every
   module lookup after any restart silently got the fresh, empty stub instead of the real persisted
   state, for that whole run (the real data was still sitting in the SQLite blob row underneath and
   would resurface correctly on the *next* restart, since `save()`'s per-type-key upsert means
   whichever of [stub, real] is last in the list - the real one, appended after - wins when it's
   re-persisted - but every module's live behavior in between saw nothing there). Fixed:
   `settings.load()`'s per-chat loop now does `c.chatData.RemoveAll(existing => existing.GetType() ==
   plugin.pluginChatDataType)` before adding the loaded row, so it replaces the stub instead of
   sitting alongside it. A module with genuinely no saved row yet (freshly added, or never touched by
   that chat) correctly keeps its `initPlugins()` stub, unchanged.

Both found and fixed via the same synthetic-fixture approach as the tests below - built a `settings`
object programmatically (not hand-authored XML, so it stays correct as the schema evolves), serialized
it with the exact same `XmlSerializer` shape real legacy XML uses, then drove it through
`loadFromLegacyXml` → `save()` → `settings.load()` and asserted the counts matched. Neither bug is
migrator-specific - both affect the *live app's own* restart behavior right now, on every instance,
independent of any migration work. Given the severity (bug 1 crashes startup outright; bug 2 silently
discards live game/quote/birthday state for a whole session on every restart), this needed fixing
before any real import was trusted, not deferred as a "known limitation."

**A third finding, real but benign, not a bug**: a genuine import against `data/robotolive.xml` (a
real 2021 production export) initially reported a mismatch - 23 stat types before, 13 after. Root
cause: 10 of those 23 had zero recorded slices (e.g. "Critical Errors", "New Games Started" - real
production, but never actually fired). `SqliteStateStore`'s `stats` table only stores per-slice rows
(`stat_name, module_type, time_slice` primary key) - a type with no slices has nothing to persist and
genuinely can't survive a save+reload through this schema. Not data loss: every module re-registers
its own stat types fresh on every startup regardless of persistence (already noted as expected in
phase 3's own verification, for the identical reason), so an empty type just reappears from code, not
data, on next boot. `ImportReport.StatTypeCount` now only counts types that actually carry data (an
exact, honest metric that round-trips cleanly); `StatTypesWithNoData` tracks the rest separately and
is reported but not diffed, so a real import doesn't cry wolf over an already-accepted, harmless gap.

**Sanity-checked by breaking something**: reverted the `[JsonConstructor]` fix and confirmed the
round-trip test failed with the exact `NotSupportedException` this closes; reverted the
`chatData.RemoveAll` fix and confirmed the round-trip test failed with every populated module showing
a `1 -> 2` duplicate count (plus a spurious `mod_standard_chatdata: 0 -> 1`, the stub-only case) -
both reverted back afterward.

**Verified**: build clean; `dotnet test` green across repeated runs (83/83, up from 78). A real dry
run against `data/robotolive.xml` (2021 production export, 6 chats, 3964 questions/8792 answers/57
packs, 10 quotes, 13 birthdays) parsed cleanly with no crashes and no printed token. A real `--real`
import into a fresh local `data/robotolive/` (never `data/robotolive.xml` itself modified) wrote
successfully and round-tripped with **zero count mismatches** on reload. `data/robotolive/bot.env`
was created with a blank `TelegramToken` (`InstanceBootstrapper`'s normal first-run stub) - filling
that in with a **test** bot token, never production, is the operator's explicit next step, not
something this tool does automatically.

### `.github/workflows/docker-publish.yml` - retargeted from the abandoned branch

This branch had no GitHub Actions workflow at all - only `rewrite/dotnet-docker-port` does, scoped
to `on: push: branches: [rewrite/dotnet-docker-port]`, so pushing *this* branch was confirmed to
trigger nothing (workflow triggers evaluate using the workflow file as it exists in the pushed ref,
and this branch had none). Retargeted the abandoned branch's own `docker-publish.yml` at this
branch's real layout (`Roboto/Roboto.csproj`, not `src/Roboto.Bot/`) - same GHCR destination, same
`latest` + short-SHA tagging, same manual `workflow_dispatch` escape hatch. Flagged directly to the
user: once this is pushed, GHCR's `latest` tag comes from pushes to *this* branch instead of the
abandoned one - anything pulling `latest` (a TrueNAS app set to auto-update, for instance) picks up
this branch's build from then on. Pushing itself has to happen from the user's own machine - this
sandboxed session has no GitHub credentials (checked SSH keys, env vars, `.netrc`, git credential
helpers, `gh` CLI - none present).

### Live-on-`robotolive` bug report: bot-judge round skips the "All answers received!" announcement

First real bug report from the actual cutover instance (`robotolive`, running real production data via
phase 8's import). The user also asked about `/statsgraph` returning the plain `/stats` message -
not a bug: the real command is `/statgraph` (no `s` before `graph`); `/statsgraph` (with the extra
`s`) genuinely does start with `/stats`, so `mod_standard.cs`'s `StartsWith("/stats")` check matches
it first, same as it would for any other typo starting with those 6 characters - confirmed by reading
the dispatch order and char-by-char comparing both strings, not by guessing.

**The real bug**: `beginJudging`'s bot-judge branch (`tzar.isBot`, "Add Bots" - phase 9, not a legacy
feature at all) picked a random answer and called `judgesResponse` immediately, returning before ever
reaching the `Messaging.SendMessage(chatID, chatMsg)` call that sends the "All answers received!..."
group announcement - that send only lived inside the human-judge branch's `SendQuestion` result
handling, below the bot-judge branch's early `return`. Effect: whenever the *judge* for a round
happened to be a bot (not just a non-judge player, which phase 9's own tests already covered), the
chat went straight from "everyone's answered" to "a winner's been picked" with no announcement of
what the answers even were in between. Never caught before because `AddBotsLetsASoloStarterReachThree
PlayersAndPlayARound` only played round 1, where the starter (always human, by construction) is
tzar - round 2 (where `lastPlayerAsked` rotates to the first added bot) is the first round with a bot
judge, and nothing had exercised it until this live report.

Fixed: the bot-judge branch now sends the same `chatMsg`, gated by the same `judgesMessageOnly` flag
the human-judge path already respects (so re-judging after a kicked judge, phase 9's own no-op
decision, still correctly suppresses a duplicate announcement for the same round). Extended the
existing add-bots test to play into round 2 and assert the announcement is sent for both rounds.

**Sanity-checked by breaking something**: disabled the new send (`if (false && !judgesMessageOnly)`)
and confirmed the extended test failed (1 announcement instead of 2), then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (83/83, unchanged - extended an
existing test rather than adding a new one). Not yet re-verified live against `robotolive` - the fix
needs to ship there (rebuild + redeploy) before the user can confirm round 2+ with a bot judge now
announces correctly.

### Live-on-`robotolive` bug report: `/xyzzy_settings` silently vanishing mid-round

A genuine legacy bug, not something this port introduced - `askQuestion()`'s own comment already
called it out: `//TODO - this causes issues if someone is changing settings in the middle of a
round.` Never fixed, in any version checked, until now.

**Root cause**: `Messaging.clearExpectedReplies(chat_id, pluginType)` clears *every* `ExpectedReply`
for the given chat and plugin type, with no awareness of which conversation each one belongs to.
`askQuestion()` calls this unconditionally at the start of every round (originally meant to clean up
a previous round's unanswered "Question" replies) - so if a player's `/xyzzy_settings` request was
still queued behind their own outstanding "Question" reply (`Messaging`'s per-user single-
outstanding-message serialization) when a new round dealt immediately after judging - e.g. a bot
judge auto-deciding with no human wait, exactly the shape confirmed live on `robotolive` - this call
deleted the still-queued, not-yet-sent Settings menu before it ever got a chance to send. From the
user's own perspective: `/xyzzy_settings` just silently did nothing.

**Fix**: `clearExpectedReplies` gained an optional `messageDataFilter` parameter (default `""`,
unchanged blanket-clear behavior for every other call site) - `askQuestion()`'s call now passes
`"Question"`, so it only clears the stale state it actually exists to clean up, leaving unrelated
queued conversations (Settings, and anything else) for other users/contexts alone. The other
`clearExpectedReplies` call sites (`reset()`, wrapUp, judging-phase invalid-reply cleanup, force-
question) are all genuine "wipe everything for this chat" moments (game reset, game over, actively
correcting a broken judging state) where a blanket clear is the intended behavior, not the same bug -
left as-is, not swept up into this fix.

**Sanity-checked by breaking something**: reverted `askQuestion()`'s call back to the unfiltered
blanket clear and confirmed the new test failed (`/xyzzy_settings`' menu never sent), then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (84/84, up from 83). Not yet
re-verified live against `robotolive` - needs the fix to actually ship there first.

### `chat_mangler_bot` import, and a second false-positive class in `ImportReport.Diff`

Ran the same dry-run -> real-import sequence against `data/chat_mangler_bot.xml` (a much smaller,
fresher export than `robotolive.xml` - 1 chat, no xyzzy catalog/games, 2 birthdays, 0 quotes). Surfaced
a second, structurally identical false positive to the stat-types-with-no-data one: the real import
reported `Chats with mod_steam_chat_data: 0 -> 1` and `Chats with mod_xyzzy_chatdata: 0 -> 1` - this
one chat had never played xyzzy or used Steam tracking, so it had no data for either module in the
source XML, but `chat.initPlugins()` (see `settings.load()`'s own comment above the `RemoveAll`/`Add`
fix from the earlier phase 8 bugs) correctly stub-fills every registered module for every chat on
reload - expected behavior, not data loss.

**Fixed properly this time** rather than patched around per-import: `ImportReport.Diff`'s per-module
"chats with X" check now only flags a *drop* (a chat that really had a module's data in the source
coming back without it), never a *gain* (a chat correctly picking up a fresh stub for a module it
never touched) - the latter will legitimately happen on every real import for any module some chat
never used, so treating it as a mismatch would cry wolf on essentially every real import from now on.
2 new tests: a gain producing no diff, a genuine drop still being caught.

**Sanity-checked by breaking something**: reverted the gain/drop distinction back to a plain
inequality check and confirmed `ChatGainingAStubForAModuleItNeverTouchedIsNotAMismatch` failed with
exactly the mismatch shape seen live, then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (86/86, up from 84). Real import
against `data/chat_mangler_bot.xml` now reports "Counts match" cleanly. `data/chat_mangler_bot/`
(`roboto.db` + a blank-token `bot.env`) is the result, same as `robotolive`'s.

### Module allow-list moved from a launch-time flag to `bot.env`

The user asked how to configure `chat_mangler_bot`'s module allow-list (it should only load
`mod_wordcraft`, `mod_standard`, `mod_quote`, `mod_birthday` - not `mod_xyzzy`/`mod_steam`), which
legacy always set via a `-plugin` CLI arg per launch. That flag still works exactly as before (kept,
per CLAUDE.md's own note, as low-blast-radius/unrelated) - but for a real multi-instance Docker/
TrueNAS deployment, a launch-command argument is awkward: it has to be remembered and maintained
separately from every other per-instance setting, and survives however the container happens to get
started rather than living with the instance's own data.

**Decision, discussed with the user rather than assumed**: `bot.env` gained an optional `Plugins=`
line (comma-separated module class names, blank = load everything - today's default, unchanged) -
`InstanceBootstrapper.TryLoad` parses it into `BotOptions.Plugins`, merged into `Roboto.pluginFilter`
alongside (not instead of) whatever `-plugin` CLI args are also passed, so both mechanisms keep
working together. Chosen over a docker-compose `command:` override because it colocates with every
other per-instance setting (token, username, Steam key) that already lives in this same file, and
because each instance genuinely does get its own separate compose/app config on this user's TrueNAS
setup (confirmed directly, not assumed) - so there was no actual constraint forcing the CLI-arg route,
just legacy's own original mechanism carried forward out of habit.

`data/chat_mangler_bot/bot.env` (already existed from the earlier import, predating this change, so it
didn't get the new stub content automatically) updated directly with `Plugins=mod_wordcraft,mod_standard,
mod_quote,mod_birthday`.

**Sanity-checked by breaking something**: reverted the `Plugins=` parsing to always return an empty
list and confirmed `ConfiguredPluginsLineParsesIntoATrimmedList` failed, then reverted.

**Verified**: build clean (both `Roboto.csproj` and `Migrator.csproj` - `InstanceBootstrapper.TryLoad`'s
signature grew a parameter, updated its one other call site in `Migrator/Program.cs`); `dotnet test`
green across repeated runs (89/89, up from 86).

### `/version` (mod_standard) - tracing a running instance back to its actual build

The user asked for a way to check which build TrueNAS is actually running, since it's otherwise
opaque once an image is deployed. There's no separate version-numbering scheme in this project (never
was, in legacy or here) - a real git commit *is* the version, so `/version` reports the exact commit
and build timestamp baked into the running assembly, not an invented build-counter.

**`Roboto.csproj`**: `GitCommit` is a real MSBuild property, embedded via `AssemblyMetadata` items so
it's compiled into the assembly itself (survives however the container gets launched, unlike an env
var read at runtime). `-p:GitCommit=<sha>` wins if passed in (the Docker/CI path - see below); a
`SetGitCommit` target falls back to running `git rev-parse --short HEAD` itself otherwise, for local
dev builds where the working tree's own repo is right there.

**Real MSBuild gotcha, cost real debugging time**: `AssemblyMetadata` items need to be added *inside*
a `<Target>`, not a bare top-level `<ItemGroup>` - the latter evaluates once at project-load time,
before any target has run, so `Value="$(GitCommit)"` would always see it as still empty. Even inside a
target, the hook point matters: `BeforeTargets="GenerateAssemblyInfo"` and even
`BeforeTargets="CoreGenerateAssemblyInfo"` both fired too late - `GetAssemblyAttributes` is the actual
target that converts `@(AssemblyMetadata)` into the attribute list `CoreGenerateAssemblyInfo` writes
out, and it runs *before* `CoreGenerateAssemblyInfo` in the dependency graph. Confirmed correct only
by inspecting the SDK's own `Microsoft.NET.GenerateAssemblyInfo.targets` file directly and tracing the
real per-target execution order with `-v:diag`, not by guessing from the target names - `BeforeTargets=
"GetAssemblyAttributes"` is the one that actually works. Verified for real: extracted the compiled
`Roboto.dll` from a real `docker build --build-arg GIT_COMMIT=abc1234` image and confirmed `abc1234`
is actually embedded, not just present in a local (non-Docker) build.

**Docker/CI wiring**: `Dockerfile` gained `ARG GIT_COMMIT=unknown`, threaded into `dotnet publish
-p:GitCommit=$GIT_COMMIT`; `.dockerignore` already excludes `.git/` deliberately (secrets/build-context
hygiene, predates this), so the SDK build stage has no repo to fall back on itself - a manual `docker
compose build` with no `--build-arg` honestly reports "unknown" rather than guessing. `.github/workflows/
docker-publish.yml` passes `build-args: GIT_COMMIT=${{ github.sha }}` - the real checked-out commit
from the actual CI build, more trustworthy than anything computed inside the build stage.

**`Core/BuildInfo.cs`**: small reflection-based reader for the two `AssemblyMetadataAttribute`s.
`mod_standard.cs`'s `/version` command reports both; added to `getMethodDescriptions()`'s `/help`
listing alongside `/stats`.

**Sanity-checked by breaking something**: renamed the command check to `/versionx` and confirmed
`VersionReportsTheGitCommitAndBuildDateBakedIntoTheAssembly` failed, then reverted.

**Verified**: build clean; `dotnet test` green across repeated runs (90/90, up from 89); `docker build
--build-arg GIT_COMMIT=abc1234` then extracting the compiled DLL from the resulting image and
confirming `abc1234` is actually present in it (not just "builds without error"); `docker compose
build` (no build-arg, matching a real local/manual build) still succeeds cleanly.

### Live-on-`chat_mangler_bot` bug report: `/quote_config` crashing the main loop on a failed send

Real production crash, `NullReferenceException` inside `mod_quote.replyReceived`, taking down the
whole message loop (recovered on the next poll cycle via `getUpdates()`'s own top-level catch, but
the `/quote_config` request itself was silently lost). Root cause was two layers deep, both confirmed
present byte-for-byte in legacy - genuine pre-existing bugs, just rarely exercised (needs an actual
failed send, which needs a real Telegram-side error, e.g. the live trigger: `400 - Bad Request:
message to be replied not found`, a stale reply-to target):

1. **`Messaging.parseFailedReply`** (called whenever `TelegramAPI.postExpectedReplyToPlayer` catches
   a real send failure) calls `pluginToCall.replyReceived(er, null, true)` - passing `null` for the
   message, since there's no genuine incoming message for a failed *outbound* send. Every module's
   `replyReceived` override (`mod_quote`, `mod_xyzzy`, `mod_birthdays`, `mod_steam`, `mod_standard`,
   `mod_wordcraft` - checked all six) unconditionally dereferences fields off it (`m.text_msg.
   ToLower()`, string concatenation, etc.) with no null check of its own - `mod_quote`'s `/quote_config`
   CONFIG-menu flow was just the first to actually hit it live.
2. Even past that, `parseFailedReply` **threw `InvalidProgramException`** if the plugin's
   `replyReceived` didn't return `true` for a failed-send callback - which a synthetic, mostly-empty
   message practically guarantees for any branch keyed on real user input (`m.text_msg == "Set
   Duration"` never matches an empty string). Combined with (1), a failed send was effectively
   guaranteed to crash the main loop, one way or another.

**Fixed at the single call site** (`Messaging.parseFailedReply`) rather than auditing/patching all six
modules' many branches individually: builds a minimal synthetic `message` from the `ExpectedReply`'s
own fields (`chatID`/`userID`/`userName`) - `message` gained an `internal message()` constructor for
this, explicitly emptying (not leaving null) the string fields that had no field initializer
(`text_msg`/`userFirstName`/`userSurname`/`userFullName`/`chatName`) - and the `throw` became a plain
log line, since a plugin not having a specific branch for "the message I wanted to send never
arrived" is an expected, soft case, not a programming defect worth crashing over.

**Sanity-checked by breaking something**: reverted to `replyReceived(er, null, true)` and confirmed
`QuoteConfigDoesNotCrashTheMainLoopWhenTheDmFailsToSend` failed with the exact `NullReferenceException`
shape seen live; separately restored just the `throw` (with the null fix still in place) and confirmed
it *still* failed, proving both halves of the fix are independently necessary. Both reverted back
afterward.

**Verified**: build clean; `dotnet test` green across repeated runs (91/91, up from 90). Not yet
re-verified live - needs the fix to ship to `chat_mangler_bot` (and `robotolive`, since this affects
every module, not just `mod_quote`) first.

### Live-on-`beefy` bug report: bot self-de-admin crashing in a basic (non-super) group, plus a real background sweep

**Crash**: `PromoteChatMember` (the only "demote" mechanism the Bot API has - there's no separate
"remove admin" call) only works for supergroups/channels. A basic (non-super) group has no per-member
admin distinction via the Bot API at all, even though a human can still make the bot admin there
through the Telegram app's own UI - confirmed live: promoting the bot to admin in "Beef Test" (a
basic group) crashed the whole update loop with an unhandled `ApiRequestException` ("400 Bad Request:
method is available for supergroup and channel chats only"). Not a legacy bug - legacy never had any
admin-only functionality (see phase 9 notes); this is entirely within the "bot self-de-admin" delta
itself, just never tested against a basic group before now.

**Fix**: `TelegramAPI.DeAdminSelf` (extracted as a shared method - see below) tries `PromoteChatMember`
in a try/catch; on the specific "supergroup and channel" error it sends
`BotSelfDeAdminBasicGroupExplanation` instead ("I've been made an admin here, but Telegram's Bot API
has no way to demote a member in a regular group... please remove my admin rights manually") rather
than crashing; any other failure just logs.

**User's explicit ask**: "we are doing this by checking for an event from telegram - can we bake this
into the rolling background checks instead" - then revised mid-turn to "actually, let's leave the
existing check and add the background check as well". `TelegramAPI.EnsureNotAdminInAnyChat()` -
called from `mod_standard.backgroundProcessing()`, the established home for whole-bot janitorial work
- checks every known chat's current membership via `GetChatMember` and applies the same `DeAdminSelf`
logic if it finds the bot is currently an administrator. Kept the reactive `MyChatMember` handler
exactly as it was (just refactored to share `DeAdminSelf`), added the sweep alongside it as a safety
net for a promotion whose event was ever missed (e.g. a restart racing it) - not a replacement.

**A second real gap, found while verifying the sweep, not assumed**: `Roboto.Settings.chatData`
only ever gained an entry via a real text message - a chat where the bot is added and promoted
straight to admin, with *no* other message ever exchanged, was invisible to
`EnsureNotAdminInAnyChat()` entirely, since the sweep only checks known chats. Confirmed live: the
local `beefy-livetest` instance (a fresh instance, `chats:index` genuinely empty) had never seen a
single message from "Beef Test," despite already being an admin there from the earlier crash.
Fixed: `DispatchUpdate`'s `MyChatMember` handling now registers the chat (`Chats.getChat`/`addChat`,
same get-or-create pattern the real message-dispatch path already uses) for *any* membership change,
not just promotions - the bot should always know about every chat it's actually a member of.

**Also, incidentally**: added `TelegramAPI.BotId` (lazily cached `Client.GetMe()`, reset alongside
`Client` in `SetClientForTesting`) - the sweep needs the bot's own user ID per chat it checks, and
nothing had needed to cache it before this.

**Test fidelity gap found and fixed alongside this**: the existing
`PromotingTheBotToAdminStripsItsRightsBackOffAndExplainsWhy` test used `ChatType.Group` (a basic
group) for what it asserted was a *successful* strip - unrealistic, since real Telegram would have
failed exactly the way this bug did; `FakeTelegramBotClient`'s `PromoteChatMemberRequest` never
validated chat type at all. `TestHarness.PromoteBotToAdmin` now defaults to `ChatType.Supergroup`
(the type that's actually realistic for this to succeed), and the fake gained `BasicGroupChatIds`
(makes `PromoteChatMemberRequest` fail the same way real Telegram does) and `ChatsWhereBotIsAdmin`
(backs the new `GetChatMemberRequest` case, for the sweep's own tests).

**Sanity-checked by breaking something**: reverted the basic-group catch clause (`when (false)`) and
confirmed the new basic-group test failed; separately disabled the `mod_standard.backgroundProcessing()`
wiring and confirmed the wiring-specific test failed; separately reverted the `MyChatMember` chat-
registration block and confirmed `PromotionRegistersTheChatEvenWithNoOtherMessageEverSent` failed.
All three reverted back afterward, independently confirming each piece is load-bearing.

**Verified live, not just via tests** - genuinely the most thorough live round-trip this project has
done for a single fix: rebuilt and restarted the local `beefy-livetest` instance, confirmed clean
startup, then had the user re-promote the bot to admin in the real "Beef Test" basic group. The
*reactive* event hadn't fired by the time a regular text message registered the chat instead, so the
real proof came from watching `mod_standard`'s own 5-minute-throttled background pass fire
independently and correctly detect + gracefully handle the still-admin state - `[TelegramAPI:
DeAdminSelf] Promoted to admin in -5308893237 (Beef Test), but it's a basic (non-super) group -
nothing to strip via the API. Explaining instead.` - with the main loop continuing normally
afterward, no exception, no crash. This specifically proves the background sweep works standing
entirely on its own, independent of whether the reactive path ever fires - exactly what was asked for.

**Verified**: build clean; `dotnet test` green across repeated runs (96/96, up from 91).

## What's still open

Phases 8 and 10 - see the phase table above and the full plan file for what each phase actually
involves, the confirmed architecture decisions (real background scheduler, decomposed persistence +
relational tables for whole-bot lists, carry-forward deltas), and the resolved/open sub-decisions
(chatPriority sort - decided, implement; daily XML backup - decided, not needed, TrueNAS snapshots
instead; background-scheduler batching caps - decided, keep as legacy has them). Phase 9 was done out
of plan order (ahead of phase 8) at the user's explicit request. Phase 5 (hybrid keyboards) is
deliberately deferred, not blocking - see its own notes above for why, and the design to pick back up
if it's ever revisited (which also resolves the card/pack-ID-for-callback_data sub-decision, since
that only matters once inline keyboards exist). Phase 7's own deferred items (mod_wordcraft, mod_steam's
network-bound flows, `ChatKeyedLockTests` re-derivation) are additional, narrower follow-ups, not
separately tracked here.
