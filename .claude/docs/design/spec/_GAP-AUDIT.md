# Spec Gap Audit — 2026-06-17

Adversarial **read-only** audit of all 22 specs by 5 parallel passes. **Supersedes `_READINESS.md`'s "all ready" verdict** — that report was auto-generated (partly owner-asleep) and missed the systemic gaps below. Every item is a real blocker or seam for a single clean implementation pass. Each carries its owner.

> **UPDATE (2026-06-17, verification pass).** Every **BLOCKING** item (B1–B7) and every **CROSS-CUTTING** item (X1–X4) below has since been **verified RESOLVED** against the live spec files: B1 — `roles-permissions.md §7.1` is now a full seed catalogue (Mgmt ~95 keys + Community ~24 + C.1 `IamPermissions` + C.2/C.3 role bundles); B2 — canonical `DomainEventBase` across every §2 block (0 stale `Timestamp`/`string EventId`/`string? BroadcasterId`, incl. the B8b sweep of `moderation.md`/`twitch-helix.md`); B3 — `CommunityRole` phantom gone; B4 — single `Result<string> Upcast`; B5 — 4-arg `IScriptExecutor` with `IScriptHostBridge`; B6 — billing cipher on `IFieldCipher`/`ISubjectKeyService`; X1 — `TimeProvider` (`_BUILD-ORDER` Phase 0); X2 — `backend-structure §5.2` ordered seed manifest; X3 — `_BUILD-ORDER.md`; X4 — `twitch-helix §10` fakes/fixtures. The original entries are kept for history. **The genuinely-open gaps are now the PRODUCT-EDGE & OPS band — see that section below.**

---

## BLOCKING — stops a clean single-pass build

**B1. Permission seed catalogue is unwritten** — owner: `roles-permissions.md`
No spec enumerates the `ActionDefinitions` (Gate-2 keys) or `IamPermissions` / `IamRoles` / `IamRolePermissions` (Plane-C) seed contents. ~30+ keys are referenced as "already seeded" across identity-auth, commands-pipelines, federation, gdpr, moderation, twitch-eventsub, custom-code — but the seeder is pointed at "line ~170" with no contents. Gate-2 fails closed on an unknown key → **every gated endpoint 403s on a fresh install.** `roles-permissions.md` must own the full table: key · DefaultLevel · FloorLevel · FloorTier · IsGrantableViaPermit, plus the Plane-C permission keys and the system role→permission bundles. _Patched one row at a time so far (`twitch:diagnostics:read`, `code:script:author`); never owned as a whole._

**B2. `DomainEventBase` shape contradiction (systemic)** — owners: `event-store.md`, `stream-admin.md`, `discord.md`, `monetization-billing.md` (each §2)
Canonical base (platform-conventions §2.0) = `Guid EventId` · `OccurredAt` · `Guid BroadcasterId` (no redeclare). These four describe it as `string EventId` · `Timestamp` · `string? BroadcasterId` (+ bolt-on `BroadcasterGuid`). Their entire §2 event blocks won't compile against the locked base. `moderation.md` got it right — use it as the template. Fix each §2 to the canonical base and state how tenant-scoped events set `BroadcasterId`.

**B3. `CommunityRole` phantom enum** — owner: `commands-pipelines.md`
`BuiltinCommandContext` / `BuiltinCommandOverrides` reference `CommunityRole`, which does not exist (only `ManagementRole` + `CommunityStanding`). Collaborators `ChannelSnapshot` / `StreamSnapshot` / `IChatResponder` / `ITemplateRenderer` are undefined in any spec (`ITemplateRenderer` duplicates `ITemplateEngine`). Replace enum with `CommunityStanding`; reuse `ITemplateEngine`/`IChatProvider` or define+own the rest. _`_READINESS` quoted this exact shape as "RESOLVED" and missed the phantom type._

**B4. `IEventUpcaster` defined twice, contradictorily** — owner: `event-store.md`
§3.3a (`string Upcast(...)`, throw-based) vs §3.6 (`Result<string> Upcast(...)`). Incompatible signatures, same type, same file. Delete the §3.3a interface block (keep its prose); §3.6 is canonical (matches the DI table + `IEventUpcasterRegistry`).

**B5. `IScriptExecutor.ExecuteAsync` 3-arg vs 4-arg** — owner: `platform-conventions.md` §3.9
§3.9 reproduces the **3-arg** body (missing `IScriptHostBridge bridge`) despite a header that says "AUTHORITATIVE: custom-code §3.1 … do not show a diverging body." custom-code/sandbox are 4-arg. Without `bridge` the `bot.*` facade is un-wireable. Sync to 4-arg or drop the body.

**B6. monetization-billing codes against the retired `IEncryptionService`** — owner: `monetization-billing.md` §7/§9
Routes the `[PII-shred]` cipher columns (`StripeCustomerIdCipher`, `BillingEmailCipher`) through legacy AES-CBC `IEncryptionService`, which **cannot crypto-shred** (no per-subject DEK). Route through `IFieldCipher` / `ISubjectKeyService` (gdpr-crypto §3.2).

**B7. No rewards spec — Domain F.5 `Rewards` / F.6 `RewardRedemptions` unowned** — write `rewards.md` — **RESOLVED (2026-06-17)**
`rewards.md` written. Owns F.5/F.6; declares `RewardRedeemedEvent` (the per-topic event the twitch-eventsub dispatcher maps `channel.channel_points_custom_reward_redemption.add` to) + fulfilled/refunded/config-changed events; `IRewardService` (managed Helix CRUD), `IRewardRedemptionService` (fulfill/refund), `IRewardRedeemedPipelineTrigger` (the reward→pipeline feeder for `PipelineRequest.RewardId`/`RedemptionId`), and `RewardRedemptionProjection` (F.6 fact log). Managed vs unmanaged split via the **`IsManaged`** column (renamed from `IsPlatform` in schema F.5) — managed = bot-created on Twitch (CRUD + fulfill/refund), unmanaged = observe-and-react only (Twitch client_id rule). Action keys `reward:read|manage|sync|redemption:read|fulfill|refund` added to `roles-permissions.md` §7.1. Registered in `_INDEX`/`_READINESS`.

---

## CROSS-CUTTING — no spec owns these

**X1. Clock / time abstraction** — owner: `platform-conventions.md`
No `IClock`/`TimeProvider`; `DomainEventBase` hardcodes `DateTimeOffset.UtcNow` (the opposite of a seam). Timers, cooldowns, TTL sweeps, billing periods, sub-tenure all need deterministic, fakeable time to be testable per the "tests prove behavior" bar. Adopt .NET 10 `TimeProvider` as the single injected clock.

**X2. Seed manifest + ordering** — owner: `backend-structure.md` §5
`ISeeder` auto-discovery exists but defines **no run order**, despite real FK deps (ActionDefinitions→PermitGrants, BillingTier→TierLimit). Define an ordered, idempotent seed manifest (upsert-by-natural-key).

**X3. Build / slice order** — currently punted to "the task board" (event-store §9), unowned
No cross-subsystem build sequence; only pairwise hard-deps exist. A master dependency-ordered order prevents building consumers before owners.

**X4. Twitch test-double strategy** — owner: `twitch-helix.md` or `twitch-eventsub.md`
Zero fake/fixture/mock content in the Twitch specs, yet nearly every subsystem's behavior tests depend on Twitch responses. Define seam-fakes (`ITwitchApiService`/`IChatTransport`/EventSub source) + recorded Helix JSON fixtures.

---

## MISSING SPECS — to write

- **M1. `rewards.md`** — Domain F.5/F.6 (this is B7; BLOCKING). — **RESOLVED (2026-06-17)**, see B7.
- **M2. `analytics.md`** — **RESOLVED (2026-06-17)**. Written, but **re-scoped** from this line's over-assignment: analytics owns **Domain M only** (M.1 ViewerProfiles, M.2 WatchSessions, M.3 WatchStreaks, M.4 MessageActivityDaily, M.7 ViewerEngagementDaily, M.8 ChannelAnalyticsDaily — six `IProjection`s folded from the permanent journal) + a read API (`IViewerAnalyticsService`, `IChannelAnalyticsService`, SaaS-only `IPlatformAnalyticsService`). It does **not** own J.4/J.5 + the `HeatScore`/`TrustScore` formula (those are `moderation.md` + the canonical `TrustScoreCalculator`), N.5 usage metering (`monetization-billing.md`), or L.* leaderboards (`economy.md`). Registered in `_INDEX`/`_READINESS`; action keys `analytics:read`/`analytics:viewer:read` + Plane-C `platform:analytics:read` seeded.
- **M3. `rollout-updates.md`** — **RESOLVED (2026-06-17)**. Written as a **rulebook** (no new schema/interfaces — every mechanism already exists). Owns the playbook: rolling deploys (SaaS) / single-process restart (self-host); forward-only **expand-contract** migrations (additive per release, destructive DDL deferred to a later contract release, two provider sets, auto-migrate under `IRunOnceGuard`); event-schema evolution via `IEventUpcaster`; **feature-flag staged rollout** over the existing `IFeatureFlagService` (platform-conventions §3.4) — dark-launch → beta overrides → ramp `RolloutPercentage` → tier/deployment/consent gates → GA + live kill-switch; and the drop-a-class auto-discovery path for adding actions/projections (user commands stay runtime DB rows). **Note:** the "legacy retirements" framing in the original line is **moot** under clean-slate — the legacy tables (`Subscription`/`ChannelSubscription`/`DiscordServerAuthorization`) never ship, and the `string→Guid` tenant widen is in the single greenfield migration (`_BUILD-ORDER.md` Phase 1), not a post-launch sequence. Registered in `_INDEX`/`_READINESS`.

---

## PRODUCT-EDGE & OPS GAPS — found 2026-06-17 (completeness audit)

The in-spec engine/feature/platform band (B1–B7, X1–X4) is **closed and verified**. A dedicated completeness audit — every `CLAUDE.md` controller + the full capability surface + the deploy/distribution story cross-mapped to the spec set — surfaced a **product-edge + ops** band that **no spec owns**. These block a clean build of the full two-model product even though the engine is solid. Verified against the live tree: the `CLAUDE.md`-advertised `app/`, `web/`, `deploy.sh`, `deploy.ps1`, root `docker-compose.yml`, `.env.example`, `nomnomzbot-design/` **do not exist on disk** — that layout is aspirational (the repo root is `.claude .env .git CLAUDE.md README.md SECURITY_ARCHITECTURE.md docs server`).

| # | Gap | Sev | Why it blocks | Owner to write |
|---|---|---|---|---|
| **P1** | Onboarding / setup-wizard **backend** + `SystemController` setup-status | High | the frontend entry-gate (`frontend.md §5`) probes a setup-status endpoint no spec defines | **✅ DONE — `spec/onboarding-setup.md`** (2026-06-17) |
| **P2** | **Spotify / YouTube OAuth connect** flows (authorize → callback → scopes → refresh) | High | music can't connect a provider; identity-auth had only the generic token vault | **✅ DONE — `spec/integrations-oauth.md`** (connect-only; provider *manage* surface = Group E below) |
| **P3** | `CommunityController` + `DashboardController` §5 contracts | High | the viewer-community list + dashboard stats-aggregation endpoints have no owning spec (twitch-helix calls them "existing"; analytics adds a *different* `AnalyticsController`) | new `spec/community-dashboard.md` or §5 tables on twitch-helix/analytics |
| **P4** | **Self-host distribution & deploy artifacts** (Docker image, quickstart, `deploy.*`, root compose, `.env.example`) + SaaS LB/topology as buildable | High | no spec owns how a streamer **acquires & runs** the bot in either model; SaaS topology is a mermaid map, not infra | new `spec/deployment-distribution.md` |
| **P5** | Serving the **wasmJs dashboard** from the bot (static hosting / SPA fallback) | Med | `frontend.md §3/§6` says "served first-party by its bot" but no backend spec wires it | platform-conventions or P4 spec |
| **P6** | Self-host **mDNS service advertising** (backend side of LAN discovery) | Med | `frontend.md §6` punts it to "backend concern"; nothing owns advertising `_nomnomz._tcp` | platform-conventions or P4 spec |
| **P7** | **External-API management coverage** (was "poll/prediction" — much larger) | High | the [[external-api-full-management-coverage]] rule (owner: "EVERYTHING a user can do via an external REST API, we let them manage") means the bot must own the **full** Helix/Spotify/YouTube manage surface. A dedicated coverage audit found **~20+ manageable Helix resources with no spec** + provider-remote gaps — see the management-coverage breakdown below | Groups A–F below |

**Not built yet (expected — implementation, not spec gaps):** the KMP `app/`, the public `web/` pages, and the deploy artifacts exist as specs/designs, not code.

### P7 expanded — external-API management coverage (audit 2026-06-17)

The owner's coverage rule turns "polls/predictions" into a **management-surface buildout**. Engine ingests the read-side events (`PollBeganEvent`, `RaidEvent`, …) but offers **no write/manage surface** for most live-ops. Channel-points (`rewards.md`), core moderation (ban/timeout/unban/delete), AutoMod settings, channel title/game/tags, shoutout, add-moderator, and **Discord (6/6)** are already covered — these are NOT re-specced. The open clusters:

| Group | Scope | Owner-to-write | Manageable resources (Helix scope) |
|---|---|---|---|
| **A — Broadcaster live-ops writes** | High | new `spec/broadcaster-liveops.md` | Polls (`channel:manage:polls`), Predictions (`:predictions`), Raids start/cancel (`:raids`), Ads/commercials (`channel:edit:commercial`+`channel:read:ads`), Stream Schedule segments+vacation (`channel:manage:schedule` — also rename the false-friend `stream:schedule:write`), Stream Markers + Clips create (`channel:manage:broadcast`, `clips:edit`) |
| **B — Chat & channel controls** | High | extend `moderation.md` (chat-controls section) | Chat Settings slow/follower/sub/emote/unique/non-mod-delay (`moderator:manage:chat_settings`), Shield Mode (`:shield_mode` — only prose today), Announcements (`:announcements`), Chat Color (`user:manage:chat_color`) |
| **C — Moderation write completeness** | High | extend `moderation.md` / `twitch-helix.md` | Add/Remove VIP (`channel:manage:vips`), Remove Moderator (`channel:manage:moderators` — only Add exists), Unban Requests read/resolve (`moderator:*:unban_requests`), User Block List (`user:manage:blocked_users`), Suspicious Users (`moderator:manage:suspicious_users`), + **wire the abstracted Helix legs**: native warn (`moderator:manage:warnings`), AutoMod held-message approve/deny (`moderator:manage:automod`), blocked-terms CRUD push |
| **D — Channel-info field gating** | Med | extend `stream-admin.md` | CCL / broadcaster language / branded-content — today DTO fields with no action key/floor (`channel:manage:broadcast`) |
| **E — Music provider remote** | Med | extend `music-sr.md` | Spotify previous/seek/shuffle/repeat/transfer-device + playlist CRUD + saved-tracks + follow; YouTube playlist/ratings/subscriptions — connect-auth handled by `integrations-oauth.md`, manage endpoints land here |
| **F — Low-priority / explicit decisions** | Low | per-spec note | Whispers (`user:manage:whispers`), Extensions config (`user:edit:broadcast`), **Guest Star — SKIP** (Twitch deprecated the API), **Charity/Goals ingest** (read-only events not even subscribed — an ingest add, not a manage gap) |

> Each new manage surface gets controller + service + action key (seeded in `roles-permissions.md §7.1`) + progressive Twitch scope + schema only where it holds state (active poll/prediction/schedule). All gated through the existing 3-plane authz; scopes requested progressively (only when the feature is enabled).

**RESOLVED (2026-06-17) — the full P1–P7 + Group A–F band is specced:**
- **P1** `onboarding-setup.md` · **P2** `integrations-oauth.md` · **P3** `community-dashboard.md` · **P4/P5/P6** `deployment-distribution.md` (distribution + wasm-serving + mDNS).
- **Group A** `broadcaster-liveops.md` (polls/predictions/raids/ads/schedule/markers/clips; +schema F.12/F.13; +`ITwitchLiveOpsApi` in twitch-helix).
- **Groups B/C** extended into `moderation.md` + `twitch-helix.md` (chat settings, shield mode, announcements, chat color, VIP add/remove, remove-mod, unban-requests, block-list, suspicious-users; wired the prose-only warn/automod/blocked-terms Helix legs; +`AutoModConfigs.ShieldModeActive`).
- **Group D** `stream-admin.md` (CCL/language/branded-content + extensions gating). **Group E** `music-sr.md` (full Spotify remote + YouTube manage; YouTube search=API-key / manage=OAuth auth-stance stated). 
- **Group F** decisions: whispers IN (key `chat:whisper:send`; home = the chat-send path — a Helix `POST /whispers` leg + a `send_whisper` commands-pipelines action); extensions IN (`stream-admin`); **Guest Star SKIPPED** (Twitch deprecated the API — do not build on a dead API); **Charity & Goals = ingest-only** (no manageable write endpoints exist).
- ~32 action keys seeded in `roles-permissions.md §7.1`; specs registered in `_INDEX`/`_READINESS`; schema F.12/F.13 + `AutoModConfigs.ShieldModeActive` applied.
- **Residuals:** (1) ✅ `send_whisper` pipeline action added to `commands-pipelines.md §6.1`. (2) **Charity/Goals EventSub ingest** — the single remaining decided micro-task: subscribe `channel.charity_campaign.*` + `channel.goal.*` and fan out read-side events alongside the other ingested topic events (placement follows wherever `twitch-eventsub.md` homes its per-topic read-side events / `TwitchChannelEventLog` F.4 read-model). Zero design uncertainty — ingest-only, no manageable write endpoints exist; a mechanical add for the eventsub owner.

---

## SHOULD-FIX — real seams, not blocking

**RESOLVED (2026-06-17) via the reconciliation sweep** — every item below is now fixed in its spec: Gate-2/Plane-C §5 preambles de-conflated (identity-auth / gdpr / federation); `IChatProvider`→`IChatTransport`; eventsub pagination verified already-correct (no drift); tts `MaxCharacters`→tier cap (`tts_max_characters` seeded in schema N.2 + billing, absolute ceiling 8000); custom-code `AllowQuery` added (spec §1 + schema H.7), byte fields already `long`, meter names reconciled to the custom-code owner; identity-auth custom-bot→`AllowsCustomBotName`; `MinPermissionLevel`→`[AllowedValues(0,2,4,6,10,20,30,40)]`; stream-admin `BeginTenantAccessAsync`→`IamRoleAssignment`. The federation registry item was closed by a dedicated design pass (**2026-06-17**, see its bullet) — the SHOULD-FIX list now carries **zero** open items.

- Gate-2 (B.3 `ActionDefinitions`) vs Plane-C (C.1 `IamPermissions`) seed-table **conflation** in identity-auth / gdpr / federation §5 preambles.
- twitch-helix `IChatProvider`/`HelixChatProvider` vs canonical `IChatTransport`/`HelixChatTransport` (scaling-qos §6) — reconcile naming.
- twitch-eventsub `PageRequestDto` (§5.1) vs canonical `PaginationParams` (§3.2).
- tts `MaxCharacters` static `[Range(1,500)]` vs the binding tier-scaled cap.
- custom-code boundary-type debt: `MaxMemoryBytes`/`MaxEgressBytes` `int`→`long`; `AllowQuery` absent from §1/§4.
- `IScriptExecutionMeter` name drift: `Check/RecordSandboxUsageAsync` (custom-code §3.3, owner) vs `Reserve/SettleSandboxBudgetAsync` (sandbox §11.3).
- identity-auth custom-bot gate: use `IBillingTierService.AllowsCustomBotName` (N.1), not a non-existent "Pro+" tier-key compare.
- commands-pipelines `MinPermissionLevel` `[Range(0,100)]` vs the real ladder (community 0/2/4/6/10 + management 10/20/30/40).
- federation: define `FederatedEventType`→typed-event registry + `peerId`→`partnerBroadcasterId` resolution; ed25519-key validation rule; dynamic per-peer OIDC scheme registration. **RESOLVED (2026-06-17, design pass):** federation-oidc §3.7 adds `IFederationInboundHandler` (one per type, auto-discovered, owned by the applying subsystem — registered `Type` set = closed accept-set; unknown→`schema_invalid`) + the recognized-type catalog (each type → gating `OptInType` → owning handler) + inbound identity/target resolution (peer from mTLS cert thumbprint + `OriginInstanceId` match; cross-instance identity by **stable external ids**, never a peer's local `Guid`; directed vs directory-broadcast target fan-out by opt-in predicate). §3.3 fixes the ed25519 rule (verify is `rsa-sha256`-only; ed25519 storable but `algorithm_unsupported` at verify; `TrustPeerAsync` requires an active rsa-sha256 key). §7 makes the per-peer OIDC client scheme **dynamic** (`IFederationSchemeRegistrar` + decorating `FederationSchemeProvider` + warm-up `IHostedService`, `fed:{InstanceId}` schemes on trust/revoke). Also fixed in-pass: stale §2 `DomainEventBase.BroadcasterId string?` note → canonical `Guid`; two events' redeclared `EventId`→`JournalEventId` (B8b-class shadow).
- stream-admin `BeginTenantAccessAsync`: "`IamRoleAssignment` **or** access-grant record per impl" — decide one (nothing left open).

---

## MINOR

**RESOLVED (2026-06-17) in the sweep** — `Guid.Empty` platform sentinel (identity-auth); `CipherAad.KeyVersion` → new `CryptoKey.KeyVersion` (schema Q.1); `UserAccessTokenOwner` → `EventSubTokenOwnerKind` enum; `TwitchScopeRequirementDto` gained `IsProgressive`/`GatedByFeature`; discord `ConfigSchemaVersion` given an on-read upcast consumer; widgets link-preview threshold homed in an `AppSetting` (`widgets/link_autoembed_min_trust`, default 0.75). The `HeatScore` item stays a `moderation.md` question (below).

- identity-auth platform-event `BroadcasterId` sentinel-vs-null convention (`Guid.Empty` vs `null`).
- gdpr `CipherAad.KeyVersion` source undefined (`CryptoKey` has no version column).
- twitch-eventsub `EventSubSubscriptionRequest.UserAccessTokenOwner` free-text `string?` → should be an enum.
- twitch-helix scope-diagnostics `TwitchScopeRequirementDto` can't express "feature-gated/progressive".
- discord `DiscordNotificationConfig.ConfigSchemaVersion` dangling (no consumer reads/migrates on it).
- widgets `ILinkPreviewService` auto-embed threshold source undeclared (no config home).
- moderation `HeatScore` formula: **owner corrected** — it is **not** analytics' (M2 grounding showed J.5 `UserTrustScore` + the `TrustScoreCalculator` are `moderation.md`'s). If the heat-accumulation weighting (timeouts/bans/reports/filter-hits → heat) is genuinely unspecified, the gap belongs in `moderation.md` §3.8 (`RecomputeTrustAsync`), the J.5 owner. Verify whether `TrustScoreCalculator` already defines it before opening a gap.

---

## RESIDUAL — surfaced during the 2026-06-17 sweep

- **B8b — positional `BroadcasterId` redeclared across §2 event blocks. RESOLVED (2026-06-17).** The prior B8 pass converted event `class`→`record` but left `BroadcasterId` as a **positional record parameter** in the older §2 blocks. A positional `Guid BroadcasterId` shadows the inherited `DomainEventBase.BroadcasterId` (platform-conventions §2.0: events must NOT redeclare `EventId`/`OccurredAt`/`BroadcasterId`). A full sweep of every spec's §2 found the defect in exactly **two** files (all brace-body events elsewhere already carry explicit "do not redeclare" notes from the B8 pass; remaining `BroadcasterId` hits are DTOs/contracts/entities/method-params/prose):
  - **`moderation.md` §2** — root cause was a stale §2 intro describing `DomainEventBase` as living in `Domain.Common` and providing only `OccurredAt`. Reconciled the intro + tenant-key note to the canonical base (platform-conventions §2.0, `Domain.Events`, provides `EventId`/`BroadcasterId`/`OccurredAt`), dropped the stale `using NomNomzBot.Domain.Common;`, and stripped the redeclared `Guid BroadcasterId` from all 6 affected events (`ModerationActionApplied/Reverted`, `ModerationQueueItemEnqueued/Resolved`, `ViewerReportFiled`, `UserHeatThresholdCrossed`). The distinct `OriginBroadcasterId` fields (origin channel ≠ tenant) on shared-ban / network-nuke events are **kept** — they are explicit payload, not the inherited tenant key.
  - **`twitch-helix.md` §2** — its 4 events derived the undefined legacy base `: DomainEvent` (same class of bug as resolved gap #6 for `custom-code`). Rebased all 4 to `DomainEventBase`, corrected the §2 prose, and stripped the redeclared `Guid BroadcasterId` / `Guid? BroadcasterId` from the 3 that carried it; app/bot-token (non-tenant) rate-limit + circuit-breaker events publish with the inherited `BroadcasterId = Guid.Empty`.

  Verified: zero `: DomainEvent;` (legacy base) remain in any spec; zero positional tenant `BroadcasterId` remain in `moderation.md` event headers; both files fence-balanced.

---

### Verdict

Not "21 specs ready + 3 to write." It's **7 blocking in-spec repairs + 4 unowned cross-cutting decisions + 3 missing specs + ~10 should-fix seams.** Two systemic root causes dominate: the **unwritten permission seed catalogue** (B1) and the **`DomainEventBase` shape drift** (B2) — fix those two and a third of the list collapses. Dependency note: the missing specs (rewards/analytics/rollout) and several should-fixes depend on B1, B2, X1, X2 landing first.
