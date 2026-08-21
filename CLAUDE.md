# Roboto

A C# Telegram chatbot framework. Primary payload is `mod_xyzzy`, a Cards-Against-Humanity-style
game ("xyzzy") played in Telegram group chats. Other modules: quotes, birthdays, steam lookups,
wordcraft. This branch (`rewrite/legacy-structure-port`) ports the legacy WinForms/.NET Framework
line to modern .NET on Linux/Docker **while keeping legacy's actual class/dispatch/game-logic
structure intact** - see `MIGRATION.md` for the phase-by-phase plan and current progress, and
`/home/davepike/.claude/plans/rustling-launching-cake.md` for the full plan and its rationale.

## Why this branch exists (read before assuming "rewrite" conventions apply)

An earlier branch, `rewrite/dotnet-docker-port`, ported this bot by replacing legacy's structure
wholesale - DI, interfaces, a new `CommandRouter`/`ReplyRouter` instead of legacy's reflection-based
module system and `ExpectedReply` state machine. That branch is **kept, untouched**, as a reference/
parity-check source and a source to copy specific infra pieces from (its `IStateStore`,
`InstanceBootstrapper`, `Telegram.Bot`/ScottPlot usage idioms, and test-harness pattern are all
directly useful) - but its architecture is **not** what this branch follows. The user lost confidence
in that path specifically because of how much had to be manually re-derived from reading legacy code,
which is exactly where subtle behavioral nuance gets lost. This branch instead keeps legacy's own
classes, dispatch (`chatEvent`/`StartsWith` chains), and `ExpectedReply` conversational-state machine
completely intact, swapping only what's structurally forced by running on modern/cross-platform .NET
(runtime, persistence, config, logging sink, Telegram HTTP layer, JSON library, charting) plus a
small number of explicitly-decided additions (real background scheduler, hybrid inline keyboards,
relational tables for whole-bot growing lists, DB-backed logging). **Before changing how something is
structured, check whether the plan file already made a deliberate call on it** - most architectural
decisions for this branch are already settled there, not open questions.

## Repo layout

- Repo root **is** `/home/davepike/Documents/Code/Roboto/` - a normal, single git repo (`.git`
  lives directly here), tracking `origin` = `https://github.com/davep1ke/roboto.git`.
- Legacy/this branch's solution/project: `Roboto.sln` (VS-specific, may be stale for the new
  SDK-style csproj - not load-bearing for the CLI-driven `dotnet build`/`dotnet test` workflow this
  project actually uses), `Roboto/Roboto.csproj`.
- Branches/tags:
  - `master` - legacy WinForms/.NET Framework line, in sync with `origin/master`. Left untouched as
    the rollback/reference point until cutover.
  - Tag `legacy-winforms-baseline` on `master` - permanent bookmark for "last state before any
    porting work", and this branch's actual starting point (`git checkout -b
    rewrite/legacy-structure-port legacy-winforms-baseline`).
  - Branch `rewrite/dotnet-docker-port` - the abandoned from-scratch rewrite (see above). Kept as a
    reference source, not merged into this branch.
  - Branch `rewrite/legacy-structure-port` - **this branch**, where all current work happens.
  - Nothing has been pushed to `origin` yet from this branch - only do that with explicit go-ahead,
    this is a real GitHub repo backing a live bot.
- Git identity is configured (`user.name`/`user.email`) - commits work fine.

## Current stack (`Roboto/`, this branch)

- .NET 10, SDK-style `Roboto.csproj`, console `Exe` (not `WinExe`) - no more `[STAThread]`/WPF UI
  thread split.
- Legacy's actual module system kept intact: `RobotoModuleTemplate`/`RobotoModuleDataTemplate`/
  `RobotoModuleChatDataTemplate` abstract base classes, reflection-discovered via
  `Plugins.initPluginAssemblies()`, registered into the static `Plugins.plugins` list. Every concrete
  module (`mod_xyzzy`, `mod_standard`, `mod_quote`, `mod_birthdays`, `mod_wordcraft`, `mod_steam`)
  and its `_coredata`/`_chatdata` classes carry their existing method surface unchanged.
- Dispatch kept intact: the per-plugin loop (currently still inside `TelegramAPI.getUpdates()`) calls
  `plugin.chatEvent(m, chatData)` for every plugin with `chatHook=true`, and each module's own
  `StartsWith("/xyzzy_start")`-style chains inside `chatEvent` are unchanged. Phase 2 swaps the
  transport underneath this (hand-rolled `HttpWebRequest`+`JObject` → `Telegram.Bot` package)
  without changing this dispatch shape.
- `ExpectedReply`/`Messaging` conversational-state machine kept intact: single global
  `List<ExpectedReply>`, the match predicate (`userID` + either direct-PM or
  `replyMessageID == outboundMessageID`), remove-before-invoke ordering, per-user
  single-outstanding-question serialization. Phase 5 adds `CallbackQuery` (inline-keyboard taps) as a
  *second trigger* into this same matching path, not a parallel dispatch mechanism.
- Logging: `Core/logging.cs` keeps its public method shapes (`log`/`logItem`/`longOp`/`loglevel`/
  `cleanse`) but the `Color?` parameter (only ever drove a WPF `LogWindow`'s text color) is gone, and
  output goes through Serilog (console today; phase 3 adds a DB sink + 30-day purge).
  `System.Windows.Forms.DataVisualization.Charting`-based chart rendering (`Core/stats.cs`) is stubbed
  pending the ScottPlot port (phase 6) - the series-selection/datapoint-gathering logic is
  chart-library-agnostic already and is kept as-is.
- Persistence: still `XmlSerializer` → `%appdata%\Roboto\<context>.xml` today (unchanged from legacy,
  including a known-but-not-yet-fixed literal-backslash path-separator bug, harmless on Linux - see
  `MIGRATION.md`). Phase 3 replaces this with SQLite: `IStateStore` blob rows for small/bounded
  per-chat/per-module state, real relational tables for whole-bot growing lists (`expected_replies`,
  `stats`, `chat_presence`, `xyzzy_cards`/`xyzzy_packs`, `quotes`, `birthdays`, `logs`).
- Config: still `-context <name>`/`-plugin <name>` CLI flags today. Phase 3 replaces this with
  `.env`-per-instance + `ROBOTO_INSTANCE`, matching `rewrite/dotnet-docker-port`'s
  `InstanceBootstrapper`/`BotOptions` pattern (reused near-verbatim - see the plan file).
- JSON: Newtonsoft.Json (now via `PackageReference`, not a checked-in DLL) for both the legacy
  persistence format and CardCast/Steam API response parsing (dynamic `JObject`/`JToken` traversal,
  never C#'s `dynamic` keyword). Phase 3 introduces `System.Text.Json` alongside it, only for the new
  SQLite persistence layer - Newtonsoft stays for the external-API parsing, an unrelated concern.
- No DI container, no service interfaces - pure abstract-class + reflection + static-list registry,
  matching legacy exactly. This is deliberate, not a gap: the DI/interfaces restructuring on the
  abandoned rewrite branch was one of the two things that broke the user's confidence in that
  approach (the other being the `ExpectedReply` replacement) - see the plan file's Context section.

## ⚠️ This is a live production bot with several thousand users

The XML settings/chat-data file being replaced by SQLite is **live production data**, not a toy
fixture. Any data-migration work (legacy XML → SQLite) must be treated accordingly:
- Never modify/consume the production XML in place - copy it first.
- Build the importer against a **real copy** of the production XML, not just synthetic test data,
  before trusting it.
- Validate with counts/checksums (chat count, player count, expected-reply count, etc. before vs.
  after import) rather than eyeballing it.
- Have a dry-run / read-only verification mode before any real cutover.
- Cutover should be reversible (keep the old process/data available to fall back to) until the new
  version has run clean for a while.

**Critical safety rule (user-stated): never let dev/test work talk to real users.** The bot
actively messages people in live games - if we import/run against the real production XML with a
live-pointed token, we risk sending bot messages into other people's in-progress games. So:
- All development and testing happens against **separate test bot tokens** (created via
  @BotFather), never the production token. E.g. a test bot `@Beefy_Surprise_bot` (`beefy`) was used
  throughout the abandoned rewrite branch's testing and is available for this branch too.
- The user will provide a copy of the production XML **and** the corresponding test-bot
  tokens/XMLs once we're actually ready to build/test the migration importer - don't ask for or
  assume access to production credentials before that point.
- Even the *importer* should be exercised against test data first; only point it at a real copy of
  the prod XML once it's already proven correct, and even then the target must be a test-bot
  context, not anything wired to the live token.

## Dev environment (Linux Mint 22 / Ubuntu-noble-based)

- Docker: installed and working (Compose v5 plugin, daemon reachable without sudo).
- VS Code: installed, C# Dev Kit extension installed. This session runs Claude Code as a **VS Code
  extension**, so file links in responses use markdown `[file](path#Lline)` syntax for click-through
  - keep using that format here.
- .NET SDK 10.0.400 installed via the official `dotnet-install.sh` script, to `~/.dotnet`.
  `DOTNET_ROOT`/`PATH` exports are in both `~/.bashrc` and `~/.profile`.
- git 2.43, identity configured.
- SQLite needs no separate service/install - embedded library via `Microsoft.Data.Sqlite` (phase 3).
- Test bot tokens: one Telegram bot (via @BotFather) per test setup, never the production token.
  Stored per-instance under `data/<instance>/bot.env` once phase 3 lands (gitignored via `/data/` -
  already added to `.gitignore` on this branch even though nothing consumes that path yet, since the
  old rewrite branch's leftover `data/` directory, containing real tokens/XML, was sitting untracked
  in the working tree when this branch was created).

## Working conventions for this project

- **Check the plan file before making an architectural call.** `/home/davepike/.claude/plans/
  rustling-launching-cake.md` already settled most of the "how should this work" questions for this
  branch (persistence granularity, keyboard model, background scheduling, what carries forward from
  the abandoned rewrite, resolved sub-decisions like `chatPriority`). Don't re-litigate what's already
  decided there; do flag genuinely new forks the same way it was done originally (explain the
  tradeoff, ask, don't assume).
- **When modifying a piece of code, check it for an existing comment block first.** This codebase's
  convention is: durable "why does this file look like this" narratives - prior designs that got
  superseded, real bugs that were hit and fixed, non-obvious constraints - live as comments in the
  code itself, not centralized in this file. When you change something or fix a bug, update/add that
  comment rather than letting the history only exist in chat history. Two examples already in place
  from phase 0: `Modules/ModuleStorage/mod_xyzzy_classes.cs`'s `[XmlIgnore]` fix, `Core/Plugins.cs`'s
  `getPluginData(Type)` null-vs-throw fix.
- **Prefer the smallest change that gets a phase to a genuinely verified state**, matching this
  branch's whole reason for existing: legacy's structure survives *because* it isn't being rewritten,
  not because every file happens to look similar. Resist "while I'm in here" cleanups that touch code
  outside a phase's actual scope - if something looks like a bug while porting, fix it and document
  why (see the two phase-0 examples above), but don't restructure adjacent working code.
- This file is for durable orientation (what/where/why at the project level, safety rules, machine
  setup). Active phase-by-phase progress, what's blocked on what, and the TODO list live in
  `MIGRATION.md` - a working document, not meant to be durable forever.
- Verification workflow per phase (matches the abandoned rewrite branch's own established practice,
  worth keeping): `dotnet build`, `dotnet test` once a test suite exists (phase 7) - run 3-4x in a
  row, not just once - deliberate-breakage sanity checks on new critical logic (the scheduler/lock,
  the callback-to-`ExpectedReply` bridge, decomposed-persistence reassembly), `docker compose build`
  once a Dockerfile exists, and a live round-trip against the `beefy` test bot before any
  live-Telegram-facing phase is considered done - never against production data/tokens.
- Background test runs against the live test bot must **not** be time-boxed - even a 30-minute
  window has died mid-conversation before, racing the user's actual pace. Run with no `timeout` at
  all, and stop it explicitly (`kill`) only once testing is confirmed done, not on a guessed schedule.

## Notes for future sessions

- No automated test suite exists yet on this branch (phase 7 builds one, adapting the abandoned
  rewrite branch's `TestBot`/`FakeTelegramBotClient` harness - confirmed dispatch-mechanism-agnostic,
  see the plan file).
- No CI exists yet - nothing's pushed to `origin` for it to run against regardless.
- `CLAUDE.md` + `MIGRATION.md` + the plan file are the primary sources of project context for this
  branch; no other docs/README exist. The abandoned rewrite branch's own `CLAUDE.md`/`MIGRATION.md`/
  `MIGRATION_HISTORY.md` (on `rewrite/dotnet-docker-port`) describe a different architecture - useful
  as prior art/parity-check reference (per the plan file), not as instructions for this branch.
