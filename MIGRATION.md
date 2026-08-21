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
| 5. Hybrid keyboards (`InlineKeyboardMarkup`/`CallbackQuery` bridged into `ExpectedReply`) | Not started | — |
| 6. Charting: ScottPlot on legacy's own `stats.cs` data shape | Not started | — |
| 7. Test harness + business-logic test suite | Done, verified (partial coverage - see notes) | `28d4714` |
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
`Messaging.processUpdates()`. This is genuinely new behavior, not a faithful port of anything: tracing
`Roboto.cs`/`Core/Plugins.cs`/`Core/Messaging.cs` confirmed `Plugins.backgroundProcessing()` was only
ever invoked once, after the message loop had already exited, or manually via `/background` - legacy
never actually ran this live on a timer in any version checked. The batching caps in
`mod_xyzzy_coredata` (5 full-checks + 50 mini-checks per pass) are kept exactly as legacy had them, not
simplified now that a live timer exists - confirmed still load-bearing (a real background pass
genuinely takes measurable time even across a small number of games).

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

## What's still open

Phases 5, 6, 8, 9, 10 - see the phase table above and the full plan file for what each phase actually
involves, the four explicitly-confirmed architecture decisions (hybrid keyboards, real background
scheduler, decomposed persistence + relational tables for whole-bot lists, carry-forward deltas), and
the resolved/open sub-decisions (chatPriority sort - decided, implement; card/pack ID scheme - open,
needs a decision during phase 5; daily XML backup - decided, not needed, TrueNAS snapshots instead;
background-scheduler batching caps - decided, keep as legacy has them). Phase 7's own deferred items
(above) are additional, narrower follow-ups within phases 5/6/8's own scope, not separately tracked
here.
