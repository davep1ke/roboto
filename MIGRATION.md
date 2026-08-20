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
| 8.2 `mod_xyzzy`: round loop + inline-keyboard/callback-query infra | Done, verified | `14beec1` |
| 8.3 `mod_xyzzy`: background scheduler, reminders/timeouts/throttle, quiet-hours | Done, verified | `b68c07d` |
| 8.4 `mod_xyzzy`: `/xyzzy_settings` admin/moderation menu | Done, verified | `b00e43f` |
| 8.5 `mod_xyzzy`: proper `/xyzzy_start` setup wizard (defaults/configure) | Done, verified | `e641fb6` |
| 8.6 `mod_xyzzy`: setup/begin keyboards moved to DM, bot players | Done, verified | `24571d1` |
| 8.7 `mod_xyzzy`: `/xyzzy_settings` keyboard + pending-action reminder | Done, verified | `eecdd66` |
| 8.8 `ReplyRouter` multi-context support (several pending replies per user) | Done, superseded by 8.9 | `7440283` |
| 8.9 `DmOutbox`: strict per-user one-thing-at-a-time DM serialization | Done, verified | `a18c972` |
| 9. Remaining modules (quote, birthdays, wordcraft, steam) | Done, verified | `d3742dd`/`d703b11`/`38e0035`/`ccbf285` |
| 10a. Stats engine (`StatsRecorder`, `/stats` extended) | Done, verified | `89fdf50` |
| 10b. Charting (ScottPlot), `/statgraph` | Done, verified | `31f2b7c` |
| 8.10 `mod_xyzzy`: multi-answer ("Pick 2"+) question support | Done, verified | `4a6b1d8` |
| 8.11 `mod_xyzzy`: settings-menu completeness (Re-deal/Reset/Extend/Force Question/Change Packs), catalog lookup performance, bot top-up re-check | Done, verified | `e73d4ec` |
| 13. Dormant-chat purge (`ChatPurgeReconciler`) | Done, verified | `e73d4ec` |
| 14.1 Pack default-semantics reversal (`XyzzyPackFilter`) | Done, verified | `d24a007` |
| 14.2 Stats engine dual-track (`StatBucket`), `/stats`/`/statgraph` rebuild | Done, verified | `c74bec5` |
| 14.3 Settings menu: fixed Abandon confirm, Extend on a running game, Mess With | Done, verified | `1b5f24e` |
| 14.4 `/xyzzy_leave` DM picker, `/xyzzy_get_settings` real content | Done, verified | `691b1ad` |
| 14.5 Live crcast pack import/sync (`CrCastPackImportService`) | Done, verified | `12e7d1b` |
| 14.6 Pack selection in the `/xyzzy_start` setup wizard | Done, verified | `4cc6087` |
| 14.7 Fix the "infinite timeout" bug (input + reconciler) | Done, verified | `012ab89` |
| 14.8 Instance identity merge (hostname-derived `ROBOTO_INSTANCE`) | Done, verified | `dca00bd` |
| 14.9 Fix `DmOutbox` front-insert window preempting unrelated queued requests | Done, verified | `52042e5` |
| 14.10 Round-flow message parity: judging/win-announcement wording, multi-game DM chat stamp | Done, verified | `9fe6481` |
| 14.11 Real chat-name stamp (not chat ID), bolded winning answer, round-start wording | Done, verified | — |
| 11. XML→SQLite migration importer | In progress — stages A (8.10) and pack-filter import wiring (8.11/14.1) done; card/chat/reply mapping done and dry-run-verified against real production XML; real (non-dry-run) import not yet performed | — |
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

## `mod_xyzzy` 8.3: background scheduler, timeouts, throttle, quiet-hours — done and verified (2026-08-17)

First real scheduled-task infrastructure anywhere in the app - before this, `TelegramPollingService`'s
poll loop was the only `BackgroundService`, and there was no timer/cron abstraction at all.
`Xyzzy/XyzzyRoundSchedulerService.cs` (a thin `BackgroundService`, ticks every 60s, registered
directly in `Program.cs` like `TelegramPollingService` - not inside `AddRobotoBot()`, so tests never
get a real ticking timer) delegates every tick to `Xyzzy/XyzzyRoundReconciler.cs`, which is fully
testable on its own (same split `MessageDispatcher` got from `TelegramPollingService`). Mirrors
legacy's `check()`: a reminder DM at 75% of `MaxWaitHours`, a force-advance at 100% (judge with
whatever answers came in, or skip to a fresh question if nobody answered at all; a judging timeout
auto-picks a random submission rather than legacy's "dock the judge" quirk, not judged essential to
keep).

Also lands the `MinWaitHours` throttle and quiet-hours integration that 8.2 deliberately deferred: a
new `XyzzyStatus.WaitingForNextHand` phase sits between rounds when either applies, and
`XyzzyRoundReconciler` resumes play once both clear. `Commands/QuietHoursQuery.cs` is a small
read-only query against the same key `SetQuietHoursCommand` already writes (its `QuietHoursKey`
helper made `public` for this) - mirrors legacy's cross-module `mod_standard.isTimeInQuietPeriod`
call. `IStateStore` gained `LoadAllAsync<T>(keyPattern, ct)` (a SQL `LIKE` query) since the scheduler
needs to find every active game, not one known key - `XyzzyGameRepository.GetAllActiveAsync` is the
only caller so far.

**Verified**: 8 new tests (44 total, up from 36) across `QuietHoursQueryTests.cs` (including the
overnight-wraparound case, e.g. 22:00-06:00, via an optional `now` override on `QuietHoursQuery`
added purely so that branch is deterministically testable without a full clock-abstraction refactor)
and `Xyzzy/XyzzyRoundReconcilerTests.cs` (reminder-sent-once, timeout-with-partial-answers,
timeout-with-zero-answers, judging-timeout auto-pick, and the throttle actually holding then
releasing the next hand) - all driven by directly backdating `StatusChangedUtc` rather than waiting
in real time, and calling `XyzzyRoundReconciler` directly rather than the real scheduler timer.
Stable across 8 repeated full runs. Sanity-checked per the established pattern: disabled the
reminder-already-sent guard, confirmed exactly `ReminderIsSentAt75PercentAndOnlyOnce` failed with a
clear values-differ message while the other 43 passed, then reverted. `docker compose build`
unaffected.

## `mod_xyzzy` 8.4: `/xyzzy_settings` admin/moderation menu — done and verified (2026-08-17)

`Xyzzy/Commands/XyzzySettingsCommand.cs` - a single free-text DM command (`abandon`,
`timeout <hours>`, `throttle <hours>`, `kick`, `score <player> <points>`, `cancel`) rather than
porting legacy's per-action keyboard sub-flows one at a time. Only `kick` needs a second question
(which player - there's no message to reply-to inside a DM the way `/addadmin` uses in-group
replies, so it lists player names and matches the free-text reply). Everything else resolves in one
round trip. Admin-gated via `ChatState.IsAdmin`, same as `/addadmin`/`/xyzzy_begin`; reuses
`ReplyRouter` exactly as `/setquiethours` does (single-admin, one-at-a-time - the reason `ReplyRouter`
was fine to leave as-is back in phase 8.2 rather than extending it for round-play).

This completes the phase-8 `mod_xyzzy` port. `/xyzzy_settings`'s legacy "Mess With" (joke/fake score
display) and full pack-management sub-menus stay dropped per the v1 scope cuts below - `score`
covers the one moderation need (correcting a mistake) that isn't purely cosmetic.

**Verified**: 9 new tests in `tests/Roboto.Bot.Tests/Xyzzy/XyzzySettingsTests.cs` (53 total, up from
44) - admin gating, abandon, timeout/throttle (including an invalid value being rejected without
touching state), the two-step kick flow (including kicking an unknown name cleanly), score override,
cancel, and running the command with no game active. Stable across 5 repeated runs. Sanity-checked
per the established pattern: disabled the admin-gate check, confirmed exactly
`OnlyAnAdminCanOpenTheMenu` failed with a clear message while the other 52 passed, then reverted.
`docker compose build` unaffected.

## `mod_xyzzy` 8.5: proper `/xyzzy_start` setup wizard — done and verified (2026-08-17)

Un-deferred from the original v1 scope cuts after manual testing surfaced the gap directly (user
testing round 2026-08-17 - "assume it's good, carry on... which might be the more complex game
setup routine"). `/xyzzy_start` now asks "defaults" / "configure" / "cancel" over DM before the game
reaches `Invites`, with "configure" walking through question limit, timeout, and throttle as three
follow-up DM questions - same free-text-reply shape as `/xyzzy_settings` and `/setquiethours`, not
legacy's keyboard-driven chain. Pack-filter selection specifically stays cut (see the scope-cuts note
below) since v1 only has the one hardcoded pack.

New `XyzzyStatus.SettingUp` phase covers the whole conversation (game exists, starter already
added as a player, before Invites) - a game abandoned mid-setup (starter never replies) is
auto-reset to `Stopped` after 24h by `XyzzyRoundReconciler`, mirroring legacy's own "idle setup
auto-resets" behavior, so it can't squat the chat's one-game slot forever. If the starter has no
open DM at all, the whole thing rolls back to `Stopped` immediately instead of leaving a
nobody-can-ever-finish game stuck in `SettingUp`.

Also added real support for the question-limit setting itself, which existed on paper in legacy
(`enteredQuestionCount`) but had no equivalent field yet in the port: `XyzzyGameState.QuestionLimit`
(-1 = unlimited), checked by a new shared `XyzzyRoundService.TryEndGameAsync` (also folded into the
existing "not enough players left" check, previously duplicated inline in `PickWinnerAsync`) after
every completed round, including the reconciler's "nobody answered, skip ahead" timeout path.

**Verified**: 8 new tests in `tests/Roboto.Bot.Tests/Xyzzy/XyzzyStartWizardTests.cs` (61 total, up
from 53) - the full configure path setting all three values, invalid input at each step
re-prompting without losing progress, cancel resetting the game completely (and a fresh
`/xyzzy_start` working right after), an unrecognised choice re-prompting, the no-DM rollback, a
question-limit actually ending a game after the configured number of rounds, and the 24h
abandoned-setup auto-reset. All 15 pre-existing `mod_xyzzy` tests across four other files needed a
one-line update each (drive the new DM step to reach `Invites`) since `/xyzzy_start` no longer
reaches it in one call - a real behavior change, not a test-only fixup. Stable across 6 repeated
full runs. Sanity-checked per the established pattern: disabled the question-limit end-game check,
confirmed exactly `QuestionLimitEndsTheGameAutomatically` failed while the other 60 passed, then
reverted. `docker compose build` unaffected.

## Bug fix: enum ordinals silently reinterpreted after a persisted status shifted (2026-08-17)

Live-bot testing round 3 (2026-08-17) surfaced a real bug: the user's in-progress game looked stuck
("thinks it's asked me the setup, but nothing's waiting") right after phase 8.5 shipped. Root cause:
`SqliteStateStore` used `System.Text.Json`'s default options, which serialize enums as their raw
underlying *number*, not their name. `XyzzyGameState.Status` had been persisted as `"Status": 1`
while `1` meant `Invites` (the pre-8.5 enum ordering); inserting `XyzzyStatus.SettingUp` into the
*middle* of the enum in 8.5 shifted every later value's ordinal by one, so that same persisted `1`
silently became `SettingUp` on deploy - no exception anywhere, just quietly wrong data.

Fixed at the root in `Persistence/SqliteStateStore.cs`: enums now serialize by name
(`JsonStringEnumConverter`), so future enum insertions/reordering can never again change what
already-persisted data means. The specific corrupted row (the user's stuck game, which had no real
round in progress - only the starter had joined) was reset by hand rather than migrated, then the
live `beefy` instance was rebuilt and restarted with the fix. Full incident note lives as a comment
in `SqliteStateStore.cs` and `XyzzyStatus.cs`, per this project's "why does this look like this"
convention.

**Verified**: 2 new tests in `tests/Roboto.Bot.Tests/SqliteStateStoreTests.cs` (63 total, up from
61) - one asserts persisted JSON contains the enum's name rather than a number, one confirms old
numeric-encoded data is still readable (the converter accepts a bare number on read, so this fix
doesn't break anything already on disk, it just stops the class of bug from recurring). Sanity-checked
by temporarily reverting to default JsonSerializerOptions and confirming exactly
`EnumsArePersistedByNameNotOrdinal` failed with a clear message while the other 62 passed, then
restored. Full 63/63 suite green, `docker compose build` unaffected.

## `mod_xyzzy` 8.6: setup/begin keyboards moved to DM, bot players — done and verified (2026-08-17)

Direct response to live-testing feedback (round 4, 2026-08-17): "bring back the keyboard options
for setup", "move /xyzzy_begin over to the private chat", "not add language about forcing the game
to start", and "populate the empty slots with bots" when a game starts short of players.

**Setup choice is a real inline keyboard again.** `/xyzzy_start`'s "Use Defaults / Configure Game /
Cancel" question (phase 8.5 had made this free-text) is back to matching legacy's own keyboard for
that exact decision - new `XyzzySetupCallbackHandler` (`xy:su:<chatId>:<choice>`) owns the taps. The
three "configure" follow-ups (question limit/timeout/throttle) stay free-text through `ReplyRouter`
- legacy asked those as plain numbers too, they were never keyboard-driven even in the original app.

**`/xyzzy_begin` (the group command) is gone entirely**, replaced by a "Start" button DM'd to the
starter once setup finishes (`XyzzyRoundService.FinishSetupAsync` → new `XyzzyBeginCallbackHandler`,
`xy:sb:<chatId>`). No admin-gate needed any more either - only the starter's own DM ever has the
button, so *having* it is the access control, same as legacy sending that keyboard specifically to
`m.userID`.

**Bot players fill empty slots instead of anyone ever needing to force-start.** Tapping Start tops
the game up to `XyzzyRoundService.MinPlayers` (3) with bots (`XyzzyPlayer.IsBot`, negative synthetic
`PlayerId`s so they can never collide with a real Telegram user) if it's short - the old
`/xyzzy_begin force` 2-player escape hatch, and all its messaging, is gone along with the command
that carried it. Bots get a normal hand and "pick randomly for now" (user's own words) - they
auto-submit an answer the instant a round deals hands, and auto-pick a winner the instant judging
would otherwise DM them (which never happens - PlayerId is synthetic, there's no real chat to
message). `XyzzyRoundService.TryEndGameAsync` now also stops a game if only bots are left, which
turned out to be load-bearing, not just tidy: a game that lost its last real player would otherwise
recurse `BeginQuestionAsync → bot auto-submit → BeginJudgingAsync → bot auto-pick → next round → ...`
forever with nothing to ever pause it - confirmed for real by the sanity check below (a genuine
`StackOverflowException`, not a hypothetical).

Auditing every place `JudgePlayerId!.Value` was dereferenced surfaced a second, pre-existing latent
bug this same shape of change made much easier to actually hit: a departing judge (leave or kick)
sets `JudgePlayerId` to null, and three call sites (`BeginJudgingAsync`, and the reconciler's
reminder/force-advance) assumed it couldn't be. Fixed with a null-judge guard at the top of
`BeginJudgingAsync` (redeals with a freshly-rotated judge) and in the reconciler's timeout dispatch;
`XyzzyLeaveCommand` also resolves it immediately (via `TryEndGameAsync`/`BeginQuestionAsync`) rather
than waiting up to a minute for the next scheduler tick.

**Verified**: `docker compose down` + rebuild + restart against `@Beefy_Surprise_bot` per the
standing "leave the bot running" convention. 65 tests (up from 63) - every pre-existing `mod_xyzzy`
test's helpers updated for the button-based setup/begin flow (a real behavior change, not a
test-only fixup - same as 8.5's transition), plus new coverage: tapping Start with only the starter
present fills bots and actually starts a round; a full multi-round game played entirely by a solo
human against two bots (deterministic judge rotation makes both "human judges, bots auto-answer" and
"bot judges, human answers, bot auto-picks and chains to the next round" reachable in one test); the
last-human-leaves safety stop; a tampered/invalid setup callback rejected cleanly with the real
buttons still working afterward. Stable across 15 repeated full runs. Sanity-checked per the established pattern, with a genuinely
notable result this time: temporarily reverting the all-bots-end check didn't just fail an
assertion, it reproduced a real `StackOverflowException` - concrete proof the guard is load-bearing,
not just tidy defensive code. `docker compose build` unaffected.

## `mod_xyzzy` 8.7: `/xyzzy_settings` keyboard + pending-action reminder — done and verified (2026-08-17)

Two pieces of direct live-testing feedback (round 5, 2026-08-17): "we need to add the keyboard back
in there" (re: `/xyzzy_settings`, still free-text from phase 8.4) and, more importantly, running
`/xyzzy_settings` mid-round left "no way to know that it was still expecting my answer to the game
question" once the settings interaction finished.

**Keyboard**: `/xyzzy_settings`'s top-level menu (Abandon/Timeout/Throttle/Kick/Score/Cancel) is an
inline keyboard again (new `XyzzySettingsCallbackHandler`, `xy:se:<chatId>:menu:<action>`). Kick and
Score both needed "which player" - also now a keyboard (`xy:se:<chatId>:kick|score:<playerId>`)
rather than free-text name matching, which is a correctness improvement in its own right (player
identified by ID, not a case-insensitive name lookup that could theoretically collide). Timeout/
Throttle/the final score-points value stay free-text through `ReplyRouter` - no sensible keyboard
for an arbitrary number, same reasoning as `/xyzzy_start`'s configure flow.

**Reminder**: every settings action now ends by calling new `XyzzyRoundService.RemindIfActionPendingAsync`
for whoever ran the command - a no-op unless that admin currently has an outstanding card to play or
a winner to pick in *this* game, in which case it re-sends the actual keyboard (not just a text
nudge) with a "Reminder: ..." prefix, so there's nothing to scroll back to find.

**Also addressed directly (not just coded around)**: the user separately flagged, unprompted, that
`ReplyRouter`'s one-pending-reply-per-user design exists specifically because legacy's
`ExpectedReply` let one user hold several outstanding conversations at once (multiple groups/games
without overlap) - worth bearing in mind for any future free-text flow. Confirmed this specific bug
isn't actually that: card answering/judging is callback-query-based, not `PendingReply`-based, so it
never contends for the one slot regardless of how many games a player is in - the reminder gap was
purely "the message got buried", not a state collision. The broader concern is real, not yet hit,
and now explicitly tracked (`PendingReply`'s own doc comment, and the deferred-work list below)
rather than left implicit.

**Verified**: 67 tests (up from 65) - keyboard-driven Kick/Score end-to-end, a tampered kick target
(forged callback_data) rejected cleanly, and - the actual regression test for the reported bug - a
full scenario playing round 1 out completely (judge rotation is deterministic, so round 2 reliably
leaves the admin themselves with an outstanding card), running `/xyzzy_settings` mid-round-2, and
confirming their hand keyboard gets resent with a reminder afterward; plus a negative case confirming
no reminder fires when nothing is actually outstanding. Stable across 10 repeated runs. Sanity-checked
per the established pattern: temporarily short-circuited `RemindIfActionPendingAsync` to a no-op,
confirmed exactly the regression test failed with a clear "reminder never sent" message while the
other 66 passed, then reverted. `docker compose build` unaffected.

## 8.8: `ReplyRouter` multi-context support — done and verified (2026-08-17)

Direct follow-up to the concern the user raised while reviewing 8.7's reminder fix, then confirmed
as a real requirement rather than a hypothetical: "I need to be able to handle this... not uncommon
for users to be in multiple groups, and I can see someone opening the settings menu and it all going
to pot." `ReplyRouter`/`PendingReply` previously tracked exactly one outstanding free-text
conversation per user, globally (see 8.7's section above and `PendingReply`'s own doc-comment
history) - two of those outstanding at once (e.g. `/xyzzy_settings` open in two different chats)
would silently clobber each other.

Fixed to match legacy's actual approach: `ReplyRouter` now stores a **list** of pending replies per
user (new key `pending-replies:{userId}`, plural - deliberately not reusing the old singular key,
since the stored shape changed from one object to a list and reusing it would mean deserializing old
data into the wrong shape; any conversation genuinely mid-flight across this exact deploy is simply
orphaned, not migrated - an acceptable one-time hiccup for a DM conversation, not real state).
`PendingReply` gained `QuestionMessageId` (the bot's own sent message ID); an incoming DM is matched
by requiring it be a Telegram "reply" to that specific message whenever more than one thing is
outstanding. With exactly one pending, a plain (non-reply) message still resolves it, unchanged from
before - only genuine ambiguity requires an explicit reply-to, and if it's ambiguous the bot says so
explicitly rather than guessing which context to apply the answer to.

Card answering/judging was never affected by any of this - it's inline-keyboard/`CallbackQuery`-based
specifically so it doesn't share this mechanism at all, confirmed and documented in 8.7 already.

**Verified**: 4 new tests in `tests/Roboto.Bot.Tests/ReplyRouterMultiContextTests.cs` (71 total, up
from 67) using `/setquiethours` as the vehicle (a real two-step DM flow, triggered from a group but
always answered in the same private chat - exactly the shape that needs disambiguating) - two
simultaneous flows with *identical question text* resolved correctly and independently via
reply-to, even when answered out of order; genuine ambiguity (no reply-to, two pending) gets an
explicit "reply directly" response rather than a guess; the single-pending case still works without
requiring reply-to (backward compatible); replying to an untracked message id falls through to
normal command dispatch rather than being swallowed. Stable across 8 repeated runs. Sanity-checked
per the established pattern: broke reply-to matching to always pick the first pending entry
regardless of which message was replied to, confirmed exactly the two tests that exercise real
disambiguation failed - with a values-crossed-between-contexts message, the exact failure mode this
phase exists to prevent - while the other 69 passed, then reverted. `docker compose build`
unaffected.

## 8.9: `DmOutbox` — strict per-user DM serialization — done and verified (2026-08-18)

Supersedes 8.8 entirely, not an incremental change to it. Walking through the scenario the user
posed - mid-setup in a third group while two other games are in progress - exposed that 8.8's
reply-to-disambiguation design still let every game send DMs the instant it had something to say;
only *answering* was disambiguated. The user rejected that outright: "The question is still sat
there waiting, but I don't like that behaviour as its confusing for users. They will forget to
answer the original question as it will scroll off the chat window. We should queue everything
(questions, expected replies, messages) and only send things when the window is clear." They also
explicitly rejected treating button questions and typed-reply questions as different cases: "I
don't see any difference between a question thats been asked with a keyboard vs a question thats
been asked waiting for a text reply."

Built `Commands/DmOutbox.cs`: every DM the bot sends a user that's part of a conversation - a
notice, a button question, or a free-text question - now goes through one per-user FIFO queue
(`dm-outbox:{userId}`) instead of `bot.SendMessage` directly. Only the queue head is ever actually
delivered; everything else waits until it's resolved. `ReplyRouter` and the pre-existing
`CallbackQueryRouter` (8.2) both shrank to thin adapters over it - `PendingReply` lost the
multi-context reply-to matching 8.8 added (`QuestionMessageId`, the "which of several pending
replies" logic) since there's structurally only ever one thing to answer per user now.
`CallbackQueryRouter` gained a stronger staleness guard as a side effect: a tap is only dispatched
if the tapped message ID is exactly the user's current DmOutbox head, ahead of (not replacing) the
existing per-round staleness check baked into `xy:*` callback_data itself.

Two ordering subtleties came up that weren't obvious until real flows were traced through:

- **Remove-then-pump, not remove-and-pump.** Resolving the queue head and pumping the next item had
  to be split into two separate calls (`RemoveCurrentHeadAsync`, then `PumpNextAsync`), with the
  answering handler's own dispatch happening in between. Discovered as a real bug, not
  hypothetically: a multi-step flow's own next question (e.g. `/setquiethours`' end-time prompt,
  asked from inside the handler that just validated the start time) was landing *behind* an
  unrelated game's already-queued message instead of immediately after its own start-time question
  - exactly the fragmentation the user's directive was written to prevent. Fixed by giving
  `DmOutbox` a per-user "resolving" window (`_resolvingInsertIndex`, in-memory only): entries
  enqueued between `RemoveCurrentHeadAsync` and `PumpNextAsync` insert at the front of the queue,
  in call order, ahead of anything a different flow already queued behind the head that just
  resolved - `PumpNextAsync` then delivers whichever of those is now first.
- **A rejected/invalid answer needs a real replacement, not just an error toast.** For typed
  replies this was already true (`SetQuietHoursCommand`/`XyzzyStartCommand` re-`AskAsync` a fresh
  prompt on invalid input). Button flows didn't have the equivalent: since the router removes the
  head unconditionally before dispatching, `XyzzySetupCallbackHandler`'s "not a valid choice"
  branch was silently orphaning the queue with nothing to replace the removed head - a second tap
  on the same still-visible keyboard then got rejected as stale, permanently stuck in `SettingUp`.
  Fixed by having that branch re-enqueue the identical choice keyboard (`XyzzyStartCommand.
  BuildChoiceKeyboard`, extracted to a shared static so the two callers can't drift out of sync) -
  the resolving-window fix above means it lands immediately as the new head, exactly like a typed
  re-prompt would.

`RemindIfActionPendingAsync` (8.7's nudge for "you have an unanswered card, resolve it before
opening settings") was deleted outright rather than adapted - the bug it patched over is now
structurally impossible, since `/xyzzy_settings`'s own menu question can't be delivered at all
while something else is still outstanding.

**Known, accepted UX consequence, surfaced to the user rather than silently worked around**: a
game's own starter cannot do anything else - including open `/xyzzy_settings` - while their own
"Start" button is still outstanding in their queue. This falls directly out of the "one thing at a
time" design and isn't a bug; noting it here in case it's ever reported as one.

**Verified**: full suite stays at 81 tests (no net new tests - existing coverage rewritten in place
rather than expanded, since this replaces 8.8's mechanism rather than adding to it).
`ReplyRouterMultiContextTests.cs` fully rewritten for strict-queue semantics (a second flow's first
question isn't sent at all until the first resolves, then appears automatically with no user
action; a real two-games-in-progress-plus-a-new-setup scenario using genuinely joined players -
not solo bot-filled games, which turned out to make the human an immediate blocking judge and
obscure the point being tested - proves the third game's setup choice waits in line behind two
real hand-keyboard questions from unrelated games and then proceeds normally once they clear).
`XyzzySettingsTests.cs` and `XyzzyStatsTests.cs` updated for the same reason 8.7's reminder tests
were removed - both needed their game-setup helpers to actually resolve the starter's own
outstanding queue item(s) before asserting the settings menu appears. Sanity-checked per the
established pattern: temporarily made `IsCurrentHeadAsync` always return `true` (bypassing the
message-id staleness guard entirely), confirmed exactly the three tests that exercise stale/forged
taps (`AStaleTapAfterTheRoundHasMovedOnIsRejected`, `TheJudgeCannotSubmitAnAnswer`,
`AnsweringTwiceInTheSameRoundIsRejected`) failed with the old "no longer valid" toast never
appearing, while the other 78 passed, then reverted. `docker compose build` unaffected.

## 9: remaining modules (mod_quote, mod_birthdays, mod_wordcraft, mod_steam) — done and verified (2026-08-18)

Ported all four remaining legacy modules in one pass, batched with 10b and the shutdown
investigation per the user's request ("go for all of those in one pass - then one big test phase
afterwards"), rather than the previous phase-by-phase verify loop - individual commits per module,
one big verification pass at the end instead.

**Design call confirmed with the user up front**: every multi-step conversational flow (quote
add/conv/config, birthday add/remove, steam add/remove-player) goes over DM through `DmOutbox`,
matching `SetQuietHoursCommand`/`XyzzySettingsCommand` - not in the group chat the way legacy asked
them. Keeps every "bot is waiting on you" interaction in the codebase going through the same
one-thing-at-a-time queue (8.9's mandate) instead of adding a second, un-queued callback pathway
just for group-posted keyboards.

- **`Wordcraft/`** - simplest: one global (not per-chat) word list, `/craft` (ports the odd
  chained-number-suffix roll verbatim), `/craft_add`/`/craft_remove`.
- **`Birthdays/`** - per-chat `List<BirthdayEntry>`, `/birthday_add` (2-step DM), `/birthday_remove`,
  `/birthday_list`. `BirthdaysReconciler`/`BirthdaysSchedulerService` (hourly tick) is the same
  testable-class-plus-`BackgroundService` split `XyzzyRoundReconciler`/`XyzzyRoundSchedulerService`
  established - repeated here and for quotes/steam rather than introducing a new shared scheduler
  abstraction, since three more instances of an already-proven pattern is simpler than a generic one
  built speculatively for exactly three callers.
- **`Quotes/`** - the trickiest flow: `/quote_conv` is open-ended (an unknown number of "Name\text"
  lines until "done"/"cancel"), so unlike every fixed-step flow elsewhere it re-asks the same step
  repeatedly, accumulating lines into `PendingReply.Data` via a unit-separator-delimited string
  (same idea legacy's own flattening approach used, replacing its fragile `<<#::#>>` text marker).
  `/quote_config`'s menu (`QuoteConfigCallbackHandler`) re-offers itself on an unrecognised
  choice - the same fix `XyzzySetupCallbackHandler` needed in 8.9 (the router removes a tapped
  keyboard as the resolved head before the handler runs, so an unhandled choice needs a real
  replacement or the flow gets stuck) - applied proactively here instead of shipping the same bug
  into new code.
- **`Steam/`** - the only module needing a real external dependency: a Steam Web API key.
  `bot.env` gains an optional `SteamApiKey=` line (`InstanceBootstrapper`/`BotOptions`) - blank by
  default, and unlike `TelegramToken` a blank value never blocks startup; commands say tracking
  isn't configured, the background job logs once and no-ops. `SteamApiClient` ports the three GET
  calls with `HttpClient`/`System.Text.Json` instead of legacy's `WebClient`/`Newtonsoft.Json`.
  **Deliberate correction, not a faithful-bug port**: achievement-fetching filters to `Achieved ==
  1`. Legacy's equivalent added every achievement name the endpoint returned regardless of that
  flag, which would have misannounced every achievement in a game as "just earned" the first time
  any player was ever checked - clearly not the intent for a live announcer bot, so this fixes it.

**Verified**: 32 new tests (113 total, up from 81) across `Wordcraft/WordcraftTests.cs` (5),
`Birthdays/BirthdaysTests.cs` (7, including the reconciler's once-per-day guard),
`Quotes/QuotesTests.cs` (12, including the full `/quote_conv` loop, a malformed-line abandons-the-
flow case matching legacy, and the config-menu re-offer), `Steam/SteamTests.cs` (8, including a
`FakeSteamHttpHandler`-backed test of `SteamApiClient` against realistic Steam API response shapes
and a full reconciler run proving a new achievement gets announced once and only once). Stable
across repeated runs. `docker compose build` unaffected. `beefy` rebuilt and restarted on this code
so the user can do a live round-trip pass over Telegram - not done here, since it needs a real
Telegram client (`/craft`, `/birthday_add` + `/birthday_list`, `/quote_add` and `/quote_conv`, and
Steam's no-key-configured messaging on `/steam_addplayer`/`/steam_stats`/`/steam_help` are the
natural things to try).

## 10b: `/statgraph` charting — done and verified (2026-08-18)

On top of 10a's `StatsRecorder` - purely a rendering layer, no new data collection. Added the
`ScottPlot` NuGet package (5.1.59), which pulls in `SkiaSharp.NativeAssets.Linux.NoDependencies` -
statically linked, so no `libfontconfig1`/etc needed in the Debian runtime image after all (the
Dockerfile's own comment had flagged this as a likely need; turned out not to be one, confirmed by
actually running `docker compose build` after adding the package rather than assuming). `/statgraph
<name>` renders the named `StatSeries`' `RecentPoints` (already bounded to
`StatsRecorder.MaxRecentPoints`) as a PNG line chart via `plot.GetImageBytes(...)`, sent with
`bot.SendPhoto`. No name, or an unrecognised one, lists the available stat names (same list
`/stats` already shows).

`FakeTelegramBotClient` gained a `SendPhotoRequest` case (captures chat id/caption/filename/raw
bytes onto a new `SentPhoto` record), following its own doc comment's "add a case as soon as a
command needs something else" convention.

**Verified**: 4 new tests (117 total, up from 113) (`StatGraphTests.cs`) - no-argument and unknown-name both list available
stats without sending a photo; a known stat with recorded history actually renders and sends a real
PNG (asserted via the file's magic-byte signature, not just "didn't throw"); name matching is
case-insensitive. `docker compose build` succeeds with the new dependency. Not yet done: a live
`/statgraph` round-trip against the running `beefy` container over real Telegram - the test suite
proves the rendering code path works, but the statically-linked-SkiaSharp assumption (no
`libfontconfig1`/etc needed) is only fully proven once an actual image comes back inside the real
runtime container, not just a local `dotnet test` run on a dev machine that already has more system
libraries installed than the container does. `beefy` is rebuilt and running on this code for the
user to try it.

## 10a: Stats engine — done and verified (2026-08-17)

Requested alongside 8.8 ("let's build that, and the stats engine at the same time"). Replaces
legacy's `Roboto.Settings.stats.registerStatType`/`logStat` with `Stats/StatsRecorder.cs` - a
lightweight named-counter engine, deliberately just the data-collection half, not a charting system
(that's `/statgraph`, phase 10b, still separately deferred: needs ScottPlot/SkiaSharp and a
debian-slim base image change - no reason to couple "can we record numbers over time" to "can we
render a graph of them"). No registration step needed unlike legacy - a `StatSeries` is created on
first use with whatever `StatMode` (`Cumulative` - running total, or `Snapshot` - replaces each
time) the caller passes, same "just add a property, no schema migration" philosophy as the rest of
`IStateStore`. Every recorded value also lands in a bounded (`MaxRecentPoints = 500`) time series, so
10b has real history to plot without a data migration whenever it lands.

Wired into five real `mod_xyzzy` events to prove it end-to-end, not just built-and-unused:
`xyzzy.games-started` (`XyzzyStartCommand`, on a successful start), `xyzzy.hands-played`
(`XyzzyRoundService.PickWinnerAsync`, once per completed round), `xyzzy.games-ended`
(`XyzzyRoundService.TryEndGameAsync`'s two stop conditions, plus the `/xyzzy_settings` Abandon
action), and `xyzzy.active-games`/`xyzzy.active-players` (`XyzzyRoundReconciler.ReconcileAllAsync`,
a free snapshot each scheduler tick reusing the active-games list it already loads for its normal
timeout/throttle work - no extra query). `/stats` now shows all recorded series alongside the
existing per-command usage counts it already tracked.

**Verified**: 10 new tests (81 total, up from 71) - `StatsRecorderTests.cs` covers the engine itself
(cumulative accumulation, snapshot replacement, `GetAllAsync`, bounded history, persistence across a
restart); `Xyzzy/XyzzyStatsTests.cs` covers all five wired-in events actually firing, including the
reconciler's active-games/players snapshot and `/stats` surfacing a recorded stat by name. Stable
across 8 repeated runs. Sanity-checked per the established pattern: disabled the bounded-history
truncation, confirmed exactly `RecentPointsAreBoundedRatherThanGrowingForever` failed (500 expected,
510 actual) while everything else passed, then reverted. `docker compose build` unaffected.

## 8.10: `mod_xyzzy` multi-answer ("Pick 2"+) question support — done and verified (2026-08-18)

Prerequisite for phase 11's importer, not an isolated feature request: the user handed over real
production XML exports (see phase 11's kickoff section below) and asked that the real card catalog
- including genuine multi-answer cards, which the placeholder `CardCatalog` and v1 round-play logic
never supported - import with full fidelity rather than being silently dropped. Built this first,
on its own, before any importer code.

Good news uncovered while scoping it: `XyzzyGameState.Submissions` (`Dictionary<long, List<string>>`)
was already shaped to hold more than one card per player - it just wasn't used that way. This was a
flow/UI change on top of an already-compatible data shape, not a data migration of its own.

- `XyzzyCard` ([CardCatalog.cs](src/Roboto.Bot/Xyzzy/CardCatalog.cs)) gained `AnswerCount = 1`
  (only meaningful on question cards).
- `XyzzyRoundService.SubmitAnswerAsync` now appends to the player's submission list instead of
  hard-rejecting a second submit; below the question's `AnswerCount` it re-offers a "pick your next
  card" hand keyboard (excluding cards already picked this round - `BuildHandKeyboard`) instead of
  checking for judging. The "is everyone done" check changed from "does everyone have an entry in
  Submissions" to "has everyone reached their full AnswerCount" - the former was a real bug caught
  before it shipped (a partially-submitted multi-answer player would have incorrectly counted as
  done).
- `BuildJudgeKeyboard` shows one button per *submitter*, not per card - multi-card submissions join
  with `" >> "` (`CombinedAnswerText`), legacy's own fallback format, used deliberately instead of
  reproducing legacy's primary regex-based per-blank interleaving. `PickWinnerAsync`'s winning-answer
  message uses the same join for a multi-card win; a single-card win is completely unchanged
  (still substitutes directly into the question's blank).
- Bots (`BeginQuestionAsync`'s auto-answer loop) submit in a loop until they've met the question's
  `AnswerCount`, same as a real player tapping through several cards.
- `XyzzyCallbackData`'s `xy:a:...`/`xy:j:...` encoding didn't need to change - each tap still
  submits/represents exactly one card; only the flow around how many taps are needed changed.

**Verified**: one new dedicated test
(`MultiAnswerQuestionRequiresPickingTheFullSetBeforeJudgingBegins`, 118 total, up from 117) that
forces a real 2-answer question via direct repository state (not a random draw - see below for
why), proving: a lone first card doesn't trigger judging and the re-offered hand excludes it; both
answerers fully completing triggers judging with one combined (`" >> "`-joined) button per
submitter; picking a winner advances the round normally. The full existing suite (117 tests, zero
changes to their own assertions) stayed green across 8 repeated runs both *before and after* adding
a multi-answer card to the real `CardCatalog.Questions` pool - the first attempt at adding it
non-conditionally caused one genuine, real flake
(`XyzzySettingsTests.StillWaitingOnACardBlocksTheSettingsMenuUntilItsAnswered`, caught immediately,
not left latent) because several existing tests assumed "tap one hand card = fully answered".
Fixed at the root with a new shared `TestBot.AnswerHandFullyAsync` helper (taps hand-keyboard
buttons in a loop until the round genuinely accepts the answer as complete) and updated every
affected call site across `XyzzyRoundLoopTests.cs`, `XyzzyRoundReconcilerTests.cs`,
`XyzzySettingsTests.cs`, `XyzzyStartWizardTests.cs`, and `ReplyRouterMultiContextTests.cs` -
deliberately *not* left as an accepted low-probability flake, since this codebase's own posture on
determinism (e.g. the judge-rotation-order invariants used throughout) treats that as a real defect
to fix, not noise to tolerate. Sanity-checked per the established pattern: temporarily forced
`SubmitAnswerAsync`'s "not yet done" branch to `if (false)`, confirmed exactly the new multi-answer
test failed (with the exact wrong message - "Answer submitted!" instead of "...pick your next
card") while all 117 others passed, then reverted. `docker compose build` unaffected.

## Phase 11 kickoff: XML→SQLite migration importer (2026-08-18)

User handed over three real production XML exports at `data/beefy/` (gitignored, confirmed via
`git check-ignore -v` before touching anything): `chat_mangler_bot.xml` (~139KB, 8 chats),
`robotolive.xml` (~4.1MB, 6 chats, includes mod_xyzzy + mod_steam + a real Steam API key),
`chat_against_humanity_bot.xml` (~114MB, 4783 chats, 766 pending ExpectedReplies - the xyzzy
production bot, mod_xyzzy-only). Explicitly flagged by the user as sensitive (live user data, live
pending conversations, live credentials) - schema reconnaissance was done safely, reading only tag
names/counts via Python `xml.etree.ElementTree`, never field values, confirming the root `settings`
type (`Roboto/settings.cs`) and its polymorphic per-module `chatData`/`pluginData` blobs
(`xsi:type`-tagged subclasses of `RobotoModuleChatDataTemplate`/`RobotoModuleDataTemplate`, one per
legacy module). This copy is stale (the user's own words) and for development/proof only - there
are still active games on the real server, and a *fresh* export will be pulled nearer actual
cutover, not this one.

**Decisions confirmed with the user for this phase:**
- Resume in-flight games (current question/hands/judge/pending replies) on import, not reset to
  Stopped - the harder, higher-fidelity path, which is why 8.10 (above) had to happen first.
- One `ROBOTO_INSTANCE` per source bot identity - `chatmangler`, `robotolive`, `cah` - imported in
  that order, matching the user's own stated sequence (easiest/smallest first).
- `robotolive`'s real Steam API key gets carried into that instance's `bot.env` - the *only*
  credential the importer will ever write. `telegramAPIKey` (present at the XML root on every
  export) is never written anywhere, in any instance, under any circumstance.
- **Stale-timestamp safety (explicit user concern, not a footnote)**: resumed games/replies are
  old enough that a reconciler tick would see them as instantly overdue and fire reminder/timeout
  logic the moment anything runs against the imported data. The importer resets every
  reconciler-driving timestamp (`StatusChangedUtc`, `ReminderSent`, quotes' `NextAutoQuoteAfter`,
  birthdays' `LastDayProcessed`) to import time rather than carrying the original stale value -
  and, as a second, independent layer, the default validation path never runs any live bot process
  (real or test token) against imported data at all - pure SQLite-level count/checksum reads. A
  live round-trip check, if ever wanted, only happens against a brand-new dedicated test-bot token
  that's never touched a real chat.

**Cutover shape (confirmed, not being done yet)**: legacy runs from a Windows VM on the user's NAS
(TrueNAS); the rewrite moves each bot to its own Docker container on that same NAS. Per bot: user
stops/disables the legacy app, downloads that bot's now-final XML export to this dev laptop, the
importer runs here producing `roboto.db` (validated via the checksum pass above) - `bot.env`'s
`TelegramToken` is deliberately never written by the importer, staying blank exactly like
`InstanceBootstrapper`'s existing stub-file convention already handles, so the real production
token never has to pass through this laptop or session at all. User fills it in themselves
directly on the NAS, uploads the generated data, and instantiates the new container. One bot at a
time, old legacy VM kept as an untouched rollback.

**Not started yet**: the importer itself (stage B). Deliberately not detailed further here until
it's actually underway - the exact legacy-status→`XyzzyStatus` mapping and `ExpectedReply`→
`DmOutboxEntry` reconstruction design depend on specifics that are easier to get right with 8.10's
finished shape in hand than to speculate about now.

## 8.11: settings-menu completeness, catalog performance, bot top-up, pack filtering (2026-08-19)

Prompted by a real dry-run against `chat_against_humanity_bot.xml` (114MB, 4783 chats): a genuine
completeness audit against legacy's live `sendSettingsMessage` turned up several features that were
live in production and simply never built in the rewrite's 8.4 admin-menu phase, not a deliberate
scope cut as originally (and incorrectly) reported.

- **`XyzzySettingsCommand`/`XyzzySettingsCallbackHandler`**: added Reset Scores, Game Length
  (mid-game question-limit change), Re-deal (`XyzzyRoundService.RedealAsync` - clears hands/decks,
  deals fresh), Extend (`TryExtendAsync` - resumes a Stopped game with the same roster/scores), and
  Force Question (exposes `XyzzyRoundReconciler.ForceAdvanceAsync`, now `internal`, as an on-demand
  admin action). Fixed a real bug caught while wiring Extend: both `XyzzySettingsCommand.ExecuteAsync`
  and `XyzzySettingsCallbackHandler.HandleAsync` gated on `Status is not Stopped` *before* dispatch,
  which made Extend - built specifically to work on a Stopped game - permanently unreachable. Both
  gates now only refuse when `Status is Stopped && Players.Count == 0` (truly nothing to manage or
  extend).
- **`CardCatalog` O(1) lookups**: `FindQuestion`/`FindAnswer` via `Dictionary<string, XyzzyCard>`
  indexes, replacing `Questions.First(...)`/`Answers.First(...)` O(n) scans - measured as a real cost
  (up to ~2.3M comparisons to build one 10-card hand) once real catalogs (72k-229k cards) are loaded,
  not just a theoretical concern.
- **Bot top-up re-check**: `FillBotSlots` now runs at the start of every `BeginQuestionAsync`, not
  just once at `FinishSetupAsync` - closes the gap where a kick or a leave could strand a game under
  `MinPlayers` with no bot fill and no end-condition catching it (previously logged below as
  deferred).
- **Pack filtering** (`XyzzyGameState.EnabledPackIds`): a chat's deck now draws only from its
  selected packs via `XyzzyRoundService`'s `FilteredQuestions`/`FilteredAnswers` (empty list = all
  packs, falls back to the unfiltered catalog if a stale filter would otherwise empty the deck
  entirely - a filter can never brick a game). New "Change Packs" paginated toggle UI in
  `XyzzySettingsCallbackHandler` (`PacksPerPage = 30`, matching legacy's own `maxPacksPerPage`, plus
  an "Enable All Packs" escape hatch mirroring legacy's `AllPacksEnabledID` sentinel). The importer
  (`XyzzyImportMapper.BuildCatalog`/`MapEnabledPackIds`) now carries `mod_xyzzy_coredata.packs` and
  each card's `packID` into the new catalog, and translates each chat's `packFilterIDs` onto
  `EnabledPackIds` - legacy's scheme is the *inverse* of the rewrite's (defaults to one specific
  pack, "all packs" is the explicit `AllPacksEnabledID`/`Guid.Empty` sentinel appearing *inside* the
  list), both of which collapse onto the rewrite's simpler "empty = all" representation.

Verified: 139 `Roboto.Bot.Tests` + 12 `Roboto.Migrator.Tests`, stable across repeated runs; each new
behavior (Extend's gate fix, Re-deal, pack-filter fallback, pack-toggle UI, importer pack mapping)
proven with a deliberate break-then-confirm-the-test-catches-it pass, not just green-on-first-try;
`docker compose build` clean.

## 13: dormant-chat purge (2026-08-19)

Same completeness audit turned up legacy's `Chats.removeDormantChats()`/`chat.tryPurgeData()` -
automated deletion of a chat's data once it's gone idle past `purgeInactiveChatsAfterXDays` (100),
with per-module opt-outs. Explicitly confirmed with the user to build the **full automated version**
(not a log-only candidate report) given the destructive/irreversible nature of the feature - see
CLAUDE.md's caution around risky actions.

- **`ChatState.LastActiveUtc`**, bumped by the new `ChatRepository.TouchAsync` from
  `MessageDispatcher` on every incoming message/callback - mirrors legacy's
  `chat.resetLastUpdateTime()`.
- **`ChatPurgeReconciler`** (ticked daily by `ChatPurgeSchedulerService`): for every chat idle past
  `PurgeInactiveAfterDays` (100), every module gets a say - if even one objects, the whole chat is
  skipped this pass (all-or-nothing, matching legacy). Per-module rules ported exactly, including
  legacy's own quirks:
  - Quotes block purge only while actual quotes exist (`mod_quote.isPurgable()`).
  - Birthdays block purge *permanently* once the module's ever been touched for a chat, even if
    every birthday was later removed (`mod_birthdays.isPurgable()` - a genuine legacy quirk,
    reproduced faithfully rather than "fixed").
  - Xyzzy blocks purge unless its own `StatusChangedUtc` is also past `XyzzyKillInactiveAfterDays`
    (30) - legacy's separate `killInactiveChatsAfterXDays` setting, almost always already satisfied
    by the time the outer 100-day gate trips since playing xyzzy is itself chat activity.
  - Steam and quiet-hours have no protection in legacy either - always purged once eligible.
  - `BirthdaysRepository.ExistsAsync`/`XyzzyGameRepository.ExistsAsync` check the raw stored value
    rather than going through each repository's own `GetAsync` "?? new default" fallback, which
    can't distinguish "never touched" from "touched but now empty/fresh" - the exact distinction
    legacy's own per-module rules depend on.
- New `IStateStore`-backed `DeleteAsync`/`ExistsAsync` helpers added to `QuotesRepository`,
  `BirthdaysRepository`, `SteamRepository` (`DeleteChatAsync`), `XyzzyGameRepository`, and
  `ChatRepository` itself (`GetAllAsync` filters `chat:%` keys down to exactly `chat:{chatId}` -
  SQL `LIKE` alone can't exclude longer keys sharing that prefix, e.g. quiet-hours'
  `chat:{chatId}:quiet-hours`).

Verified: 6 new tests in `ChatPurgeReconcilerTests` (full purge, quotes-blocks, birthdays-blocks-
even-when-empty, recently-active-chat-skipped, xyzzy's-own-inactivity-window, steam-always-purged) -
each confirmed to actually catch a deliberately-reintroduced regression before being trusted.

## 14: full xyzzy parity pass (2026-08-20, in progress)

Follow-up to a real dry run against `chat_against_humanity_bot.xml`: rather than trust memory of
what 8.4/8.11 covered, three research passes read the entire legacy `mod_xyzzy` implementation
(every prompt/keyboard/menu item), every module's background-processing loop, and the whole legacy
stats engine, cross-referenced against the rewrite's actual current state - published as a
[parity audit](https://claude.ai/code/artifact/62a67bff-83a3-43b2-a2e7-6a42e9c3b419) the user then
went through item by item to confirm scope. Corrected one wrong finding from that audit before
starting: quote auto-posting and Steam achievement polling are already fully built
(`QuotesReconciler`/`SteamReconciler`, since phase 9) - the audit's own research missed both.
Full decision list and phase breakdown captured in the approved plan (now executed phase by phase,
each with its own build+test+break-something verification pass, matching every prior phase here).

### 14.1: pack default-semantics reversal - done, verified

`XyzzyGameState.EnabledPackIds`' "empty = all packs" convention (built in phase 11) was inverted
from legacy's real behavior and has been reverted. New `Xyzzy/XyzzyPackFilter.cs` is now the single
source of truth: `AllPacksId = "*"` is an explicit sentinel (legacy's `AllPacksEnabledID`/
`Guid.Empty`) that must be *present in* the list to mean "all packs" - a brand-new chat instead
defaults to exactly one pack (`XyzzyPackFilter.DefaultSelection()`, matching legacy's
`packFilterIDs = [primaryPackID]`), resolved via `CardCatalog.DefaultPackId` (`XyzzyPack.IsDefault`,
computed once per catalog load/override). The default is applied in `XyzzyGameRepository.GetAsync`'s
`?? new` fallback - the one funnel every code path goes through, evaluated after `CardCatalog.
LoadOverrideAsync` has already run; `XyzzyStartCommand` deliberately doesn't touch `EnabledPackIds`,
since a chat's pack selection should persist across games, same as legacy.

The "Change Packs" picker (`XyzzySettingsCallbackHandler.cs`) now matches legacy's actual UI more
closely: packs sorted enabled-first-then-name (a real win once a catalog runs to hundreds of packs),
an "Active Packs"/"Inactive Packs" breakdown with ✅/❌ in the message body (plain text, not
markdown-bold - `DmOutbox` doesn't carry a `ParseMode` through to the real send yet, a gap noted for
whenever that's needed), "All Packs" (adds the sentinel) and "Reset to Base Pack" (restores the
default) instead of a "None" button - a genuine empty selection has no coherent outcome (it's what
legacy's own broken "None" button produces), so the UI structurally can't reach it: toggling off the
last enabled pack is rejected with a clear message instead. Legacy's pack-picker pagination
off-by-one (`(count / perPage) + 1` always shows one trailing empty page on an exact multiple) was
**not** reproduced - confirmed with the user as a bug worth just fixing, not parity-worthy.

`Roboto.Migrator`'s `XyzzyImportMapper` updated to match: `BuildCatalog` identifies the primary pack
(exact legacy GUID match → `packCode == "CAHBS"` → first pack, in that order) and flags it
`IsDefault`; `MapEnabledPackIds` maps the `AllPacksEnabledID` sentinel onto `XyzzyPackFilter.
AllPacksId` and a genuinely-empty legacy `packFilterIDs` (only reachable via legacy's own broken
"None") onto the imported default pack rather than leaving a resumed chat unable to draw a card.

Verified: `tests/Roboto.Bot.Tests/Xyzzy/XyzzyPackFilteringTests.cs` (rewritten/expanded - default
selection, the all-packs sentinel, toggle-on-then-off, materializing the full list on first toggle
against the sentinel, the last-pack guard, Reset to Base Pack) and `tests/Roboto.Migrator.Tests/`
(primary-pack detection, sentinel mapping) - every new/changed assertion confirmed to actually catch
a deliberately-reintroduced regression, not just green-on-first-try. Full suite (143 `Roboto.Bot.
Tests` + 12 `Roboto.Migrator.Tests`) stable across repeated runs.

### 14.2: stats engine dual-track, `/stats`/`/statgraph` rebuild - done, verified

Phase 10a's `StatSeries` only ever tracked an all-time `Total` plus the last 500 raw points
(pruned by count, not time) - legacy's own stats engine is the opposite: a pure rolling 15-min-
bucketed/48h window with no persisted all-time total at all. The user wanted both, as two parallel
tracks on the same series rather than picking one: `StatSeries` now has `Total`/`Latest`/
`FirstRecordedUtc`/`LastRecordedUtc`/`HasAllTimeTotal` (all-time, mode-dependent exactly as before -
`Mode` governs how a value combines into the *current bucket*, not just `Total`, so a Snapshot
gauge's `Total` still just mirrors the latest value rather than ticking up nonsensically every
scheduler tick) alongside a new sparse `Buckets` list (`StatBucket`, 15-min granularity, 192-bucket/
48h retention - legacy's exact numbers), pruned inline on every write rather than via a separate
housekeeping sweep (every series is already loaded-and-rewritten per `RecordAsync` call, so pruning
there is free). `RecordAsync` delegates to an `internal RecordAtAsync(..., DateTime nowUtc, ...)` so
bucket-rollover/pruning is testable with synthetic timestamps instead of waiting on real 15-minute
boundaries. `StatMode`'s enum member names were deliberately left untouched (`JsonStringEnumConverter`
persists by name and throws on an unrecognised one) and so was `Total` (a silent rename would zero
every persisted all-time counter instead of failing loudly).

`/stats` no longer dumps the stat registry alphabetically - it's rebuilt to legacy's actual hybrid
shape (bot name/uptime/chat count, then each module's own live-computed snapshot line), via a new
`IModuleStatsProvider` interface discovered by the same reflection-registration loop `IBotCommand`/
`ICallbackQueryHandler` already use. `XyzzyStatsProvider` (active players/games, packs+cards loaded,
plus the two new lines the user asked for - total all-time games/hands, phrased "since {date}" since
these counters start at zero at rewrite-deploy time, not truly all-time the way legacy never tracked
any total at all), `SteamStatsProvider` (players tracked/achievements known), `StandardStatsProvider`
(messages awaiting reply, via a new `DmOutbox.CountAwaitingReplyAsync`). Sent with `ParseMode.
Markdown` for legacy's `*bold*` module headers - the first place anything in this codebase passes a
parse mode to `SendMessage`. Kept the existing command-usage table as a "Top commands" tail.

`/statgraph` restores legacy's multi-series regex matching (space/`|`-split args, each a regex
against a series name, capped at 8 series) that 10b's first pass had dropped to a single exact-name
lookup, and densifies the sparse bucket list into a continuous 48h window on read (zero-fill for
Cumulative gaps, carry-forward for Snapshot gaps, `NaN` before a series' first-ever sample so
ScottPlot renders a true gap rather than a fake baseline). Visual redesign taken with full creative
freedom per the user's own go-ahead rather than porting legacy's WinForms `Chart` output (1200x600
JPEG, one hardcoded pastel-blue plot area, default column/line styling) - Cumulative series render
with a filled area (closer to a histogram than a gauge reading), Snapshot as a plain line, color a
stable hash of the series name so a given stat stays the same color across renders.

Verified: `StatsRecorderTests.cs` rewritten for the bucket model (accumulate-within-a-bucket,
roll-over-to-a-new-bucket, retention-window pruning with an exact-boundary assertion, first/last-
recorded tracking) plus the existing cumulative/snapshot/restart-survival tests; `XyzzyStatsTests.cs`
updated to match the new `/stats` phrasing; `StatGraphTests.cs` unchanged (its "Usage:"/"No recorded
history" substrings were deliberately preserved in the rebuild). Bucket-pruning test confirmed to
actually catch a deliberately-reintroduced regression before being trusted. `docker compose build`
clean.

### 14.3: settings menu completions - Abandon confirm, Extend, Mess With - done, verified

- **Abandon**: legacy's own Yes/No confirm is cosmetic-only (its reply handler never checks which
  button was tapped - any reply abandons). Fixed rather than reproduced: a real confirm
  (`xy:se:{chatId}:abandonconfirm:{yes|no}`) that only abandons on "Yes".
- **Extend**: previously only worked on a Stopped game (`TryExtendAsync` returned false/no-op for
  anything in progress). Now matches legacy's actual scope - legacy's `extend()` always adds more
  cards to the deck (`addQuestions()`/`addAllAnswers()`) regardless of status, only *additionally*
  resuming play when coming from Stopped. On a running game, clears `RemainingQuestionCardIds`/
  `RemainingAnswerCardIds` so the next natural draw reshuffles fresh from the current pack filter
  (picking up anything enabled since the piles were last built) without touching hands, the current
  question, or the round in progress - that's Re-deal's job, a deliberately more disruptive action.
- **Mess With**: new `XyzzyPlayer.MessedWith` bool + settings-menu picker (same shape as Kick/
  Score), toggled per player. A new `XyzzyRoundService.ScoreDisplayText` helper (legacy's
  `getPointsMessage()`) substitutes a randomized number and nonsense unit when set - real multiplier
  range and unit list ported verbatim from legacy - used by `/xyzzy_status` and the round-win
  announcement, but deliberately *not* `TryEndGameAsync`'s final game-over scoreboard, preserving a
  legacy asymmetry (ambiguous in the source whether deliberate) rather than "fixing" something
  nobody asked to change.
- Wording pass: the "Throttle" button renamed to "Delay" to match legacy exactly (pure label, no
  behavior change) - the broader wording-parity pass is being done incrementally as each area gets
  touched (per the parity audit's own note) rather than as one separate sweep.

Verified: new/updated tests in `XyzzySettingsTests.cs` (confirm-then-abandon, decline-leaves-
running, Extend-on-a-running-game leaves hands/round untouched, Mess With toggles the flag without
touching real `Wins`) and `XyzzyStatsTests.cs` (Abandon's stat now requires the confirm tap) - the
Abandon-confirm fix confirmed to actually catch a deliberately-reintroduced regression before being
trusted. `docker compose build` clean.

### 14.4: `/xyzzy_leave` DM picker, real `/xyzzy_get_settings` content - done, verified

Caught a near-miss here worth recording: `/xyzzy_get_settings` already existed (phase 8.1, a
deliberate placeholder pending real pack support - "Reports the hardcoded catalog counts... since
v1 only has the one built-in pack"), and this phase's first pass almost blind-overwrote it without
checking. `git stash`/`pop` recovered the original before anything was lost. The corrected version
keeps the file but replaces its placeholder content with legacy's actual settings-summary text
(remaining deck counts, timeouts, enabled-pack count/names capped at 30 with a "plus N more" tail,
matching the pack picker's own cap) - real content is possible now that packs actually exist
(phase 14.1), which is exactly what the placeholder was waiting on.

`/xyzzy_leave`'s DM variant (typed with no chat context) was a genuine, confirmed-cut v1 gap -
restored now: scans every active game the caller is in (`XyzzyGameRepository.GetAllActiveAsync`)
and shows a "which game?" picker (chat title, falling back to the raw chat ID if the title was
never captured - only `/start`/`/stop` set it today) via a new `XyzzyLeavePickerCallbackHandler`
(`xy:lv:<chatId>` / `xy:lv:cancel`). The existing group-context `/xyzzy_leave` is unaffected.

Verified: `XyzzyGameSkeletonTests.cs` updated (the "rejected in private chats" test no longer
includes `/xyzzy_leave`, which now has its own legitimate DM behavior; the settings test now starts
a real game first and asserts the actual summary text). New `XyzzyLeaveDmVariantTests.cs` (picker
lists every active game, picking one only removes you from that one, Cancel leaves everything
unchanged) - the game-filter fix confirmed to actually catch a deliberately-reintroduced regression
before being trusted. `docker compose build` clean.

### 14.5: live crcast pack import/sync - done, verified

Ports legacy's `Helpers/cardCast.cs`/`mod_xyzzy_coredata.importCardCastPack` - a real HTTP client
(`CrCastClient`, `System.Net.Http.Json` + `System.Text.Json`, not Newtonsoft per the user's explicit
ask) hitting `api.crcast.cc`'s two endpoints (pack info, then cards), reached only through Change
Packs → **Import Pack** (no separate slash command, matching legacy), routed through `ReplyRouter`
for the free-text pack code the same way Timeout/Throttle already are.

`CrCastPackImportService` distinguishes a fresh import (adds every card + the pack to the live
catalog, persists via `IStateStore` then reloads in-process via `CardCatalog.LoadOverrideAsync`)
from a re-sync of an already-known pack code: cards are matched by exact text - unchanged text keeps
its ID, new text gets a new sequential one, and text that's gone is "removed" and mapped onto a
surviving card of the same kind (question/answer), preferring one from the same pack. The genuinely
tricky part - **every active game's live state referencing a removed card gets remapped**, not left
dangling: hands, submissions, remaining decks, and the current question all get walked and patched.
Mirrors legacy's own replacement-GUID remap in spirit, adapted to the rewrite's short sequential IDs
and per-game state instead of a single global reference table.

A new `CrCastSyncReconciler`/`CrCastSyncSchedulerService` pair (hourly tick - legacy's own 1-minute
tick is unnecessarily frequent given each pack's sync window is 3-9 days) re-syncs crcast-sourced
packs whose `NextSyncUtc` has passed, capped at 3 per tick (legacy's `maxPacksToSyncInOneGo`). A
failed fetch still reschedules `NextSyncUtc` forward (legacy's `syncFailed()` does the same) so a
persistently-broken pack code doesn't get hammered every tick forever.

**Dormant-pack removal deliberately not built** - `CrCastPackImportService.RemoveDormantPacksAsync`
exists as an explicit no-op, never called from anywhere, matching legacy's own current disablement
(`//TODO DISABLE AS CARDCAST DEAD`) rather than reviving logic legacy itself has turned off.

Verified: `CrCastPackImportServiceTests.cs` against a fake HTTP backend (`FakeCrCastHttpHandler`,
same pattern as Steam's `FakeSteamHttpHandler` - no real network call) - fresh import, invalid pack
code rejected without a network call, a failed fetch changes nothing, re-sync adds/keeps/removes
cards correctly, and the card-remapping test specifically confirmed to catch a deliberately-
reintroduced regression before being trusted. `docker compose build` clean.

### 14.6: pack selection in the setup wizard - done, verified

Legacy's real order is Game Length → Pack Filter → Timeout → Throttle → Invites; the rewrite's
wizard skipped straight from Game Length to Timeout with no pack step at all. Rather than duplicate
a second picker implementation, the "Change Packs" message/keyboard building was pulled out of
`XyzzySettingsCallbackHandler` into a shared `XyzzyPackPickerUi` (internal static class) that both
it and `XyzzyStartCommand`'s "Configure Game" chain now call - every pack-picker button still routes
through the same `xy:se:...` callback data and `XyzzySettingsCallbackHandler` regardless of which
caller opened it, since the underlying game/pack state is identical either way. Only the exit
differs: the picker's "Continue" button now dispatches to a new `HandlePackDoneAsync`, which checks
`game.Status` - `SettingUp` advances straight into the setup chain's Timeout step (referencing
`XyzzyStartCommand.AskTimeout`'s public constant, not a runtime dependency), anything else just
closes the menu. If no real catalog is loaded (the hardcoded placeholder dev/test set), the wizard
skips the pack step entirely, matching `XyzzySettingsCallbackHandler`'s own "Change Packs" gate.

Caught a small pre-existing bug while wiring this up: the picker's "Continue" button previously
reused the standalone menu's `menu:cancel` action, so finishing a pack review said "Cancelled." even
though every toggle had already been saved as it happened - fixed as part of the same change
(`HandlePackDoneAsync` now says "Done." outside the wizard).

Verified: `XyzzyPackFilteringTests.cs` - the setup chain shows the picker with Game Length's answer
already applied, toggling and continuing lands on Timeout with the right `EnabledPackIds` and the
rest of the wizard still reaches Invites cleanly; the no-catalog-loaded case still skips straight to
Timeout (matching every pre-existing wizard test's own assumption); "Continue" from a normal
`/xyzzy_settings` session says "Done.", not "Cancelled." - the pack-step gate confirmed to actually
catch a deliberately-reintroduced regression before being trusted. `docker compose build` clean.

### 14.7: fix the "infinite timeout" bug - done, verified

Legacy's sentinel for "never auto-skip a slow player" is `0` hours (its own quick-pick keyboard had
a dedicated "No Timeout" button for it, or you could just type `0`). The rewrite's `XyzzyStartCommand.
HandleTimeoutAsync`/`XyzzySettingsCommand`'s equivalent both rejected `hours <= 0` outright, so "no
timeout" was simply impossible to express. Fixed on both sides, since fixing only the input would
have been actively worse: `XyzzyRoundReconciler.ReconcileTimeoutAsync`'s own check
(`elapsed >= TimeSpan.FromHours(game.MaxWaitHours)`) would force-advance every round *instantly*
once `0` was accepted as input, since `elapsed >= TimeSpan.Zero` is always true - the exact opposite
of "never". Added an explicit `if (game.MaxWaitHours <= 0) return;` skip ahead of that check instead.

**Quick-pick keyboard not built** - deliberately scoped down from the original plan after checking
the architecture: `DmOutbox.TryGetHeadTextQuestionAsync` only matches an incoming free-text reply
against a queue entry that has no `Keyboard` set - a real, deliberate phase 8.9 design choice (one
outstanding thing per user, unambiguous). Legacy's Timeout/Throttle prompts want *both* a tappable
shortcut *and* free-text input on the same message, which would need a real change to that matching
logic to support a hybrid entry - not something to take on as a side effect of a wording fix. "0
means never" is instead conveyed by wording alone (`XyzzyStartCommand.TimeoutPrompt`, shared across
every place the prompt is asked from, rather than four copies of similar-but-drifting text): "How
many hours should I wait for answers/judging before auto-advancing? Enter 0 for no timeout (never
auto-advance)." The confirmation message on the /xyzzy_settings path also now says "Timeout disabled
- I'll never auto-advance a slow round." instead of the slightly odd "Timeout set to 0h."

Verified: `XyzzyRoundReconcilerTests.cs` (`MaxWaitHours = 0` backdated a full year still never force-
advances - the reconciler-side fix specifically confirmed to catch a deliberately-reintroduced
regression before being trusted), `XyzzySettingsTests.cs` (`0` accepted, disabled-confirmation
wording), `XyzzyStartWizardTests.cs` (the pre-existing "invalid values reprompt" test's own timeout
case was actually testing the bug - `0` was its "invalid" example - swapped for a genuinely invalid
value now that `0` is correct). `docker compose build` clean.

### 14.8: instance identity merge - done, verified

Merges what were three separate names for the same concept (`ROBOTO_INSTANCE` env var, Docker's
`container_name`/`hostname`, legacy's `-context` CLI flag) into one: `Program.cs` now falls back to
`Environment.MachineName` - which Docker sets to the container's own hostname automatically - when
`ROBOTO_INSTANCE` isn't explicitly set, instead of the literal string `"default"`. A real
single-purpose deployment (e.g. one TrueNAS app per bot) only has to name the container once;
`ROBOTO_INSTANCE` remains available as an explicit override for the rare case that needs the two to
differ (kept as the primary lever in this repo's own local dev `docker-compose.yml`, which is
shared across several test instances picked at invocation time, not one fixed identity).

No automated test covers this directly - it's a startup-configuration concern `Program.cs` owns,
and the existing test suite bypasses `Program.cs` entirely (`TestBot` builds its own `BotOptions`
directly). Verified instead with a real, non-mocked smoke test against the built image: `docker run
--hostname mytest-instance` with no `ROBOTO_INSTANCE` set correctly created `/data/mytest-instance/
bot.env`; a second run with both `--hostname` and `ROBOTO_INSTANCE` set confirmed the env var still
wins as an explicit override. `docker compose build` clean.

### 14.9: fix `DmOutbox` front-insert window preempting unrelated queued requests - done, verified

Live bug, found by actually playing against the beefy test bot rather than by the automated suite:
in a bot-heavy game, repeatedly asking for `/xyzzy_settings` while a round was in progress kept
getting shouted down by fresh round-dealt messages, no matter how many times it was retried.

Root cause was in `DmOutbox`'s 8.9 "resolving window" design (`_resolvingInsertIndex`, opened by
`RemoveCurrentHeadAsync`, closed by `PumpNextAsync`, in `CallbackQueryRouter.HandleAsync`): any
`AddAsync` call for that user during the window front-inserts instead of appending. That's correct
for a flow's own genuine continuation (e.g. "pick your next card" on a multi-answer question), but
it was applied unconditionally to *every* enqueue during the window - including a brand new round
being dealt as a side effect of the same callback, when the judge and/or remaining answerers are
bots and answering/judging cascades synchronously through `XyzzyRoundService.BeginQuestionAsync`/
`BeginJudgingAsync` before `PumpNextAsync` ever runs. Each new round's hand keyboard jumped the
queue ahead of a `/xyzzy_settings` request that was already legitimately waiting - and since
resolving that round just deals straight into the next one, it kept happening every time the player
answered.

Fixed with a new `allowFrontInsert` parameter (default `true`, preserving the 8.9 behavior
everywhere else) on `DmOutbox.EnqueueNoticeAsync`/`EnqueueButtonQuestionAsync`/the private
`AddAsync`; `XyzzyRoundService.BeginQuestionAsync`'s and `BeginJudgingAsync`'s broadcast calls now
pass `allowFrontInsert: false` - a freshly-dealt round is an independent event, not the triggering
player's own flow continuing, regardless of which callback happened to cause it. `SubmitAnswerAsync`'s
"pick your next card" re-prompt is deliberately left at the default (still front-inserts, correctly).

New regression test `tests/Roboto.Bot.Tests/Xyzzy/DmOutboxQueueOrderingTests.cs` reproduces the
exact reported shape (solo starter + 2 auto-filled bots, so round 2's judge is a bot): queues
`/xyzzy_settings` behind an outstanding round-2 hand, answers that hand (cascading synchronously
through judging and dealing round 3), asserts the settings menu - not round 3's hand - wins.
Verified failing without the fix (temporarily reverted the `allowFrontInsert: false` call sites,
confirmed the test failed with round 3's hand delivered instead of settings) and passing with it,
run 4x for stability. Full suite (165 Roboto.Bot.Tests + 12 Roboto.Migrator.Tests) green throughout.
`docker compose build` clean.

### 14.10: round-flow message parity, multi-game DM chat stamp - done, verified

Live feedback after 14.9 shipped: legacy's actual round-flow wording never got ported during the
14.1-14.8 parity pass (only the settings menu, packs, stats and a handful of other areas got a
wording pass) - the two most visible group-chat messages, the "everyone's answered" judging notice
and the "someone won" announcement, were still rewrite-only placeholder text. Also reported live: no
way to tell which of two simultaneous games a DM question belonged to (legacy had this, via a
Presence-based chat-title stamp explicitly dropped from this port back in the original parity
scoping - see `CLAUDE.md`/this file's own phase-11 notes on presence).

**Judging message** (`XyzzyRoundService.BeginJudgingAsync`) now matches legacy's `beginJudging`
chat message exactly: "All answers received! The honourable {judge} presiding." + the question +
every non-judge player's answer, sorted (not player-ordered, so the list doesn't tip off the judge)
+ anyone who never got a submission in called out by name under "Skipped these chumps:" - only ever
populated via `XyzzyRoundReconciler`'s timeout force-advance path, since the normal
`SubmitAnswerAsync`-triggered path requires everyone present to have already answered.

**Win announcement** (`XyzzyRoundService.PickWinnerAsync`) now matches legacy's `judgesResponse`
message: "{winner} wins a point!" + the filled-in question + **every** player's score
highest-first (previously only showed the winner's own new tally, not the whole table). Fixed
`ScoreDisplayText`'s real-score wording to legacy's exact "{wins} points." (was "{wins} win(s)",
this rewrite's own wording) - shared by `/xyzzy_status` and this message, so both now read
identically to before. `TryEndGameAsync`'s final scoreboard also ported to legacy's `wrapUp()`
wording ("Game over!" + the Extend hint + "Scores are:" + real (non-messed-with) per-player
scores - preserving the existing, deliberate real-vs-messed-with asymmetry documented on
`ScoreDisplayText` itself).

**Multi-game DM chat stamp** (`XyzzyRoundService.StampChatAsync`, new): legacy's
`TelegramAPI.postExpectedReplyToPlayer` stamped a per-user DM with the originating chat's title
whenever Presence tracking showed the recipient active in more than one chat at once. Presence
itself stays out of scope (explicitly dropped, nothing else needs it), but the concrete case it
enabled - two simultaneous xyzzy games, no way to tell which one a "Pick a card" DM belongs to -
is real and was reported live. Substitutes "how many active xyzzy games is this player currently
in" (`XyzzyGameRepository.GetAllActiveAsync`) as the narrower equivalent signal, stamping with
legacy's own non-markdown fallback format ("=>{title}\n{text}", since DmOutbox doesn't carry a
ParseMode through to the send). Applied to every per-round DM a player can receive while a second
game might be active: the round-start hand keyboard and judge notice, the multi-answer "pick your
next card" reprompt, the judge's "pick the best answer" keyboard, and the setup wizard's "tap
Start" DM.

**Setup-wizard "ask immediately, don't drop to the back of the queue" behaviour** - also raised in
the same feedback - was checked and found already correct: every setup-wizard follow-up
(`ReplyRouter.AskAsync`/`DmOutbox.EnqueueButtonQuestionAsync`, all still defaulting to
`allowFrontInsert: true`) runs inside the same resolving window opened by `RemoveCurrentHeadAsync`
before the answering handler runs and closed by `PumpNextAsync` right after - so it's delivered as
the very next message regardless of anything else already sitting in that user's queue. 14.9 only
changed this for `XyzzyRoundService`'s own *new-round broadcast* calls (an independent event, not a
continuation of the answering user's own flow); no setup-wizard call site was touched, and none
needed to be.

New/extended tests: `XyzzyMultiGameDmStampTests.cs` (new - a player in two active games gets a
stamped hand-keyboard DM for the second one; a player in only one does not), extended
`XyzzyRoundReconcilerTests.TimeoutWithPartialAnswersForceAdvancesToJudging` (asserts the missing
player is named under "Skipped these chumps:"), extended
`XyzzyRoundLoopTests.FullRoundEndToEndDealAnswerJudgeAndAdvance` (asserts every player, not just
the winner, appears in the win message). Every "Pick the winner"/"wins the round" test substring
matcher across the suite updated to the new "Pick the best answer"/"wins a point" wording. Both new
assertions verified failing when deliberately sabotaged (stamp condition forced to always skip;
missing-player branch forced off) and passing once restored - full suite (166 Roboto.Bot.Tests + 12
Roboto.Migrator.Tests) green across 4 consecutive runs. `docker compose build` clean.

Also hardened `DmOutboxQueueOrderingTests` (14.9's own regression test) while working in this same
area: it answered round 2's card with a single button tap, silently assuming a single-answer
question - a multi-answer "Pick 2"+ card landing on round 2 (possible depending on catalog draw
order) left the round incomplete and failed the test. Switched to `AnswerHandFullyAsync`, which
answers however many cards the question actually needs, same as every other round-loop test already
does.

### 14.11: real chat-name stamp, bolded winning answer, round-start wording - done, verified

Follow-up live feedback on 14.10, three small gaps:

**Chat stamp showed a bare numeric chat ID, not a name.** `XyzzyRoundService.StampChatAsync`'s
fallback (`ChatState.Title` empty → show `game.ChatId`) was firing far more than intended: `Title`
was previously only ever set by `StartCommand`/`StopCommand` (i.e. only if someone had run
`/start`/`/stop` in that specific chat), so most real groups - active game or not - had never had
it captured at all. Fixed at the source instead of patching the fallback: `ChatRepository.
TouchAsync` (already called on every incoming message/callback, `MessageDispatcher`) now also
takes and stores the chat's title when the incoming update carries one, so it's captured from
*any* ordinary interaction, not just `/start`/`/stop`, and stays fresh if a group renames itself.
Every game start/join already goes through a group-typed slash command, so this self-heals for any
currently-running game the moment anyone next types anything in it.

**The winning answer wasn't bolded.** Present in legacy (`judgesResponse` wraps the winning
card(s) in `*...*` and sends with `markDown=true`) but not reproduced when the win-announcement
wording was ported in 14.10. `XyzzyRoundService.PickWinnerAsync` now bolds the winning answer(s) -
both the single-answer substituted-into-the-blank case and the multi-answer "Answer: ..." fallback
- and sends with `parseMode: ParseMode.Markdown`.

**Round-start group message's quoting style was inconsistent.** `BeginQuestionAsync`'s "Round N!
{judge} is judging." broadcast (a rewrite-only addition - legacy's `askQuestion` never announced a
new round to the group at all, so there's no legacy string to port here) still quote-wrapped the
question (`"{question}"`) after 14.10 switched the sibling judging-message to legacy's own
unquoted "Question: {text}" style. Reworded to match: `Question: {text}`, no quotes.

Also gave `TestBot.GroupMessage` an optional `title` parameter (defaulted to the existing "Test
Group" so no other test needed touching) so the chat-stamp test could exercise two distinctly-named
chats and assert on the real name, not just "a stamp is present."

New/updated tests: `XyzzyMultiGameDmStampTests` now names its two chats ("Thursday Game Night" /
"Work Chat") and asserts the stamp shows the real name; extended `XyzzyRoundLoopTests.
FullRoundEndToEndDealAnswerJudgeAndAdvance` to assert the winning answer appears bolded
(`*{text}*`) in the group win message. Both sabotaged and confirmed failing (title-check forced to
always skip; bold wrapper stripped to plain text) before restoring - full suite (166
Roboto.Bot.Tests + 12 Roboto.Migrator.Tests) green across 4 consecutive runs. `docker compose
build` clean.

## Explicitly deferred / blocked work

- **`mod_xyzzy` v1 scope cuts** (deliberately dropped for size, not structurally hard to add back) -
  almost all of these turned out to be real gaps, not deliberate cuts, once the full parity audit
  (phase 14) actually checked: ~~multi-blank ("Pick 2") questions~~ built in phase 8.10;
  ~~"Mess With"~~ built in phase 14.3; ~~cross-chat DM `/xyzzy_leave`~~ built in phase 14.4;
  ~~stats/metrics~~ built out across phases 10a/10b/14.2; ~~pack-filter selection~~ built in phase
  14.1. ~~CardCast/CRCast pack import~~ built in phase 14.5 (dormant-pack removal specifically
  stays an unwired placeholder, matching legacy's own current disablement). The elaborate
  multi-step setup wizard (defaults-vs-custom chain, question-limit/timeout/throttle prompts before
  the game starts) was cut here too but got built out in phase 8.5.
- **XML→SQLite migration importer** — first-class, must-be-safe deliverable, see the live-production
  warning in `CLAUDE.md`. Needs a real copy of the production XML + test-bot tokens from the user
  before work starts; build and prove it against test data first, only point it at a real prod XML
  copy once already proven correct, and even then only into a test-bot context.
- **JSON library for code that reads legacy-shaped data** (e.g. the migration importer) — the new
  SQLite layer uses `System.Text.Json`; whether the importer wants `Newtonsoft.Json` instead (to
  match the legacy code's looser `JObject`-style parsing) is still open, not urgent.
- ~~**`ROBOTO_INSTANCE` vs legacy `-context`**~~ merged in phase 14.8 - instance identity now
  derives from the container's own hostname by default, `ROBOTO_INSTANCE` staying as an explicit
  override rather than the only lever.
- **`/save`, `/background`** — legacy mod_standard commands with no equivalent need any more
  (SQLite already writes incrementally; no periodic background-processing loop exists yet to
  manually trigger). Not planned to be ported as-is; revisit only if a real need shows up.
- ~~**`mod_xyzzy` bot top-up doesn't get re-checked after a kick or a leave.**~~ Fixed in phase
  8.11: `FillBotSlots` now re-runs at the start of every `BeginQuestionAsync`, not just once at
  setup.

## Investigated and closed out, no code change needed

- **Shutdown/cancellation.** User's words (phase 8 era): "the exit logic sucks." The concern was
  that `TelegramPollingService` might inherit legacy's shape - Ctrl-C/SIGTERM waiting out whatever
  long-poll HTTP call is in flight. Actually measured before starting phase 9's shutdown-redesign
  item: `time docker stop -t 30` against the running `beefy` container came back at **0.17s**. The
  installed `Telegram.Bot` 22.10.2.1's `ReceiverOptions` doesn't even have a `Timeout` property
  (confirmed via reflection) - there's no multi-second long-poll actually held open to wait out the
  way legacy's hand-rolled poller had. Nothing to fix; the assumption predated anyone actually
  timing it.
