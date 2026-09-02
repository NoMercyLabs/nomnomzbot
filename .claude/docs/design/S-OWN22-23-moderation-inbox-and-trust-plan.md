# S-OWN22 / S-OWN23 — Actionable attention inbox + trust & auto-action configuration

Owner request 2026-09-02 (jump the queue). Two slices, executed in order. This doc is the worked-out
plan; the queue entries in `SHORTCOMINGS-EXECUTION-PLAN.md` point here. Delete this file when both
slices are closed.

**Owner's words (condensed):** the Home "Needs your attention" items give nothing to do and can't be
removed; placement is wrong (belongs between stream status and Recent Activity); each item needs a
modal with real moderator actions (allow, block, ban, timeout, …); the user must have full control
over what is bannable and all trust-score weights as advanced configuration, with every weight
explained and full awareness of what gets auto-actioned; this ties into our own Sery-bot plan
(`spec/spam-defense.md`). Bonus (aaoa): stat number cards become a single row above the stream title
card and action buttons.

**Binding context** (all grounded 2026-09-02):

- Inbox is derived per request, id-less: `ActionRequiredInboxService.cs:40-104` computes from
  `ModerationQueueItems` (AutoMod holds, `:78-104`) + `IntegrationConnections` (dead tokens,
  `:51-76`). DTO `ActionRequiredItemDto.cs:19-26` has no id, no dismiss.
- The row's only interaction is a **dead click**: `deepLinkRoute` is a URL path
  (`"/moderation/queue"`) but `HomeScreen.kt` `onNavigate` takes a `ShellRoute` name (`:236-238`).
- Severity is binarised in `ActionRequiredRow` (`HomeScreen.kt:648-656`) — non-critical always
  renders "Warning", so `info` would be mislabeled.
- Approve/deny already exists end-to-end: `POST …/moderation/automod/queue/{queueItemId:guid}/resolve`
  (`ModerationController.cs:1184-1205` → `ModerationQueueService.cs:163-238`, Helix relayed first via
  `ManageHeldAutoModMessageAsync`, local status stamped only on Helix success). Client:
  `ModerationApi.kt:281-288,636-652`, `AutomodQueueRow` `ModerationScreen.kt:1798-1860`.
- Helix client is field-complete: ban/timeout/unban (+ `AsOperator` variants), blocked terms,
  `GetAutoModSettingsAsync`/`UpdateAutoModSettingsAsync` (`ITwitchModerationApi.cs`) — but the two
  AutoMod-settings methods have **no controller caller** (the `GET/POST …/moderation/automod`
  endpoints serve the bot's own local `AutoModerationEngine` config, not Twitch's).
- Trust weights are compile-time consts: `TrustScoreCalculator.cs:97-106` (weights .25/.25/.30/.20,
  decays .599/.499/.999/.0003, follow ×0.75, boost, −5/−10/−30 penalties, tiers 25/50/75). The
  calculator is **shared** — song-request gating (`ITrustService`) and moderation trust
  (`ModerationProjectionService.cs:298-316`, "never a fork") both use it.
- The only auto-action thresholds today: heat auto-timeout (`AutoModConfig.AutoTimeoutOnHeat` +
  `HeatTimeoutThreshold`, default 80, `ModerationProjectionService.cs:42,331-355`, editable via
  `HeatThresholdRow`), the escalation ladder, and local `AutoModerationEngine` per-filter actions.
- `spam-defense.md` (settled, NOT implemented) sets the philosophy this must align with: §6 every
  knob stored + explained + default shown; SD7 every action explainable; SD11/SD8 no auto-action on
  standing viewers. Its `SpamDefensePolicy`/L0–L5 build order stays its own track — these slices do
  NOT implement it, they make today's real enforcement configurable and visible in a way §6 extends
  later without rework.
- Open sibling bug `S-OBS-05` (Moderation page not channel-scoped) stays its own slice; everything
  NEW here must be channel-scoped from birth (route `{channelId}` → tenant resolution as usual).

House rules that bite here: explicit types (no `var`), license header on new files, **both**
migration assemblies (SQLite + Postgres), translations = resource keys only (en + nl),
`scripts/slice-check.ps1` before each commit, tests prove behavior (state change + side effects),
truthful data (never show unenforced state), consequences visible before saving.

---

## S-OWN22 — Home layout + actionable attention inbox

### Task 1 — Home layout reorder (aaoa's bonus included) — own commit, `app/` only

**DONE, verified 2026-09-02 (`d1c45e75`)**: `ReadyContent` now emits `PageHeader` →
`ActionRequiredCard` (unmoved, per plan — Task 3/4 replace its internals separately) →
`StatTilesRow` (single row, 8 tiles at `weight(1f)`, intrinsic width below 960dp) → `LiveBanner` →
`PlatformsRow` → `FirstRunChecklistCard` → the existing two-column Row. Verified by
`compileKotlinWasmJs` (BUILD SUCCESSFUL) and diff trace; `jvmTest` could not run to green — shared
gradle build-dir contention from concurrently-running sibling agents (known trap, not a code issue),
re-verify jvmTest once the shared build dir is free.

`HomeScreen.kt` `ReadyContent` (`:290-451`). New order inside the scroll Column:

1. `PageHeader`
2. `StatTilesRow` — **single row**: all 8 tiles in one `Row`, each `weight(1f)`, min tile width
   guarded by making the row horizontally scrollable below the width where 8 don't fit (same
   breakpoint mechanism the file already uses for the two-column split; design-system spacing
   tokens only, no raw dp).
3. `LiveBanner` (stream status)
4. `PlatformsRow` ("Streaming to" belongs to the status cluster)
5. **Attention inbox** (Task 3's component; only when non-empty)
6. `FirstRunChecklistCard` (only when non-empty)
7. Existing two-column Row: Recent Activity | Quick Actions + TopCommands + ChatPollsCard

Verify in the running app (web dev run) at wide and narrow widths. `jvmTest` +
`compileKotlinWasmJs` both green (wasm-only breakage is a known trap).

### Task 2 — Backend: inbox items get identity, grouping, dismissal

**Contract** — `ActionRequiredItemDto` gains:

- `Id` (string, stable item key): held message → `held:{queueItemGuid}`; grouped item id =
  `held-user:{sourceUserId}`; dead token → `token:{connectionId}:{invalidatedAtUtcTicks}` (a token
  that dies again after a fix produces a NEW key, so an old dismissal can't hide it).
- `SourceUserId` / `SourceUserName` (held messages), `Count`, `QueueItemIds: List<Guid>` —
  held messages are **grouped per user**: 6 holds from `ashleyflores_01` = ONE item ("6 messages
  from ashleyflores_01 held for review"), not 6 rows (the current Home screenshot shows exactly
  this failure).
- Keep existing fields; `deepLinkRoute` stays as-is on the wire (frontend stops using it — Task 3).

**Dismissal** — new entity `Domain/Notifications/Entities/ActionRequiredDismissal.cs`
(`ChannelId`, `ItemKey`, `DismissedByUserId`, `DismissedAt`, soft-delete convention, unique
`(ChannelId, ItemKey)` respecting the cross-tenant unique-index rule). EF config + **both**
migration assemblies. Endpoint: `POST /api/v1/channels/{channelId}/notifications/action-required/dismiss`
body `{ ids: [string] }` (Gate-2: same read/manage pattern as the controller's existing key —
dismissing is a mod-surface act, use `moderation:queue:resolve`'s read-sibling if one exists, else
`dashboard:read` is too weak: add `notifications:dismiss`, registered like every other action key).
Dismissing a grouped item writes one row per contained `held:{guid}` key.
`ActionRequiredInboxService.GetItemsAsync` excludes dismissed keys. Resolving a queue item removes
its item naturally (already true — item derives from `Status == Pending`).

**Tests** (Infrastructure.Tests, behavior not surface): grouping (3 holds same user + 1 other user
→ 2 items with correct counts/ids); dismissal persists and filters (dismiss → re-query excludes;
new hold from same user AFTER dismissal → new item appears); dead-token key changes on
re-invalidation.

### Task 3 — Backend: resolve with follow-up moderation action

Extend `ResolveModerationQueueItemRequest` (`ModerationQueueDtos.cs:28-32`):
`Action` (existing `approve|deny`) + `FollowUp: string?` (`none|timeout|ban`),
`TimeoutSeconds: int?`, `Reason: string?`. Validation: follow-up only with `deny`; timeout requires
seconds in Twitch's 1s–1209600s range.

`ModerationQueueService.ResolveAsync`: unchanged Helix-first order; on `deny` + follow-up, after the
deny succeeds call `TimeoutAsOperatorAsync`/`BanAsOperatorAsync` (operator-token rule: the acting
dashboard user is the operator; broadcaster fallback per the existing performAction path). Stamp
`ResolutionAction` = `denied` | `denied_timeout` | `denied_banned` (extend the value set the entity
already stores). If the follow-up Helix call fails, the deny still stands: record `denied`, return
the follow-up failure in the envelope so the UI can say "message blocked, but the ban failed: …" —
never silently half-report.

**Tests**: deny+ban relays BOTH Helix calls in order and stamps `denied_banned`; follow-up failure
still stamps `denied` and surfaces the error; approve+follow-up → `VALIDATION_FAILED`; timeout
range validation.

### Task 4 — Frontend: attention inbox component + detail modal with real actions

New `feature/home/ui/AttentionInbox.kt` (+ state in `HomeController`), replacing
`ActionRequiredCard`/`ActionRequiredRow`:

- Row per item: severity chip (three-way: critical/warning/info — fixes the binarisation), title,
  message, count badge when grouped, relative time, **Dismiss** (X) and **Review** actions.
- Severity → design-system tokens (destructive / warning / muted per catalogue); labels are i18n
  keys, en + nl.
- Navigation: map `kind` → `ShellRoute` name in the frontend (`held_chat_message` → Moderation,
  `integration_token_dead` → Integrations); stop consuming `deepLinkRoute`. This fixes the dead
  click.
- **Held-message modal** (design-system dialog, mirrors `UserModerationContextDialog` patterns):
  - Loads the pending queue rows for the item's `QueueItemIds` via the existing
    `ModerationApi` automod-queue read; shows per message: full `MessageContentSnapshot`,
    `AutoModCategory`, held-at, and the user context strip the Moderation screen already has
    (trust/heat badges + notes affordance — reuse, don't fork).
  - Actions per message AND bulk-for-all-from-this-user:
    **Allow** (approve) · **Block** (deny) · **Timeout** (deny + duration picker: 60s/10m/1h/1d) ·
    **Ban** (deny + optional reason) — all through Task 3's endpoint;
    **Block term** (send the message text to the existing blocked-terms endpoint).
  - Every action: optimistic-free — apply on success, surface envelope errors verbatim, item/group
    leaves the inbox AND the Moderation queue list (single source: re-fetch or hub event).
- Dismiss calls Task 2's endpoint; item stays gone after reload (validate live).
- Live refresh: `HomeController.subscribeToHub` currently never updates `actionRequired`
  (`HomeController.kt:282-345`). Wire the same dashboard-hub signal the Moderation screen's
  `subscribeToHub` (`ModerationController.kt:959`) uses for `automodQueue`; if tracing shows no
  queue-change event reaches the hub, publish one from `ModerationQueueService`
  enqueue/resolve (both writers) and consume it in BOTH controllers — events wired end-to-end,
  publisher and consumer verified.
- i18n: every new string en + nl; per-string imports.

**Tests** (commonTest, controller-level like `ModerationControllerTest`): modal load maps queue
rows; allow/block/timeout/ban call the API with the right payload and remove the item on success;
follow-up failure keeps the item visible with the error; dismiss removes and survives a reload
(state re-fetch); severity mapping three-way; kind→route mapping. `jvmTest` + wasm compile +
`DesignSystemStyleGuardTest` green.

**Done-when (S-OWN22):** on Home, stat tiles render as one row above the stream card; a held
message opens a modal showing the real message + user context; Allow/Block/Timeout/Ban each work
against the live API and the item disappears from Home AND Moderation, surviving reload; Dismiss
hides an item persistently; a new hold reappears without reload (hub) or on next load; every button
validated live.

---

## S-OWN23 — Trust & auto-action transparency + advanced configuration

### Task 0 — Extract trust out of Music into its own module (owner decision 2026-09-02)

Trust is shared substrate (moderation projection + song-request gating today; spam-defense L1/L4
tomorrow), so it stops living under Music. Pure move first, behavior-identical, own commit:

- `TrustScoreCalculator`, `TrustContext`, `TrustTier` move from
  `Infrastructure/Music/TrustScoreCalculator.cs` to **`Domain/Trust/`** — the calculator is a pure
  static function with zero external deps, which is exactly what Domain holds. Namespace
  `NomNomzBot.Trust` per the everything-is-`NomNomzBot.*` rule → `NomNomzBot.Domain` project,
  folder `Trust/`.
- Music's `ITrustService` (song-request scale) STAYS in Music and consumes the moved calculator;
  `ModerationProjectionService` updates its using. No logic change, tests
  (`TrustScoreCalculatorTests`) move alongside and stay green unmodified (proof the move is pure).
- `UserTrustScore` (J.5, the per-user per-channel projection row) stays in `Domain/Moderation` —
  it is a moderation projection, not the engine.

### Task 1 — `TrustPolicy` entity (per-channel, defaults = today's consts)

New `Domain/Trust/Entities/TrustPolicy.cs` — one row per channel, tenant-scoped,
soft-delete, created-with-defaults on first read:

- Score weights (validated server-side to sum 1.0): `RequestCountWeight` .25, `AccountAgeWeight`
  .25, `ContentAgeWeight` .30, `ContentPopularityWeight` .20
- Decay rates: `RequestCountDecay` .599, `AccountAgeDecay` .499, `ContentAgeDecay` .999,
  `ContentPopularityDecay` .0003
- `NotFollowingFactor` .75 · `ReputationBoostEnabled` true · penalties `SkipPenalty` 5,
  `TimeoutPenalty` 10, `BanPenalty` 30
- Tier ceilings: `UntrustedMax` 25, `LowMax` 50, `StandardMax` 75 (ladder-valued; users see NAMES)
- Heat: `HeatHalfLifeHours` 24.0 + per-action heat deltas (seed from
  `ModerationProjectionService.HeatDeltaFor`, stored as rows/JSON — pick the shape the existing
  persistence style favors and say so in the commit)
- `AutoTimeoutOnHeat`/`HeatTimeoutThreshold` STAY on `AutoModConfig` (J.7) — referenced, not
  duplicated.

EF config + **both** migration assemblies + the ~26 DbSet test-fake touch-ups the new DbSet will
break.

### Task 2 — Calculator takes the policy; both consumers use it

`TrustScoreCalculator.Calculate(TrustContext, TrustPolicyValues)` — pure, static, defaults
preserved as the parameter's default instance. Policy resolution goes through a new
`Application/Trust/Services/ITrustPolicyService.cs` (get-or-create with defaults, tenant-scoped),
implemented in `Infrastructure/Trust/TrustPolicyService.cs`, registered in
`Infrastructure/DependencyInjection.cs`. `ModerationProjectionService` AND the song-request
trust path (`ITrustService` impl) resolve the channel's policy through it — one trust number per viewer per
channel, never a fork. Heat decay half-life and deltas read from policy.

**Guard (SD11-aligned, minimal honest version):** heat auto-timeout must never fire on the
broadcaster or a moderator — verify whether `UserHeatThresholdCrossedEvent`'s consumer already
exempts them; if not, add the exemption where the timeout is issued, with a test proving a mod
crossing the threshold is flagged, not timed out.

**Tests**: weight change moves the computed score (assert exact recomputation, not non-null);
policy weights not summing to 1.0 → validation error; song-request gating shifts with the same
policy (shared-calculator proof); mod-exemption test above; heat delta/half-life from policy honored.

### Task 3 — Endpoints

- `GET/PUT /api/v1/channels/{channelId}/trust/policy` — new `TrustPolicyController` (one
  controller per module; trust is its own module now), Gate-2 keys `trust:policy:read` /
  `trust:policy:manage` registered like every other action key. PUT validates ranges server-side
  and returns the saved policy. Contract: backend carries VALUES only; all explanation prose is
  frontend i18n (translations never in code).
- `GET/PUT /api/v1/channels/{channelId}/moderation/twitch-automod` — the REAL Twitch AutoMod
  levels via `GetAutoModSettingsAsync`/`UpdateAutoModSettingsAsync` (closes the "no dashboard form
  for real Twitch AutoMod settings at all" gap). Honor Twitch's semantics: `overall_level` XOR
  per-category values; surface which mode is active. Scopes ride the existing
  `[RequiresTwitchScope]` registry.
- Refresh `server/openapi/v1.json`; sync Kotlin DTOs; `ApiContractTest` updated.

### Task 4 — Frontend: "Trust & Automation" section on the Moderation screen

New section (beside the existing escalation/automod panels, channel-scoped from birth):

1. **What happens automatically** — a truthful, DERIVED panel (computed from the same config
   objects enforcement reads, zero hardcoded claims): current Twitch AutoMod mode/levels; each
   local `AutoModerationEngine` filter and its action; heat auto-timeout on/off + threshold; the
   escalation ladder steps; and an explicit line for what can end in an automatic ban vs. what
   never does. If a control is off, the panel says so — never show unenforced state.
2. **Trust score weights (advanced)** — every `TrustPolicy` field: control + current
   value + shown default + reset-to-default per field; each field carries a plain-language i18n
   explanation of what it measures and what moving it costs (STE style, en + nl), e.g.
   `moderation_trust_weight_account_age_explain` = "How much a user's account age counts. Raise it
   and older accounts are trusted faster. Lower it and account age matters less." Weight rows show
   the live sum with an inline error when ≠ 1.0. A blast-radius note on the section header: these
   weights also gate song requests (consequences visible before saving).
3. **Twitch AutoMod levels** — the Task 3 form, per-category sliders or overall level, matching
   Twitch's semantics, with the same explained-default treatment.

Role-gate: section at the manage floor (disable + reason tooltip below it, don't hide).

**Tests**: controller tests proving save round-trips (PUT then GET returns the edited values),
validation error rendering, derived-panel truthfulness (panel reflects a changed config object),
i18n keys exist in both locales. Live validation of every field (writes survive reload).

**Done-when (S-OWN23):** every number that influences automatic action (trust weights, decays,
penalties, tier ceilings, heat deltas/half-life/threshold, escalation ladder, local filter actions,
Twitch AutoMod levels) is editable per channel from the dashboard, each with default + plain
explanation in en and nl; edits demonstrably change enforcement (test-proven) and persist across
reload; the "What happens automatically" panel derives from live config and names exactly what can
auto-ban; the Twitch AutoMod form works against live Helix.

---

## Alignment notes (do not lose)

- `spec/spam-defense.md` §6/§7 later replaces/extends this editor with `SpamDefensePolicy` +
  `SpamDefenseDefaults` (default-tracking/pinning, dry-run, replay). S-OWN23 deliberately builds
  the per-channel editor in the same shape (value + default + explanation + reset) so §6 is an
  extension, not a rewrite. Do NOT build platform-wide defaults/pinning now (YAGNI; that ships
  with spam-defense).
- 🔒 Owner call, separately: queue `spam-defense.md` §9 build order (steps 1–3 stop the motivating
  attack) as its own slice family? The spec is settled and gated on your word.
- `ROADMAP.md`'s "Advanced moderation — specced, no backend yet" bullet is stale (all listed items
  exist in code) — fix the line when touching that file.
