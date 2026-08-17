# Roboto: Linux/Docker/.NET migration plan

Working document tracking the port from the legacy WinForms/.NET Framework bot (`Roboto/`, still on
`master`) to a modern .NET/Docker bot (`src/Roboto.Bot/`, on `rewrite/dotnet-docker-port`).

**This file is temporary** — it's a working plan/progress-tracker for the duration of the
migration, not durable project context. Delete it once cutover is done, folding anything still
relevant into `CLAUDE.md` first. See `CLAUDE.md` for durable orientation (repo layout, safety
rules, dev environment, architecture decisions). Detailed "what we tried, what broke, how it got
fixed" narratives for specific pieces of code live as **comments in that code**, not here or there
— check the file you're about to change before assuming its current shape is arbitrary.

## Phase status

| Phase | Status | Commit |
|---|---|---|
| 0. Baseline: tag legacy code, branch for rewrite | Done | `c6bbc61` |
| 1. .NET 10 + Docker skeleton, self-bootstrapping per-instance config | Done, verified | `4e626c5` |
| 2. Module framework: command router + DI | Done, verified | `5933fe5` |
| 3. SQLite persistence layer | Done, verified | `5d54890` |
| 4. `mod_standard` port, partial (`/start`, `/stop`, real per-chat state) | Done, verified | `f495a0c` |
| 5. Conversational-flow / `ExpectedReply` system, + `/setquiethours` | Done, verified | `c6541d3` |
| 6. Automated test harness (xUnit + fake Telegram client) | Done, verified | `16b4b0b` |
| 7. `mod_standard` remainder (`/addadmin`, `/removeadmin`) | Done, verified | `534f9a8` |
| 8.1 `mod_xyzzy`: game skeleton (start/join/leave/status, persistence, no round-play) | Done, verified | `af61ffc` |
| 8.2 `mod_xyzzy`: round loop + inline-keyboard/callback-query infra | Done, verified | (this commit) |
| 8.3 `mod_xyzzy`: background scheduler, reminders/timeouts/throttle, quiet-hours | Not started | — |
| 8.4 `mod_xyzzy`: `/xyzzy_settings` admin/moderation menu | Not started | — |
| 9. Remaining modules (quote, birthdays, wordcraft, steam) | Not started | — |
| 10. Stats/graphs (ScottPlot), `/statgraph` | Not started | — |
| 11. XML→SQLite migration importer | Not started — needs real prod XML copy from user first | — |
| 12. Cutover | Not started | — |

"Verified" means actually exercised for real (build + run + real Telegram round-trip, sometimes
through Docker too), not just "compiles" — see each phase's commit message and in-code comments for
what was specifically tested and any bugs that were caught along the way.

## What's built so far

- `src/Roboto.Bot/Program.cs` — Generic Host entry point, env-var config, Serilog console logging.
- `InstanceBootstrapper.cs` / `BotOptions.cs` — per-instance config bootstrap. `ROBOTO_INSTANCE` env
  var selects an identity; its credentials self-bootstrap under `{DataDir}/{Instance}/bot.env`.
- `TelegramPollingService.cs` — long-polling via the `Telegram.Bot` package.
- `Commands/` — `IBotCommand`, `CommandRouter` (name-based dispatch + mute-gating + usage stats),
  `PingCommand`, `HelpCommand`, `StatsCommand`, `StartCommand`, `StopCommand`, `SetQuietHoursCommand`,
  `AddAdminCommand`, `RemoveAdminCommand`. `ReplyRouter`/`PendingReply`/`IReplyHandler` for
  conversational flows, `MessageDispatcher` for the actual per-message routing logic (pulled out of
  `TelegramPollingService` so it's directly testable).
- `Persistence/` — `IStateStore`/`SqliteStateStore`, JSON-blob-per-key over one SQLite table.
- `Chats/` — `ChatState`/`ChatRepository`, the first real per-chat data (`ChatId`, `Title`,
  `Muted`) — deliberately module-agnostic, a real module's own per-chat data gets its own separate
  `IStateStore` key rather than a field bolted on here.
- `Dockerfile`, `docker-compose.yml`, `.dockerignore` — Docker packaging. `docker-compose.yml` runs
  as the host UID (bind-mount ownership fix, see its comments) and needs no per-instance host path.

## Conversational-flow / `ExpectedReply` replacement — done and verified (2026-08-17)

Replaces legacy `ExpectedReply` (`Storage/ExpectedReply.cs`, `Core/Messaging.cs` — a single global
`List<ExpectedReply>`, linearly scanned, matched by chat/user id + reply-to-message-id, keyed by an
opaque `messageData` string per handler) with `Commands/PendingReply.cs`/`ReplyRouter.cs`/
`IReplyHandler.cs` — see those files' own doc comments for the actual design (deliberately
simplified vs. legacy: one pending reply per user, not a full queue; DM-only matching, not also the
group reply-to variant; both explicitly justified as "nothing needs more yet, revisit when
something does" rather than gaps that were missed).

Proved for real with `SetQuietHoursCommand` (`/setquiethours`) — genuinely useful ported
functionality, not a throwaway demo, and it was already blocked on exactly this system.

**Verified, not just written**, across two rounds (the first round's server logs were too sparse to
independently confirm the middle of the conversation, which prompted adding explicit step-logging
inside `SetQuietHoursCommand` before trusting it - see its own comments):
- No circular-DI exception at startup (a real risk here, same shape as the earlier `HelpCommand`
  gotcha - `SetQuietHoursCommand` resolves `ReplyRouter` lazily via `IServiceProvider` instead of as
  a constructor dependency, since `ReplyRouter` needs every `IBotCommand` built first, itself
  included).
- Full round trip via the real group chat + DM: `/setquiethours` → asked for start time over DM →
  `disable` → correctly cleared, with each step now traceable from the command's own log lines, not
  just "a message arrived."
- `docker compose build` + `docker compose run` — same six commands registered, clean auth, no
  regressions.
- **Process note that actually changed how testing works going forward**: time-boxed background
  test runs (even a 30-minute window) raced the user's actual pace and died mid-conversation twice.
  Switched to running the bot **with no timeout at all**, stopped explicitly (`kill`) only once
  testing is confirmed done - see `CLAUDE.md`'s working-conventions section, which this superseded
  the previous "1800s has worked well" note.

## Automated test harness — done and verified (2026-08-17)

User wanted to offload most round-trip testing rather than being the human-in-the-loop for every
change, reserving manual testing for a final pass close to deployment. Real Telegram automation
turned out to be a dead end worth ruling out explicitly rather than silently avoiding: Telegram
bots can't message other bots (blocked platform-side, an anti-loop measure), so no combination of
test bots can play "the human" over the real network; a scripted real user account (Telegram's
Client/MTProto API, not the Bot API) could technically do it but means logging a real account into
a script - meaningfully more setup/risk than a BotFather token, not attempted.

What got built instead, in `tests/Roboto.Bot.Tests/`: `Telegram.Bot`'s client is exposed as
`ITelegramBotClient`, and (confirmed via reflection against the actual installed package rather
than assumed) is centered on one method, `SendRequest<TResponse>`, that every higher-level call
(`SendMessage`, `GetMe`, ...) funnels through as a typed request/response pair. Faking that one
method (`FakeTelegramBotClient`, pattern-matches on request type, records what "got sent") covers
everything built on top of it - no network, no real bot token, no human.

- `RobotoServiceCollectionExtensions.AddRobotoBot()` (`src/Roboto.Bot/`) - pulled the DI
  registration out of `Program.cs`'s top-level statements so tests build the *exact* same service
  graph as production, not a hand-maintained approximation that can quietly drift.
- `MessageDispatcher` (`src/Roboto.Bot/Commands/`) - pulled the actual "what do we do with an
  incoming message" logic out of `TelegramPollingService` (a `BackgroundService`, awkward to test
  directly) into its own directly-callable class.
- `TestBot` (`tests/Roboto.Bot.Tests/`) - the test fixture: builds that same service graph against
  a temp-directory SQLite file and the fake client, bypassing `InstanceBootstrapper`'s file-prompt
  flow entirely (irrelevant to application logic). `TestBot.Restart()` builds a second, fully
  independent service provider pointed at the same on-disk data - a real "did this survive a
  restart" test, not an in-memory shortcut that would pass even if persistence were actually broken.

Covers command dispatch, mute-gating (including group-chat scenarios that previously needed the
user to actually open a group chat on their phone), the full `/setquiethours` conversational flow,
and persistence-across-restart. Doesn't cover, and isn't meant to: real Docker/filesystem behavior
(the bind-mount UID bug wouldn't have been caught here - `docker compose build`/`run` stay the
right tool, and don't need the user either) or genuine Telegram API/library integration surprises
(still worth an occasional real smoke test, just not on every change).

**Verified, not just written** - specifically, verified the tests can actually *fail*, not just
that they pass (a suite that trivially passes regardless of the code is worse than no suite):
deliberately broke `CommandRouter`'s mute-gating (`isGroupChat` hardcoded to `false`), confirmed
exactly the expected test failed with a clear assertion message while the other 13 still passed,
then reverted. 14/14 pass on the real code, `dotnet build` clean on both projects, `docker compose
build` unaffected (`tests/` added to `.dockerignore` - dev-time only, never needed in the image).

## `mod_standard` remainder: `/addadmin`, `/removeadmin` — done and verified (2026-08-17)

Redesigned rather than carried straight over: legacy asked "who?" via a keyboard built from
presence-tracked recent chat members (`Presence`/`chatPresence`, a whole subsystem this codebase
doesn't have and wasn't worth building just for this). Uses Telegram's standard "reply to their
message" pattern instead - `/addadmin`/`/removeadmin` as a reply to the target user's message,
resolving `Message.ReplyToMessage.From` directly. No conversational flow needed, no presence data
needed, and arguably more idiomatic for group-management bots generally. Kept legacy's bootstrap
special case: a bare (non-reply) `/addadmin` with no existing admins makes the caller the first one.

`Admins` (a `List<long>`) went onto `ChatState` alongside `Muted`, not a mod_standard-specific blob
- same reasoning as `Muted`: legacy put admin status directly on the core `chat` class since other
things might plausibly gate on it later, not something owned solely by mod_standard's own commands.

**First real payoff of the test harness just built**: this phase used `dotnet test` as the primary
verification, not a live Telegram round-trip - 6 new tests (bootstrap, reply-based add/remove,
privilege enforcement, no-admins-yet messaging, private-chat rejection), plus a repeat of the
"deliberately break it and confirm the test catches it" sanity check, this time on the privilege
check itself (`chat.IsAdmin(caller.Id)` hardcoded to always pass) - caught immediately. 20/20 tests
pass, `docker compose build` unaffected. No manual/live-bot testing was done for this phase at all,
matching the user's stated preference going forward.

## `mod_xyzzy` port — architecture decisions (2026-08-17)

Full design lives in the phase-8 plan used to scope this work; summary of the decisions that
matter for anyone picking this up mid-flight:

- **Card content**: legacy ships no card text at all (only exists in the live prod XML). v1 uses a
  hardcoded sample of the public CC BY-NC-SA-licensed CAH base set (`Xyzzy/CardCatalog.cs`) as a
  placeholder default pack — not permanent, real content arrives via the phase-11 XML importer.
- **Card UX**: legacy matches players' answers/judge picks against mangled Telegram reply-keyboard
  button text via several fuzzy fallbacks (an acknowledged legacy pain point). The port uses real
  inline keyboards + `CallbackQuery` instead - `callback_data` format
  `xy:<action>:<groupChatId>:<round>:<cardId>`, no fuzzy matching. New generic infra
  (`ICallbackQueryHandler`/`CallbackQueryRouter`), not xyzzy-specific, mirrors the existing
  `IBotCommand`/`IReplyHandler` reflection-discovery pattern.
- **Stable player IDs**: `JudgePlayerId: long?` (a Telegram user ID), not legacy's array-index
  judge pointer (`lastPlayerAsked`) — removes the ~60-line reindexing dance legacy needed in
  `removePlayer` (self-flagged `//TODO - should be an ID!` in the legacy code itself).
- **Per-chat game state gets its own POCO/key** (`xyzzy:{chatId}:game`), not a field on `ChatState`
  — `ChatState`'s own doc comment names this exact case as the reason it stays module-agnostic.
- **Setup/admin flows stay on the existing `ReplyRouter`/DM system** (single-admin, one-at-a-time —
  exactly what it already fits, same shape as `/setquiethours`); only round-play (N players
  submitting simultaneously) needed the new callback-query mechanism, since `ReplyRouter` is
  deliberately "one pending reply per user, DM-only" and can't represent that.
- **Background scheduler is new infrastructure built from scratch** — confirmed nothing like it
  exists anywhere in `src/Roboto.Bot` yet (the only existing `BackgroundService` is
  `TelegramPollingService`'s poll loop). A second `BackgroundService`
  (`Xyzzy/XyzzyRoundSchedulerService.cs`) handles reminders/timeouts/throttle (phase 8.3).

## `mod_xyzzy` 8.1: game skeleton — done and verified (2026-08-17)

`src/Roboto.Bot/Xyzzy/` - `XyzzyGameState`/`XyzzyPlayer`/`XyzzyGameRepository` (same
get-or-create-then-save shape as `ChatRepository`, key `xyzzy:{chatId}:game`), `CardCatalog`
(hardcoded CC BY-NC-SA CAH base-set sample, 30 questions/90 answers - see the architecture-decisions
section above), and the non-round commands: `/xyzzy_start`, `/xyzzy_join` (DM-reachability checked
up front, same reasoning as `/setquiethours`), `/xyzzy_leave` (group-only; the cross-chat DM variant
is a deliberate v1 cut, see below), `/xyzzy_status`, `/xyzzy_get_settings`.

No round-play yet - a game just sits in `Invites` once enough players have joined. That's phase 8.2
(needs the inline-keyboard/callback-query infra first).

**Verified**: 31 tests in `tests/Roboto.Bot.Tests/Xyzzy/XyzzyGameSkeletonTests.cs` (up from the
prior 20), covering start/join/leave/status/get-settings, the DM-unreachable-player rejection (and
that they can still join afterward once reachable, not permanently locked out), private-chat
rejection, and persistence-across-restart (`TestBot.Restart()`) for the game state itself - the
first proof `XyzzyGameRepository` round-trips through SQLite correctly. Sanity-checked per the
established pattern: deliberately disabled the join-requires-a-running-game gate, confirmed exactly
`JoinRequiresAGameToAlreadyBeRunning` failed with a clear message while the other 30 passed, then
reverted. `docker compose build` unaffected.

## `mod_xyzzy` 8.2: round loop + inline-keyboard/callback-query infra — done and verified (2026-08-17)

New generic infra (not xyzzy-specific, mirrors the `IBotCommand`/`IReplyHandler` reflection pattern):
`Commands/ICallbackQueryHandler.cs`, `Commands/CallbackQueryRouter.cs` (always answers the callback
query itself - real Telegram API requirement, otherwise a tapped button spins forever - centralized
so no future handler has to remember it). `TelegramPollingService` now subscribes to
`UpdateType.CallbackQuery`; `MessageDispatcher` branches to `CallbackQueryRouter` before the
existing message-only path.

The actual game: `Xyzzy/XyzzyCallbackData.cs` (encodes `xy:<action>:<groupChatId>:<round>:<cardId>`
into `callback_data` - the group chat ID travels in the button itself, same problem legacy solved
by round-tripping `chatID` through `ExpectedReply`; `<round>` rejects stale taps from a message
whose round has already moved on). `Xyzzy/XyzzyRoundService.cs` holds the shared deal/ask/judge/
advance mechanics (judge rotation by stable ID, self-refilling decks that never let the same card
be in two hands at once - see its own doc comment for why that's what makes "match a tapped card ID
back to a submission" unambiguous with no extra bookkeeping). `/xyzzy_begin` (admin-gated, 3+
players or "force" for 2) kicks off the first round; `XyzzyAnswerCallbackHandler`/
`XyzzyJudgeCallbackHandler` handle the two button types. `/xyzzy_status` now reports the live
question and who's still got to answer.

**Verified**: 5 new tests in `tests/Roboto.Bot.Tests/Xyzzy/XyzzyRoundLoopTests.cs` (36 total, up
from 31) - critically, `FullRoundEndToEndDealAnswerJudgeAndAdvance` plays out a whole round for
real: deals hands via `TestBot.SendCallbackAsync`-tappable buttons captured off `SentMessage.Buttons`,
two players answer, judging kicks in automatically, judge picks a winner, round auto-advances to
round 2 with an updated scoreboard - the actual proof the callback-query design works end-to-end,
not just that its pieces compile. Also covers admin-gating, double-answer rejection, the judge being
blocked from answering, and a stale (round-already-over) button tap. Confirmed flakiness-free (the
deck/judging shuffles use `Random.Shared`) across 8 repeated full runs. Sanity-checked per the
established pattern: disabled the "all answers in, start judging" trigger, confirmed exactly the two
round-completion tests failed with clear errors while the other 34 passed, then reverted.
`docker compose build` unaffected.

## Explicitly deferred / blocked work

- **`mod_xyzzy` v1 scope cuts** (deliberately dropped for size, not structurally hard to add back):
  CardCast/CRCast pack import (the original service is dead, current code points at a community
  mirror and already has its own dormant-pack-removal disabled); multi-blank ("Pick 2") questions;
  "Mess With" (joke/fake score display, purely cosmetic); cross-chat DM `/xyzzy_leave` (typed with
  no chat context, scans every chat you're in) — v1's `/xyzzy_leave` is group-context only like
  every other command; stats/metrics (`registerStatType`/`logStat` calls throughout legacy) — no
  stats subsystem exists yet beyond command-usage counts; the elaborate multi-step setup wizard
  (defaults-vs-custom chain, pack-filter pager, timeout/throttle prompts before the game starts) —
  v1 starts straight into `Invites` with fixed defaults, `/xyzzy_settings` (phase 8.4) covers
  adjusting afterward.
- **Charting** (`/statgraph`) — ScottPlot/SkiaSharp, debian-slim runtime base. Not started at all.
- **XML→SQLite migration importer** — first-class, must-be-safe deliverable, see the live-production
  warning in `CLAUDE.md`. Needs a real copy of the production XML + test-bot tokens from the user
  before work starts; build and prove it against test data first, only point it at a real prod XML
  copy once already proven correct, and even then only into a test-bot context.
- **JSON library for code that reads legacy-shaped data** (e.g. the migration importer) — the new
  SQLite layer uses `System.Text.Json`; whether the importer wants `Newtonsoft.Json` instead (to
  match the legacy code's looser `JObject`-style parsing) is still open, not urgent.
- **Shutdown/cancellation redesign.** User's words: "the exit logic sucks." Current
  `TelegramPollingService` inherits the legacy shape by construction — Ctrl-C/SIGTERM has to wait
  out whatever long-poll HTTP call is currently in flight before it can actually exit. Not yet
  decided how to do better (shorter poll timeouts? cancel the in-flight call outright? just accept
  and document a bounded worst-case delay?).
- **`ROBOTO_INSTANCE` vs legacy `-context`** — functionally the same concept (which bot identity to
  run as). Worth a deliberate naming/consistency pass later rather than carrying two names for one
  idea indefinitely; not urgent.
- **`/save`, `/background`** — legacy mod_standard commands with no equivalent need any more
  (SQLite already writes incrementally; no periodic background-processing loop exists yet to
  manually trigger). Not planned to be ported as-is; revisit only if a real need shows up.
