# Widget usability/reliability/configurability audit — scope and plan

Audit of the first-party overlay widgets (23 files in `server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets`,
counted 2026-09-30), the drop-game mechanic, the chat overlay, and pipeline/command/event-response authoring
ergonomics. Findings are file:line evidenced. Only OPEN items remain: the widget/DTO field-name contract, the
`chat_box.vue` layout, the editor fire-bar samples, the `test-run` dry-run wiring, the step-branching UI and the
helper picker were verified fixed in code on 2026-09-30 and removed. Section numbers are kept as stable IDs.

## 2. Chat overlay — one open item

`chat_box.vue` itself is done (username/message break, truncation, avatar, `contrastColor()` username contrast,
`em`-relative emotes, arrival animation, mention chip). The dashboard's own live chat view is not:
`ChatScreen.kt:372` still renders the chatter's raw colour (`message.color?.toComposeColor()`) with no contrast
adjustment against the theme, so a dark name on a dark theme (or light on light) goes illegible. Port the
`contrastColor()` behaviour from `chat_box.vue` (or reuse the token-level `ensureContrastAgainst` in `Tokens.kt`).

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

## 5. Remaining per-widget verdicts

23 widgets ship in the asset folder (2026-09-30). All "broken" verdicts (field-name mismatch, dead `goal` event,
sub-train gift count, socials schema text, inert redemption `sound`) are closed. Open items:

| Widget | Verdict | Core issue |
|---|---|---|
| drop_game.vue | **wrong mechanic** | pure RNG, not skill-based (§4) — needs redesign |
| countdown_timer.vue | solid | minor — no completion transition (static `onCompleteText` only) |
| crash.vue, heist.vue | solid | schema under-covers color/position (`crash.vue` exposes only `accentColor` + `hideAfterMs`) |
| emote_wall.vue | needs polish | binds to a chat-fragment shape not confirmed populated |
| raffle.vue | needs polish | minor — blank-winner text on a degenerate empty round |
| sr_queue.vue | solid | no automatic backend DTO yet, currently pipeline-action-only |
| tts_caption.vue | solid | no automatic backend DTO yet (pipeline-only); speaker label shows the raw `user` value — confirm it is a display name, not a numeric Twitch ID |

Not audited here: `lucky_feather.vue`, `tts_audio.vue` (added after this audit). `spotify_player.vue` no longer
exists; its Spotify player lives in `now_playing.vue`.

## 7. Single-file bloat — which widgets need splitting into components

The compile pipeline **already supports** multi-file widgets (`App.vue` + child
components + composables) — `EsbuildWidgetBuildService.BuildVueAsync` compiles every
`.vue` file in a project independently and bundles them with real relative-import
resolution; `WidgetVersion.FilesJson` already stores a `path → content` map. This is
proven, working machinery, not something to build. The gap is narrower than "widgets
can't be split": the 23 **first-party** widgets specifically bypass it —
`FirstPartyWidgetCatalogueSeeder` embeds each as one asset into
`WidgetGalleryItem.SourceCode`, a single `string?` column with no `FilesJson`/
`ManifestJson` equivalent. Splitting a first-party widget into components needs one
small, well-scoped addition (a file-set source column on `WidgetGalleryItem`, and the
seeder embedding a file set instead of one asset) — not a rebuild of anything.

**Real size ranking** (all 23, by line count, 2026-09-30) — the largest are, in order:
`chat_box.vue` (729), `now_playing.vue` (504), `alerts.vue` (262), `poll_prediction.vue` (226),
`lucky_feather.vue` (210), `drop_game.vue` (194), `crash.vue` (181), `redemption_alert.vue` (179),
`event_ticker.vue` (176), `goal_bar.vue` (169), `custom_data.vue` (167). Everything from `emote_wall.vue` (155)
down to `tts_audio.vue` (82) is under 160 lines.

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
under ~155 lines, one visual concept, one event source each. Splitting these would add
import/prop-plumbing overhead with no reuse value.

> **Cross-reference (2026-08-22):** `usability-shortcomings-audit-scope-and-plan.md` extends this plan —
> §A3/A4/A5 (TTS: `tts_caption.vue` ignores `audioUrl` so server TTS is silent in OBS; a system-level TTS
> surface with an audio queue; voice lookup/override bugs; segment-based `play_tts`), §A7 (the variable
> picker, made concrete: helper registry endpoint + shared "All helpers" dialog), §A6
> item 5 (resource-picker field kinds in the pipeline catalogue), and §B5 (widget setup: per-widget
> tokens, unused test-fire endpoint, no preview/last-seen, settings-form gaps).

## 8. Remediation plan — open items (numbers are stable IDs; done items removed 2026-09-30)

5. **§4 drop game redesign** — the largest single item; needs a product decision on the
   actual skill mechanic (what does "landing" input from a viewer look like?) before
   implementation can start. Reusable pieces (leaderboard, ledger, stream-window
   scoping, existing track/target visual) cut the build cost significantly.
9. **Template helper expansion (narrowed)** — text transforms already shipped
   (`{transform.upper|lower|title|spaced|alternating|reverse|trim|truncate.<n>:text}`). Still open: no math
   namespace beyond `random.number.<n>`; no custom date/time formatting beyond `time`/`time.utc`/`date`; no
   inline conditional/ternary helper; no way to read any step's output but the last one (`last.output`);
   helpers with arguments still use per-feature regex/prefix parsing instead of the general
   `{{namespace.key:arg1:arg2}}` grammar `commands-pipelines.md` §6.3 called for; and `{{stream.viewers}}` is
   registered and validated but never populated (`TemplateResolver.cs:408`).
10. **§5 remaining per-widget polish items** — the rows in the §5 table (countdown completion transition,
    raffle's blank-winner edge case, emote_wall's unconfirmed fragment shape, tts_caption's speaker label,
    crash/heist schema coverage) plus the off-theme error colour once noted on `spotify_player.vue`
    (re-check in `now_playing.vue`) — lowest urgency, batch opportunistically.
11. **§7 component splitting** — needs the small `WidgetGalleryItem` file-set storage addition first (the
    seeder/storage gap, not the compile pipeline — that part already works), then: `now_playing.vue` and
    `chat_box.vue` splits (highest line-count payoff, no cross-widget dependency), then the
    `useAlertQueue`/`AlertCard.vue` extraction (`alerts.vue` + `redemption_alert.vue`, highest duplication
    payoff), then `useGameRound`/`GameResultsBoard.vue` (`drop_game.vue` + `crash.vue`) AFTER the item 5
    redesign, so the extracted files are not re-touched right away. Do each as its own dedicated slice.
