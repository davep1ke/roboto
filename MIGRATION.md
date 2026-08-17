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
| 5. Conversational-flow / `ExpectedReply` system | **In progress** | — |
| 6. `mod_standard` remainder (`/setquiethours`, `/addadmin`, `/removeadmin`) | Blocked on #5 | — |
| 7. `mod_xyzzy` port (the big one, ~3,800 LOC of game logic) | Not started | — |
| 8. Remaining modules (quote, birthdays, wordcraft, steam) | Not started | — |
| 9. Stats/graphs (ScottPlot), `/statgraph` | Not started | — |
| 10. XML→SQLite migration importer | Not started — needs real prod XML copy from user first | — |
| 11. Cutover | Not started | — |

"Verified" means actually exercised for real (build + run + real Telegram round-trip, sometimes
through Docker too), not just "compiles" — see each phase's commit message and in-code comments for
what was specifically tested and any bugs that were caught along the way.

## What's built so far

- `src/Roboto.Bot/Program.cs` — Generic Host entry point, env-var config, Serilog console logging.
- `InstanceBootstrapper.cs` / `BotOptions.cs` — per-instance config bootstrap. `ROBOTO_INSTANCE` env
  var selects an identity; its credentials self-bootstrap under `{DataDir}/{Instance}/bot.env`.
- `TelegramPollingService.cs` — long-polling via the `Telegram.Bot` package.
- `Commands/` — `IBotCommand`, `CommandRouter` (name-based dispatch + mute-gating + usage stats),
  `PingCommand`, `HelpCommand`, `StatsCommand`, `StartCommand`, `StopCommand`.
- `Persistence/` — `IStateStore`/`SqliteStateStore`, JSON-blob-per-key over one SQLite table.
- `Chats/` — `ChatState`/`ChatRepository`, the first real per-chat data (`ChatId`, `Title`,
  `Muted`) — deliberately module-agnostic, a real module's own per-chat data gets its own separate
  `IStateStore` key rather than a field bolted on here.
- `Dockerfile`, `docker-compose.yml`, `.dockerignore` — Docker packaging. `docker-compose.yml` runs
  as the host UID (bind-mount ownership fix, see its comments) and needs no per-instance host path.

## Now in progress: conversational-flow / `ExpectedReply` replacement

Legacy `ExpectedReply` (`Storage/ExpectedReply.cs`, `Core/Messaging.cs`): a single global
`List<ExpectedReply>`, linearly scanned to match an incoming reply back to whichever module asked
the question, tracking whether the message was actually sent yet, queuing follow-up messages per
user so a player never gets two outstanding questions at once. Needed for `/setquiethours`,
`/addadmin`, `/removeadmin`, and presumably most of `mod_xyzzy`'s actual turn-by-turn play (picking
an answer card, judging). This is very likely the single most load-bearing piece of the legacy
module framework left to replace — worth designing carefully rather than rushing.

Design notes go here as they're made; implementation notes go in the code once it exists.

## Explicitly deferred / blocked work

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
