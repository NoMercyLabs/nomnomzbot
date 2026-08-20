# Widget usability/reliability/configurability audit — scope and plan

Full audit of all 21 first-party overlay widgets, the widget code editor's live-preview
system, the drop-game mechanic, the chat overlay, and pipeline/command/event-response
authoring ergonomics (template helpers + chain-building UX). Six parallel investigation
lanes, all findings below are file:line evidenced. Nothing in this doc is fixed yet —
investigation and scope only, per request.

## 1. THE systemic bug — widget JS reads field names the backend doesn't send

This is the single highest-value fix in the whole audit: it silently breaks **6 of the
21 widgets** and degrades a 7th, all from the same root cause, confirmed independently
by two separate audit lanes on two separate widget batches.

**Root cause:** widget `.vue` scripts read event payload fields under names that don't
match the real, camelCase-serialized DTOs the backend actually broadcasts
(`AlertDtos.cs` et al. — confirmed the widgets receive the *same decorated DTO the
dashboard gets*, not a hand-rolled minimal shape). Every failure follows the identical
defensive-coding pattern: `(d && d.wrongFieldName) || fallback` — so `undefined` gets
silently swallowed into an empty string or `1`, never a console error, never a crash.
The widget just permanently shows its empty/placeholder state against real live events.

**Broken (functionally dead against live traffic):**
- `recent_followers.vue:15` — reads `d.user`; real field is `displayName`
  (`FollowAlertDto`, `AlertDtos.cs:33-41`). Never populates, forever shows "Waiting for
  the first follow…"
- `top_cheerers.vue:19-21` — reads `d.user`/`d.amount`; real fields are `displayName`/
  `bits` (`CheerAlertDto`, `AlertDtos.cs:62-68`). Leaderboard never accumulates.
- `sub_train.vue:28` — reads `d.amount` on gift events; real field is `count`
  (`GiftSubAlertDto`, `AlertDtos.cs:54-60`). A 50-sub gift bomb only increments the
  train by 1 instead of 50 — directly defeats the feature's own stated design intent
  ("a gift of N counts as N", the widget's own code comment).
- `goal_bar.vue` — worse than a field mismatch: it and `labels.vue` both subscribe to a
  `"goal"` widget event that **no server component ever broadcasts at all**.
  `GoalBeganEvent`/`GoalProgressEvent`/`GoalEndedEvent` translate from EventSub into
  domain events but nothing routes them to `IWidgetNotifier`/`OverlayHub` — the
  "authoritative goal value" code path in both widgets is entirely dead. `goal_bar.vue`
  is broken outright; `labels.vue`'s `latest_follower`/`latest_sub`/`top_cheerer` stat
  modes (3 of its 5 configurable modes) are non-functional for the same reason plus the
  same field-mismatch bug.

**Needs polish (partially affected — some event types work, most don't):**
- `alerts.vue`, `event_ticker.vue` — read a generic `{user, amount, viewers, months}`
  shape that doesn't match real records (`displayName`, `bits`, `count`, `viewerCount`,
  `fromDisplayName`, etc.). Every alert type except resub silently shows placeholder
  text ("Someone", "0") on live events.

**Fix:** one pass across `AlertDtos.cs`'s real field names vs. every widget's event
handler — this is a single, well-scoped, mechanical correction (rename the read side to
match the send side), not 7 separate investigations. Given the pattern is proven
identical everywhere it appears, the fix should also add a lightweight contract test
(deserialize a real `AlertDtos.cs` record, feed it through each widget's expected shape)
so this class of bug can't silently recur — nothing today catches a field-name
mismatch between backend and widget script; it's Vue Composition API string-keyed
object access with no compile-time link between the two sides.

**Confirmed independently by a second pass over the same widgets** (cross-checking a
different subset against the real DTOs) — same root cause, same verdicts on
`recent_followers.vue`/`sub_train.vue`/`top_cheerers.vue`, plus two more distinct bugs
in this family worth calling out on their own:
- **`redemption_alert.vue`'s `sound` config field is completely inert.** The schema
  exposes it, the Vue reads it into `cfg.sound`, but nothing in the file ever calls a
  playback API — no widget anywhere in the library has an SDK method for triggering
  audio (that only exists as a separate host-level `PlaySoundAction` pipeline action).
  A broadcaster who sets a sound on this widget's settings panel gets silence,
  guaranteed. Write-only, misleading configurability — either wire real playback or
  remove the field so it stops promising something it can't do.
- **`socials.vue`'s own schema help text tells broadcasters the wrong field name.**
  The schema hint (`WidgetSettingsSchemaProvider.cs:356-362`) documents handles as
  `{label, url}` objects, but `normalize()` in the Vue reads `h.handle`, not `h.url`,
  and drops any entry with an empty `handle` — so a broadcaster who follows the
  field's own documented format gets a permanently empty rotation. This is the same
  failure mode as F1's DTO drift, but the mismatch is between the UI's own help text
  and its own parsing code, not between backend and frontend.

## 2. Chat overlay (`chat_box.vue`)

**Confirms the owner's exact report:** no line break between username and message body
anywhere in the markup/CSS — `.head` (badges/name) and `.body` (message) are both
inline elements glued together (`chat_box.vue:174-206`, `251-256`). Fix is a `display:
block` (or an explicit line break) between the two.

**Further issues found in the same file, same sweep:**
- No long-username truncation/ellipsis (`.name`, lines 261-264) — can stretch/overflow
  the head row.
- No avatar image rendered at all (`ChatLine` interface, lines 75-85 has no
  `avatarUrl`) — the dashboard's own live chat view (`ChatScreen.kt:414-420`) DOES
  render one; the widget doesn't.
- Chat-color-derived username text has zero contrast adjustment against the overlay's
  dark/light/transparent theme (line 179) — a dark color on the dark theme, or light on
  light, can go illegible. (Confirmed this same gap exists in the dashboard's chat view
  too — `ChatScreen.kt:348` — so it's systemic, not "fixed in one place, stale in the
  other".)
- Emotes/cheermotes are fixed-px sized (lines 276-288), not relative to the
  configurable font size — bumping font size for readability leaves emotes visually
  undersized.
- Timestamp shares the same crowded inline row that lacks the username/message break.
- No arrival animation for new messages — only a fade-OUT-and-remove exists (driven by
  `fadeAfterMs`), new lines pop in instantly with no fade/slide-in.
- Mentions have no visual highlight (background/chip) unless the mentioned user
  happens to have a known chat color — otherwise just plain bold text.

**"Stacked transition" animated overlay style (the owner's second ask) — confirmed
net-new, not a wire-up.** Grepped all 21 widget `.vue` files for any `<Transition>`/
`<TransitionGroup>` Vue usage: **zero matches anywhere in the widget library.** No
animation-config schema field precedent exists either. Building an opt-in "stacked,
animated" chat overlay style is genuinely from-scratch plumbing — there is no existing
animation infrastructure anywhere in this codebase's widget system to extend. (The
referenced legacy-bot design at `C:\Projects\StoneyEagle\nomercy-bot` is outside this
sandbox's readable paths — flagged as inaccessible rather than guessed at.)

## 3. Code editor's live-preview "test buttons" — confirmed root cause

The owner's complaint is precisely located: `ProjectEditor.wasmJs.kt:513-548`
(`refreshFireBar`) discovers a widget's subscribed event names via regex over its own
source, and renders one test button per event — but clicking a button always sends
`postMessage({ __nnzFire: { type: ev, data: {} } }, '*')`. **Line 542 — `data: {}`,
always empty, for every event type.** So a chat-box widget's handler fires but gets no
username/text/emotes/tier — nothing to render, regardless of which event is tested.

Two clarifying findings:
- The **compiled-output side of the preview is NOT the bug** — `buildEsbuildPreview`
  genuinely re-bundles current editor content live on every edit (debounced 500ms) and
  re-renders in a sandboxed iframe. The preview pane itself is accurate; only the
  *test-fire payload* is empty.
- A richer, per-event-type sample generator **already exists on the backend** —
  `WidgetTestEventController.cs` / `WidgetTestSamples.For(eventType)` — including a
  realistic `"ChatMessage"` sample with fragments/emotes/badges/pronouns/color. But it's
  built for a different flow ("test this overlay live in OBS") and is **completely
  disconnected from the editor's fire bar** — zero references to it anywhere in the
  widget-editor frontend code.
- Even that backend sample set has only ONE fixed chat-message sample, not the "all
  types we support" variety the owner wants (sub/mod/vip tiers, reply/mention,
  bits-attached, action/`/me`, multi-platform). That variety doesn't exist anywhere as
  reusable fixture data today — would need building.

**Fix is two-part:** (a) wire the editor's fire bar to send a real per-event-type stub
payload — reuse/port `WidgetTestSamples`' catalogue rather than the current empty
object; (b) for chat specifically, offer multiple named variants ("bombard" with the
full range of message types) rather than one fixed sample.

## 4. Drop game — confirmed mislabeled and mismechanized, not a rename fix

**Current mechanic (`DropGame.cs`):** both the target (0-100) AND the player's landing
position are pure `Random.NextDouble()` rolls — the player does not aim, drag, or time
anything. Landing within `win_radius` (default 10) of the target wins a flat payout
multiplier. This is a coin-flip gambling game with a landing-strip visual, not a
skill-based landing mechanic, and not Twitch's actual "Drops" entitlement feature
either (confirmed: no `EntitlementGrant`/Drops-API integration exists anywhere in the
codebase — the naming collision is purely coincidental branding, not a mis-wired
integration).

**What's reusable toward the requested mechanic** (parachute landing on a target,
scored continuously 0-100 by proximity, tracked per-stream and all-time):
- The widget (`drop_game.vue`) already renders a track/win-zone/marker visual — just a
  plain colored dot, not a parachute/avatar sprite, and shows only win/lose + payout,
  not a numeric proximity score (even though the server already computes `distance` and
  passes it in the frame data — it's just not surfaced).
- `GamePlay` (append-only per-play ledger) already stores one row per play forever —
  exactly what a score-history feature needs, just missing a dedicated
  distance/score column today.
- `LeaderboardConfig`/`LeaderboardSnapshot`/`LeaderboardOptOut` already implement
  periodic, per-channel ranking — currently driven off currency values, structurally
  reusable for a "landing accuracy" leaderboard type.
- `EconomyStreamWindow.CurrentStreamStartAsync` already gives the exact "this stream
  vs. all-time" scoping pattern other economy features use — directly reusable for
  per-stream drop-game scoring.

**Not reusable, needs building from scratch:** an actual skill/input-driven landing
mechanic (or at minimum a continuous 0-100 accuracy score replacing the binary
hit/miss), a parachute/avatar sprite replacing the dot marker, and either a new
leaderboard type or dedicated aggregation (current `LeaderboardSnapshot.Value` is
currency-shaped, not accuracy-shaped).

## 5. Full 21-widget verdict table

| Widget | Verdict | Core issue |
|---|---|---|
| alerts.vue | needs polish | field-name mismatch (§1) — wrong content on all but resub |
| chat_box.vue | needs polish | username/message run-together (§2) + 7 more layout gaps |
| countdown_timer.vue | solid | minor — no completion transition |
| crash.vue | solid | payload matches engine exactly; schema under-covers color/position |
| custom_data.vue | solid | best-practice example in the batch — proper idle placeholder |
| drop_game.vue | **wrong mechanic** | pure RNG, not skill-based (§4) — needs redesign |
| emote_wall.vue | needs polish | binds to a chat-fragment shape not confirmed populated |
| event_ticker.vue | needs polish | same field-name mismatch as alerts (§1) |
| goal_bar.vue | **broken** | subscribes to an event the server never emits (§1) |
| heist.vue | solid | payload matches engine exactly; same schema gap as crash |
| labels.vue | needs polish | 3 of 5 stat modes non-functional (§1) |
| now_playing.vue | solid | extra fields degrade gracefully, honestly flagged in code |
| poll_prediction.vue | solid | all 7 lifecycle events wired, full config coverage |
| raffle.vue | needs polish | minor — blank-winner text on a degenerate empty round |
| recent_followers.vue | **broken** | field-name mismatch (§1) — never populates |
| redemption_alert.vue | needs polish | `sound` config field is inert — never plays audio |
| socials.vue | needs polish | schema help text (`label+url`) contradicts actual parsing (`label+handle`) — empty rotation for anyone who follows the docs |
| spotify_player.vue | solid | only widget ignoring the shared accent-color theme (hardcoded red) |
| sr_queue.vue | solid | exact field match where checked; note — no automatic backend DTO yet, currently pipeline-action-only |
| sub_train.vue | **broken** | undercounts gift bombs — 50-sub gift only counts as 1 (§1) |
| top_cheerers.vue | **broken** | field-name mismatch (§1) — leaderboard never accumulates |
| tts_caption.vue | solid | no automatic backend DTO yet (pipeline-only); one nit — speaker label shows a raw numeric Twitch ID, not a display name |

**Net: 5 broken, 8 needs-polish, 8 solid** (plus the drop game as its own category —
"working as coded, but coded as the wrong feature"). 6 of the 13 non-solid widgets
share the single §1 root cause; the other 2 needs-polish items (`redemption_alert`
sound, `socials` schema-text mismatch) are distinct bugs in the same family — a config
field that promises something it doesn't deliver.

## 6. Pipeline/command/event-response authoring ergonomics

**Sharpest finding: a full dry-run mechanism already exists on the backend and is
completely unwired on the frontend.** `POST /pipelines/{id}/test-run`
(`PipelinesController.cs:170-182`) executes conditions/variable math/pick-list draws
live but *captures* every side-effecting action (chat, TTS, widgets, moderation,
economy, rewards, schedules, run_code) instead of performing it — exactly the
"simulate without going live" mechanism the owner's ask ("testing/validating a chain
before it goes live") wants. `PipelinesApi.kt` has zero references to it. No screen
(Pipelines, Commands, Event Responses, Timers) has a test-fire/dry-run button anywhere
in the dashboard. This is the highest-value, lowest-effort fix in this whole section —
pure frontend wiring, the backend work is already shipped.

**Branching is schema-ready but editor-blind.** `PipelineStep.ParentStepId`/`Branch`
("then"/"else"/null) fully supports nested conditional branches at the domain level,
but the step-authoring dialog (`PipelinesScreen.kt`, `StepFormDialog`) only exposes a
single condition + a `stopOnMatch` boolean on a flat step list — no way to nest a step
under a parent's then/else lane. A broadcaster wanting real branching today can only
fake it with duplicated inverse conditions across flat rows.

**No variable-picker/autocomplete exists anywhere.** Every message/text field in the
command/event-response/timer editors is a bare text field — a broadcaster must know the
exact `{{namespace.key}}` syntax from memory. Confirmed zero picker/chip/dropdown
component exists in the whole design system for this.

**Template helper catalogue gaps** (full current catalogue enumerated in the source
audit — `user.*`, `channel.*`, `stream.*`, `random.*`, pronoun-grammar, pick-lists,
custom-data, counters): no math namespace beyond `random.number.<N>`, no string
manipulation (upper/lower/truncate/pluralize), no custom date/time formatting beyond 3
fixed strings, no inline conditional/ternary helper, and no way to reference any
pipeline step's output except the immediately-preceding one (`last.output` only — a
5-step chain can't see step 2's output from step 5). The spec
(`commands-pipelines.md` §6.3) originally called for one general parameterized
`{{namespace.key:arg1:arg2}}` grammar; what shipped is ad hoc per-feature regex
parsing — every new "helper with an argument" needs its own bespoke code, not a
generic mechanism.

**Dead variable found:** `{{stream.viewers}}` is listed as resolvable (matches the
"needed" check) but the value is never actually set — a silent dead template key.

**Not a problem:** permission-level authoring already surfaces role NAMES only (not raw
numeric levels), correctly following the project's own hard rule — the audit lane
checked for this specific risk and it did not materialize. Single-step authoring
friction (add a step, pick action, configure, save) is reasonable; the pain is
specifically in multi-step/branching chains and blind template authoring.

## 7. Single-file bloat — which widgets need splitting into components

The compile pipeline **already supports** multi-file widgets (`App.vue` + child
components + composables) — `EsbuildWidgetBuildService.BuildVueAsync` compiles every
`.vue` file in a project independently and bundles them with real relative-import
resolution; `WidgetVersion.FilesJson` already stores a `path → content` map. This is
proven, working machinery, not something to build. The gap is narrower than "widgets
can't be split": the 21 **first-party** widgets specifically bypass it —
`FirstPartyWidgetCatalogueSeeder` embeds each as one asset into
`WidgetGalleryItem.SourceCode`, a single `string?` column with no `FilesJson`/
`ManifestJson` equivalent. Splitting a first-party widget into components needs one
small, well-scoped addition (a file-set source column on `WidgetGalleryItem`, and the
seeder embedding a file set instead of one asset) — not a rebuild of anything.

**Real size ranking** (all 21, by line count) — the largest are, in order:
`now_playing.vue` (360), `chat_box.vue` (311), `poll_prediction.vue` (226),
`alerts.vue` (195), `drop_game.vue` (194), `crash.vue` (181), `goal_bar.vue`/
`custom_data.vue` (167), `event_ticker.vue`/`redemption_alert.vue` (164). Everything
from `emote_wall.vue` (155) down to `recent_followers.vue` (82) is under 160 lines.

**Concrete split proposals** (named pieces, not generic "break it up" advice):

- **`now_playing.vue`** — two genuinely separate concerns: the visual card/pill/iframe
  rendering vs. a full Spotify Web Playback SDK device (~120 lines: token fetch, SDK
  loader, player lifecycle, autoplay probe). Split into `NowPlayingCard.vue`
  (presentational) + a `useSpotifyConnectDevice.ts` composable — the composable becomes
  independently unit-testable (mock `fetch`/`window.Spotify`) without mounting Vue at
  all, which it currently isn't.
- **`chat_box.vue`** — the template interleaves 5 structurally distinct fragment
  renderers (html/emote/cheermote/mention/link/plain) inside one `v-for`. Extract
  `ChatFragment.vue` (one fragment → one visual unit) so the parent shrinks to line-list
  + settings/event wiring, and each fragment type becomes independently testable.
- **`poll_prediction.vue`** — poll and prediction share one visual shape (title + bars
  + won-highlight) driven by two disjoint event families. Extract a generic
  `useRoundState.ts` (show/scheduleHide/locked/ended state machine, parameterized) and
  a presentational `RoundBars.vue` — reusable by anything that renders a ranked bar
  list, not just these two.
- **`alerts.vue` + `redemption_alert.vue`** — confirmed **near-duplicates**: identical
  queue/current/visible/cardKey/timer state, a byte-for-byte identical `showNext()`
  timing function (enter → hold `durationMs` → 400ms exit fade), and an identical
  `.card` CSS block (same easing curve, same shadow formula) in both files. Strongest
  candidate in the whole library for a shared `useAlertQueue<T>(durationMs)` composable
  plus a shared `AlertCard.vue` presentational shell — both widgets would shrink to
  60-80 lines of pure event-mapping logic each.
- **`drop_game.vue` + `crash.vue`** — same "game round" pattern (phase state, reset,
  kind-keyed frame dispatch, scheduleHide) and an identical results-board CSS block.
  Candidate: `useGameRound.ts` + a shared `GameResultsBoard.vue`. Check `heist.vue`
  against the same shape before finalizing the composable's API (Rule of Three — a
  third confirmed occurrence should shape the interface, not just the first two).

**Confirmed cross-widget duplication independent of any single file's size** — real
drift risk, not just bloat: `alerts.vue` and `event_ticker.vue` each hardcode their own
identical copy of the 10-type event enumeration (`ALL_EVENTS`) and an identical
`money()` formatting helper — if a new event type is ever added, both files need
editing in lockstep with nothing tying them together.

**Fine as single-file, no split warranted** (avoid over-engineering small widgets):
`recent_followers.vue`, `socials.vue`, `sub_train.vue`, `top_cheerers.vue`,
`labels.vue`, `heist.vue`, `countdown_timer.vue`, `raffle.vue`, `sr_queue.vue` — all
under ~140 lines, one visual concept, one event source each. Splitting these would add
import/prop-plumbing overhead with no reuse value.

## 8. Remediation plan, in priority order

1. **§1 systemic field-name fix** — highest value, most mechanical, fixes 6 widgets in
   one pass; add a contract test so it can't silently recur.
2. **§6 wire the existing `test-run` dry-run endpoint into the dashboard** — backend is
   already built; this is pure frontend work with an outsized payoff across commands,
   event-responses, timers, and pipelines all at once.
3. **§3 code-editor fire-bar stub data** — port `WidgetTestSamples` into the editor's
   test buttons; extend chat specifically to cycle through message-type variants.
4. **§2 chat_box.vue layout fixes** — username/message line-break (the originally
   reported bug) plus the other layout issues found in the same sweep (truncation,
   avatar, contrast, emote sizing, arrival animation, mention highlight) — batch these,
   they're all in one file.
5. **§4 drop game redesign** — the largest single item; needs a product decision on the
   actual skill mechanic (what does "landing" input from a viewer look like?) before
   implementation can start. Reusable pieces (leaderboard, ledger, stream-window
   scoping, existing track/target visual) cut the build cost significantly.
6. **§6 branching UI** — expose `ParentStepId`/`Branch` in the step dialog; schema is
   ready, this is Compose UI work.
7. **§6 variable picker** — net-new design-system component; unblocks confident
   template authoring across every message field in the product.
8. **§2 "stacked transition" animated chat overlay style** — confirmed from-scratch
   (no animation infra exists anywhere); lowest priority of the chat-overlay items
   since it's additive/opt-in rather than fixing something broken, and needs the
   owner's liked reference design described more concretely (legacy-bot repo wasn't
   accessible to compare against).
9. **§6 template helper expansion** — math/string/date-format namespaces, and ideally
   the general parameterized-argument mechanism the original spec called for, so future
   helpers don't each need bespoke regex.
10. **§5 remaining per-widget polish items** — the "needs polish"/"solid-but" nits
    (spotify_player's off-theme error color, tts_caption's numeric-ID speaker label,
    raffle's blank-winner edge case, emote_wall's unconfirmed fragment shape) — lowest
    urgency, batch opportunistically.
11. **§7 component splitting** — do this AFTER §1 (the field-name fixes), since
    `alerts.vue`/`redemption_alert.vue` and `drop_game.vue`/`crash.vue` are both on the
    split list and on the field-mismatch/redesign list respectively; splitting first
    would mean re-touching the same extracted files again right after. Needs the small
    `WidgetGalleryItem` file-set storage addition first (the seeder/storage gap, not
    the compile pipeline — that part already works), then: `now_playing.vue` and
    `chat_box.vue` splits (highest line-count payoff, no cross-widget dependency), then
    the `useAlertQueue`/`AlertCard.vue` and `useGameRound`/`GameResultsBoard.vue`
    composable extractions (highest duplication payoff, touches multiple widgets at
    once so do them as their own dedicated slice).
