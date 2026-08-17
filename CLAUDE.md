# Roboto

A C# Telegram chatbot framework. Primary payload is `mod_xyzzy`, a Cards-Against-Humanity-style
game ("xyzzy") played in Telegram group chats. Other modules: quotes, birthdays, steam lookups,
wordcraft. Legacy line is Windows-only (WPF + .NET Framework). Actively being ported to modern
.NET on Linux/Docker.

## Repo layout

- Repo root **is** `/home/davepike/Documents/Code/Roboto/` — this is a normal, single git repo
  (`.git` lives directly here), tracking `origin` = `https://github.com/davep1ke/roboto.git`,
  101+ commits back to "Initial Version". Nothing is nested a level down any more.
  - History: this used to be split across two accidental layers — an empty wrapper repo at this
    same path (artifact of cloning one level too low) plus the real repo nested inside a `roboto/`
    subfolder. Both problems are now fixed: the empty wrapper `.git` was deleted (2026-08-16), and
    on 2026-08-17 the entire contents of `roboto/` (including its `.git`) were moved up into this
    directory and the now-empty `roboto/` folder removed. Verified afterwards with `git fsck`,
    `git log`, and `git remote -v` — history, remote, and the tag below all survived intact.
- Legacy solution/project: `Roboto.sln`, `Roboto/Roboto.csproj` (at repo root, sibling to `src/`).
- New (rewrite) project: `src/Roboto.Bot/Roboto.Bot.csproj`, solution `src/RobotoBot.slnx`.
- Branches/tags as of 2026-08-17:
  - `master` — legacy WinForms/.NET Framework line, clean, in sync with `origin/master`. Left
    untouched as the rollback/reference point until cutover.
  - Tag `legacy-winforms-baseline` on `master` at `eef3a98` — permanent bookmark for "last state
    before the rewrite."
  - Branch `rewrite/dotnet-docker-port` — all rewrite work happens here (or branches off it). Has
    one marker commit (`c6bbc61 Begin major rewrite...`) plus the Phase 1 skeleton (currently
    uncommitted — see below).
  - Nothing has been pushed to `origin` yet — only do that with explicit go-ahead, this is a real
    GitHub repo backing a live bot.
- Git identity **is now configured** (`user.name`/`user.email` set 2026-08-17) — no longer a
  blocker for commits/tags.
- `CLAUDE.md` (this file) previously lived outside any repo (in the old empty wrapper); it's now
  naturally inside the repo working tree at the root and shows up as untracked — fine to commit if
  the user wants it version-controlled.

## Current stack (legacy line, `Roboto/`, still on `master`)

- .NET Framework 4.7.2, `OutputType=WinExe`, platform x86, old-style `packages.config` NuGet.
- `Newtonsoft.Json.dll` is checked into the repo directly (not restored via NuGet).
- WPF `LogWindow.xaml`/`.cs` is the only UI — a RichTextBox log viewer + long-op progress bars.
  App won't run headless; `Main()` is `[STAThread]` and blocks on `logWindow.ShowDialog()`.
- Charts (`Core/stats.cs`) use `System.Windows.Forms.DataVisualization.Charting` — Windows-only
  GDI+ charting, rendered to JPEG and sent back to Telegram via `/statgraph`.
- Telegram integration is hand-rolled (`APIs/TelegramAPI.cs`, ~600 lines): manual long-polling loop,
  manual `NameValueCollection` POST construction, manual `JObject.SelectToken` parsing of updates.
- Persistence: **entire app state** (settings, all chats, all plugin data, stats, expected replies)
  is one `settings` object, XML-serialized in full on every save to
  `%appdata%\Roboto\<context>.xml` (or `settings.xml` if no `-context` arg). No database. Save is a
  full-file rewrite with a same-day backup copy.
- Config: two hand-parsed CLI flags only — `-context <name>` and `-plugin <name>` (repeatable).
  Nothing is env-var driven.
- Global mutable statics everywhere: `Roboto.Settings`, `Roboto.log`, `Plugins.plugins` — no DI
  container, no interfaces for testability.

## Module ("plugin") framework — the thing the user says "sucks"

- `RobotoModuleTemplate` subclasses are discovered via reflection over the **same assembly**
  (`Assembly.GetExecutingAssembly().GetTypes()` + `Activator.CreateInstance`) — these are not real
  pluggable/loadable plugins, just an internal organizational pattern compiled into one binary.
- Each module optionally declares `pluginDataType` (global data) and `pluginChatDataType`
  (per-chat data), both abstract XML-serializable templates. Lookup is by `Type`, via linear scans
  and casts (`Plugins.getPluginData<T>()`, `chat.getPluginData<T>()`).
- `XmlSerializer` needs every module's data type enumerated up front
  (`Plugins.getPluginDataTypes()`) — fragile, easy to break silently when adding a new module.
- Command routing = every module's `chatEvent(message, chat)` gets called per incoming message and
  does manual `m.text_msg.StartsWith("/xyzzy_start")` chains. Ordering/collision is handled by
  bolted-on flags (`chatPriority`, `chatEvenIfAlreadyMatched`, `chatIfMuted`) rather than a router.
- Conversational flows are implemented via `ExpectedReply`: a single global
  `List<ExpectedReply>` that gets linearly scanned to match replies back to the asking module — a
  hand-rolled, somewhat fragile finite-state machine.
- Biggest single module: `mod_xyzzy` + `mod_xyzzy_coredata` + `mod_xyzzy_chatdata` ≈ 3,800 LOC —
  this is the valuable, hardest-to-rewrite business logic. Card packs come via
  `Helpers/cardCast.cs` — originally Cardcast (shut down ~2019), already swapped to **crcast** and,
  per the user, still works. No action needed here unless it breaks.
- **Redesign decided and Phase 2 started** (2026-08-17): scope is command router + DI, replacing
  the compiled-in-one-assembly modules' dispatch/data-access, **not** true loadable/shareable
  plugin DLLs. User's original long-term goal had been separately-compiled DLL plugins people could
  write and pass between bot instances; explicitly dropped as not worth it — the actual pain
  (dispatch chains, statics, fragile data lookup) doesn't need assembly isolation/a stable ABI/
  sandboxing to fix, and nobody's ever actually wanted to ship a third-party Roboto plugin. Not a
  dead end if that ever changes: command routing and assembly loading are separable, so dynamic
  loading could still be added later without redoing this.
  - Built in `src/Roboto.Bot/Commands/`: `IBotCommand` (`Name`, `Description`,
    `ExecuteAsync(CommandContext, CancellationToken)` — one class = one command, no grouping
    abstraction yet, revisit when a real multi-command module gets ported), `CommandRouter`
    (name-based dispatch, replaces the `StartsWith` chains, no priority/collision flags needed),
    reference commands `PingCommand`/`HelpCommand`. Discovery is reflection over the assembly in
    `Program.cs` (same "harmless" idea as the legacy scan, just registers each `IBotCommand` with
    DI instead of hand-listing them) — modules take injected dependencies (e.g. `ILogger<T>`), no
    static globals touched anywhere in the new code.
  - **Real gotcha hit and fixed**: `HelpCommand` needs to see every registered command, which is an
    easy way to accidentally create a circular DI dependency (`CommandRouter` needs all
    `IBotCommand`s built including `HelpCommand` → `HelpCommand` needs `CommandRouter` →
    circular). Fixed by having `HelpCommand` take `IServiceProvider` and resolve `CommandRouter`
    lazily inside `ExecuteAsync` rather than as a constructor dependency. Worth remembering for any
    future command that needs to know about its siblings.
  - **Not yet covered by this redesign**: the `ExpectedReply` conversational-flow state machine
    (multi-turn interactions, e.g. "what's your answer?") — that's a separate, bigger piece of the
    module framework, deliberately out of scope for this pass so it stayed testable in one slice.
  - Verified for real against `@Beefy_Surprise_bot`: `/ping` and `/help` both round-tripped
    correctly, both via plain `dotnet run` and through the full `docker compose` path — confirmed
    no circular-DI exception at runtime (the risk above only shows up at runtime, not compile time).

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
  a test bot `@Beefy_Surprise_bot` was used to verify the Phase 1 skeleton (see below).
- The user will provide a copy of the production XML **and** the corresponding test-bot
  tokens/XMLs once we're actually ready to build/test the migration importer — don't ask for or
  assume access to production credentials before that point.
- Even the *importer* should be exercised against test data first; only point it at a real copy of
  the prod XML once it's already proven correct, and even then the target must be a test-bot
  context, not anything wired to the live token.

## Working agreement for the Docker/Linux port

- Preference: **stay on C#**, migrate to modern .NET, not a full rewrite in another language.
  Target **.NET 10** (current LTS as of mid-2026, GA'd Nov 2025, supported to ~Nov 2028).
- Drop WPF entirely. Logging → structured console (stdout/stderr, Docker-native) via Serilog; no
  in-process log window. **Done** in Phase 1.
- Drop `System.Windows.Forms.DataVisualization` charts → cross-platform rendering (ScottPlot /
  SkiaSharp), debian-slim (not alpine) runtime base if SkiaSharp is involved. **Not started yet.**
- Replace hand-rolled Telegram HTTP/long-poll code with the `Telegram.Bot` NuGet package. **Done**
  in Phase 1.
- Config → env vars / `IConfiguration` (12-factor), not CLI flags. One container = one bot
  token/instance. **Done** in Phase 1, then redesigned once — see "Instance bootstrap" below.
  Footnote for later tidy-up: `ROBOTO_INSTANCE` is basically the modern equivalent of the legacy
  `-context` flag (same job — which bot identity to run as) — worth a deliberate look at whether
  the two concepts should be reconciled/renamed for consistency once we're deeper into the port,
  rather than just carrying "instance" as an unrelated-sounding new term.
- Persistence → **SQLite replaces the XML state file only**, not the logs — JSON-blob-per-
  aggregate. **Started 2026-08-17** — see "Persistence layer" below. Still not a *decided* deliverable
  for the real production XML→SQLite migration itself, though — that's a separate, much higher-
  stakes piece; see the production-data warning above, which still fully applies.
- Module framework → decided, Phase 2 built. See dedicated section above.

## Phase 1 skeleton — done and verified (2026-08-17)

`src/Roboto.Bot/`: .NET 10 console app on Generic Host.
- `Program.cs` — host setup, Serilog console logging, `AddEnvironmentVariables(prefix: "ROBOTO_")`
  for exactly two real env vars (`Instance`, `DataDir` — see below), then hands off to
  `InstanceBootstrapper` before the host is even built.
- `BotOptions.cs` — `Instance` (default `"default"`), `DataDir` (default `/data`),
  `TelegramToken`/`BotUsername` (populated by the bootstrapper, *not* bound from env vars).
- `InstanceBootstrapper.cs` — **the config/data model, redesigned once already** (see below).
- `TelegramPollingService.cs` — a `BackgroundService` wrapping `Telegram.Bot` (v22.10): calls
  `GetMe()` at startup to log identity, long-polls via `ReceiveAsync`, replies to `/ping` with
  `pong`. Wraps the loop in try/catch so a fatal error sets `Environment.ExitCode = 1` and calls
  `IHostApplicationLifetime.StopApplication()` — **note:** `BackgroundServiceExceptionBehavior.
  StopHost` (the Generic Host default) stops the host cleanly on an unhandled exception but does
  *not* set a non-zero process exit code by itself; this was an actual bug caught and fixed during
  Phase 1 testing (mattered for Docker restart-policy correctness). Top-level `Program.cs` must
  `return Environment.ExitCode;` at the end, not a hardcoded `return 0;`, or the fix is silently
  discarded.
- `Dockerfile` (repo root) — multi-stage, `mcr.microsoft.com/dotnet/sdk:10.0` build /
  `mcr.microsoft.com/dotnet/runtime:10.0` runtime (Debian, not alpine), runs as the non-root
  `$APP_UID` user baked into the official image.
- `.dockerignore` — deliberately excludes the legacy `Roboto/`/`Roboto.sln` from the build context.
- `docker-compose.yml` — see "Instance bootstrap" below for the `ROBOTO_INSTANCE`/`user:` shape.

### Instance bootstrap (config + data model) — redesigned once already, this is the current version

First attempt (superseded, don't resurrect): container config purely via `ROBOTO_TELEGRAMTOKEN`/
`ROBOTO_BOTUSERNAME` env vars, with per-test-bot `*.env` files (e.g. `BeefySurprise.env`) at the
repo root selected via a `docker-compose.yml` `${ENV_FILE:-.env}` trick. User's objection: creating
a new instance meant hand-authoring a new env file yourself, *and* the compose file only bind-
mounted one fixed `./data` path — nothing stopped two concurrently-running instances from
colliding and overwriting each other's SQLite state. Both real problems, not just preference.

**Current design**: the only thing that varies per bot identity is `ROBOTO_INSTANCE` (env var,
default `"default"` — direct spiritual successor to the legacy `-context` flag, see footnote
above). Everything else for that identity — credentials *and* future SQLite state — lives under
`{DataDir}/{Instance}/`, which the app manages itself:
- `{DataDir}/{Instance}/bot.env` — `TelegramToken=`/`BotUsername=`, plain `key=value` lines (own
  tiny parser in `InstanceBootstrapper.Parse`, no external dotenv package — format is small and
  fully ours, not general-purpose env-file compatibility).
- `{DataDir}/{Instance}/roboto.db` — where SQLite will live once that phase lands (not built yet).
- If the instance folder/file doesn't exist, or exists with a blank `TelegramToken`,
  `InstanceBootstrapper.TryLoad` creates the folder + a stub `bot.env`, prints a message telling the
  user to fill it in, and the app exits (1) — deliberately mirrors the legacy app's "first run
  creates a blank XML, edit it and restart" bootstrap, just per-instance-folder instead of
  per-`-context`-XML-file.
- Because every instance's data is a *subfolder* of one shared mount, `docker-compose.yml` only
  ever needs one bind mount (`./data:/data`) for every instance, forever — there's no per-instance
  host path to configure or get wrong. Spinning up a brand new instance is: pick a name, run
  `ROBOTO_INSTANCE=<name> docker compose up --build`, fill in the stub it creates, run it again.
  Running several instances concurrently: same mount, add `-p <name>` per `docker compose` project
  to keep container names/networks apart.
- **Real bug hit and fixed while verifying this**: the Dockerfile's baked-in non-root `$APP_UID`
  owns `/data` *inside the image*, but the compose bind mount (`./data:/data`) overlays that with
  the *host* directory's ownership at runtime, so the container couldn't write into it
  (`UnauthorizedAccessException`). Fixed with `user: "${DOCKER_UID:-1000}:${DOCKER_GID:-1000}"` in
  `docker-compose.yml` — runs the container as the host user for the bind-mount case. `1000:1000`
  covers this dev machine out of the box; override via `DOCKER_UID`/`DOCKER_GID` if a host differs.
- The old `.env.example`, `BeefySurprise.env`, and the `*.env`/`ENV_FILE` gitignore special-casing
  are gone — superseded by the above. The real `@Beefy_Surprise_bot` credentials were migrated into
  `data/beefy/bot.env` (gitignored, same as every instance folder — `/data/` is the one gitignore
  rule that matters now, nothing env-file-specific needed any more).

**Verified, not just written** (this matters — build/tests alone wouldn't have caught the exit-code
bug, the bind-mount permission bug, or a real Telegram auth failure):
- `dotnet build` clean throughout both the Phase 1 skeleton and the instance-bootstrap redesign.
- No token → exit 1 fast, clear message (original design).
- Bad token → real call to Telegram's API, genuine 401, logged, clean shutdown, exit 1.
- Same checks repeated inside the built Docker image via `docker run` — confirms non-root user,
  entrypoint, and network egress all work in-container.
- `docker compose up` with a bad token confirmed `restart: unless-stopped` actually crash-loops as
  expected; torn down cleanly with `docker compose down`.
- Brand-new-instance bootstrap tested for real, twice: once locally (`dotnet run` against a temp
  `ROBOTO_DATADIR`), once through `docker compose run` — both correctly self-created the stub
  `bot.env` and exited; the second run also surfaced and confirmed the bind-mount UID bug above.
- **Real round-trip**: ran against the actual `@Beefy_Surprise_bot` test bot (after fixing a
  token/username field swap the user had in the original `BeefySurprise.env`), sent `/start` and
  `/ping` from Telegram, got `pong` back — confirmed via both the user's report and the service's
  own logs. Re-confirmed auth still works post-redesign, both via plain `dotnet run` and through
  the full `docker compose run` path with the corrected UID. Graceful shutdown (SIGTERM) also
  confirmed clean (exit 0).

Committed 2026-08-17 on `rewrite/dotnet-docker-port` (Phase 1 skeleton + instance-bootstrap
redesign, one commit; module framework Phase 2 in a second commit).

## Persistence layer — done and verified (2026-08-17)

`src/Roboto.Bot/Persistence/`:
- `IStateStore` — `InitializeAsync`, `LoadAsync<T>(key)`, `SaveAsync<T>(key, value)`. Deliberately
  generic/untyped-by-key rather than one method per aggregate type - callers own their own key
  scheme and POCO shape, no schema migration needed when a field gets added later. This is the
  `IModuleStore<T>`-shaped abstraction floated during early planning, now real.
- `SqliteStateStore` — the only implementation. One `state(key TEXT PRIMARY KEY, json TEXT,
  updated_utc TEXT)` table, `System.Text.Json` for the blob. **Resolves the deferred JSON-library
  TODO for this part of the codebase**: went with `System.Text.Json` (built-in, no extra
  dependency) over `Newtonsoft.Json` - nothing here needed Newtonsoft's extra features. The legacy
  XML→SQLite *migration importer* is a separate concern and may still want Newtonsoft to match the
  legacy code's existing `JObject`-style parsing; not decided, not urgent.
  - Opens a fresh `SqliteConnection` per operation rather than holding one open for the app's
    lifetime - Microsoft.Data.Sqlite's own recommended pattern (the provider pools the underlying
    native handles), and it sidesteps `SqliteConnection` not being thread-safe for concurrent
    access. Relies on Telegram update handling being sequential (one update processed at a time,
    true today with the current `ReceiveAsync` setup) - revisit if that ever changes.
  - DB file lives at `{DataDir}/{Instance}/roboto.db` (`BotOptions.InstanceDir`), i.e. inside the
    same per-instance folder `bot.env` already lives in - no new volume/path concept, same "one
    shared /data mount, app partitions it by instance" model as the config bootstrap.
- `BotOptions.InstanceDir` — new computed property (`Path.Combine(DataDir, Instance)`) so this path
  isn't duplicated between the store and anything else that needs it later. `InstanceBootstrapper`
  still computes its own copy of this path internally rather than using the property, since it runs
  *before* `BotOptions` is populated - not worth restructuring startup ordering to dedupe two lines.
- `CommandRouter` now takes `IStateStore` and records a usage count per command name on every
  successful dispatch (key `"command-usage"`, a `Dictionary<string,int>` blob) - a genuinely useful
  cross-cutting feature (mirrors the legacy `/stats` command's spirit), and it's what got used to
  prove persistence actually works, not a throwaway. New `StatsCommand` (`/stats`) reports the
  counts. This is a minimal starting point, not the full legacy stats/graphing system (that's still
  a separate, not-started deliverable - see the working agreement above).

**Verified, not just written** - specifically the thing that actually matters for a persistence
layer (surviving a restart, not just working within one process lifetime):
- `dotnet build` clean.
- Fresh instance, table creation on first run: no errors.
- Sent `/ping` twice then `/stats` against `@Beefy_Surprise_bot` - correct count reported within the
  same run.
- **Killed the process, started a brand new one against the same `data/beefy/roboto.db` file, sent
  only `/stats` (no `/ping` first)** - the earlier count was still there. Confirms real crash-safe
  persistence, not just in-memory state.
- Repeated the whole thing through `docker compose build` + `docker compose run` - same result, and
  confirmed the SQLite file on the host ends up owned by the correct (host) user, no permission
  issues from the earlier bind-mount UID fix.

## mod_standard port (partial) — done and verified (2026-08-17)

Legacy `mod_standard` has ten commands. Deliberately scoped this pass down to the two that are
actually foundational (introduce real per-chat state for the first time) rather than porting
everything and quietly also building the conversational-flow system on the side:

- **Ported**: `/start`, `/stop`. `/help`, `/stats` already existed (Phase 2) and needed no legacy-
  parity changes beyond `/stats` gaining an uptime line (`AppClock`, a trivial `DateTime.UtcNow`
  singleton captured at startup).
- **Blocked on the still-deferred conversational-flow system** (not attempted): `/setquiethours`,
  `/addadmin`, `/removeadmin` - all legacy `SendQuestion`/`ExpectedReply` flows.
- **Blocked on charting** (not attempted, not started at all): `/statgraph`.
- **Not ported, no longer meaningful**: `/save` (SQLite already writes incrementally per-operation,
  there's no batched in-memory state to flush) and `/background` (no periodic background-processing
  loop exists yet to manually trigger).

What got built:
- `src/Roboto.Bot/Chats/ChatState.cs` / `ChatRepository.cs` — the **first real per-chat state** in
  the new codebase. `ChatState` deliberately only holds what's genuinely chat-level/module-agnostic
  (`ChatId`, `Title`, `Muted` - the equivalent of fields that lived directly on the legacy `chat`
  class itself). A module's own per-chat data (once one exists, e.g. ported `mod_xyzzy`) gets its
  *own* separate `IStateStore` key/POCO, not a field bolted onto `ChatState` - avoids recreating the
  legacy problem where one shared chat object had to know about every module's data shape.
- `StartCommand`/`StopCommand` — private chats get a short explanatory reply instead of touching
  chat state at all (mirrors the legacy `chat` class not existing for PM chats).
- `CommandRouter` mute-gating — group chats only (`ChatType.Group`/`Supergroup`), checked after
  resolving the command but before dispatch. `/start` and `/stop` are always let through by name;
  everything else (including `/help`, `/ping`, `/stats`) is silently ignored while muted - matches
  observed legacy behavior (mod_standard's `chatIfMuted=true` kept its own `chatEvent` running while
  muted, but individual commands still did their own per-command mute checks internally; `/help`
  explicitly required `muted == false`, `/stop`/`/save` had no mute check at all).

**Verified, not just written** - specifically the new/risky part (mute-gating actually changing
dispatch behavior), via a real Telegram group chat created for this test:
- Private chat: `/start` → "Hello! Type /help..."; `/stop` → "This only applies to group chats."
- New group chat with the bot added: `/ping` → replied normally. `/stop` → silenced. `/ping` again →
  **silently ignored**, confirmed independently via the router's own log line (`Ignoring /ping in
  muted chat <id>`), not just the user's report. `/start` → un-silenced. `/ping` again → replied
  normally again. Full mute/unmute cycle confirmed correct from server-side logs.
- Repeated `docker compose build` + `docker compose run` against the same instance afterward - same
  five commands registered, clean auth, no regressions.
- Process note: background test runs need a genuinely long window (ended up using 1800s) - shorter
  ones raced against the user's actual response time and the bot kept closing before they could test.

## Deferred decisions / TODO list

Flagged, deliberately not being solved now — come back to these:

- ~~Module framework redesign~~ — **done**, see the dedicated section above.
- **JSON library choice for the rewrite** — **partially resolved**: the new SQLite persistence layer
  uses `System.Text.Json` (see "Persistence layer" above). Still open for anything that ends up
  needing to *read* legacy-shaped JSON/data (e.g. the eventual XML→SQLite migration importer,
  card-pack data) — the legacy app has `Newtonsoft.Json.dll` checked directly into `Roboto/` (not
  restored via NuGet), and Newtonsoft is more permissive about the kind of loosely-typed JSON that
  code assumes. Not urgent yet.
- **Shutdown/cancellation design needs rethinking, not just carried over.** User's words: "the exit
  logic sucks." The legacy app's shutdown (`Messaging.quit()` sets a flag; `Roboto.cs` warns "this
  could take up to `waitDuration` seconds" — `waitDuration` defaults to 60s, the long-poll timeout)
  waits out whatever HTTP long-poll call happens to be in flight before it can actually exit. The
  current `TelegramPollingService` inherits the same shape by construction — `ReceiveAsync`
  ultimately blocks on Telegram's long-poll HTTP calls, so Ctrl-C/SIGTERM can't return instantly
  either, it has to wait for the current poll to come back (or time out) before honoring
  cancellation. Not yet designed how to do better (shorter poll timeouts + faster cancel checks?
  cancel the in-flight HTTP call outright rather than waiting it out? just document and accept a
  bounded worst-case delay?) — needs an actual decision, not an assumption that today's shape is fine.

## Dev environment (checked 2026-08-16/17, Linux Mint 22 / Ubuntu-noble-based)

- Docker: installed and working (Docker 29.7.1 + Compose v5 plugin, daemon reachable without sudo).
- VS Code: installed, C# Dev Kit extension installed by the user. Errors on `.cs` files were just
  the missing SDK, resolved once it was installed + window reloaded.
  - Note: this session runs Claude Code as a **VS Code extension**, so file links in responses use
    markdown `[file](path#Lline)` syntax for click-through — keep using that format here.
- .NET SDK: **installed** 2026-08-16 — SDK 10.0.400 via the official `dotnet-install.sh` script, to
  `~/.dotnet`. `DOTNET_ROOT`/`PATH` exports added to both `~/.bashrc` (interactive shells) and
  `~/.profile` (login shells) — needed both, since `.bashrc` early-returns for non-interactive
  shells and plain `bash -lc` doesn't source it.
- git: 2.43. Identity configured 2026-08-17 (`user.name`/`user.email`) — was previously a blocker
  for tags/commits, no longer is. Git config changes are the user's to make, not something Claude
  Code executes automatically, even on request.
- SQLite needs no separate service/install — embedded library via `Microsoft.Data.Sqlite`, nothing
  to run.
- Test bot tokens: user's established practice is one Telegram bot (via @BotFather) per test setup,
  never the production token. Now stored per-instance under `data/<instance>/bot.env` (see
  "Instance bootstrap" above), not as root-level files any more.

## Notes for future sessions

- No automated tests, no CI exist today.
- No README/docs existed as of the last check — this file is the primary source of project context.
