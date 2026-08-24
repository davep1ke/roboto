# Roboto

A C# Telegram chatbot framework. Primary payload is `mod_xyzzy`, a Cards-Against-Humanity-style
game ("xyzzy") played in Telegram group chats. Other modules: quotes, birthdays, steam lookups,
wordcraft. Runs on .NET 10 / Linux / Docker, live against several thousand real Telegram users.

This codebase is the result of a from-legacy port completed 2026-08-24 (`rewrite/legacy-structure-
port`, merged into `master` - see "History: how this codebase got its current shape" below and
`MIGRATION.md` for the full phase-by-phase record). The port's whole point, and the reason the
architecture looks the way it does, was to move off WinForms/.NET Framework onto modern
cross-platform .NET **while keeping legacy's actual class/dispatch/game-logic structure intact**,
rather than rewriting it - see that section before assuming ordinary "rewrite" conventions apply
anywhere in this repo.

## History: how this codebase got its current shape (read before assuming "rewrite" conventions apply)

An earlier branch, `rewrite/dotnet-docker-port`, ported this bot by replacing legacy's structure
wholesale - DI, interfaces, a new `CommandRouter`/`ReplyRouter` instead of legacy's reflection-based
module system and `ExpectedReply` state machine. That branch is **kept, untouched**, as a reference/
parity-check source and a source to copy specific infra pieces from (its `IStateStore`,
`InstanceBootstrapper`, `Telegram.Bot`/ScottPlot usage idioms, and test-harness pattern were all used
this way) - but its architecture is **not** what this codebase follows. The user lost confidence in
that path specifically because of how much had to be manually re-derived from reading legacy code,
which is exactly where subtle behavioral nuance gets lost. The port that became this codebase instead
kept legacy's own classes, dispatch (`chatEvent`/`StartsWith` chains), and `ExpectedReply`
conversational-state machine completely intact, swapping only what was structurally forced by running
on modern/cross-platform .NET (runtime, persistence, config, logging sink, Telegram HTTP layer, JSON
library, charting) plus a small number of explicitly-decided additions (real background scheduler,
relational tables for whole-bot growing lists, DB-backed logging). Hybrid inline keyboards were
scoped in the original plan but explicitly decided against, not deferred - see "Current stack" below.
`MIGRATION.md` has the full phase-by-phase record (what changed and why, every real bug found along
the way, root-caused incidents) and `/home/davepike/.claude/plans/rustling-launching-cake.md` has the
original plan and its full rationale - both still worth reading before assuming a structural decision
here was arbitrary; most of them were deliberate calls made during the port, not open questions.

## Repo layout

- Repo root **is** `/home/davepike/Documents/Code/Roboto/` - a normal, single git repo (`.git`
  lives directly here), tracking `origin` = `https://github.com/davep1ke/roboto.git`.
- Solution/project: `Roboto.sln` (VS-specific, may be stale for the SDK-style csproj - not
  load-bearing for the CLI-driven `dotnet build`/`dotnet test` workflow this project actually uses),
  `Roboto/Roboto.csproj`. Also `Migrator/Roboto.Migrator.csproj` (the legacy-XML-to-SQLite importer,
  MIGRATION.md phase 8) and `tests/Roboto.Tests/Roboto.Tests.csproj`.
- Branches/tags:
  - `master` - the current mainline, this codebase, as ported. Local `master` is ahead of
    `origin/master` (the port was merged in locally 2026-08-24, fast-forward from
    `legacy-winforms-baseline`) - **not yet pushed**, only push with explicit go-ahead, this is a real
    GitHub repo backing a live bot.
  - Tag `legacy-winforms-baseline` on the old pre-port history - permanent bookmark for "last state
    before any porting work" and the port's actual starting point. Still the rollback point if
    something about the port needs reverting.
  - Branch `rewrite/dotnet-docker-port` - the abandoned from-scratch rewrite (see "History" above).
    Kept as a reference source, never merged.
  - Branch `rewrite/legacy-structure-port` - where the port itself happened, now fully merged into
    `master` (identical HEAD). Kept around rather than deleted; not where new work happens.
- Git identity is configured (`user.name`/`user.email`) - commits work fine.

## Current stack (`Roboto/`)

- .NET 10, SDK-style `Roboto.csproj`, console `Exe` (not `WinExe`) - no more `[STAThread]`/WPF UI
  thread split. WPF (`LogWindow`) and WinForms charting are gone entirely.
- Legacy's actual module system kept intact: `RobotoModuleTemplate`/`RobotoModuleDataTemplate`/
  `RobotoModuleChatDataTemplate` abstract base classes, reflection-discovered via
  `Plugins.initPluginAssemblies()`, registered into the static `Plugins.plugins` list. Every concrete
  module (`mod_xyzzy`, `mod_standard`, `mod_quote`, `mod_birthdays`, `mod_wordcraft`, `mod_steam`)
  and its `_coredata`/`_chatdata` classes carry their existing method surface unchanged.
- Dispatch kept intact: the per-plugin loop (inside `TelegramAPI.DispatchUpdate`) calls
  `plugin.chatEvent(m, chatData)` for every plugin with `chatHook=true`, and each module's own
  `StartsWith("/xyzzy_start")`-style chains inside `chatEvent` are unchanged. The transport underneath
  this is the `Telegram.Bot` package (replacing legacy's hand-rolled `HttpWebRequest`+`JObject` layer)
  - the dispatch shape itself never changed.
- `ExpectedReply`/`Messaging` conversational-state machine kept intact: single global
  `List<ExpectedReply>`, the match predicate (`userID` + either direct-PM or
  `replyMessageID == outboundMessageID`), remove-before-invoke ordering, per-user
  single-outstanding-question serialization. Every mutation of that list write-throughs to SQLite
  per-row (not just at the periodic full save) - see `Messaging.addExpectedReply`/
  `removeExpectedReply` and `ExpectedReply.dbId`'s own comment. Hybrid inline keyboards
  (`CallbackQuery` as a second trigger into this same matching path) were scoped in the original plan
  but explicitly decided against (2026-08-24) - not planned, don't assume it's coming.
- Logging: `Core/logging.cs` keeps its public method shapes (`log`/`logItem`/`longOp`/`loglevel`/
  `cleanse`) but the `Color?` parameter (only ever drove the old WPF `LogWindow`'s text color) is gone.
  Output goes through Serilog: console plus a custom DB sink into the `logs` table (30-day purge, in
  `mod_standard`'s `backgroundProcessing()`). Chart rendering (`Core/stats.cs`) uses ScottPlot on
  legacy's own series-selection/datapoint-gathering logic, kept as-is.
- Persistence: SQLite via `Microsoft.Data.Sqlite`, replacing legacy's `XmlSerializer` →
  `%appdata%\Roboto\<context>.xml`. `IStateStore`/`SqliteStateStore` blob rows (key→JSON,
  `System.Text.Json`, `IncludeFields = true` is load-bearing - see that class's own comment) for
  small/bounded per-chat/per-module state, plus real relational tables for whole-bot growing lists:
  `expected_replies`, `stats`, `chat_presence`, `xyzzy_cards`/`xyzzy_packs`, `logs`. `DbBackup` snapshots
  the `.db` file on every startup (`roboto.<timestamp>.db`, keeps the last 10).
- Config: `.env`-per-instance + `ROBOTO_INSTANCE`, replacing legacy's `-context`/`-plugin` CLI flags -
  `InstanceBootstrapper`/`BotOptions` (ported near-verbatim from `rewrite/dotnet-docker-port`).
- JSON: Newtonsoft.Json (via `PackageReference`, not a checked-in DLL) for CardCast/Steam API response
  parsing (dynamic `JObject`/`JToken` traversal, never C#'s `dynamic` keyword) - an unrelated concern
  from persistence, kept deliberately. `System.Text.Json` is used only for the SQLite persistence
  layer.
- No DI container, no service interfaces - pure abstract-class + reflection + static-list registry,
  matching legacy exactly. This is deliberate, not a gap: the DI/interfaces restructuring on the
  abandoned rewrite branch was one of the two things that broke the user's confidence in that
  approach (the other being the `ExpectedReply` replacement) - see the plan file's Context section.
- Real background scheduler (`Core/BackgroundScheduler.cs`, its own thread, ~60s tick) drives every
  module's `backgroundProcessing()` concurrently with live message dispatch - legacy never actually
  had this running live (see `MIGRATION.md`'s phase 4 notes). `ChatKeyedLock` (per-chat
  `Monitor`-based, reentrant) is the concurrency primitive this made necessary - wrap any new code that
  touches a chat's own data from both the message thread and the background thread in it, the same
  chokepoint everything else already uses.

## ⚠️ This is a live production bot with several thousand users

Every real bot's data now lives in a SQLite `.db` file (`data/<instance>/roboto.db`), migrated from
legacy's `XmlSerializer` XML - **live production data**, not a toy fixture, backed up on every startup
(`DbBackup`, keeps the last 10) but still the one copy of years of real game/chat state. Any further
data work (the `Migrator/` importer against a bot not yet on this codebase, a schema change, a
one-off DB edit) must be treated accordingly:
- Never modify/consume a production `.db`/XML file in place - copy it first.
- Build/change the importer against a **real copy** of production data, not just synthetic test data,
  before trusting it.
- Validate with counts/checksums (chat count, player count, expected-reply count, etc. before vs.
  after import) rather than eyeballing it.
- Have a dry-run / read-only verification mode before any real cutover.
- Cutover should be reversible (keep the old process/data available to fall back to) until the new
  version has run clean for a while.

A real incident already happened this way once (2026-08-24, see `MIGRATION.md`): a reused GUID
constant caused a genuinely populated production pack (457 answers/90 questions) to be mistaken for a
disposable bootstrap seed and dropped on restart. Recovered, but it's the concrete reason these rules
exist, not a hypothetical.

**Critical safety rule (user-stated): never let dev/test work talk to real users.** The bot
actively messages people in live games - if we import/run against the real production XML with a
live-pointed token, we risk sending bot messages into other people's in-progress games. So:
- All development and testing happens against **separate test bot tokens** (created via
  @BotFather), never a production token. E.g. `@Beefy_Surprise_bot` (`beefy`) has been used
  throughout this project's testing, including the abandoned rewrite branch's.
- Real production data (XML and, since the SQLite migration, `.db` files) has already been used for
  the importer/migration work under the rules above - this isn't a hypothetical future step. Any
  *new* production data work still follows the same rules: copy first, test-data-proven before
  real-data, test-bot target even then.
- Never display, type, or echo a real production token in conversation or tool output - copy the
  token file instead of showing its contents.

## Dev environment (Linux Mint 22 / Ubuntu-noble-based)

- Docker: installed and working (Compose v5 plugin, daemon reachable without sudo).
- VS Code: installed, C# Dev Kit extension installed. This session runs Claude Code as a **VS Code
  extension**, so file links in responses use markdown `[file](path#Lline)` syntax for click-through
  - keep using that format here.
- .NET SDK 10.0.400 installed via the official `dotnet-install.sh` script, to `~/.dotnet`.
  `DOTNET_ROOT`/`PATH` exports are in both `~/.bashrc` and `~/.profile`.
- git 2.43, identity configured.
- SQLite needs no separate service/install - embedded library via `Microsoft.Data.Sqlite`.
- Test bot tokens: one Telegram bot (via @BotFather) per test setup, never a production token.
  Stored per-instance under `data/<instance>/bot.env` (gitignored via `/data/`).

## Working conventions for this project

- **Check `MIGRATION.md` and the plan file before assuming a structural decision is arbitrary.**
  `/home/davepike/.claude/plans/rustling-launching-cake.md` settled most of the "how should this work"
  architectural questions during the port (persistence granularity, background scheduling, what
  carries forward from the abandoned rewrite, resolved sub-decisions like `chatPriority`, hybrid
  keyboards decided against); `MIGRATION.md` has the phase-by-phase record of what actually got built
  and every real bug found along the way. Don't re-litigate what's already decided there; do flag
  genuinely new forks the same way it was done during the port (explain the tradeoff, ask, don't
  assume).
- **When modifying a piece of code, check it for an existing comment block first.** This codebase's
  convention is: durable "why does this file look like this" narratives - prior designs that got
  superseded, real bugs that were hit and fixed, non-obvious constraints - live as comments in the
  code itself, not centralized in a doc. When you change something or fix a bug, update/add that
  comment rather than letting the history only exist in chat history. This convention predates and
  outlives the port itself - keep following it for all new work, not just migration-era changes.
- **Prefer the smallest change that gets something to a genuinely verified state**, matching the
  whole reason this codebase looks the way it does: legacy's structure survived the port *because* it
  wasn't being rewritten, not because every file happened to look similar. Resist "while I'm in here"
  cleanups that touch code outside a change's actual scope - if something looks like a bug while
  working nearby, fix it and document why, but don't restructure adjacent working code.
- This file is for durable orientation (what/where/why at the project level, safety rules, machine
  setup). `MIGRATION.md` is the historical record of the port itself (complete as of 2026-08-24) -
  read it for "why does this look like this" context on anything port-related, but it's not an active
  TODO list any more; there isn't currently a separate working doc for that, ask the user how they
  want ongoing work tracked if that's needed.
- Verification workflow (matches what was established during the port, worth keeping): `dotnet build`,
  `dotnet test` (`tests/Roboto.Tests/Roboto.Tests.csproj`) - run 3-4x in a row, not just once, since
  this project's test suite has caught real flakiness before - deliberate-breakage sanity checks on
  new critical logic (revert the fix, confirm the specific test fails with the expected shape, revert
  back), `docker compose build`, and a live round-trip against the `beefy` test bot before any
  live-Telegram-facing change is considered done - never against production data/tokens.
- Background test runs against the live test bot must **not** be time-boxed - even a 30-minute
  window has died mid-conversation before, racing the user's actual pace. Run with no `timeout` at
  all, and stop it explicitly (`kill`) only once testing is confirmed done, not on a guessed schedule.

## Notes for future sessions

- A real test suite exists: `tests/Roboto.Tests/` (116 tests as of 2026-08-24), built around
  `TestHarness`/`FakeTelegramBotClient` (adapted from the abandoned rewrite branch's own harness,
  confirmed dispatch-mechanism-agnostic). Tests can't run in parallel with each other - see
  `tests/Roboto.Tests/AssemblyInfo.cs`'s own comment for why (shared static `Roboto.*` state).
- `.github/workflows/docker-publish.yml` builds and publishes a Docker image to GHCR - **currently
  still triggers only on pushes to `rewrite/legacy-structure-port`**, not `master`, a leftover from
  before the port was merged. Worth updating (or retargeting) before relying on it to publish from
  `master` pushes - flag this to the user rather than assuming it's already been handled.
- `CLAUDE.md` + `MIGRATION.md` + the plan file are the primary sources of project context; no other
  docs/README exist. The abandoned rewrite branch's own `CLAUDE.md`/`MIGRATION.md`/
  `MIGRATION_HISTORY.md` (on `rewrite/dotnet-docker-port`) describe a different architecture - useful
  as prior art/parity-check reference (per the plan file), not as instructions for this codebase.
