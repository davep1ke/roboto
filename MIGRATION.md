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
| 3. Persistence swap (`IStateStore` blob rows + relational tables), `.env`/`ROBOTO_INSTANCE` config | Done, verified | — |
| 3b. Split the xyzzy card/pack catalog out of its blob into the `xyzzy_cards`/`xyzzy_packs` tables | Not started | — |
| 3c. `logs` table + custom Serilog DB sink + 30-day purge task | Not started | — |
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
that would be a multi-MB read/write on every save) and is still planned as `xyzzy_cards`/`xyzzy_packs`
- just **deferred to phase 3b**, not done in this pass, to keep this already-large change reviewable.
Still one blob (`module:mod_xyzzy_coredata`) for now, catalog included.

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

## What's still open

Everything from phase 3 onward - see the phase table above and the full plan file for what each phase
actually involves, the four explicitly-confirmed architecture decisions (hybrid keyboards, real
background scheduler, decomposed persistence + relational tables for whole-bot lists, carry-forward
deltas), and the resolved/open sub-decisions (chatPriority sort - decided, implement; card/pack ID
scheme - open, needs a decision during phase 5; daily XML backup - decided, not needed, TrueNAS
snapshots instead; background-scheduler batching caps - decided, keep as legacy has them).
