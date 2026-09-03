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

## Status ledger (live — checked off as each member is PROVEN, not merely written)

- [x] 22-T1 Home layout reorder — d1c45e75 + 1b7b3723; jvmTest 966 green, wasm green
- [x] 22-T2 inbox identity/grouping/dismissal — 6ad0d283; 10 tests, 6/6 mutations kill (two were toothless and were rewritten), both migrations + PendingModelChangesGuard green, Api 871/871, csharpier clean
- [x] 22-T3 resolve deny+follow-up + queue hub broadcast — b7781133 on own22-t3; build green, 14+3 targeted tests green, 8/8 mutations killed (baseline restored), csharpier clean; full suite runs at 22-I on the merged tree
- [ ] 22-T4 attention inbox UI + modal — b77f299e green on own22-t4 (979 jvm tests, wasm); NOT merged
- [x] 22-T5 frontend consumes `automod_queue_changed` — 541d0924 on own22-t4 atop b77f299e; jvmTest+wasm green, 3/3 mutations killed (target string, Home refresh, Mod refresh), full suite restored green including new empty-state fallback test
- [x] 22-I merged t2 → t3 → t4+t5 into master (6ad0d283, b7781133, 541d0924; merge d3373017). openapi/v1.json conflict resolved to the regenerated snapshot (carries all T2 fields, T3's followUp/timeoutSeconds/reason, and both endpoints — T4's hand-edit was redundant). Merged-tree gate: server build green, csharpier clean (3098 files), Application 70/70, Domain 96/96, Api 874/874, Infrastructure 4816/4820 + 2 PRE-EXISTING load-sensitive flakes outside this slice (`Step_ConfigJsonWithoutEmbeddedType_StillExecutes` from peer commit 60835906, `Fanout_returns_before_the_slow_http_send…`) — both pass in isolation on master and on the pre-merge branches alike; filed as S-FLAKE-TIMING. jvmTest forced rerun green in 80s. Test counts reconcile: 4831 − 17 moved to Domain by T0 + 6 from T3 = 4820.
- [ ] 22-V live validation on the running app (every button, dismiss survives reload) + tracker close
- [x] 23-T0 trust extraction — 9f23af61, pure move proven (17=17 byte-identical tests), full server suite green (4805 Infra / 869 Api / 96 Domain / 70 App), csharpier clean; fast-forwarded into master
- [x] 23-T1 TrustPolicy entity + both migrations — f2d8a6e8; all 21 constants carried with shipped defaults, both migration sets (PG 20260903010857 / SQLite 20260903011018), PendingModelChangesGuard green both providers, 50 test fakes patched, blast-radius registered, csharpier clean
- [x] 23-T2 calculator takes policy — 25e75029; 12 tests asserting NON-DEFAULT values (a changed
  weight/ceiling/penalty/heat-delta/half-life changes the outcome; an untouched policy reproduces the
  shipped score exactly), full server suite green 70/106/874/4820, csharpier clean.
  **Two findings corrected the plan's own assumptions:** (a) song requests do NOT share this
  calculator — they run a separate `Music/TrustService` (0.0–1.0, own constants); the only bridge,
  `MusicService.CheckTrustPermission`, was dead code and was deleted → S-TRUST-UNIFY. (b) The
  mod/broadcaster auto-timeout exemption test this task called for **cannot be written**: there is no
  auto-timeout to exempt from. `UserHeatThresholdCrossedEvent` has zero consumers and
  `AutoTimeoutOnHeat` does not exist in code → S-HEAT-UNENFORCED. Not deferred silently — it is the
  gating question for 23-T4's automation panel, which must not claim heat auto-timeout works.
- [x] 23-T2b autoban enforcement (the gap T2 surfaced) — 2bba61d7; `HeatThresholdAutoTimeoutHandler`
  consumes the previously-orphaned `UserHeatThresholdCrossedEvent`. `AutoTimeoutOnHeat` (default OFF)
  + `HeatTimeoutSeconds` (default 600) added to `AutomodConfigDto`. Immunity is a short-circuit
  checked BEFORE the action: broadcaster + the whole moderator roster, role-agnostic; VIP-by-badge
  NOT claimed (not tracked locally). 5 tests, 3 of them immunity/opt-in. S-HEAT-UNENFORCED closed.
- [ ] 23-T3 trust-policy + twitch-automod endpoints + contract sync
- [ ] 23-T4 Trust & Automation UI (Appendix A copy) + derived automation panel
- [ ] 23-V live validation + tracker close + delete this file

---

## S-OWN22 — Home layout + actionable attention inbox

### Task 1 — Home layout reorder (aaoa's bonus included) — own commit, `app/` only

**DONE, verified 2026-09-02 (`d1c45e75`)**: `ReadyContent` now emits `PageHeader` →
`ActionRequiredCard` (unmoved, per plan — Task 3/4 replace its internals separately) →
`StatTilesRow` (single row, 8 tiles at `weight(1f)`, intrinsic width below 960dp) → `LiveBanner` →
`PlatformsRow` → `FirstRunChecklistCard` → the existing two-column Row. Fully verified 2026-09-02:
`jvmTest` 966 tests green (style guard passing; one unrelated DashboardHubClientReconnectTest
coroutine-timeout flake, green on rerun) + `compileKotlinWasmJs` green. The guard initially caught a
raw `960.dp` in d1c45e75 — fixed in `1b7b3723` by extracting the shared `Breakpoints` token
(theme/Breakpoints.kt: Compact 720 / Wide 960) consumed by HomeScreen, ShellScreen and
ParticipantShell (their grandfathered baseline entries removed). The attention card's POSITION move
(to below the stream-status cluster) intentionally rides with Task 4's new component.

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


---

## Appendix A — Trust & Automation i18n copy (S-OWN23 Task 4 lifts this verbatim)

Plain-language STE copy for every editable field: short sentences, one idea per sentence, what it
measures + what moving it costs. Key pattern `moderation_trust_<field>_title` / `_explain`. The
23-T4 agent copies these into `values/strings.xml` (en) and `values-nl/strings.xml` (nl) verbatim.

| Key stem | en title | en explain | nl title | nl explain |
|---|---|---|---|---|
| `weight_request_count` | Activity weight | How much a user's activity here counts. Raise it and active users are trusted faster. All four weights must add up to 1.0. | Activiteitsgewicht | Hoe zwaar iemands activiteit hier meetelt. Hoger = actieve gebruikers worden sneller vertrouwd. De vier gewichten moeten samen 1.0 zijn. |
| `weight_account_age` | Account age weight | How much the age of the user's account counts. Raise it and older accounts are trusted faster. Lower it and account age matters less. | Gewicht accountleeftijd | Hoe zwaar de leeftijd van het account meetelt. Hoger = oudere accounts worden sneller vertrouwd. Lager = leeftijd telt minder mee. |
| `weight_content_age` | Content age weight | How much the age of requested content counts. Mostly affects song requests. Raise it and brand-new content is trusted less. | Gewicht content-leeftijd | Hoe zwaar de leeftijd van aangevraagde content meetelt. Geldt vooral voor song requests. Hoger = gloednieuwe content wordt minder vertrouwd. |
| `weight_content_popularity` | Content popularity weight | How much the popularity of requested content counts. Raise it and obscure content is trusted less. | Gewicht content-populariteit | Hoe zwaar de populariteit van aangevraagde content meetelt. Hoger = onbekende content wordt minder vertrouwd. |
| `decay` (one explain shared per decay row, suffix per field) | Growth speed | How fast this score part grows toward its maximum. Higher = it maxes out sooner. Lower = users need more history for the same score. | Groeisnelheid | Hoe snel dit scoredeel naar zijn maximum groeit. Hoger = eerder op het maximum. Lager = meer geschiedenis nodig voor dezelfde score. |
| `not_following_factor` | Not-following penalty | The score multiplier for users who do not follow the channel. 0.75 means their score is cut by a quarter. 1.0 turns this penalty off. | Straf voor niet-volgers | De vermenigvuldiger voor gebruikers die het kanaal niet volgen. 0.75 = score een kwart lager. 1.0 = geen straf. |
| `reputation_boost` | Reputation boost | Gives mods, VIPs, subscribers and proven regulars a big head start. Turning this off treats them like strangers. | Reputatiebonus | Geeft mods, VIP's, subscribers en vaste kijkers een flinke voorsprong. Uit = zij worden als vreemden behandeld. |
| `skip_penalty` | Skip penalty | Points removed each time this user's request is skipped. Higher = repeated skips lower trust faster. | Skip-straf | Punten eraf telkens als een verzoek van deze gebruiker wordt geskipt. Hoger = herhaald skippen verlaagt vertrouwen sneller. |
| `timeout_penalty` | Timeout penalty | Points removed for each timeout on this user. Higher = a timeout hurts their trust more. | Timeout-straf | Punten eraf voor elke timeout van deze gebruiker. Hoger = een timeout schaadt het vertrouwen meer. |
| `ban_penalty` | Ban penalty | Points removed for each ban on this user. This is the heaviest penalty. | Ban-straf | Punten eraf voor elke ban van deze gebruiker. Dit is de zwaarste straf. |
| `tier_untrusted_max` | Untrusted ceiling | Scores at or below this are Untrusted. Raise it and more users count as Untrusted. | Grens Onvertrouwd | Scores tot en met deze waarde zijn Onvertrouwd. Hoger = meer gebruikers gelden als Onvertrouwd. |
| `tier_low_max` | Low-trust ceiling | Scores above the Untrusted ceiling up to this are Low trust. | Grens Laag vertrouwen | Scores boven de Onvertrouwd-grens tot en met deze waarde zijn Laag vertrouwen. |
| `tier_standard_max` | Standard ceiling | Scores above the Low ceiling up to this are Standard. Everything above is Trusted. | Grens Standaard | Scores boven de Laag-grens tot en met deze waarde zijn Standaard. Alles daarboven is Vertrouwd. |
| `heat_half_life` | Heat cool-down (hours) | Heat marks recent bad behavior. After this many hours, half of it is gone. Shorter = users are forgiven faster. | Heat-afkoeltijd (uren) | Heat markeert recent wangedrag. Na dit aantal uren is de helft weg. Korter = gebruikers worden sneller vergeven. |
| `heat_delta_<action>` | Heat per <action> | Heat added when this happens. Higher = this action pushes a user toward the auto-timeout line faster. | Heat per <actie> | Heat die erbij komt als dit gebeurt. Hoger = deze actie duwt een gebruiker sneller richting de auto-timeoutgrens. |
| `heat_threshold` (J.7, existing) | Auto-timeout line | When a user's heat crosses this line, the bot times them out automatically — if the switch below is on. Mods and the broadcaster are never auto-timed-out. | Auto-timeoutgrens | Als de heat van een gebruiker over deze grens gaat, geeft de bot automatisch een timeout — als de schakelaar hieronder aan staat. Mods en de streamer krijgen nooit een automatische timeout. |
| `section_blast_radius` | — | These weights also decide who may use song requests. Changing them changes !sr for everyone. | — | Deze gewichten bepalen ook wie song requests mag doen. Aanpassen verandert !sr voor iedereen. |
| `automation_panel_title` | What happens automatically | This list is computed from your current settings. It shows exactly what the bot does without asking a human. | Wat er automatisch gebeurt | Deze lijst wordt berekend uit je huidige instellingen. Hij toont precies wat de bot doet zonder een mens te vragen. |
| `automation_can_ban_line` | — | Nothing on this channel auto-bans unless it is listed here. | — | Niets op dit kanaal geeft automatisch een ban, behalve wat hier staat. |

Copy rules honored: users see role/tier NAMES, never numbers, in labels; the numbers appear only as
the editable values themselves. Dutch uses informal "je". `<action>`/`<actie>` is substituted per
heat-delta row from the action type's existing display name.
