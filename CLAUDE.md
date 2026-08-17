# Roboto

A C# Telegram chatbot framework. Primary payload is `mod_xyzzy`, a Cards-Against-Humanity-style
game ("xyzzy") played in Telegram group chats. Other modules: quotes, birthdays, steam lookups,
wordcraft. Legacy line is Windows-only (WPF + .NET Framework). Actively being ported to modern
.NET on Linux/Docker — see `MIGRATION.md` for the phase-by-phase plan and current progress.

## Repo layout

- Repo root **is** `/home/davepike/Documents/Code/Roboto/` — a normal, single git repo (`.git`
  lives directly here), tracking `origin` = `https://github.com/davep1ke/roboto.git`, 100+ commits
  back to "Initial Version".
- Legacy solution/project: `Roboto.sln`, `Roboto/Roboto.csproj` (repo root, sibling to `src/`).
- New (rewrite) project: `src/Roboto.Bot/Roboto.Bot.csproj`, solution `src/RobotoBot.slnx`.
- Branches/tags:
  - `master` — legacy WinForms/.NET Framework line, in sync with `origin/master`. Left untouched as
    the rollback/reference point until cutover.
  - Tag `legacy-winforms-baseline` on `master` — permanent bookmark for "last state before the
    rewrite."
  - Branch `rewrite/dotnet-docker-port` — all rewrite work happens here.
  - Nothing has been pushed to `origin` yet — only do that with explicit go-ahead, this is a real
    GitHub repo backing a live bot.
- Git identity is configured (`user.name`/`user.email`) — commits work fine.

## Current stack (legacy line, `Roboto/`, still on `master`)

- .NET Framework 4.7.2, `OutputType=WinExe`, platform x86, old-style `packages.config` NuGet.
- `Newtonsoft.Json.dll` checked into the repo directly (not restored via NuGet).
- WPF `LogWindow.xaml`/`.cs` is the only UI — won't run headless; `Main()` is `[STAThread]`.
- Charts (`Core/stats.cs`) use `System.Windows.Forms.DataVisualization.Charting` — Windows-only.
- Telegram integration hand-rolled (`APIs/TelegramAPI.cs`, ~600 lines, manual long-polling).
- Persistence: entire app state XML-serialized in full on every save, to
  `%appdata%\Roboto\<context>.xml`. No database.
- Config: `-context <name>` / `-plugin <name>` CLI flags. Nothing env-var driven.
- Global mutable statics everywhere: `Roboto.Settings`, `Roboto.log`, `Plugins.plugins` — no DI, no
  interfaces for testability.
- Module ("plugin") system: reflection-discovered subclasses compiled into the same assembly (not
  real dynamic plugins), dispatch via manual `StartsWith` chains, conversational flows via a
  hand-rolled `ExpectedReply` global-list state machine. This is the thing the user called out as
  "sucks" — full detail on why in `MIGRATION.md`'s module-framework notes, since that's where the
  redesign decision and its rationale live.

## ⚠️ This is a live production bot with several thousand users

The XML settings/chat-data file being replaced by SQLite is **live production data**, not a toy
fixture. Any data-migration work (legacy XML → SQLite) must be treated accordingly:
- Never modify/consume the production XML in place — copy it first.
- Build the importer against a **real copy** of the production XML, not just synthetic test data,
  before trusting it.
- Validate with counts/checksums (chat count, player count, expected-reply count, etc. before vs.
  after import) rather than eyeballing it.
- Have a dry-run / read-only verification mode before any real cutover.
- Cutover should be reversible (keep the old process/data available to fall back to) until the new
  version has run clean for a while.

**Critical safety rule (user-stated): never let dev/test work talk to real users.** The bot
actively messages people in live games — if we import/run against the real production XML with a
live-pointed token, we risk sending bot messages into other people's in-progress games. So:
- All development and testing happens against **separate test bot tokens** (created via
  @BotFather), never the production token. This is the user's established practice already — e.g.
  a test bot `@Beefy_Surprise_bot` is used throughout the rewrite's testing.
- The user will provide a copy of the production XML **and** the corresponding test-bot
  tokens/XMLs once we're actually ready to build/test the migration importer — don't ask for or
  assume access to production credentials before that point.
- Even the *importer* should be exercised against test data first; only point it at a real copy of
  the prod XML once it's already proven correct, and even then the target must be a test-bot
  context, not anything wired to the live token.

## Architecture decisions for the port

(See `MIGRATION.md` for what's actually built vs. still pending against each of these.)

- Stay on C#, target **.NET 10** (current LTS as of mid-2026) — not a rewrite in another language.
- Drop WPF/WinForms entirely — Serilog console logging (Docker-native stdout/stderr), no
  in-process UI.
- Charts: cross-platform rendering (ScottPlot/SkiaSharp) when that phase happens; debian-slim, not
  alpine, runtime base (SkiaSharp/ICU + musl is a known source of pain).
- Telegram integration via the `Telegram.Bot` NuGet package, not hand-rolled HTTP/JSON parsing.
- Config via env vars, 12-factor style — `ROBOTO_INSTANCE` selects a bot identity, everything else
  for that identity is self-managed under `{DataDir}/{Instance}/` (see `InstanceBootstrapper.cs`).
- Persistence: SQLite replaces the XML state file only, not the logs — JSON-blob-per-aggregate via
  `IStateStore` (see `SqliteStateStore.cs`). The real XML→SQLite *migration* is a separate, much
  higher-stakes deliverable — see the production-data warning above.
- Module framework: reflection-discovered, DI-resolved command router (`CommandRouter`/
  `IBotCommand`), not loadable third-party plugin DLLs. The separately-loadable-plugin idea was the
  user's original long-term goal but was explicitly discussed and dropped — not worth the ABI/
  assembly-isolation/sandboxing cost for a single-maintainer project nobody's ever asked to extend
  externally. Not a dead end if that ever changes: command routing and assembly loading are
  separable concerns.

## Dev environment (Linux Mint 22 / Ubuntu-noble-based)

- Docker: installed and working (Compose v5 plugin, daemon reachable without sudo).
- VS Code: installed, C# Dev Kit extension installed. This session runs Claude Code as a **VS Code
  extension**, so file links in responses use markdown `[file](path#Lline)` syntax for click-through
  — keep using that format here.
- .NET SDK 10.0.400 installed via the official `dotnet-install.sh` script, to `~/.dotnet`.
  `DOTNET_ROOT`/`PATH` exports are in both `~/.bashrc` and `~/.profile`.
- git 2.43, identity configured.
- SQLite needs no separate service/install — embedded library via `Microsoft.Data.Sqlite`.
- Test bot tokens: one Telegram bot (via @BotFather) per test setup, never the production token.
  Stored per-instance under `data/<instance>/bot.env` (gitignored via `/data/`).

## Working conventions for this project

- **When modifying a piece of code, check it for an existing comment block first.** This codebase's
  convention is: durable "why does this file look like this" narratives — prior designs that got
  superseded, real bugs that were hit and fixed, non-obvious constraints — live as comments in the
  code itself, not centralized in this file. Don't assume the current shape is arbitrary without
  checking. When you change something or fix a bug, update/add that comment rather than letting the
  history only exist in chat history.
- This file is for durable orientation (what/where/why at the project level, safety rules, machine
  setup). Active phase-by-phase progress, what's blocked on what, and the TODO list live in
  `MIGRATION.md` — a working document, not meant to be durable forever.
- Background test runs against the live test bot need a genuinely long window (1800s has worked
  well) — shorter ones race the user's actual response time and the process exits before they can
  react.

## Notes for future sessions

- No automated tests, no CI exist today.
- `CLAUDE.md` + `MIGRATION.md` are the primary source of project context; no other docs/README exist.
