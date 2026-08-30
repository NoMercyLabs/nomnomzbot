# Execution plan — the slice index (one ordered queue, top to bottom)

Binding inputs: `PRODUCT-ALIGNMENT.md` (decisions D1–D9) · findings in
`stability-audit-scope-and-plan.md` (**S**·F1–F19), `widget-quality-audit-scope-and-plan.md`
(**W**·§1–§8), `usability-shortcomings-audit-scope-and-plan.md` (**U**·A1–A7, B1–B7, C0–C7),
`sleak-review-2026-08-22.md` (**K**). This file only orders them.

**How to execute:** one slice at a time, in this order; each slice is the smallest testable vertical
cut (contract → service → data → UI where it applies → test). Test-first, locally (`tdd-local-no-ci`);
commit when the **Done-when** is proven; then **delete the slice from this file** (tracker = remaining
work only). A slice may be split further while executing, never merged. 🔒 = needs the owner first;
skip it and continue. Persona priority (owner): streamer → moderator of many → viewer.

**Ordering rule (owner, 2026-08-22): stabilize the CURRENT feature set first; merge new code only
where a fix requires it; add the new stuff after.** Phases: ~~0-S security first (COMPLETE 2026-08-23)~~ · 0 truth-and-safety · 1 runtime stability ·
2 existing platforms made to work (minimal spine) · 3 form infrastructure · 4 existing-feature truth/
reach · 5 new model (one channel, many platforms; any login) · 6 new features + personas · 7 polish.
Slice IDs are stable; the order is the queue.

---

## AT A GLANCE — what is open, in one screen

Read this block first. It is the only summary; everything below is detail.

**Your asks, and where each one is:**

| Your words | Slice | State |
|---|---|---|
| pipeline page needs love, nested if/and/or, add-remove-reorder | S-PIPE-TREE | engine + named params shipped; nested block-list EDITOR remains |
| make effects and repercussions visible | S-CONSEQ | law recorded, applies to every slice |
| item pickers show a rich list, not opaque ids | S-RICH-PICKERS | backend building - dashboard half after |
| budget system for payment tiers by resource usage | S-BUDGETS | queued - intent recorded: recover real cost, not upsell |
| old-bot behaviour only from generic blocks | (standing rule) | verified against the spec |
| stream-facing first (commands + overlays) | (ordering) | in force |

**Phases 0-S, 0 and 1 are EMPTY — all closed.** The queue is: DO NEXT, then Phase 2 onward.

**Rules that now bind every future slice** (learned the hard way, each cost rework):
1. A guard that checks only a hand-written list is not a guard — enumerate from the real source.
2. Every model change lands in BOTH migration sets (SQLite AND Postgres) or Postgres deploys break.
3. Never show state that is not actually enforced.
4. Every control says what it does and what changes; destructive saves show a counted blast radius.

---

## OWNER REQUEST 2026-08-30 — replay button on the home activity feed (jump the queue, dispatched immediately)

Owner ask, verbatim intent: a Replay action next to each item in the home screen's activity feed
(subs, bits, redemptions, etc.) so that if OBS/overlay/TTS missed an event to a short WebSocket
interruption, the streamer can re-show it to the viewer who paid for it — "a just-in-case feature that
could prevent a lot of tickets." Decided with the owner (AskUserQuestion, 2026-08-30):
- **Presentation-only.** Replay re-fires the overlay alert + TTS exactly as they'd have looked/sounded
  live. It NEVER re-runs currency grants, loyalty points, reward fulfillment, or any other
  persistent-side-effect logic — those already ran once when the event first landed; re-running them
  would double-credit the viewer.
- **Source list = the existing home-screen activity feed** (`DashboardController.GetActivity`,
  backed by `ChannelEvents` rows, surfaced to the client as `ActivityEventDto`/`ActivityEvent`) — no
  broader EventJournal browsing needed.

**Design decision (to avoid re-deriving TTS/overlay output and risking drift or accidental
re-processing):** do NOT reconstruct the alert from the raw `ChannelEvent.Data` and re-run
alert/TTS-eligibility logic. Instead, **capture exactly what was pushed to widgets the first time**
(every `WidgetAlertDispatch.RouteAsync` call — including `tts_speak` — already carries the SAME
decorated dto both the dashboard and the widgets receive, per `WidgetAlertHandlers.cs`'s own doc
comment) and **record it against its originating `ChannelEvent.Id`**. Replay then re-broadcasts that
recorded payload byte-for-byte — no new computation, no chance of drift, no chance of accidentally
re-running a persistent side effect.

- **S-REPLAY-CAPTURE** — DONE, verified (8d4cfb69): `RenderedAlertCapture` table + `WidgetAlertDispatch.
  RouteAsync` choke-point wiring, 40-row-per-channel pruning, real dispatch→capture test. **Found its
  own follow-up blocker**: `RouteAsync`'s callers never thread a `ChannelEvent.Id` through, so captures
  have NO correlation to the activity-feed row that produced them yet — S-REPLAY-ENDPOINT cannot map
  "replay this feed row" to its captures until that's fixed.
- **S-REPLAY-CORRELATION** — DONE, verified (c0abd07d): `ChannelEventId` threaded through every
  `RouteAsync` caller (follow/cheer/raid/sub/ban/role/shoutout/reward/hype-train/poll/prediction/
  dashboard/custom-data/sr-queue/tts_speak + the widget-test-event caller). Two honest gaps found, not
  invented around:
  - The "no `ChannelEvent` row logged" gap is now closed for all three affected alert types: Vip
    (acb97be6), Moderator add/remove (af15b466, same `RoleBroadcastHandlers.cs`), and Shoutout
    (fe9baa6a, separate `ShoutoutBroadcastHandlers.cs`) — each verified with a real
    `ChannelEvent`-row + correlated-`RenderedAlertCapture` test. Every alert type now appears on the
    activity feed and is replayable, except the intentionally-out-of-scope standalone chat-command TTS.
  - `tts_speak` is genuinely uncorrelated: `TtsUtteranceDispatchedEvent` carries no reference to any
    triggering redemption/command, and a viewer `!tts` request never logs a `ChannelEvent` at all —
    `ChannelEventId` is explicitly null for TTS captures, no fake time-window join was invented.
    **Owner decision (2026-08-30): a free chat command (`!tts`) never needs replay — nothing was paid
    for, out of scope permanently.** — **DONE, verified (219cad38)**: pipeline-triggered TTS (e.g. a
    reward redemption whose action chain includes a TTS action) now correlates to its real
    `ChannelEvent.Id`, threaded through `PipelineRequest`/`PipelineExecutionContext`/
    `TtsSpeakRequest`/`TtsUtteranceDispatchedEvent` — 1-field addition through 6 existing hops, no
    engine refactor needed (`RewardRedeemedEvent.EventId` was already the same id the activity feed
    uses). A standalone chat-command TTS still correctly captures `ChannelEventId = null`.
- **S-REPLAY-ENDPOINT** — DONE, verified (9c8d4f2f): `POST api/v1/dashboard/{channelId}/activity/
  {eventId}/replay` (matches `DashboardController`'s real route base), action key `dashboard:replay`
  (Mod floor). Re-pushes the exact captured payload to every currently-subscribed widget verbatim; a
  real 404 (zero notifier calls) when the event has no capture — never a fake success.
- **S-REPLAY-UI** — code-complete and unit-verified (1c3c8ef4): Replay icon-button on each
  `ActivityRow`, calls the endpoint with the row's exact event id, distinct "replayed" vs "nothing to
  replay" vs generic-failure states, disables only the clicked row while in flight. **NOT yet
  live-validated** — jvmTest proves the client-side wiring only; nobody has clicked it on a running
  dashboard against a real connected OBS browser-source yet. Per house rule (never call a page done
  from tests alone), do that live pass — real redemption/sub → confirm the alert renders → drop the
  WS → click Replay → confirm it re-renders in OBS — before calling the whole Replay feature shipped.

---

## MILESTONE 2026-08-25c — candidate for the first milestone push

Owner's cadence is milestone pushes. This batch qualifies: the live box currently replays the current
song on every restart (S-SR-INFLIGHT-DURABLE, 169a52e4) and logs errors during normal operation
(S-CHATTERDAY-LOGNOISE-b, 79b6baba) — both user-visible on stream. Verify HEAD in a throwaway worktree,
then push + `scripts/ship.ps1` + watch.

## FOUND ON THE LIVE BOX — 2026-08-25 (after the first successful deploy)

## LIVE OUTAGE 2026-08-25 — root cause fixed, two follow-ups

The deployed bot was crash-looping (11 restarts) with a 502/503 dashboard, and the music queue
"kept requeueing the same songs". ONE root cause, fixed in 27649f50: a Spotify call hit HttpClient's
100s timeout -> `TaskCanceledException`, which DERIVES from `OperationCanceledException`, so the music
poller's `catch (Exception ex) when (ex is not OperationCanceledException)` did NOT catch it. It escaped
`ExecuteAsync` and `BackgroundServiceExceptionBehavior.StopHost` killed the whole host.

## BLOCKED ON THE OWNER — cannot be solved from this side

**ANSWERED 2026-08-25c (owner, via AskUserQuestion) — these three are no longer blocked:**
1. **Deploy = MILESTONE PUSHES.** Not every green slice, not never: push and ship at a milestone.
   `tdd-local-no-ci` still governs day-to-day (local test-first); a milestone is the trigger to push +
   `scripts/ship.ps1` + watch. The orchestrator decides when a batch constitutes a milestone and says so.
2. **Discord: DISABLE STREAMCORD, TAKE OVER.** Owner turns off Streamcord's live-role and go-live
   announcement for his channel; NomNomzBot becomes the sole driver of role `1388128843147120761`.
   So the go-live ANNOUNCEMENT is now ours to own too, not just the role — Streamcord will no longer
   post it. Still needs the physical steps in item 2 below (install, Manage Roles, role above target,
   account linked, friends' links accepted).
3. **S067 song-request pricing: FREE BY DEFAULT, COST OPTIONAL.** Ships with a max-duration cap and a
   per-user cooldown enforced for everyone; the channel-currency cost is an opt-in per-channel setting,
   default OFF. This is the near-free/abuse-floor side of [[limits-safety-baseline-then-tier]], not a
   paid gate.


These are not "not done"; they are done-as-far-as-code-can-go and need a real-world action or a call
only Stoney can make. Do not burn agent time trying to work around them.

1. **Deploy.** Nothing this session is pushed or deployed (`tdd-local-no-ci`: local test-first, no CI,
   deploy only on the owner's call). The deployed box is a DIFFERENT SYSTEM from this tree — every
   "verified" below means verified at a commit in a throwaway worktree, never on the live instance.
2. **Discord, for tomorrow's stream** — needs the owner in his own server:
   - NomNomzBot must be INSTALLED in the guild and hold **Manage Roles**.
   - The bot's own highest role must sit **ABOVE** role `1388128843147120761` in Server Settings > Roles.
     Streamcord working proves nothing here: ours is a separate member with its own role, which lands at
     the bottom by default.
   - His Discord account must be LINKED to his channel so the bot can resolve which member to mark live.
   - **Streamcord overlap:** it already drives that role and posts go-live announcements. Running both
     double-posts and has two bots fighting over the same role. He must disable Streamcord's live-role +
     announcement for his channel, or point ours at a different role while testing.
   - Friends' channels need an ACCEPTED LINK each (separate tenants); no shortcut exists that preserves
     tenant isolation.
3. **End-to-end Discord verification is impossible from here.** Unit tests prove the add/remove-role call
   is made with the right arguments against a FAKE handler. They can never prove Discord accepted it,
   that the token is valid, that the hierarchy is right, or that the resolved member is really him.
4. **Open call for him:** S-BUDGETS classifies *registering a command* as near-free -> abuse floor, not a
   paid ceiling, per his own stated reason (recover real cost, never manufacture upsell). He cited
   commands as an example of a tier limit, so he may want to overrule. Files/TTS/CPU/bandwidth are
   cost-driving either way.

---

## OWNER OBSERVATIONS 2026-08-29 — fold in at the right stage, do not jump on them

Twelve live observations from using the bot, given as a batch with the instruction to slot each into
its natural phase rather than working them immediately. Each gets its own slice id (`S-OBS-*`) so they
survive independently as the queue is worked top to bottom.

- **S-OBS-01** stale-then-fresh flash — a page loads cached data first, then the real server response
  replaces it a moment later, showing wrong data briefly before the correct data appears. Done-when:
  either the cache is never shown when a fresher fetch is already in flight, or the UI clearly marks
  cached data as loading/stale until the real response lands — no silent wrong-then-right flash.
- **S-OBS-02** multi-chat channel badges don't show the broadcaster's own Twitch chat-color per channel
  (the dashboard's dynamic accent already derives from chat color elsewhere — reuse that mechanism).
  Done-when: each channel's badge in the combined multi-chat view is tinted with that broadcaster's
  real chat color.
- **S-OBS-03** no single place for server errors — errors need to surface both (a) in one consistent
  place (top-of-page banner or snackbar) AND (b) inline at the exact control/location that caused them.
  Done-when: every server error the dashboard receives does both, consistently, everywhere.
- **S-OBS-04** music vs song-request pages have mixed concerns — the Music page shows song-request UI,
  and the dedicated Song-Request page is nearly empty and serves no purpose as currently split. Decide
  the model (likely: Song-Request page owns the SR UI, Music page stays playback/queue-management only)
  and move UI to match. Done-when: each page has one clear, non-overlapping purpose.
- **S-OBS-05** moderation page is not channel-scoped — it shows bans across ALL channels instead of the
  currently-selected channel, making per-channel ban management impractical for a mod-of-many. Done-when:
  the moderation page respects the active channel-switch context like every other tenant-scoped page.
- **S-OBS-06** soundclips have no single-playback enforcement or stop control — multiple clips can play
  concurrently and none can be stopped once started. Done-when: starting a new clip stops any clip
  already playing, and a stop control exists.
- **S-OBS-07** media page's `!media <url>` command works but the resulting media has no click-to-open
  popup, no on-page player, and no overlay-widget playback — it's captured but never actually watchable
  from the dashboard. Done-when: a `!media` result can be opened/played from the dashboard or an overlay.
- **S-OBS-08** bare Twitch clip links (no `!media` prefix) in chat should auto-enqueue into the same
  moderator approval queue `!media` uses, and be playable directly in that queue for review. Done-when:
  a plain clip link posted in chat appears in the approval queue, playable inline, without needing the
  command prefix.
- **S-OBS-09** played clips linger in the approval/media queue instead of being removed once played.
  Done-when: a clip leaves the queue after it has been played.
- **S-OBS-10** the code-scripts intermediate landing page is pointless — it should navigate straight to
  the script editor instead of an in-between page. Done-when: opening code scripts goes directly to the
  editor (this dovetails with the just-shipped Monaco-class editor work, S-CODE-EDITOR).
- **S-OBS-11** replying to a chat message with `!quote` should create a credited quote from the message
  being replied to (quote text + author credited), not just log the invoking user's own line. Done-when:
  `!quote` as a reply captures the replied-to message and credits its author.
- **S-OBS-12** `!quote N` is broken — it should quote the Nth message in that channel's chat history, but
  currently does not work at all. Done-when: `!quote N` returns and stores the actual Nth prior chat
  message.

---

## FUTURE INITIATIVE — global "system widget" architecture (NOT NOW, needs a dedicated co-working session)

Owner, 2026-08-29, explicit: "this is not now, this is when the time is right and the system is stable
to start working on this." Do not dispatch any slice against this until the owner opens that session.
Recorded here so it isn't lost, and so a future session picks the shape up as design context, not a
cold start.

**The shape of the ask:** some widgets are not per-consumer clone-and-customize instances (the current
model — code editor + clone feature per widget) — they are **system widgets**: one global thing the
admin (NoMercy Labs) authors and maintains centrally, and every consumer just picks CONFIGURATION for
it (a/b/c/d-style choices — e.g. corner position: top-left/top-right/bottom-left/bottom-right; a theme
choice) rather than getting their own editable copy. Two named examples:
- **TTS audio widget** — currently presumably has its own widget-clone flow; should instead be a fixed
  system widget whose URL + "open in popup" button live directly on the TTS settings page (no separate
  widget-gallery entry to browse/clone).
- **YouTube widget** — same pattern, same reasoning.
- **Chat overlay widget** — instead of many separate system-provided chat-overlay widget variants
  (one clone per look), this should be ONE system widget with a proper config system (position, theme,
  and whatever else) so a consumer picks from options rather than the admin needing to publish N
  near-duplicate widgets for each visual variant.

**Where this plugs in:** the owner frames this as belonging with the admin side of the code-scripts/
widget-editor system (S-CODE-EDITOR family, closed this session) — an "admin manages/edits the GLOBAL
widgets every consumer uses; the consumer only gets exposed config options" split, as opposed to today's
model where every widget is a per-consumer editable clone. This is a genuine two-tier authoring model
(admin-authored system widgets vs consumer-authored custom widgets) that doesn't exist yet.

**Design continuity:** there's prior Claude Design work on this — a "Chat Widget" design file at
`https://claude.ai/design/p/12517363-1a76-423c-8b05-9ed80f3e353c?file=Chat+Widget.dc.html` — pick that
up as the starting point rather than designing from zero; the owner said it "needs to be improved
first," i.e. it's a draft, not a finished spec.

**How the owner wants this session to run, verbatim intent:** "I want to do a proper co-working session
with you at that point where you ask me the ears off my head like a kid so we can make this awesome."
This means: when this is picked up, load `superpowers:brainstorming` first and interview the owner
thoroughly (scope, the exact config taxonomy per widget type, how admin-authored vs consumer-authored
widgets coexist in the data model, migration path for existing per-consumer widget clones) BEFORE
writing any spec or code — do not jump straight to a design doc or implementation plan.

**Business context**: this would give the owner's designer (aaoa-dev, who makes free stream-overlay
widgets) a real place to contribute polished system widgets that every consumer benefits from, rather
than each consumer needing their own clone-and-customize pass.

---

## Phase 2 — existing platforms made to work (Kick / YouTube are shipped features that are broken) — only the spine pieces these fixes REQUIRE

(empty — S-COMMUNITYCONTROLLER-BAN-RESULT closed 2c938bd9)

## Phase 3 — form infrastructure (stabilizes existing authoring; every 'raw text box' finding rides on it)

- **S043** "All helpers" dialog — DONE for commands, event responses, timers, chat triggers, Discord,
  pipelines (`TemplateHelpersLink`/`TemplateHelpersDialog`, chip scroller removed). Remaining: rewards
  and giveaways have no free-text template field to wire it into yet — wire it in when S063 adds the
  rewards `Response` field and when giveaways grows an announcement-text field (U·A7, W·§8 i7).
(empty — S046-remaining fully shipped: wire-format prereq c401253e, if ba140ecb/293b9ec7, switch
c3f5a6f9, loop cde1f5fa, random_branch 05b7bb85, try 34e75021, all verified. Out-of-scope notes filed:
chat-triggers/automation screens could still use `PipelineBindPicker` (not yet done); the 5
Block*Card composables in `PipelinesScreen.kt` are near-duplicated and could share an abstraction
later.)
- **S050** Shell truth — DONE. Hub-state dot now reads `DashboardHubClient.connectionState`
  (Connected/Reconnecting/Disconnected) instead of a hardcoded fill, rendered in both the compact top bar and
  the persistent desktop sidebar; proved with a real-socket jvmTest (drop → Reconnecting within the liveness
  window → resume → Connected). `ReconnectBanner` surfaces the actual `ConnectError` reason instead of one
  generic message. `ShellAccessController` now distinguishes a transient `effectiveMe`/`primaryChannel` failure
  (`status == 0 || status >= 500`) from a definitive one: `ShellAccess.Retrying` auto-re-probes instead of
  flashing the fail-closed viewer UI. `ConnectController.restoreUnreachable` + `Destination.Unreachable` +
  `UnreachableScreen` distinguish "remembered session, backend unreachable" (auto-retries, keeps custody) from
  a genuine logged-out state.

## Phase 4 — existing features: truth, reach, completeness

- **S052-remaining** TTS system surface. **Auto-provisioning DONE, verified (17cc7a43)**:
  `IWidgetService.EnsureSystemWidgetAsync` (get-or-create by gallery natural key, never a gallery
  browse/install) + `GET tts/overlay` on `TtsConfigController` wired to it — a fresh channel gets a
  working `tts_caption` overlay URL on first call, no widget install required (widgets-overlays.md
  §1.2). Remaining (each its own follow-up slice, not yet done):
  - **S052-frontend-overlay-card** — DONE, verified (8daba7f6): TTS page calls `GET tts/overlay` on
    load, shows a copyable browser-source URL and a distinct "never ran" vs "last ran Xm ago" state,
    degrades cleanly on a failed call.
  - **S052-audio-queue** — DONE, verified: already correctly implemented, no change needed.
    `OverlaySdkController.cs` builds `<audio>` elements from `payload.audioUrl` and queues them
    (`ttsQueue`/`playNextTts()`), strictly sequential, advances past a failed utterance — the prior
    audit note about `tts_caption.vue` ignoring `audioUrl` was stale (that logic lives at the SDK level,
    not that widget); confirmed by existing test `OverlaySdkTtsPlaybackTests.
    Utterances_are_queued_so_two_voices_never_talk_over_each_other` (3/3 green). Cosmetic-only leftover:
    `tts_caption.vue`'s header comment still implies it plays audio itself — not fixed, not urgent.
  - **S052-test-through-overlay** — DONE, verified (401c1648): Test button dispatches a real utterance
    through the SAME `ITtsDispatchService` path a production redemption uses (not a synthetic
    shortcut), `ChannelEventId` explicitly null (a manual test, not a paid event — no fake correlation
    invented). UI copy says "test sent", never claims OBS actually played it (honest about what the
    backend can and can't confirm).
  - **S052-queue-controls** — DONE, verified (83883d9a): skip/clear/pause/resume for the LIVE overlay
    playback queue (correctly disambiguated from `TtsQueueController`, which is the moderator approval
    queue — unrelated, already built). Real commands sent through to the connected overlay's
    `ttsQueue`. **Known honest gap, not fixed**: the server never reads back the overlay's true queue
    state (item count / what's actually playing) — the dashboard's pause/resume indicator is optimistic
    local UI state, not a confirmed echo from the overlay. Acceptable for now (skip/clear/pause DO fire
    real commands), but a future slice should close the loop with a real state confirmation if this
    becomes user-visible as wrong.
  - **S052-gallery-cleanup** — DONE, verified (aaa41092): `tts_caption` excluded from the browsable
    gallery listing; a new onboarding handler (`TtsSystemWidgetSeedOnOnboardingHandler`) provisions it
    at real channel creation, not just lazily on first TTS-page visit.
  - **S052-alert-sound-provisioning** — DONE, verified (15f612b9), `alerts` half: `alerts` confirmed
    as a real, distinct catalogue entry with the same gap as `tts_caption` — fixed by generalizing
    `TtsSystemWidgetSeedOnOnboardingHandler` into `SystemWidgetSeedOnOnboardingHandler` (provisions
    every natural key in `SystemSurfaceNaturalKeys` at `ChannelOnboardedEvent`, one surface's failure
    never blocking another) and adding `GET .../event-responses/overlay` mirroring the TTS page's
    entry point. **`sound` confirmed NOT to exist as a distinct catalogue entry** —
    `widgets-overlays.md` §1.2 names it, but `FirstPartyWidgetCatalogue.cs` has no `sound` key at all;
    this is unstarted work, not merely unwired, and needs its own slice once/if a Sound widget surface
    is actually built (🔒 not urgent, no current UI depends on it).
  Done-when (whole item) — **S052 is now fully COMPLETE**: TTS (auto-provisioning, audio queue,
  dashboard card, Test button, queue controls, gallery exclusion) and Alerts (gallery exclusion +
  onboarding provisioning + overlay endpoint) all shipped and verified. Sound remains genuinely
  unbuilt, tracked separately, not blocking this item's closure.
- **S060-remaining** Editor fire-bar. **Real per-event samples DONE, verified (c4846c9c)**: fire bar
  was posting `{}` for every event type — now ports the server's `WidgetTestSamples`, real
  event-distinct payloads. **Declared-events drift DONE**: the fire bar's event list now comes from
  the widget's PERSISTED `EventSubscriptions` (the same list the overlay manifest reads at runtime),
  passed through `ProjectEditorIO.editAndCompile` → the served editor page → `preview.js`; it falls
  back to the old `.on('x')` source scan only when that list is empty (a widget with no declared
  subscriptions yet). The regex-scan code itself moved from the old `ProjectEditor.wasmJs.kt` DOM
  build into `preview.js` during the served-page migration (S-CODE-EDITOR) — the file:line this note
  used to cite no longer exists. **Confirmed still open, each its own follow-up**: chat variants (only
  one generic chat-message shape — no sub/mod/vip tiers, reply/mention, bits, `/me`, multi-platform
  variants); desktop (JVM) parity — the desktop `ProjectEditor` is a Swing dialog with no live
  preview/DOM to fire into at all, needs a product decision on what "desktop gets the bar" even means
  before it's buildable, not a mechanical port (🔒 owner call).
  **Also found and fixed, not part of this plan item but critical**: `PipelinesController.kt` used
  `Map.getOrDefault` (JVM-only, backed by `java.util.Map`) — broke the ENTIRE wasmJs/web dashboard
  compile, introduced during this session's S046-remaining branching work. Fixed alongside an
  unrelated pre-existing instance in `TemplateHelpersDialog.kt` (`toSortedMap`, same JVM-only root
  cause) — commit 679ad1c7, wasmJs compile verified clean after. **New standing habit**: run
  `compileKotlinWasmJs` after any substantive `app/` Kotlin batch, not just `jvmTest` — see memory
  `jvmtest-cannot-catch-wasmjs-breaks`.
- **S061** — DONE, esbuild/Vue-compile verified (all 21 first-party widgets still build,
  `FirstPartyWidgetVueBuildTests` 21/21). `chat_box.vue` layout batch (W·§2/§8 i4): head row
  is now block-level (was `inline-flex` glued to `.body`) — the originally reported bug;
  long display names truncate with an ellipsis (`max-width` + `text-overflow`); avatar image
  added (the decorated chat DTO the widget already receives carries `avatarUrl`, same as the
  dashboard's own chat view — just never read here); chat-color username text now clamps its
  HSL lightness against the dark/light theme backgrounds (transparent theme is left as-authored —
  its existing drop-shadow already helps against an arbitrary OBS scene, and there's no fixed
  background to reason a contrast target against); emotes/cheermotes sized in `em` so they scale
  with the configurable font size; new lines fade+slide in via `<TransitionGroup>` (departing/
  reordering lines get the same treatment) instead of popping in instantly; mentions get a
  background chip so they stand out with no known chat colour. **Not done, out of scope for this
  item**: the "stacked transition" animated overlay STYLE (a distinct, larger, from-scratch ask —
  see the audit's separate note) and the `now_playing.vue`/`chat_box.vue` component-split
  refactor (a structure improvement, not a behavior fix). **Not independently visually verified**
  (chrome-devtools/playwright MCP were unavailable this session) — only compile-verified; a
  live-render check is worth doing before calling this fully closed.
- **S062** Widget setup — DONE: error/last-ran badge (commit 79284d13 — `RelativeTime` shared primitive,
  a widget row now shows "last ran Xm ago" / "never ran yet", replaced by the last runtime error when
  one exists — both fields existed on the DTO but were never rendered); Test button on the row (commit
  3916e445 — wired the dashboard to the backend's `WidgetTestEventController` test-event dispatch, which
  had zero callers anywhere in the frontend; fires the widget's first declared subscription, shows the
  real reach description, not a bare toast). **Still open**: per-widget tokens + staged rotation +
  post-rotate URL list; inline preview; overlay last-seen; in-overlay banner on rejected token; resume
  without reload; settings form by schema availability; colour picker; asset/sound/font field types;
  unsupported-type + invalid-value errors; editable subscriptions; gallery version/update + search/paging;
  sound upload limits (U·B5). Done-when: add → copy → test → live from one row.
- **S063** Rewards reach — `Response` field in create + update; `ActionType/ActionSettings` exposed or
  deleted; rewards poll backoff; null-as-empty reads → errors (U·B2).
- **S064** Economy reach — catalog item full form + edit; leaderboard config CRUD + opt-outs + display
  names; jar role dropdown + jar update/delete (U·B2). Done-when: the store can sell an item with an
  effect, stock and cooldown from the UI.
- **S065** Giveaways reach — eligibility/weighting/prize pipeline in dialog; `ClosesAt` auto-close;
  code labels; entries endpoint + list; pool picker guard; zero-value-out gate for code-pool prizes;
  platform-generic DM delivery (U·B2, spec `giveaways.md`). Done-when: a weighted sub giveaway runs
  end to end.
- **S066** Moderation reach — chat-filters screen; AutoMod settings; AutoMod held-message queue; mod
  add/remove endpoints + UI; clear chat; full `AutomodConfigDto`; concurrency guard on whole-config
  POST; chat-settings slow/followers/unique/non-mod fields (U·B3). Done-when: a mod approves a held
  message from the dashboard.
- **S067** Music UX — admission enforces every setting + `IsEnabled` + trust gate + `PreferredProvider`;
  one config editor; queue promote/ban-track/refund; public `/sr/` page built; token → URL; bounded
  steppers; enum lists from API; `public-sr` rate policy; `RequestedBy` not the owner key; hub-driven
  reloads; polling fan-out bound (U·B4). 🔒 cost/max-duration/cooldown fields. Done-when: every SR toggle
  changes what `!sr` does (test per setting).
- **S068** Legacy builtins — `!help`, `!commands`, `!lurk`/`!unlurk`, `!leaderboard`, `!songhistory`,
  `!playlist`, `!bansong`, `!whisper`, `!discord`, `!accountage` + seeded fun-command preset pack +
  on-connect announcement (U·C7). Done-when: a fresh channel has every legacy command or a seed for it.
- **S069** Bot voice everywhere — tone applied to custom commands/timers/event responses/chat triggers/
  `send_message`; tone slots for usage/errors; one reply-or-mention helper; one `ParseUserMention`;
  permit via identity path; whisper-with-fallback for GDPR + inbound whisper handler; `announce`
  action/toggle; tone catalogue per locale (U·C7, K copy). Done-when: same `!sr` sounds the same from
  builtin and pipeline; sassy channel has sassy errors.
- **S070** Settings + onboarding truth — auto-join semantics; Integrations read-failure state;
  timezone/language wired or removed; wizard `botUsername` contract; `applyBasics` failure reported;
  scope→feature map + re-grant on Settings; swallowed regrant/reconcile failures; copy fixes (U·B6).
- **S076** Multi-chat as a tool — moderation actions + composer; `joinedChannels` preserved on
  reconnect; watch list persisted; mod-log pushes with names + time; shield pushes consumed (U·B3).

- **S085** Spec-led contract deltas (the 2026-08-22 realignment now leads the code) — `ResolvedAccessDto`/
  `RoleResolver` rungs by name not int; `IAutomationEventDescriptor` → attribute catalog; `FirstPartyWidgetCatalogue`
  `domain.action` subscription names; `2026-06-16-database-schema.md` changelog for `PlatformConnection`, Provider
  columns, `BotLinePrefix`, `EventJournal.Source`; `economy.md` L.3 `SubjectTwitchUserId`. Done-when: ApiContractTest
  + openapi snapshot refreshed; no int level in any DTO.

## Phase 5 — new model (D1 one channel / D2 any login) — merged only after Phases 0–4; S023/S024 are the minimum the viewer-identity fixes need

- **S019** `PlatformConnection` model — entity (ChannelId, Provider, ExternalChannelId, name, connection,
  IsPrimary, IsLive), `Channel` loses `Provider`, `Platform` enum + `twitter`; migrations (PG + SQLite);
  provisioner creates connections under the owner's one channel; data migration folds existing sibling
  channels into one (U·C0, spec `platform-identity.md`). Done-when: a Twitch+Kick streamer is ONE
  `Channel` with two connections; all tenant-scoped reads unchanged.
- **S023** Viewer identity key sweep — `*TwitchUserId` → `*ExternalUserId + *Provider` on the 18
  entities; remove `provider = Twitch` default on `IUserService`; delete `PlatformType` (U·C0).
  Done-when: build + migration green; no call site defaults the provider.
- **S024** Viewer linking — `LinkAsync` absorption (§3.1a), `IViewerMergeParticipant` + the eight
  participants, `ViewerRowAbsorbedEvent` published (U·C0). Done-when: a viewer who chatted on Kick then
  links Twitch ends with ONE User row and ONE balance (test).
- **S025** Login any platform (D2) — `auth/providers`, `auth/{provider}/device`, `/poll`; Kick/YouTube/X
  login providers shipped; first login creates the channel, others attach (spec `identity-auth.md`).
  Done-when: a Kick-only streamer signs in, onboards, and has a channel with one Kick connection.
- **S026** Onboarding "connect more platforms" stage (non-blocking) + channel-bot connect via device
  code with poll/refresh (U·B6, spec `onboarding-setup.md`). Done-when: wizard attaches a second
  platform; bot-account card updates without reload.
- **S032** Combined management fan-in/out — combined chat composer with target selector + per-target
  result; badge every line incl. Twitch; `provider` on `ChannelSummary`; (provider,id) dedupe +
  reorder window; timers/event responses/announcements platform target sets with per-platform rate
  limit + duplicate suppression; one go-live form with per-platform results; per-platform viewer
  breakdown + total + cross-platform stream session; owner-scoped "ban on all my platforms"; earning
  credits the linked person (U·C1). Done-when: live on three platforms, one timer posts once on each;
  one ban bans the human everywhere; Home shows per-platform viewers + total.

## Phase 6 — new features and personas (D3 X, D4 viewer, moderator-of-many, new capabilities)

- **S044** Helper expansion + presets — math/string/date namespaces, general `{{ns.key:arg}}` grammar,
  any-step output, `{{stream.viewers}}`; raid preset (shoutout → raid → countdown → optional OBS/
  Spotify) + `channel.raid.out` seeded on onboarding (W·§6, U·A1 i4). Done-when: `!raid <user>` from a
  fresh channel runs every step or names the failing one.
- **S054** TTS segments — `TtsSegment` list request, per-segment voice mode, ONE `tts_speak` payload
  with ordered segments; `BypassQueue`; sub-streak preset (U·A5, spec `tts.md` §6). Done-when: the
  owner's example plays as one utterance with two voices.
- **S056** Discord triggers + action — `go_offline` + `hype_train` with handlers; action carries own
  channel/template/embed/ping; Event Responses Discord preset (U·A6 i2/3/6).
- **S057** Discord live-role sync — roles added on online, removed on offline, role picker, spec
  section in `discord.md` (U·A6 i4). Done-when: go live → roles on; offline → roles off.
- **S059** Alert system surface — one alert queue across platforms, not a gallery item (spec
  `widgets-overlays.md` §1.2). Done-when: supporter alert renders without an install.
- **S071** Notification centre + Home — action-required inbox (dead tokens, missing scopes, failed
  timers, held messages, pending unbans) with click-through; Home hero tile + collapsed activity feed
  + first-run next steps (U·B6, K). Done-when: a dead Spotify token is visible on Home within a minute.
- **S072** IA reconciliation — Admin via profile menu + chrome swap; theme + Account in profile menu;
  tabbed Settings; `MyData` on the participant rung; shipped routes listed in `frontend-ia.md`
  (U·B6). 🔒 regroup sidebar vs update spec.
- **S031** X Live as a platform connection (D3) — `IntegrationProvider.twitter`, login + connection,
  chat read/send via X's API to the extent it exposes (document limits), events where available
  (U·C4, spec `platform-identity.md` §10). Done-when: X connection attaches; chat lines carry `x`.
- **S073** Moderated-channel discovery per platform; reconcile covers moderator-mode tenants +
  "roles last synced"; live dot bound to `isLive`; roster refresh on `StreamStatusChanged`; roster
  cached (U·C6).
- **S074** Never act on the wrong channel — stale active-channel pin detected + cleared + explained;
  `primaryChannel()` hard-fails instead of substituting; switch splash with timeout/error; active role
  in the sidebar header (U·C6). Done-when: revoked access yields an explained state, never a 403 loop.
- **S075** Cross-channel awareness — hub joins every roster channel for alert/mod classes; attributed
  notifications with click-through; `GET /me/moderation/queue` + "my channels" home; queues re-fetch
  on `ModAction` (U·C6). Done-when: a mod of 4 channels sees which is live and gets attributed alerts.
- **S077** Viewer entry — switcher source "channels I appear in"; honest empty state; `MyData` on the
  participant rung; channel chip shows the channel; routes/deep links (U·C5). Done-when: a role-less
  viewer's first run lands on a usable Me page.
- **S078** Me page — GDPR export/erase, linked platforms (identity API client), own TTS voice, standing,
  profile fields, leaderboard opt-in read, per-jar contributions, own SR requests + public page link,
  preview-as-viewer forces Everyone (U·C5).
- **S079** Viewer giveaway entry/my-entries endpoint + card (or drop from IA) (U·C5).
- **S051** Design-system catalogue gap — build the 13 catalogued-but-missing primitives (Alert,
  Checkbox, Combobox, Input, Label, Popover, RadioGroup, ScrollArea, Select, Skeleton, Table, Toast,
  Avatar) or re-scope the catalogue; Patterns tier documented (spec `frontend-design-system.md`).

## Phase 4B — the surfaces round four found (U·Part E) — existing features, same stability-first rule

- **S099** Webhooks truth — outbound backoff capped + jittered, per-delivery dead-letter, delivery off the
  publishing thread, Result checked in the drain; auto-disable + attempted events consumed (toast/hub/
  feed); UI `NextRetryAt`, error vs empty, refresh/paging/replay (U·E3).
- **S100** Custom data sources truth — persist last attempt/error/failure count, backoff + auto-disable;
  allowlist checked at save; real JSON field-map parsing with inline errors; key picker from a test fetch;
  drop or wire `InboundWebhookEndpointId` (U·E3).
- **S101** Supporters — provider list + capabilities from the backend (`GET /supporters/sources`),
  mode-correct connect forms (secret / socket token / OAuth connection), error state + reason, staleness-
  derived status, per-connection test; resolve `SupporterUserId` where payloads allow + amount-scaled
  earning; dedup unique-violation handled; event-type in Patreon/Treatstream dedup key; source filter;
  ingest failure counter surfaced (U·E4). Done-when: all 11 adapters connectable and truthful.
- **S102** Billing/usage truth — Usage panel reads real counts for count-capped keys, localized labels,
  unlimited rendered; `UsageQuotaExceededEvent` + `SubscriptionTierChangedEvent` consumers; `free` tier
  limits seeded; downgrade at-period-end + over-cap warning (U·E4).
- **S103** Bundles + pick lists — export all 12 types; type filter select; semver + tags chips; installed
  version compare/update; pick-list anti-repeat window, per-item weight/enable, ETag, bulk paste/import/
  reorder (U·E4).
- **S104** Media share + sound + assets — media player widget (system surface) consuming `GetNext`;
  moderation rows with thumbnail/link/name; submitted/playback events as trigger sources; paged queue;
  sound handle threaded end-to-end; upload dialog with volume/trigger/cooldown/floor; clip replace;
  preview-on-overlay or remove dead endpoint; asset picker wherever a media URL is configured; used-by
  guard; limits shown; paging (U·E2).
- **S105** OBS + VTS truth — OBS page consumes OBS events; scene/source/input pickers in pipeline fields;
  source-visibility + replay-buffer on the control screen; bridge error vs offline; edit-reset fix; VTS
  probe + bridge status; inventory failure vs locked; parameter/tint control; endpoint prefilled +
  validated; i18n of the two hardcoded errors (U·E2).
- **S106** Stream / live-ops page — dedicated Stream destination (stream info incl. language per
  platform connection, polls/predictions with live results + hub refresh, ad countdown + snooze, raids,
  markers, clips, shield, hype train, goals, charity, guest star); errors not swallowed; raid-pending
  cleared; platform badge on every control (U·E1). Done-when: an operator runs a poll and sees votes
  move without reload.
- **S107** Schedule + journal — pickers for start/timezone/duration, edit seeds timezone, formatted rows,
  webcal subscribe URL surfaced; journal list/query endpoint + browse/filter/inspect UI; rebuild status
  polling; replay/import-legacy reachable or removed (U·E1).
- **S108** Analytics truth — failures visible, selectable window + metric, local-day boundary,
  platform-analytics client (U·E1).
- **S109** Code scripts developer experience — capability catalogue + per-script declared/granted/denied
  view with links to the toggles; all failing capabilities reported at save; SDK types failure visible;
  starter templates + capability chooser; used-by view; test-run with triggering user; execution history;
  bridge unwired capability throws; desktop editor parity decision stated in-app (U·E3).
- **S110** Automation + federation — Stream Deck run-pipeline/run-command action with picker; federation
  opt-in validated server-side, peer/capability pickers, Direction collected (U·E3).
- **S112** Self-host ops — version stamping (`/health/version` real); ready = migrations + EventSub, Degraded
  ≠ ready; update check + notice; pre-migration DB snapshot + documented rollback; backup/restore verb in
  deploy scripts; versioned image tags; firewall + log path documented, log size cap; tray parity on
  Linux/macOS or printed URL/PID; `.env` dev-password warning at boot; saas restriction marker on
  `docker-compose.yml` + `.env.example` + boot notice in saas mode (U·E5).
- **S113** Quality gates — in-process E2E host fixture so the suite runs by default; typed
  `ProducesResponseType<T>` on the 157 schema-less operations + a regenerate-and-diff contract test; Esc
  in the shared dialog; label the 17 icons; move the 47 literal labels to strings.xml; locale date/number
  formatter; hub reconnect jitter; `primaryChannel()` cached (S050 dependency); Wasm optimize step with a
  size budget; chat decoration + pronouns + engagement get a settings surface (U·E6, E4).

## Phase 6A — platform admin: reliable system-level management (U·Part D) — safety items first, then reach

- **S087** IAM mutation audit + guards — every assign/revoke/create/deactivate/reactivate writes an
  `IamAuditLog` row with target/role/scope; create transactional + validate-before-mutate; no duplicate
  or inactive-target assignment; last `iam:manage` holder protected; flag changes audited (U·D2).
- **S090** Support access that works — session grants scoped read-only Plane-B visibility (RoleResolver
  reads the session); list active grants; any `iam:manage` holder can end any grant; expiry reaper;
  "view as tenant" reuses preview-as-viewer (client downgrade) (U·D4). Done-when: support staff can read
  a tenant's console without impersonating.
- **S091** Platform-wide user controls — user detail endpoint (channels, identities, sessions,
  consent); platform disable/ban; `MergeIdentitiesAsync` exposed; compliance key for admin erasure
  (not `tenant:access`) (U·D3).
- **S092** Tenant ops — delete/purge + ownership transfer (writes `DeletionAuditLog`); per-tenant billing
  state + quota/limit view; re-run seeds for a tenant; rotate tenant tokens/secrets; `IgnoreQueryFilters`
  on admin lists; search by id/owner/GUID; Sort/Order honoured; real stats (no hardcoded "healthy"/0);
  rate limits + explicit target confirmation on destructive admin ops (U·D3).
- **S093** Ops visibility — EventSub session inventory per broadcaster; token health across tenants;
  worker status + queue depths; error-log surface; AdminHub connect snapshot + scoped pushes;
  break-glass/denial alerts from `IamAccessEvaluatedEvent` (U·D2/D3).
- **S094** Billing roles — `billing:write`/`billing:grant` keys on the four billing writes; `billing:refund`
  endpoint or key removed; `platform-billing` role usable (U·D2).
- **S095** Admin UI truth — fix the `FeatureFlag` DTO so the tab loads; render `state.error` + every
  slice's failure; writes route through `actionError`; paging on every list; refresh per tab; hub live
  state truthful + connect errors surfaced; Admin entry in profile menu gated on Plane-C roles with chrome
  swap + per-page routes/deep links (U·D6). Done-when: a 403 on any admin write is visible; Flags tab shows
  flags.
- **S096** Admin UI reach — flag editor (enable/rollout/tier/mode + per-tenant overrides); invite dialog
  (count/tier/expiry/founder); grant tier/founder actions; support-access begin/end + active list;
  impersonate confirm + justification; reasons + confirm on revoke/deactivate; Ban escalated behind
  name-echo confirm; audit filters as pickers + date range; role keys viewable + role CRUD; Channels tab
  merged into Tenants; timestamps formatted; one primary action per admin page (U·D6, K).
- **S097** System-level content — `SystemPreset` (kind command|pipeline|event-response|pick-list|tone|
  announcement, key, payload, version, enabled, origin seeded|operator) seeded from today's static
  catalogues; `/admin/presets` CRUD + `SystemPresetAdoption` (auto|optin|declined, version) with push-to-
  all and per-tenant opt-in; `Widget.IsSystem` + delete protection + restore; catalogue version stamp on
  gallery items + installed widgets ("update available"); admin kill-switch for a first-party widget and
  a builtin (`BuiltinCommandRegistry`); `PlatformNotice` (announcement/maintenance banner) read on
  bootstrap (U·D5). Done-when: the operator creates a custom command preset and every opted-in tenant gets
  it without a redeploy.

## Phase 7 — polish and structure

- **S080** Sleak pass (K) — toggles neutral + one accented CTA per screen; chat-colour clamp; accent
  derivation floor; form width cap; random-responses segmented control; chat-mode on/off state;
  concentric radius tokens; 13 px muted contrast; one identity block; re-render the six screens +
  Overlays/Economy/TTS/Integrations/Pipelines and re-run the checklist.
- **S081** Widget component splits (W·§7/§8 i11) after S058; `WidgetGalleryItem` file-set storage first.
- **S082** Drop game redesign 🔒 mechanic; stacked-transition chat style 🔒 reference (W·§8 i5/i8).
- **S083** Render-manifest + per-page hub event-class subscriptions (folded handoff, optional).
- **S084** Remaining per-widget nits (W·§8 i10), `{user.messageCount}` alias/drop, the 15 code scripts
  test-run on the live channel, S LOW/informational list.
- **S-GLYPHBUTTON-A11Y** found by S047-remaining (39314dd5): `GlyphButton.kt`'s `clearAndSetSemantics`
  wipes ancestor-contributed Disabled/stateDescription from its own semantics node, so a disabled
  `GlyphButton` only exposes its disabled state via a wrapping node (e.g. `ManageGate`'s `Box`) — affects
  every disabled icon-button app-wide, not just the pipelines Test action. Done-when: a disabled
  `GlyphButton` reports Disabled/stateDescription on its OWN semantics node (assistive tech reads it
  without depending on a specific wrapper), proven by a jvmTest on the component directly.

## 🔒 Owner calls still open
- SignalR/Redis backplane for multi-replica (S035) — single-instance acceptable for now?
- Cooldown DB write-through (S040) — scaling investment, defer?
- Music cost / max-duration / per-user cooldown fields (S067) — spec the economy hook.
- Sidebar regroup vs `frontend-ia.md` update (S072).
- Drop game mechanic; stacked chat transition reference (S082).
- Pre-existing from BUILD-TODO: authz key names (Plane-C + Gate-2), self-host owner = platform admin,
  user-scripting model (JS-first), YouTube non-BYOC client, Stripe, pipelines 6-surface unification,
  community reposition, data-sources push-bridge, federation transport, Streamer.bot import.
