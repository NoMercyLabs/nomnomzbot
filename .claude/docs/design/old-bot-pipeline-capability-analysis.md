# Old-bot capability analysis — what the new pipeline tree must support

Source: `C:\Projects\StoneyEagle\nomercy-bot` (read-only). Scope: `src/NoMercyBot.CommandsRewards/`
— 57 scripted commands, 5 rewards, 1 reward-change-handler, 1 widget, plus 1 hosted timer service
they depend on. Every C# file here is a Roslyn-compiled script implementing `IBotCommand` /
`IReward` / `IRewardChangeHandler` — this **is** the old bot's "pipeline system": hand-written C#
standing in for what the new tree must do declaratively.

Cross-checked against current specs: `commands-pipelines.md` (flat pipeline actions/conditions,
§10 deferred one-shot scheduling — already as-built) and `pipeline-control-flow.md` (tree D1–D6:
`if`/`switch`/`loop`/`random_branch`/`run_pipeline`). When this analysis was written (2026-08-24) the AND/OR
condition tree and wait-for-event were absent from the code; both are built now (see "Where this went").

**Where this went.** The gap list below fed `spec/pipeline-tree-and-editor.md` (condition tree, `try`,
`detached_step`, `wait_for_event`, persisted runs, triggers) and the code that followed it. Old-bot behaviour
is now shipped as ordinary editable pipelines built from generic blocks, not as bespoke features:
`server/src/NomNomzBot.Infrastructure/Content/Commands/CommandFlowImporter.cs` turns a command spec into
pick lists plus an `if` chain, and `RaidFlowSeeder.cs` seeds `!raid` from `start_raid`, `obs_switch_scene`,
`send_message` and `wait`. Section 3 is the closed-versus-open ledger.

---

## 1. Behaviour inventory

| # | Behaviour | File:line | Trigger | Non-trivial because |
|---|---|---|---|---|
| 1 | `!fight`/`!hit` snarky attack | `commands/Fight.cs:206-319` | command + runtime alias registration | 5-way branch (self/bot/known-user/Twitch-stranger/fake-name) via DB lookup + live Twitch API fallback; weighted random text pool per branch; TTS side effect |
| 2 | `!hug` mirrors Fight's branch shape | `commands/Hug.cs:185-246` | command | same 5-way classification + random pool + TTS |
| 3 | BSOD reward (fake OS crash) | `rewards/Bsod.cs:281-401` | channel-points redeem | reads a **Widget's JSON settings** for enabled OS list → random OS → random SSML template → string-length-conditional SSML rewrite (regex rate/break-time rewrite) → TTS synth → **timed external-service choreography**: pause Spotify, `Task.Delay(computed ms)`, resume Spotify; refund-on-any-failure |
| 4 | `!raid <user> [seconds]` | `commands/Raid.cs:30-291` | command (broadcaster only) | fire real Twitch raid, then **wall-clock-aligned** multi-stage countdown (chat messages at 45/30/15/10/5/3/2/1s) computed against Twitch's own fixed 90s server timer, parallel OBS scene switch (fire-and-forget), final wait computed from recorded timestamp not elapsed-time, then stop OBS + pause Spotify; duplicate-raid error is caught **by matching exception message text** |
| 5 | Lucky Feather steal (reward) | `rewards/LuckyFeather.cs:90-201` | channel-points redeem | self-steal blocked; holder lookup = "latest Record row of type LuckyFeather"; **live-mutates the Twitch reward's cost/title/description** (cost +1 each steal) via Helix; random flavour text with 2 named placeholders; overlay event |
| 6 | Lucky Feather enable/disable/hide/reappear | `changes/LuckyFeatherChange.cs:156-322` | **reward-configuration-change webhook**, not a command | 4 distinct sub-triggers (OnEnabled/OnDisabled/OnPauseStatusChanged/OnResumeStatusChanged) each with its own random text pool, live Helix reward-prompt rewrite, overlay event, best-effort TTS |
| 7 | Lucky Feather hold/cooldown cycle | `Services/Twitch/LuckyFeatherTimerService.cs:8-260` | stream.online / stream.offline / on-steal | **stateful hosted timer**: first steal starts a random 3-5 min hold, then auto-hides (pause reward + overlay event), random 5-10 min cooldown, then auto-reappears — re-arms only on the *first* steal after reappearing; cancelled cleanly on stream-offline; replays "already live" state on bot restart |
| 8 | Voice Swap reward | `rewards/VoiceSwap.cs:44-190` | channel-points redeem with text input | cross-user DB write (swaps two users' `UserTtsVoice` rows), **ephemeral in-memory state** (`ConcurrentDictionary` of pending swaps with expiry), self-fire-and-forget `Task.Run` timer that reverts after 5 min **only if the swap wasn't already superseded** (dedupe-by-partner-id check) |
| 9 | `!sus <user>` credibility score | `commands/Sus.cs:112-168` | command | multi-signal scoring: message count, command ratio, song-request count, first-seen recency, **+ random jitter**, clamped 0-100, then a random roll compared against the score to pick a response tier from 3 pools |
| 10 | `!stats` | `commands/Stats.cs:105-191` | command | 4 parallel DB aggregate queries, no-data branch, overlay event publish |
| 11 | `!todo add/list/done/remove` | `commands/Todo.cs:96-260` | command with subcommand arg | full CRUD over `Records`, per-user list numbering (position in filtered list ≠ DB id), target-user resolution via live Twitch API |
| 12 | `!voice` set/list/search | `commands/Voice.cs` (347 lines) | command with subcommand arg | multi-provider TTS voice catalogue, per-locale grouping, fuzzy/partial name matching against enabled providers only, per-user voice preference upsert |
| 13 | `!songrequest` | `commands/SongRequest.cs` (364 lines) | command | Spotify search, duplicate-in-queue detection, URL-vs-search-term parsing (`for (i = 0; i < urlParts.Length-1; i++)`), per-user request-count limits from DB, record persistence |
| 14 | `!ratio <user>` | `commands/Ratio.cs` (273 lines) | command | pulls stats for **two** users, tie/self/not-found branches, then buckets the delta into a pool-of-pools (outcome pool → random line within it) |
| 15 | `!quote` | `commands/Quote.cs` (219 lines) | command | random-skip over a DB count (`Random.Shared.Next(count)` then `.Skip(skip).Take(1)`) — index-based random row pick, not `ORDER BY random()` |
| 16 | `!auction`/`!telsell`/`!scam`/`!karen`/`!detective`/`!narrator`/`!dramatic`/`!stoneyai`/`!translate`/`!confess`/`!trial`/`!rigged`/`!banger`/`!weather`/`!whisper`/`!slow`/`!yell` | `commands/*.cs` (~20 files) | command | shared shape: generate 2-4 random numeric "stats" (`Random.Shared.Next(...)`), splice into one of a large random template pool, publish a `channel.chat.message.tts` widget event carrying the text — **the same generic shape repeated ~20 times by hand** |
| 17 | `!mock` | `commands/Mock.cs:53-148` | command | pulls target's **last chat message** from DB, then per-character alternating-case transform (`foreach (char c in input)`), composes with random intro |
| 18 | `!lurk` / `!unlurk` | `commands/Lurk.cs`, `Unlurk.cs` | command | single-row keyed `Storage` upsert (JSON blob keyed by broadcaster) holding a lurker id-set; already-lurking / not-lurking branches; TTS |
| 19 | `!wrongsong` | `commands/WrongSong.cs` | command | finds **most recent** `Record` of type song-request for that user, removes it, calls Spotify skip conditionally on whether it's still playing |
| 20 | `!leaderboard` | `commands/Leaderboard.cs:48-77` | command | groups `Records` by user, ranks, publishes an overlay show event with top-N |
| 21 | `!setpronoun` | `commands/SetPronoun.cs` | command | **raw parameterized SQL** (`ExecuteSqlRawAsync`) instead of EF, live Twitch user lookup, validates against an allow-list table |
| 22 | `!followage` | `commands/Followage.cs` | command | single Twitch Helix follow-relationship lookup, no-follow branch |
| 23 | `!banger`/`!banSong` playlist ops | `commands/Banger.cs`, `BanSong.cs` | command | cached-with-`??=` config lookup, Spotify playlist add/ban-list persistence, "already in playlist" branch |
| 24 | `!skip` (mod/self scoped) | `commands/Skip.cs` | command | permission-scoped: self can skip own request only, mod can skip any; looks up requester's queued `Record` rows |
| 25 | `!commands` | `commands/Commands.cs:17-41` | command | **introspects the live command registry itself** filtered by the caller's permission level — a meta-capability over whatever commands exist |
| 26 | `!help <command>` | `commands/Help.cs` | command | same registry introspection, per-command detail lookup |

**26 distinct behaviour families across ~63 files.** The ~20 "TTS bit" commands (row 16) are the
clearest existing evidence for **generic primitives already working** in the old bot — they differ
only in template pool + numeric ranges, i.e. they are *data*, not code, and should never have been
one file each.

---

## 2. Per-behaviour decomposition into generic primitives

### Fight/Hug (#1, #2) — classification branch + weighted text + TTS
- **Trigger:** `command` (+ needs "register a second trigger word for the same pipeline" — today's
  alias hack in `Fight.cs:214-247` re-registers a whole second `ChatCommand` from inside `Init`).
- **Conditions:** `var_compare equals` (self-name, bot-name checks) — already exists (§6.2).
- **Data lookup:** DB exists-check (`Users` table) — needs a **generic "record exists" condition/action**
  reading any entity by a templated key, not just the hardcoded ones current actions expose.
- **External call:** live Twitch user lookup with **try/catch-as-boolean** — needs `http_request`-style
  action (or a dedicated `twitch_user_lookup` action) whose success/failure sets a variable/branch,
  not throws.
- **Action:** `random_response` (exists) but needs **placeholder substitution beyond built-in template
  vars** (`{target}` computed from argument text, not a fixed context field) — i.e. `set_variable`
  from a rendered expression, then use in the template.
- **Action:** TTS dispatch (`send_message`+TTS is presumably a widget event today) — confirm a
  generic `speak`/`tts` action exists distinct from `send_message`.

### BSOD reward (#3) — the single richest behaviour in the corpus
- **Data lookup:** read an arbitrary widget's settings JSON, walk into a nested object, filter by
  a boolean flag → needs a **generic "read JSON field / JSON-path" primitive** usable inside a
  pipeline (`http_request`'s `ResultVariable` gives raw JSON in a variable, but there's no spec'd
  **JSON path extraction action/template function** to pull `settings.osConfig.win95.timings.glitch`
  back out).
- **Control flow:** pick random key from a filtered list → **weighted/filtered random over a list
  variable**, not just a static string pool (`random_response` only picks from a literal list).
- **Action:** string templating with **conditional rewrite rules based on computed length** — this is
  genuine string manipulation (regex replace, numeric-threshold branching) that today only exists as
  hand C#. Needs **template string functions**: length(), regex-replace, numeric threshold branch —
  or an escape hatch (`run_code`) that's explicitly allowed for exactly this class of logic.
- **Action:** TTS synth with **computed duration returned into a variable**, then arithmetic sum of
  several JSON-sourced timing numbers to get a delay — needs actions whose `Output`/`ResultVariable`
  feeds into a `wait` block's duration (**templated wait duration**, not just literal seconds).
- **External orchestration:** pause Spotify → wait (computed) → resume Spotify — this is 3 actions
  in sequence, already expressible with `wait` + provider actions, **if** Spotify pause/resume exist
  as generic actions (today only `song_*` exist per §6.1 — pause/resume of the underlying player is
  not itself listed).
- **Refund-on-failure:** every branch needs "if this step fails, run a refund action and stop" — a
  **per-block error handler** (try/catch equivalent), not just linear steps.

### Raid (#4) — the most timing-sensitive behaviour
- **Wall-clock alignment:** the old code stores `DateTime raidApiCompletedAt` and computes
  `remaining = (apiCompletedAt + 90s) - now` before its final wait — this is **not** "wait N seconds";
  it's "wait until absolute time T, where T was computed from an earlier action's completion time."
  Needs a **`wait_until (timestamp expression)` primitive**, or a documented pattern of
  `set_variable` capturing `{{now}}` + arithmetic in a later `wait` expression.
- **Multi-stage countdown loop with skip-ahead:** `RunCountdown` iterates a fixed marker array,
  skips markers past the current remaining time, and only announces at ≤15s — this is a
  **`for-each` over a literal list with per-iteration conditional action + computed wait
  between iterations**. The tree spec's `loop`/`foreach` (D3) covers the shape; the missing
  piece is a loop whose body computes **per-iteration wait duration from loop state**
  (`{{loop.item}} - previousItem`), i.e. arithmetic in template expressions.
- **Parallel/non-blocking action:** `_ = SwitchToEndingScene(ctx)` — fire-and-forget so a slow OBS
  connect never blocks the chat countdown. The tree model is depth-first sequential; needs an
  explicit **"detached step" / async-fire action modifier** (distinct from `run_pipeline detached`,
  which is for whole sub-pipelines, not a single inline action) or acceptance that OBS scene-switch
  becomes its own detached sub-pipeline call.
- **Error-shape matching:** catches "already raiding" by substring-matching the exception message —
  a **known bug class**: the new system must give provider actions **typed failure reasons**
  (`ActionResult.FailureCode`), never force a pipeline author to string-match error text.
- **Multi-provider orchestration in one flow:** Twitch (raid), OBS (scene + stop stream), Spotify
  (pause) — all three must exist as generic actions with **typed success/failure**, callable in
  sequence with conditions on each other's outcome.

### Lucky Feather trio (#5, #6, #7) — the clearest case for "trigger kinds the new system doesn't have yet"
- **Trigger kind: reward-configuration-change.** `LuckyFeatherChange.cs` responds to `OnEnabled`,
  `OnDisabled`, `OnPauseStatusChanged`, `OnResumeStatusChanged` — these are **not** redemption
  events, they're **"this reward's admin config changed" webhooks**. The new trigger catalogue
  needs a **reward-lifecycle trigger** (paused/resumed/enabled/disabled) distinct from
  "redeemed", firing its own pipeline with the reward id + new state as trigger variables.
- **Trigger kind: stream online/offline** (already presumably exists as an EventSub trigger) driving
  a **stateful multi-phase timer** (hold → hide → cooldown → reappear) that:
  - only arms on the *first* qualifying event after a reset (**"latch" semantics** — a condition like
    `if not already running, start` — expressible today via a **persistent per-broadcaster flag
    variable** checked before starting a `wait`, but the pattern isn't documented as a first-class
    primitive: **"debounce/latch — only the first trigger in a window starts an action" needs a
    named primitive** (e.g. a `set_counter`/flag-guarded sub-pipeline call), not bespoke C# state.
  - This is exactly what §10's `ScheduledPipelineTask` (deferred one-shot with `DedupeKey`) already
    covers **for a single-hop delay**, but the feather cycle is **two chained delays with different
    side effects at each hop** (hide at hold-end, reappear at cooldown-end) — needs either two
    chained `schedule_pipeline` calls (hide-pipeline schedules the reappear-pipeline) or a
    `loop`+`wait` block inside one long-lived pipeline run that IS the wait-for/resume-later model
    the owner already flagged as needed.
- **Action: live-mutate a Twitch reward's price/title/prompt.** Not in §6.1's action table at all —
  needs a generic **`update_reward` action** (title/cost/prompt/paused, templated) — currently only
  exists as raw `TwitchApiService.UpdateCustomReward` calls inside hand C#.
- **Randomized template with 2+ distinct named placeholders** (`{name}`, `{name2}`) — confirms
  `random_response`/template rendering needs **multiple independent placeholder names per call**,
  not just the fixed context vars, i.e. arbitrary `set_variable` results substitutable into a chosen
  random string.

### Voice Swap (#8) — ephemeral timed reciprocal state
- Two users' data mutated together, auto-reverted after a fixed delay **unless super­seded** —
  this maps directly onto §10's `ScheduledPipelineTask` with `DedupeKey` (exactly the design note
  in the aitm rule already says "the missing tooling behind timed follow-ups (a Voice-Swap auto-revert...)").
  **Already-solved by spec, not implemented in old bot** — flag as: confirm `schedule_pipeline` action
  is wired to a reward's `Callback`/pipeline flow, and that "supersede a pending scheduled task by
  dedupe key" is exercised by an actual authored pipeline, not just the entity design.

### Sus / Ratio / SongHistory (#9, #14) — scored pools-of-pools
- **Arithmetic on multiple DB aggregates + a random jitter, clamped, then compared against a second
  random roll to pick a response TIER, then a random line within that tier.** This is **two levels
  of "generic weighted/random pick"** stacked: (a) compute a numeric score from several `adjust_counter`/
  DB-lookup-style values via **template arithmetic**, (b) bucket that score into ranges — needs
  **numeric range/bucket conditions** (`gt`/`lt`/`gte`/`lte` chains already exist per §6.2, so this
  is expressible via nested `if`/`switch` on a computed variable), (c) pick random text within the
  matched bucket (`random_response`, exists). **Gap:** template arithmetic (`+`, `min`, `clamp`)
  across multiple resolved variables — today's template resolver is presumably lookup-only.

### Todo / Voice / SongRequest (#11, #12, #13) — subcommand routing + CRUD
- **Subcommand dispatch** (`!todo add|list|done|remove`) — one trigger, first-argument-driven
  `switch` (exists, D2) on `{{args.1}}`.
- **Full CRUD over a generic per-user record store** — the closest existing primitive is
  `NamedCounters`/`ViewerDatum` (G.4/G.14 per commands-pipelines.md) which are single
  scalar values; the old bot needs **per-user LISTS of structured records** (todo items with
  position/id, TTS voice history, song-request history) — this is a genuine gap: **no generic
  "per-user/per-channel list-of-records" data primitive** exists yet (add/list/update-by-index/
  remove-by-index), only scalar counters and single JSON blobs (`Storage`).
- **Fuzzy/partial string matching against a filtered catalogue** (Voice.cs) — needs a
  **`matches`/`contains`-driven lookup action** over a *list* variable (not just a single
  string compare), i.e. "find first item in list where field contains X".

### The ~20 "TTS bit" commands (#16) — the strongest argument FOR the generic system
Auction/TelSell/Scam/Karen/Detective/Narrator/Dramatic/StoneyAi/Translate/Confess/Trial/Rigged/
Banger-text/Weather/Whisper/Slow/Yell all reduce to:
1. Generate 1-4 random numbers in author-chosen ranges → `set_variable` with a `random.number:min:max`
   template function (already exists per CLAUDE.md's template-variable list).
2. Optionally look up a target user (DB or live Twitch) → same primitive as Fight/Hug.
3. Pick one random line from an author-authored pool, substitute variables → `random_response`
   (exists).
4. Publish a `channel.chat.message.tts` widget event → today's `send_message`+TTS action, or a
   dedicated `speak` action — **confirm one canonical action does this**, because the old bot
   hand-rolls the `IWidgetEventService.PublishEventAsync("channel.chat.message.tts", ...)` call
   in **every single file** — this is the textbook "generic primitive vs. bespoke feature" case:
   one `speak_as_bit`/`tts_flavor_text` action, reused with different data (numeric ranges +
   template pool), should replace all ~20 files.

### Mock (#17) — per-character text transform
- Needs a **template string function for character-level transform** (alternating case) — small,
  but confirms template functions must go beyond substitution into **string-mutation helpers**
  (`upper`, `lower`, `alternate-case`, `reverse`, `truncate`).

### Lurk/Unlurk (#18) — single-row set membership
- `Storage` (id-set JSON blob keyed by broadcaster) — this is the **"per-channel set/list" gap**
  again, same root cause as Todo.

### `!commands` / `!help` (#25, #26) — introspection over the live registry
- These need the pipeline system itself to expose a **read-only "list triggers I can currently fire,
  filtered by caller's role" data source** usable inside a pipeline (a virtual variable/lookup, not
  a DB table) — a generic **"introspect the command catalogue"** action/variable, not a bespoke command.

---

## 3. Gap list — closed vs open (2026-09-30)

Checked against the code on 2026-09-30. Numbers refer to the original 22-item gap list (the original text was
removed; `spec/pipeline-tree-and-editor.md` §3 maps each number to its primitive).

**Closed (built):**
- #1 boolean condition tree — `PipelineStepCondition` is a self-referencing tree.
- #2 wait-for-event / resume-later — `wait_for_event` + persisted `PipelineRunState`.
- #3 `for-each` over a list variable — `loop` mode `foreach` (comma-separated list; JSON arrays are open, see #5).
- #10 reward-lifecycle trigger — EventSub `channel.channel_points_custom_reward.add/update/remove`.
- #11 debounce/latch — closed by composition (`adjust_counter` + `if`), no new block.
- #13 per-block error handling — `try` block.
- #14 detached single action — `detached_step` block.
- #16 templated wait duration — `wait` resolves a template string.
- #17 several triggers per pipeline — `PipelineTrigger`.
- #18 several named placeholders — `set_variable` values feed any template.
- #21 deferred one-shot pipeline — `schedule_pipeline`.
- #22 tree shape — `if`/`switch`/`loop`/`random_branch`/`run_pipeline`.

**Partly closed:**
- #4 random pick — `pick_from_list` and `random.pick.*` ship; a weighted/filtered pick over a runtime list
  variable does not.
- #15 string functions — `{transform.upper|lower|title|spaced|alternating|reverse|trim|truncate.N:text}` ship;
  `length` and `regex_replace` do not.

**Open (the real remaining gaps):**
- #5 JSON-path extraction from a variable (no `json.*` template function; `foreach` cannot walk a JSON array).
- #6 template arithmetic (no `expr` function: `+ - * / min max clamp`).
- #7 `wait_until <absolute timestamp>` (only the raid-specific `wait_until_raid_fires` exists).
- #8 per-user / per-channel list-of-records primitive (`record_add` / `record_list` / `record_update` /
  `record_remove`; no `ViewerRecordList`).
- #9 generic `update_reward` action (`ITwitchChannelPointsApi.UpdateCustomReward` exists, but no pipeline action
  calls it).
- #12 typed action failure reasons (`ActionResult` has no `FailureCode`; authors can only read `ErrorMessage`).
- #19 fuzzy/contains lookup over a list variable (`list_find`).
- #20 read-only command-catalogue introspection (`commands.list` / `commands.describe`).

---

## 4. Cannot-be-generic-as-currently-scoped — and the primitive that fixes each

1. **BSOD's SSML template rewrite by regex on speech-rate strings** (`Bsod.cs:437-488`) is
   OS-template-specific string surgery that doesn't reduce to a single reusable action as written.
   **Fix:** don't generalize the regex — generalize the *shape*: a `speak (SSML template, voice,
   rate-adjust-by-length rule)` action where "rate-adjust-by-length" is a declared numeric-threshold
   table (length→rate/break-scale pairs) the author configures per pipeline, not hand C#. This turns
   a one-off script into a reusable "TTS with length-adaptive pacing" action any streamer could use
   for any long-form TTS bit, satisfying the generic-primitives rule instead of special-casing BSOD.
2. **Raid's Twitch-server-timer alignment (90s fixed, non-configurable, no early-commit API)**
   (`Raid.cs:41-56`) is inherently bound to a specific external API's fixed behavior — no generic
   primitive changes that Twitch fact. **Fix:** the primitive to add is generic (`wait_until` +
   typed action outcomes, gap #7/#12); the 90-second constant itself is legitimately pipeline
   **configuration data** (an author-set variable), not a platform capability gap.
3. **Lucky Feather's two-hop chained timer (hold→hide, then cooldown→reappear) with different
   Twitch-reward + overlay side effects at each hop** cannot be one `schedule_pipeline` call as
   spec'd today (§10 is single-hop). **Fix:** either (a) allow a scheduled pipeline's own body to
   itself call `schedule_pipeline` for the next hop (chaining — already technically possible with
   §10 as designed, just needs confirming in tests/docs), or (b) implement the owner's wait-for-
   event/resume-later persisted-run model (gap #2) so one pipeline run holds both waits — the latter
   is the more general fix and is already on the owner's roadmap.
4. **VoiceSwap's `ConcurrentDictionary`-backed in-process ephemeral state, lost on process restart**
   (`VoiceSwap.cs:29,136-140`) is itself a **bug class**, not a feature to preserve. **Fix:** replace
   with §10's durable `ScheduledPipelineTask` (survives restart) — this is a straight bug fix, listed
   here because "keep exact old behavior" would be wrong; the generic primitive (durable scheduling)
   is strictly better and already spec'd.
5. **`!setpronoun`'s raw parameterized SQL bypassing EF** (`SetPronoun.cs:41,67`) is an
   implementation shortcut, not a behavior requirement — nothing about pronoun-setting needs raw SQL;
   it needs a **generic "update one field of the current user's profile row, validated against an
   allow-list table" action**, which is just a `set_viewer_data`-shaped write (already spec'd as
   `set_viewer_data`/`adjust_viewer_data`, G.14) plus a **validate-against-lookup-table condition**
   (gap-adjacent to #19's fuzzy lookup, but exact-match against an allowed-values table).

---

## 5. Old-bot bugs that are now new-system requirements

- **Raid: string-matched exception text for "already raiding"** (`Raid.cs:201`) → gap #12 (typed
  failure reasons) is a direct fix, not a nice-to-have.
- **VoiceSwap: in-memory `ConcurrentDictionary` state lost on restart** (`VoiceSwap.cs:29`) → gap #21/
  can't-be-generic #4 (durable scheduling, already spec'd via §10) fixes this by construction.
- **Fight.cs's `Init()`-time secondary `RegisterCommand` call wrapped in a bare `catch {}`**
  (`Fight.cs:212-247`) — a failed alias registration is silently swallowed; confirms the new system's
  "N triggers per pipeline" (owner decision) must be a first-class multi-trigger list, not a
  runtime side-registration an author can get wrong per-command.
- **BSOD's `TtsService`/`SpotifyApiService` resolved via `(Type)ctx.ServiceProvider.GetService(typeof(X))`
  with no null-check before use** (`Bsod.cs:390-393`) — a missing/disabled Spotify integration would
  NRE mid-reward after the user already paid points and got a refund-less partial execution. Confirms
  gap #13 (per-block error handling / refund-on-failure) must wrap the **entire** action sequence,
  not just the try/catch the original author remembered to add around the outer body.
