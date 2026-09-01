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
  used to cite no longer exists. **Chat variants DONE, verified (4de105a4)**: `chat_box.vue` now
  renders role accent/badge (broadcaster/mod/vip/sub, highest wins), cheer/bits amount + gold accent,
  a reply quote preview, and a platform badge for non-Twitch messages — all from fields already on
  `DashboardChatMessageDto`. **`/me` action-message styling CLOSED N/A for Twitch (owner call
  2026-08-31)**: Twitch EventSub's `channel.chat.message` `message_type` enum has no action/CTCP-ACTION
  value at all (`text | channel_points_highlighted | channel_points_sub_only | user_intro |
  power_ups_message_effect | power_ups_gigantified_emote`) — Twitch dropped `/me` action-message
  signaling when IRC was retired for this bot; nothing to thread. Revisit only if Kick/YouTube
  chat-read ships and carries an equivalent.
  **Desktop (JVM) fire-bar parity — DEFERRED, not a priority (owner call 2026-08-31)**: the desktop `ProjectEditor` is a Swing dialog with no live
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
  real reach description, not a bare toast); colour picker (already shipped pre-existing — `ColorSwatch`
  + hex field in `WidgetSettingsForms.kt`, matches the spec's own definition); unsupported-type +
  invalid-value errors (5acb519e — an unrecognized schema field type used to silently render as a text
  box and overwrite structured data on save; now shows an inline notice and leaves the value untouched);
  overlay last-seen (d3375b49 — `WidgetDetail.IsAttached` backed by the existing `IOverlayPresenceRegistry`,
  row shows "Live in OBS" / "Not connected" without firing a test event first); gallery search + load-more
  paging (5e1e93b2 — the browse dialog only ever fetched page 1 of 50 with no way to reach older items or
  find one by name; uncovered and fixed a real bug along the way — a second `[FromQuery]`-bound `Search`
  property collided with `PageRequestDto.Search` on the same action and made the .NET 10 OpenAPI XML-comment
  generator 500 on EVERY request, not just this endpoint — `WidgetGalleryController` now binds
  framework/trustTier/reviewStatus as scalars per the `PickListsController`/`QuotesController`/
  `MarketplaceController` convention). **In-overlay banner on rejected token DONE, verified (7c07e41e)**:
  `OverlaySdkController`'s SDK ticket-exchange (`POST /overlay/ticket`) rejected a bad token with a bare
  401/403 and the SDK just `console.error`'d and retried silently forever — the OBS browser source
  stayed blank with no indication anything was wrong. SDK now shows an in-overlay banner
  ("Widget token invalid or revoked — reconnect from the dashboard") on 401/403, clears it on the next
  successful ticket fetch; token validation logic itself untouched.
  **Resume without reload DONE, verified (22a75bc8)**: a REAL bug the banner slice didn't fix — the SDK
  bootstrap forced `location.reload()` on every join after the first (`hadPriorConnection` flag), so a
  successful reconnect discarded the whole page instead of resuming. Removed the reload gate;
  `initialState` (the widget's saved settings) is now re-applied via `applySettings` on every successful
  join, first connect and reconnect alike. Test asserts the served SDK's join handler calls
  `applySettings` unconditionally and contains no `location.reload()`.
  **Sound upload limits DONE, verified (own test-file addition)**: found the 10MB cap already fully
  enforced in two layers (`[RequestSizeLimit]` at Kestrel + a buffer-length check in
  `SoundClipService.UploadAsync` returning a clear `SIZE_EXCEEDED` error before any persistence) — no
  code change needed, just no proving test existed. New `SoundClipServiceUploadLimitTests.cs`: an
  over-cap upload is rejected and never reaches the DB or blob store; an in-limit upload persists both.
  **Still open**: per-widget tokens + staged rotation + post-rotate URL list; inline preview; settings
  form by schema availability; asset/sound/font field types (no backend field type for these exists yet
  — needs a schema contract addition first); editable subscriptions (blocked on S085's `domain.action`
  naming realignment landing first — building this against today's ad-hoc event names would need
  re-doing); gallery version/update (U·B5). Done-when: add → copy → test → live from one row.
- **S063** Rewards reach — DONE, closed (0940f891, 737d1a7a, bced87e4, 48e93130). Most severe finding: reward
  create never actually pushed to Twitch (`CreateCustomRewardAsync` had exactly one caller in the whole
  codebase) — a dashboard-created reward could never be redeemed. Fixed alongside the `Response`/
  `ActionType`/four-sync-field gaps, the rewards poll's missing backoff, load failures silently rendering as
  "empty", and the bundle-import D2 promise ("sync pushes it to Twitch later") — `SyncWithTwitchAsync` now
  actually does that, best-effort per reward.
- **S065** Giveaways reach — spec `giveaways.md`. Done-when: a weighted sub giveaway runs end to end
  (U·B2). Backend weighted-draw math (`ComputeTickets`, `BuildCandidatePoolAsync`, CSPRNG weighted
  pick, currency/pipeline/code-pool fulfillment) is solid and spec-accurate (2026-08-31 audit); four
  of six sub-items shipped this pass, all self-contained fixes/additions, none touching the draw math:
  value-out gate for paid code-pool prizes (002f9d2e/c132fdd0 — `Giveaway.Requires18Plus` +
  `IAgeConsentService`, same pattern as `GameConfig`/`GameService`; **not yet settable from the
  dashboard**, deferred to the dialog rework below, fails closed in the meantime); code labels
  (26ee0534 — bulk add-codes now accepts `"CODE | a label"`); pool picker guard (417462d4 — a
  zero-`available` pool is disabled in the prize picker; the "already-bound to another giveaway" half
  was explicitly NOT added, no backend concept of it exists and it's a non-fatal foot-gun, not a
  fulfillment bug); `ClosesAt` auto-close (501225e5 — `Giveaway.ScheduledCloseAt` target field +
  `GiveawayAutoCloseWorker` 1-minute sweep, mirrors `GiveawayClaimSweepWorker`; **not yet settable
  from the dashboard** either, same deferral). DONE: entries endpoint + list (f8e6385d/caf03f6f —
  `IGiveawayService` had no `GetEntriesAsync`, no `GET /{id}/entries`; a broadcaster could only see
  the raw `entryCount` integer, never who's entered before drawing. Added `GetEntriesAsync` +
  `ToEntryDtosAsync` mirroring the existing `GetWinnersAsync`/`ToWinnerDtosAsync`; `GiveawayEntryDto`
  gained `ViewerDisplayName` — it previously carried only the raw `ViewerUserId`, which would have
  forced the dashboard list to render an opaque GUID; an "Entries" row action opens a read-only panel
  with resolved names + a ticket-count badge once weighting makes tickets worth showing). DONE:
  eligibility/weighting/prize-pipeline/`ClosesAt` dialog UI (342230db — `GiveawayFormDialog` now has
  eligibility filters (sub-only, min standing/watch-minutes/account-age), sub-tier + VIP ticket
  weighting, a Pipeline prize mode via the reused create-and-bind picker, the deferred
  `Requires18Plus`/`ScheduledCloseAt (minutes-from-now)` toggles, and the `requires18Plus`/
  `scheduledCloseAt` Kotlin DTO fields that had been missing since the two backend slices that added
  them). **Still open:**
  - **Platform-generic DM delivery DONE, verified (391964ac)**: new `IPlatformDirectMessageSender`
    (Application) + `TwitchWhisperDirectMessageSender` (Infrastructure) mirroring the `IChatProvider`
    pattern; `GiveawayEntry`/`GiveawayWinner` gained `Provider`/`ProviderUserId` (both Postgres + SQLite
    migrations, per house rule); `FulfillCodeAsync` now resolves the sender by the winner's `Provider` —
    a Kick/YouTube winner fails cleanly with no sender available (code stays `Assigned`, no Twitch
    whisper attempted) instead of silently misdirecting to Twitch; Twitch winners unchanged (regression
    covered). **Kick CLOSED — genuinely impossible, not unbuilt**: confirmed against `IKickApiClient`'s
    full real surface (9 operations — chat send/delete, timeout/ban/unban, event subs, channel
    read/update, matching the live Kick public API v1 docs) that Kick has NO whisper/DM endpoint at
    all, only public chat send. A Kick winner staying on the clean-failure path is correct and final,
    not a gap — consistent with the accepted design from the slice that built this mechanism (a missing
    sender fails cleanly rather than misdirecting). **YouTube CLOSED — also genuinely impossible**:
    confirmed against `IYouTubeLiveChatClient` (the full real YouTube Data API v3 surface this codebase
    wires) that it exposes only live-chat operations (send/ban/unban/delete, all public) — no
    private-message resource exists anywhere in the API itself, same shape as Kick. **X BLOCKED, not
    fabricated (2026-09-01 audit)**: unlike Kick/YouTube this is not "impossible" — X's real API does
    support DMs — but this codebase has no X API client at all beyond `TwitterLoginProvider` (OAuth
    login only, scopes `users.read tweet.read offline.access`, no `dm.write`). Building X DM sending
    needs a whole new Helix-equivalent client plus a new OAuth scope — bigger than a wiring slice, its
    own future item. Found in
    passing, not fixed (own future
    cleanup): `PlatformType.cs` is a stale 2-member (Twitch/Discord) enum unrelated to the real
    platform-routing convention. **`active_viewers` multi-platform DONE (e8637331)**: it filtered
    `ChatMessages` by broadcaster (already platform-agnostic) but then joined only against
    `Users.TwitchUserId`, silently dropping non-Twitch chatters. Fixed by resolving distinct
    `(Provider, UserId)` chat keys through the existing `UserIdentity` table (unique on
    `Provider`+`ProviderUserId`, already used by `IUserIdentityService`) instead of hardcoding Twitch —
    reused the same identity shape `Keyword`-mode entries already carried. Proven by a test that seeds a
    viewer with ONLY a Kick identity + Kick chat message (no Twitch at all) and asserts the drawn
    `GiveawayWinner` row carries the Kick provider/id. S065 is now fully closed — every open sub-item
    resolved, blocked-and-documented, or explicitly deferred.
- **S066** Moderation reach (U·B3) — fully CLOSED this session.
  **Concurrency guard on whole-config POST DONE, verified (8a245e68)**: the local AutoMod-like config
  (link/caps/banned-phrases/emote-spam filters) has no dedicated entity — it lives as free-form `Record`
  rows (`RecordType="moderation_rule"`) in a table shared with many unrelated record types, so
  blanket-marking a concurrency token would silently change behavior for every other writer of that
  table. Reused the existing `CurrencyAccountService` conditional-`ExecuteUpdateAsync` pattern instead
  (no new column, no migration): each write is guarded on `Id == existing.Id && Data == existing.Data`
  from the read it started from; zero matched rows → `Result.Failure(..., "CONCURRENCY_CONFLICT")`
  instead of silently overwriting. Test-first with a real `DbCommandInterceptor` landing a genuine
  concurrent write into the exact race window — deterministic, no timing/threading flakiness. 2 tests,
  stale-version rejected / current-version persists.
  **Chat-settings slow/followers/unique/non-mod fields DONE, verified (e63d66d2)**: found a much bigger
  bug than "missing fields" — `ChatController`'s GET/PUT/PATCH `.../chat/settings` never called Twitch
  at all, only read/wrote a fake local `Configurations` DB row; every toggle was truthful-data-violating
  no-op on the real channel. Rewired all three actions to the real Helix
  `Get/UpdateChatSettingsAsync` (already fully field-complete), removed the dead local-persistence path.
  Added the 2 fields genuinely missing from `ChatSettingsDto` (`UniqueChatMode`/R9K,
  `NonModeratorChatDelay`+duration) — slow-mode and followers-only fields already existed on the DTO but
  were equally never reaching Twitch until this fix. 3 tests assert the real outbound Helix request
  contents. Dashboard UI exposure for the 2 new fields is its own fast-follow, out of this slice's
  backend-only scope.
  **Full AutomodConfigDto CLOSED — naming collision, not a gap (75b02798)**: `AutomodConfigDto` is the
  bot's own LOCAL moderation feature (link filter, caps filter, banned phrases, emote spam) — unrelated
  to real Twitch AutoMod. The actual Twitch AutoMod Settings DTO (`TwitchAutoModSettings`/
  `UpdateAutoModSettingsRequest`) was already field-complete (all 9 documented categories). Real gap was
  test coverage only — existing tests asserted just `OverallLevel`, so a dropped category field would
  never fail a test. 2 new tests assert every field on both request and response. **No dashboard form
  exists for the real Twitch AutoMod settings at all** (only the unrelated local-feature DTO has one) —
  flagged as its own future follow-up, out of this slice's scope.
  **Mod add/remove + clear chat DONE, verified (4391b58b)**: Helix coverage already existed
  (`ITwitchModeratorsApi.AddModeratorAsync/RemoveModeratorAsync`, `ITwitchModerationApi.
  DeleteAllChatMessagesAsync`) — pure wiring. `IModerationService` gained
  `GetModeratorsAsync/AddModeratorAsync/RemoveModeratorAsync/ClearChatAsync`; new
  `GET/POST /moderators`, `DELETE /moderators/{userId}`, `POST /chat/clear` endpoints gated by existing
  `moderation:moderator:write`/`moderation:delete_message` action keys (broadcaster token, not
  delegable to an operator per Twitch's own requirement). Dashboard Moderation screen gained a
  Moderators section (list/add-by-id/remove) and a confirm-gated Clear Chat action, i18n en+nl.
  218/218 Moderation tests green, jvmTest + wasmJs compile clean.
  **Chat-filters screen DONE, verified (ea18f8a4)**: backend (`ChatFiltersController` +
  `IChatFilterService`) and the KMP client/DTOs already existed but no screen used them; Moderation
  page now has a full Chat filters section — list/add/toggle/delete, match-type (pattern/regex or word
  list) + action (delete/timeout/hold/flag/escalate) picker, role-gated the same as other moderation
  writes, i18n en+nl, `ModerationControllerTest` covers load/create/toggle/delete. Non-blocking follow-up
  found: `ChatFilterDto.FilterType`/`.Action` enums have no `JsonStringEnumConverter` registered, so GET
  responses serialize as raw ordinals not names — works today because the dashboard round-trips exact
  enum names and treats the read value as opaque display text, but is worth a real backend fix later.
  DONE-WHEN MET (f3ca6fa0 backend + e37f028c dashboard): AutoMod held-message queue —
  `ModerationQueueItem` (J.1) enqueued from `automod.message.hold`, resolved via
  `GET/POST .../moderation/automod/queue` relaying through the already-built Helix
  `POST /moderation/automod/message`; a pending-queue panel on the Moderation screen lets a mod
  approve/deny, mirroring the viewer-reports panel. Remaining sub-items above are still open — the
  tracker item stays until they're picked up.
- **S067** Music UX (U·B4) — fully CLOSED this session except 🔒 cost/max-duration/cooldown fields
  (owner-locked, not to be built without an explicit owner call). Done-when: every SR toggle changes
  what `!sr` does (test per setting).
  **`public-sr` rate policy DONE, verified (bbd7c390)**: `PublicSongRequestController` already carried
  `[EnableRateLimiting(RateLimitPolicyNames.Anonymous)]` (the S114 tiered pattern — 120 req/min sliding
  window, partitioned per IP, same mechanism webhooks/overlays/OAuth relay already use) — no new policy
  needed. Real gap was proof: existing unit tests called controller methods directly, bypassing
  middleware entirely, so nothing actually confirmed the throttle fired. New tests exercise the real
  `PartitionedRateLimiter<HttpContext>` built from the policy — 200 acquisitions from one IP proves
  exactly `PermitLimit` (120) succeed and the rest are rejected leases, plus a stays-under-limit
  regression case.
  **Token → URL DONE, verified (1e272400)**: the dashboard's Music screen already had a login-based
  pretty share link but showed the raw SR-page token as bare text with no URL/copy button — a streamer
  without a resolvable login had no working shareable link. New `buildTokenUrl` (reuses the existing
  `baseUrlProvider`, no hardcoded scheme/host) renders a real `/sr/{token}` URL with a copy button,
  updating live on rotate. Test proves the URL is built from the real origin + token, not hardcoded.
  **Hub-driven reloads + polling fan-out DONE, verified (2f5d3434)**: investigated both — hub-driven
  reload was ALREADY correctly implemented (`SongRequestsController.subscribeToHub` reloads on
  `MusicStateChanged`, dedupes redundant same-track pushes), and no polling loop exists at all to bound
  (grepped for `delay`/`Timer`/`poll`, zero matches — screen only loads once + the hub subscription).
  Added 2 regression tests since the correct-but-untested hub-reload path had no coverage.
  **Dashboard queue-push DONE, verified (aaf4851b)**: `SongRequestQueueChangedEvent` (add/remove/
  promote/ban) previously only reached the `sr_queue` WIDGET channel, never `DashboardHub` — a mod
  acting from the dashboard saw no live update for a pure queue mutation with no track change.
  `SrQueueBroadcastHandler.cs` now also pushes via `NotifyChannelAsync(..., "sr_queue_changed", ...)`,
  reusing the exact same `Items` payload already built for the widget push (no recomputation). Test
  proves BOTH the widget push and the new dashboard push fire with matching item payloads.
  **`RequestedBy` not the owner key DONE, verified (d587c4d5)**: traced all 3 admission paths (chat
  `!sr`, pipeline action, public `/sr/` page) — all 3 already correct. Found the REAL broken path was a
  4th one: the authenticated `POST .../music/queue` endpoint (the dashboard's own participant flow)
  trusted the request body's `RequestedBy` alone and never consulted the caller's own JWT identity —
  the dashboard always posts `requestedBy = null`, silently falling through to `"anonymous"` even for a
  logged-in viewer. Now falls back to `User.GetDisplayName()` only when the body omits it, so a viewer's
  self-submitted request is attributed to themselves; an operator naming a target viewer explicitly is
  still honored unchanged. 2 tests, both cases.
  **Bounded steppers + enum lists DONE, verified (c54d6573)**: `MaxQueueSize`/`MaxRequestsPerUser` were
  plain unbounded text fields — replaced with a new `BoundedIntStepper` clamped to the REAL backend
  `[Range]` bounds (1-500, 1-50). `PreferredProvider`/`MinTrustLevel` were already proper pickers, not
  free text — but no backend pick-list endpoint exists for either, so their options stay hardcoded
  client-side (matching the backend's fixed `[RegularExpression]` sets exactly); adding a real pick-list
  endpoint needs backend work, out of this slice's frontend-only scope, own follow-up. 6 tests, real
  clamp-bound behavior asserted.
  DONE (8604498a, ac02cc52): all 7 `MusicConfigDto` admission settings now enforced in `MusicService` —
  `IsEnabled`/`MinTrustLevel` refuse before ever resolving a provider (both the chat command and the
  pipeline action pass their resolved role level); `PreferredProvider`/`AllowSpotify`/`AllowYouTube`
  steer `GetActiveProviderAsync`'s selection; `MaxQueueSize`/`MaxRequestsPerUser` gate the shared
  `EnqueueResolvedAsync` enqueue point both `AddToQueueAsync` and `RequestTrackAsync` fold into. Every
  admission setting now changes `!sr` behavior.
  DONE (b30bf452): the duplicate config editor is consolidated — Music's out-of-place 7-field editor
  (frontend-ia.md puts config ownership on Song Requests, not Music) is removed; Song Requests' editor
  gained the 4 fields it was missing (`PreferredProvider`/`MaxQueueSize`/`MaxRequestsPerUser`/
  `MinTrustLevel`), so exactly one screen edits `MusicConfig` now, with all 7 settings.
  **Queue promote + ban-track DONE, verified (ad40c8f1)**: `IFairQueue<T>.MoveToFront` (recalculates
  every owner's rank after) + `IMusicService.PromoteToTopAsync` (real reorder, persists, republishes
  `SongRequestQueueChangedEvent`); `BanQueuedTrackAsync` reuses `IBlockedTrackService.BlockAsync` (same
  one `!bansong` uses) targeted at a QUEUED position, not now-playing, then removes it from the live
  queue. `POST .../music/queue/{position}/promote` + `/ban` endpoints. 4 new tests, real
  reorder/block/removal asserted, not "no exception".
  **S067b refund DONE, verified (a1c1f014)**: built the missing foundation — `Cost` (int, default 0) +
  `RequesterUserId` (nullable) added to `SongRequestQueueItem` + the in-memory `SongRequestEntry`
  (both migration assemblies, EF-generated not hand-written); new `RefundSongRequest`/`SongRequest`
  currency enum entries (no music-specific type existed, minimal addition following the existing
  per-feature Spend/Refund convention); `MusicService.RefundIfPaidAsync` mirrors `MediaShare`'s
  `RefundIfChargedAsync` pattern, wired into `RemoveFromQueueAsync` + `BanQueuedTrackAsync` (not
  `PromoteToTopAsync`) — fires only when `Cost > 0` and a requester is set. No admission path charges for
  song requests today, so tests seed a paid entry directly to prove the refund MECHANISM independently
  of the still-nonexistent charge — correctly not fabricating one.
  **S067c dashboard UI for promote/ban/paid indicator DONE, verified (77832663 + follow-up fix
  bd5051df)**: `SongRequestsApi.kt`/`SongRequestsController.kt`/`SongRequestsScreen.kt` gained
  promote/ban row actions (calling the real backend routes) and a "paid" badge for `Cost > 0` rows, ban
  gated by a confirm dialog, i18n en+nl. **Caught + fixed same session**: the builder correctly flagged
  that the backend's `QueueItemDto`/`MusicController.GetQueue` never actually projected `Cost` onto the
  wire — the domain-level `MusicQueueItem` record itself had no `Cost` field either, one layer deeper
  than the builder found, so the paid badge would always have read 0 in production despite compiling.
  Fixed the full chain (`MusicQueueItem` → `QueueItemDto` → the controller's mapping), new test proves
  `GetQueueAsync` surfaces the real cost for a paid entry and 0 for a free one. Remaining sub-items
  (public page polish, token→URL, bounded steppers, enum lists from API, rate policy, `RequestedBy`,
  hub-driven reloads, polling fan-out, cost/duration/cooldown) are still open — tracker stays open.
- **S068** Legacy builtins — `!discord` (needs a Discord invite-link concept first — backend gap, not a
  chat-builtin task) (U·C7). Done-when: a fresh channel has every legacy command or a seed for it.
  **Seeded fun-command preset pack DONE, verified (27c8b1dc)**: new
  `FunCommandPresetPackSeedOnOnboardingHandler` (`IEventHandler<ChannelOnboardedEvent>`, same
  auto-discovered pattern as `SystemWidgetSeedOnOnboardingHandler`) seeds 6 real custom commands
  (`!8ball`/`!hug`/`!slap`/`!ping`/`!rps`/`!compliment`) via `ICommandService.CreateAsync` — no `!dice`,
  redundant with the existing random-roll builtin. Idempotent via `CreateAsync`'s existing duplicate-name
  check (skip, not clobber, so a streamer's own same-named command survives onboarding untouched). 4
  tests over a real SQLite context, not mocked — real persistence + read-back proven.
  **On-connect announcement DONE, verified (1e65b8d7)**: new opt-in `Channel.AnnounceOnConnect` (default
  OFF per opt-in/default-deny house rule, both migration assemblies) — when on, `ChannelService.JoinAsync`
  composes a tone-resolved message via `IBuiltinResponseComposer` and sends it through `IChatProvider`,
  reusing the exact composer + send mechanism the `stream.online` path already uses. 2 tests (on sends
  the real message, off/default sends nothing).
  **`!leaderboard`/`!playlist` DONE, verified (f1d4f0e9)**: `!leaderboard` reads
  `IEconomyLeaderboardService.ListConfigsAsync`/`GetRankingAsync` (first public config, no
  dashboard-default flag exists yet); `!playlist` reads `IMusicService.GetQueueAsync` (current track +
  up to 5 upcoming — no shareable playlist URL exists on the service, stays a chat summary). Both route
  through `IBuiltinResponseComposer`; new `"leaderboard"`/`"playlist"` keys fall through cleanly to
  neutral fallback since `ToneTemplateCatalog.cs` wasn't in this slice's touch-list (own follow-up).
  `!songhistory` OUT-OF-SCOPE FOUND: no recently-played/song-history read path exists anywhere in the
  Music module (`IMusicService.GetQueueAsync` only returns current + forward queue) — real gap, not
  built. 4 tests, real seeded data asserted including a truthful empty-state case.
  **`!bansong`/`!whisper` DONE, verified (001aed7b)**: both reuse existing domain capability
  (`IBlockedTrackService.BlockAsync` off the real `GetNowPlayingAsync` track, `IPlatformDirectMessageSender`
  via `ITwitchUsersApi.GetUsersByLoginsAsync`-resolved id) — no new domain state invented. `!discord`
  found genuinely unbuildable: no invite-link concept exists anywhere in the Discord domain
  (`IDiscordGuildService`/`DiscordGuildDirectoryService` have no invite-URL field), tracked as its own
  future backend gap, not a chat-builtin task. 5 tests, real side effects asserted.
  **`!help`/`!commands` DONE, verified (1ac80938)**: both reuse the existing `ICommandService`/
  `IBuiltinCommandService` read paths (no duplicated query logic); `!commands` merges enabled custom +
  builtin command names; `!help <name>` resolves the real `CommandDto.Description`, falls back sanely
  for builtins/unknowns. 5 tests, real service data not hardcoded strings. **Caught + fixed in the same
  session**: neither was actually registered in DI, so both were unreachable from real chat despite
  green tests — `CommandsBuiltin`/`HelpBuiltin` added to `DependencyInjection.cs`'s
  `IBuiltinCommand` registrations (ae2629e0). Lesson: a builtin's test suite passing does not prove it's
  wired — always confirm the DI registration line exists too.
  **Bot-voice tone parity for all 4 legacy builtins DONE, verified (d5905b10)**: `CommandsBuiltin`/
  `HelpBuiltin`/`LurkBuiltins`/`AccountAgeBuiltin` replied with hardcoded, tone-less strings; now routed
  through the same `IBuiltinResponseComposer` pipeline actions use, with new `ToneTemplateCatalog` slots
  for each (5 tone variants each). Error/unresolved-account strings stay neutral per the codebase's own
  convention. 14 tests, sassy-vs-informative variants asserted as real catalog content not the old
  literal string.
  **`!lurk`/`!unlurk`/`!accountage` DONE, verified (042a3b1f)**: `User.IsLurking` (new field, both
  migration projects) flipped by the two new builtins with a confirming reply; `!accountage` resolves
  `created_at` from an already-hydrated row or falls back to a live Helix Get Users call and persists it,
  reporting a platform failure cleanly if that call fails. 5 new tests, all assert the real row/reply,
  not "no exception".
- **S069** Bot voice everywhere — tone slots for usage/errors;
  permit via identity path; tone catalogue per locale (U·C7, K copy). Done-when (narrowed per owner
  call below): the bot's own system-authored messages (builtins, `send_message`'s default/fallback
  copy) sound consistent across surfaces; user-authored content is explicitly exempt.
  **Custom commands/timers/event responses/chat-triggers/`send_message` tone — CLOSED, by design, not a
  gap (owner call 2026-09-01)**: attempted to wire tone into custom-command responses and
  `SendMessageAction`, and found `IBuiltinResponseComposer`/`ToneTemplateCatalog` only restyle a fixed
  set of hand-authored SYSTEM message variants — there is no generic mechanism to restyle a streamer's
  own freeform custom-command/pipeline text. Owner, verbatim: "the tone choices are specifically for the
  system, the user just creates their own templates as they see fit... how they choose their random
  replies or custom code scripts does not influence this." Tone applies ONLY to the bot's own default
  system voice (builtins, system fallback copy) — never rewrites what a streamer authored themselves.
  The Done-when is narrowed accordingly; nothing further to build here.
  **GDPR whisper-with-fallback CLOSED N/A**: every existing GDPR chat reply
  (`GdprSelfServiceExecutor.ForgetAsync`/`ExportAsync`/`StatusAsync`) is already designed strictly
  PII-free by construction (own doc comment: "chat is public, so they carry state words, never data") —
  the export path never posts actual data/tokens/links in chat, only points to the dashboard/operator.
  No sensitive payload exists anywhere that would need whisper-first delivery; building the mechanism
  would be speculative machinery with nothing real to protect.
  **One reply-or-mention helper DONE, verified (e3463e7e)**: only 2 real duplicate call sites found
  (`SendReplyAction.cs`, `ChatMessageHandler.SendResponseAsync`) — both already used the identical
  fallback format and shape, no inconsistency to resolve, pure consolidation. New
  `ReplyOrMentionComposer.Compose` (mirrors the `MentionParser` static-helper pattern) repoints both. 3
  new tests + 46 existing regression tests unmodified and passing.
  **One `ParseUserMention` DONE, verified (2a05e4de)**: found 10 independent inline mention-parsing call
  sites (not 3-4 as guessed), all consistent behavior (trim + strip one leading `@`), extracted into
  `NomNomzBot.Application.Commands.Builtin.MentionParser.ParseUserMention` and repointed every site 1:1
  with no behavior change. 7 new unit tests; existing call-site tests confirmed unaffected for the sites
  buildable in isolation (2 unrelated concurrent WIP branches briefly blocked a full-tree regression run
  — not a defect in this slice, re-verify once the tree is clear).
  **Legacy-builtin tone parity DONE, verified (d5905b10)** — see S068 for detail: `CommandsBuiltin`/
  `HelpBuiltin`/`LurkBuiltins`/`AccountAgeBuiltin` now route through `IBuiltinResponseComposer` like
  pipeline `send_message` does.
  **`announce` action/toggle DONE, verified (a3666432 + cf959b9e)**: Helix `SendAnnouncementAsync`
  already existed (full-API-coverage rule) — new `AnnounceAction` pipeline action (type `"announce"`,
  auto-registered via the `ICommandAction` scan) mirrors `SendMessageAction`'s tone/template resolution
  with a `color` field (primary/purple/blue/green/orange, invalid input normalizes to `null`). 4 tests
  (real Helix call with resolved message+color, invalid color normalized, missing message fails without
  calling Helix, Helix failure surfaces in `ActionResult`). **Caught + fixed same session**: the new i18n
  keys weren't in the committed `schema-i18n-keys.manifest.json` nor translated (en+nl) —
  `SchemaLocalizationManifestTests` genuinely failed on this, not a pre-existing/unrelated issue as first
  assumed; manifest regenerated + real en/nl strings added (cf959b9e). Lesson: a new pipeline action's
  help-text/description fields need BOTH a manifest entry AND real strings.xml translations, or the
  drift guard fails — always run `SchemaLocalizationManifestTests` for any new `ICommandAction`.
  **Inbound whisper handler DONE, verified (8fb98c69)**: `user.whisper.message` EventSub subscription +
  translator already existed (`UserWhisperMessageTranslator` → `WhisperReceivedEvent`), and the generic
  `NotificationDispatcher` already journals every raw notification before fan-out — an inbound whisper
  was never actually silently dropped, it just had no test proving that end-to-end for this topic. New
  test proves a real whisper payload journals + publishes correctly; no production code was missing.
  **Usage/error tone slots PARTIAL, verified (b5058e7e)**: 16 hardcoded usage/error strings found across
  builtins not routed through `IBuiltinResponseComposer`; wired the 5 most-commonly-hit
  (`WhisperBuiltin` usage+notfound, `BanSongBuiltin` nothing-playing, `UpdateUserInfoBuiltin` notfound,
  `VolumeBuiltin` usage), same `ToneTemplateCatalog` pattern as the prior success-path tone-parity slice.
  16 tests, sassy-vs-default variants asserted as real content.
  **Remaining 11 usage/error tone strings DONE, verified (44d4c3bb)**: `WhisperBuiltin`
  (TwitchUnavailable/NotAvailable), `BanSongBuiltin` (CouldNotBan), `UpdateUserInfoBuiltin`
  (TwitchUnavailable/UpdateFailed/LoginUnresolved/OwnInfoOnly), `VolumeBuiltin` (CannotRead),
  `GameBuiltins`/`CoinflipBuiltin`/`DiceBuiltin`/`SlotsBuiltin` (AccountUnresolved, per-game-key
  registered), `SongRequestBuiltin` (Disabled, `NoProviderMessage` converted to async). Same
  `ToneTemplateCatalog` pattern as before; every string now has real per-tone catalog content. 41 tests
  total across all touched builtins, sassy vs default asserted as real content.
  **Per-locale tone catalogue — DEFERRED, needs more product thought (owner call 2026-09-01)**:
  investigated and found a genuine blocker, not a wiring gap — `ToneTemplateCatalog.Pick(personality,
  builtinKey, slot)` has no locale parameter at all, no call site passes one, and `Channel.Language`
  (already confirmed dead for chat-reply purposes by a sibling slice) is read nowhere for bot-reply
  text. Building Dutch tone content today would be inert — collected but unreachable, the exact trap
  `Channel.Language` itself already represents. Whether the bot should ever reply in a non-English
  language, and what would select it, is an unresolved product question — owner deferred rather than
  deciding today. Revisit later; do not build Dutch tone content until a selection mechanism is decided.
  Remaining S069 scope: nothing else — everything but this deferred item is closed.
- **S070** Settings + onboarding truth (U·B6) — fully CLOSED this session.
  **Swallowed regrant/reconcile failures DONE, verified (f8930c74)**:
  `IntegrationTokenVault.StoreTokensAsync` awaited `IScopeGrantService.ReconcileGrantedScopesAsync` and
  discarded the `Result` — a reconcile failure (connection vanished mid-refresh, `NOT_FOUND`) was
  invisible, `StoreTokensAsync` always fell through to `Result.Success()`. Now captures and propagates
  the real failure; no refreshed-token event fires on a failed store. Frontend was already correct — no
  fix needed there. Test proved red before, green after.
  **Copy fixes CLOSED — verified clean, no changes needed**: audited `SetupWizardScreen.kt`,
  `SettingsScreen.kt`, both `strings.xml` files against all 5 sibling behavior changes this session
  (botLinePrefix, applyBasics failure, timezone, scope-feature-map/regrant, auto-join) — every string
  already matches current behavior with complete en/nl pairs. The sibling slices kept their own copy
  accurate as they shipped; nothing stale was left behind.
  **Scope→feature map + re-grant on Settings DONE, verified (fe5762ec)**: backend already had the full
  matrix (`GET /twitch/diagnostics/scopes` → `TwitchScopeDiagnosticsDto`) and the additive device-code
  re-grant endpoint — pure frontend wiring. New "Permissions" section on Settings shows one row per
  (scope, feature, granted), a Broadcaster-gated re-grant button reusing the exact
  `startRegrant()`/`AuthApi.pollDeviceLogin` mechanism Integrations already uses (no new OAuth flow).
  Test proves the matrix reflects the real backend response and re-grant drives the real device-code
  flow, re-reading only after backend-reported approval.
  **Timezone DONE, verified (772e5b69)**: `User.Timezone` was saved but only ever read back for
  display — genuinely dead. Now loaded into `ChannelContext` (`ChannelRegistry`, refreshed on
  `InvalidateSettingsAsync`); `TemplateResolver`'s `{time}`/`{date}` convert from UTC into the channel's
  configured timezone (falls back to UTC if unset/unrecognized), `{time.utc}` untouched. 3 new tests +
  51/51 Templating suite green. **Language found dead too** (saved, never read — no server-side i18n or
  bot-reply locale selection exists; the dashboard's own `LanguageStore` is a separate, unrelated UI-
  language concept) but NOT removed this pass: a clean removal touches the wizard, Settings, backend
  DTOs, the KMP client, the openapi snapshot, and `ApiContractTest` — beyond this slice's scope, flagged
  as its own future slice rather than half-removed.
  **`applyBasics` failure reported DONE, verified (bda60814)**: `applyBasics()` ignored the `ApiResult`
  from both `channelsApi.primaryChannel()` and `channelSettingsApi.updateBasics()` — on failure it just
  returned silently with no error surfaced anywhere. New `SetupError.Basics(detail)` variant renders via
  the same destructive-toned `ErrorText` pattern as `SetupError.SignIn`; wizard no longer advances on
  failure. Tests cover failure-surfaces-real-message and success-after-prior-failure regression.
  **Wizard `botLinePrefix` contract DONE, verified (978b8b2d)**: `SetupBasics` had no field for the D5
  "user-defined line prefix" the bot uses while typing as the streamer's own account, and
  `applyBasics()` never sent it — skipping bot connection silently left it unset with no way to
  configure it. `SetupState.Steps` now tracks `platformBotConnected` (from the backend's re-read step
  completion, never optimistic); `applyBasics()` sends the typed prefix when no bot is connected, or
  `null` ("leave unchanged") once one is — matching the rule Settings' own `BasicsForm` already
  enforces. The separate legacy `twitch.bot_username` system-config field (`twitch_app` step) is
  unrelated and was left untouched — no bug found there. 2 new tests assert the exact persisted value.
  **Auto-join semantics DONE, verified (9fbadca8)**: `ChannelService.JoinAsync`/`LeaveAsync` and the
  settings `AutoJoin` toggle only ever flipped `Channel.Enabled` in the DB — the actual EventSub
  subscribe/unsubscribe only happened on `BotLifecycleService`'s 5-minute reconcile tick, so toggling
  auto-join silently did nothing live for up to 5 minutes despite the controller's own doc comments
  claiming it joins immediately. `ChannelService` now calls `EnsureSubscribedAsync`/`UnsubscribeAllAsync`
  directly on join/leave/toggle (idempotent, reconcile tick stays a safe no-op fallback). 23/23 tests
  green, assert on the actual EventSub call not DB state.
  **Integrations read-failure state DONE, verified (c4c9a6b7)**: `IntegrationsController.refresh()` was
  swallowing a failed `integrationsApi.status()` call into `emptyList()`, indistinguishable from "zero
  integrations connected" — violated the truthful-data house rule. `refresh()` now sets the existing
  (previously `load()`-only) `IntegrationsState.Error` and returns early; the screen renders a
  destructive-toned retry card instead of plain text. Test covers failure → `Error` state → retry →
  real `Ready` state, i18n en+nl.
- **S076** Multi-chat as a tool (U·B3) — fully CLOSED this session.
  **Mod-log names+time DONE, verified (c2e4d481)**: `ModActionDto` gained `ModeratorDisplayName`/
  `Timestamp`; no new lookup needed — `UserBannedEvent`/`UserTimedOutEvent`/`UserUnbannedEvent` already
  carried `ModeratorDisplayName` and inherited `OccurredAt` from `DomainEventBase`, simply never plumbed
  through at the 3 `BanBroadcastHandlers.cs` construction sites (ban/timeout/unban). Distinct from
  `TargetDisplayName`, which genuinely needs the `IHubUserEnricher` DB lookup since the source event only
  carries the target's raw id. 4 tests assert the real resolved name + matching timestamp.
  **Shield mode pushes consumed DONE, verified (9395077b)**: `MultiChatController` now handles
  `HubEvent.ChannelEvent` for `shield_mode_begin`/`shield_mode_end` (wire shape confirmed against
  `ShieldModeBeganBroadcastHandler`/`ShieldModeEndedBroadcastHandler` — no new DTO needed, already
  decoded), toggling a new `shieldModeActiveChannelIds` set scoped to watched channels only. **Mod-log
  names+time OUT-OF-SCOPE FOUND**: `ModActionDto` (`HubResponseDtos.cs:56`) carries `ModeratorId` but no
  `ModeratorDisplayName`/`Timestamp` at all — unlike the enrichment already present on
  `TargetDisplayName`/`TargetAvatarUrl` on the same record. A real backend DTO gap, not a frontend
  resolution bug; nothing on the wire for the client to surface. Flagged for the backend track rather
  than guessing a shape.
  **`joinedChannels` preserved on reconnect DONE, verified (be76f6e5)**: `DashboardHubClient` already
  replayed its own transport-level joined set on reconnect — the raw symptom wasn't reproducible there —
  but `MultiChatController` gained its own controller-owned guarantee anyway: a `Reconnecting→Connected`
  edge now re-invokes `joinChannel` for every id in the current watched set, never a default. **Watch
  list persisted PARTIALLY DONE**: `WatchListStore` interface added (mirrors the existing
  `EmojiStyleStore` contract) with a `NoOp` default so nothing regresses; a real persistent
  implementation (new expect/actual store + `AppGraph.kt` DI wiring) is OUT-OF-SCOPE for this slice —
  needs its own follow-up to actually survive an app restart. 2 new tests (reconnect rejoins the real
  watched set not `c`; a second controller instance sharing a fake store restores the persisted set).
  **Moderation actions + composer DONE, verified (9648b235)**: `MultiChatController` gained
  `sendMessage`/`deleteMessage`/`timeoutUser`/`banUser`, all delegating to the existing `ChatApi` (same
  calls the single-channel Chat page uses — no duplicated network logic). Screen gained a composer
  (channel-target select + text + send) and a per-row moderation menu (delete/timeout/ban, confirm-gated),
  role-gated via `rememberManageDecision`; each row action targets that message's own channel, not the
  composer's currently-selected one. 5 new tests assert the real `ChatApi` calls + feed mutation.

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
  **Backend aggregation DONE, verified (7ee7b065)**: new `IActionRequiredInboxService.GetItemsAsync` +
  `GET /api/v1/channels/{channelId}/notifications/action-required`. Only 2 of the 5 named categories had
  a real, honest backing signal today — dead/expired integration tokens (`IntegrationConnection.Status`)
  and held AutoMod messages (`ModerationQueueItem` pending rows) — both included. The other 3 skipped,
  not fabricated: missing scopes (every gap is `IsProgressive=true` by design, not an error state), failed
  timers (`Timer` has no run-failure tracking at all), pending unbans (only live-fetchable under an
  operator's own token, not a stored/aggregatable signal). 4 tests, real seeded-row assertions +
  tenant-isolation proof. **Still open: Home hero tile + collapsed activity feed + first-run next steps**
  (frontend, next sub-slice) — Done-when ("visible on Home within a minute") isn't met until the tile
  consumes this endpoint.
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
