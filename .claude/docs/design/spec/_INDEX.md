# Interface Spec Set — Index & Consistency Gate

Consistency review of all subsystem interface specs in `docs/design/spec/` against each other and the
LOCKED schema `docs/design/2026-06-16-database-schema.md` (Domains A–R). Every spec binds to the same
conventions (namespace `NomNomzBot.*`, .NET 10 / EF Core 10, `Guid BroadcasterId`, `Result<T>`,
`StatusResponseDto<T>`/`PaginatedResponse<T>`, Newtonsoft app-JSON, deployment-profile DI adapters).

**Scope of this gate:** (1) no dangling type/DTO refs; (2) naming consistency (esp. `BroadcasterId`/`UserId`);
(3) every endpoint has request+response DTOs; (4) every service method is `Result`/`Task`-wrapped per convention;
(5) deployment-profile adapter pairs complete; (6) no two specs define the same interface differently;
(7) every spec's entities map to real locked-schema tables.

---

## Table of contents

`#Iface` = service/adapter interfaces defined or extended. `#Endpoints` = HTTP routes (rows in the §5 tables;
SignalR hub methods and pipeline actions excluded). `#PipeActions` = `ICommandAction`/condition blocks.

| Subsystem | Doc | Owns (schema domains) | #Iface | #Endpoints | #PipeActions |
|---|---|---|---|---|---|
| Platform conventions | `platform-conventions.md` | P.11–P.13, Q.3, O.9 (cross-cutting) | 8 | 8 | 0 |
| Identity & Auth | `identity-auth.md` | A.1–A.5, E.1–E.4, Q.1 (co-own) | 9 | 15 | 0 |
| Roles & Permissions | `roles-permissions.md` | B.1–B.5, C.1–C.5, O.9 | 7 | 14 | 2 |
| Twitch Helix | `twitch-helix.md` | (projection over A.2/F.x/E.1) | 6 | 1 | 0 |
| Twitch EventSub | `twitch-eventsub.md` | F.4, F.7–F.9, O.1, O.1a, O.4, Q.3 | 6 | 5 | 0 |
| Rewards | `rewards.md` | F.5, F.6 | 3 | 11 | 2 (`fulfill_redemption`, `refund_redemption`) |
| Analytics | `analytics.md` | M.1–M.4, M.7, M.8 (6 projections) | 3 | 9 | 0 |
| Commands & Pipelines | `commands-pipelines.md` | G.2, G.2a, G.3, H.1–H.7, I.1, I.2, M.5 | 13 | 24 | ~18 actions + 4 conditions |
| Pipeline Control Flow | pipeline-control-flow.md | H.2 PipelineStep (cols: ParentStepId/BlockKind/BlockConfigJson) | 0 (extends IPipelineEngine) | 0 | 3 (run_pipeline, break, continue) + block-kinds |
| Quotes | quotes.md | G.5 Quote | 1 | 6 | 1 |
| Giveaways | giveaways.md | G.6–G.10 (Giveaway/Entry/Winner/CodePool/Code) | 2 | 16 | 3 |
| Engagement Triggers | engagement.md | G.11 EngagementConfig, G.12 ViewerEngagementState | 1 | 2 | 0 (3 triggers) |
| Custom Events & Data Sources | custom-events.md | G.13 CustomDataSource | 3 | 7 | 0 (custom.<name> trigger) |
| Per-Viewer Data Store | per-viewer-data.md | G.14 ViewerDatum | 1 | 3 | 2 (set_viewer_data, adjust_viewer_data) |
| Custom Code (T3) | `custom-code.md` | H.5, H.6, H.7 | 5 | 8 | 1 (`run_code`) |
| Chat-message decoration (third-party emotes + enrichment) | `chat-decoration.md` | — (enriches the chat fragment tree; no schema delta) | — | 0 | 0 |
| Moderation | `moderation.md` | J.1–J.11, O.8 | 9 | 34 | 6 (2 exist + 4 new) |
| Economy | `economy.md` | K.1–K.11, L.1–L.3 | 8 | 49 | 5 |
| Live Games | `live-games.md` | K.9a GameSession (+ K.9 GamePlay delta; consumes K.7/K.2/K.3) | 2 (+3 IGameService deltas) | 5 | 2 |
| Music & Song Requests | `music-sr.md` | L.4–L.9, E.5, E.6 | 9 | 31 | 8 |
| Media Share | media-share.md | L.10 MediaShareConfig, L.11 MediaShareRequest | 1 | 8 | 1 (+!media builtin) |
| TTS | `tts.md` | P.1–P.5 (+ P.1a new) | 7 | 11 | 1 (`play_tts`) |
| Widgets & Overlays | `widgets-overlays.md` | P.6–P.9 | 4 | 16 | 1 (`widget_event`) |
| OBS Control | obs-control.md | P.14 ObsConnection | 4 | 13 | 20 (+`obs_event` trigger) |
| VTube Studio Control | vtube-studio.md | P.19 VtsConnection | 3 | 6 | 6 (+vts_event trigger) |
| Sound System | sound-system.md | P.18 SoundClip | 2 | 6 | 2 (play_sound, stop_sound) |
| Stream Admin + IPC | `stream-admin.md` | F.10, F.11, A.5, C.x, P.12, P.13 | 6 | 30 | 1 (`set_stream_metadata`) |
| Discord | `discord.md` | P.10 (5 tables) | 5 | 19 | 1 |
| Supporter Events | supporter-events.md | P.15 SupporterConnection, P.16 SupporterEvent | 3 | 4 | 0 (supporter.* triggers) |
| External Automation API | automation-api.md | P.17 AutomationApiToken | 7 | 5 | 0 (+ token-scoped data plane: REST + WS stream) |
| Stream Deck Integration | stream-deck.md | (none — reuses P.17) | 1 | 2 | 0 (generic device pairing) |
| GDPR & Crypto | `gdpr-crypto.md` | O.5–O.7, O.10, Q.1, Q.2, O.1a | 8 | 13 | 0 |
| Monetization & Billing | `monetization-billing.md` | N.1–N.7 | 4 | 22 | 1 (`require_tier`) |
| Event Store | `event-store.md` | O.1–O.4, O.1a, Q.3 | 11 | 11 | 0 |
| Federation & OIDC | `federation-oidc.md` | D.1–D.3 | 5 | 13 | 0 |
| Scaling, Fairness & QoS | `scaling-qos.md` | O (`CommandLogEntry` addition; cross-cutting) | 5 | 0 | 0 |
| Webhooks (in/out) | `webhooks.md` | H.8–H.10 (reuses O.1/O.4, H.7) | 8 | 16 | 1 (`send_webhook`) |
| Import/Export & Marketplace | marketplace.md | H.11 InstalledBundle | 3 | 8 | 0 |
| Code-execution sandbox | `code-execution-sandbox.md` | (security deep-dive over H.5–H.7; no new tables) | 0 (references custom-code) | 0 | 0 (`run_code` owned by custom-code) |
| Backend structure (rulebook) | `backend-structure.md` | — (structural: file placement, artifact taxonomy, auto-discovery; no schema) | 0 | 0 | 0 |
| Rollout & Updates (rulebook) | `rollout-updates.md` | — (process: rolling deploys, expand-contract migrations, feature-flag staged rollout, drop-a-class extensibility; no schema) | 0 | 0 | 0 |
| Frontend (KMP dashboard) | `frontend.md` | — (client layer: KMP+Compose desktop+wasmJs; **consumes** v1 REST + SignalR; owns no schema) | ~9 (client/connection interfaces + hub clients) | 0 (consumes) | 0 |
| Onboarding & Setup | `onboarding-setup.md` | — (setup state machine; `DeploymentProfile.SetupCompletedAt` delta) | 1 | 6 | 0 |
| Integration OAuth (Spotify/YouTube) | `integrations-oauth.md` | — (connect flow; no schema — tokens via identity-auth) | 2 | 4 | 0 |
| Community & Dashboard | `community-dashboard.md` | — (read-only aggregation; owns no schema) | 2 | ~9 | 0 |
| Pronoun Provider | `pronouns.md` | A.1 `Users.AltPronounId` (col) | 3 | 3 | 0 |
| Broadcaster Live-Ops | `broadcaster-liveops.md` | F.12 `ActivePolls`, F.13 `ActivePredictions` | 2 (+`ITwitchLiveOpsApi`) | ~16 | 5 |
| Deployment & Distribution (rulebook) | `deployment-distribution.md` | — (ops: packaging/run, SaaS phasing, wasm hosting, mDNS; no schema) | 0 | 0 | 0 |

Counts are nominal (some specs list extended-vs-new interfaces and inline supporting records); they are an
orientation aid, not a contract.

> `frontend.md` is the **client layer**, not a backend subsystem — it consumes the v1 REST API + SignalR hubs and owns no schema domain, so it is **out of scope** for the schema-consistency report below (which gates the backend/rulebook specs against the locked schema). Its own internal consistency is self-contained (stack table §2, the typed-client contract §3, the connection model §6).

---

## Consistency report

Overall: the set is **strongly consistent** — every spec binds the same conventions, every method is
`Result`/`Task`-wrapped, every endpoint table pairs a request + response DTO, every deployment-profile
abstraction names both impls, and **every entity maps to a real locked-schema table** (verified Domain by
Domain). The IDOR / `string→Guid` / fail-open defects are uniformly targeted. Every spec is now
**decision-complete** — no spec carries an open or pending question; the prior open-question gaps were all
resolved into binding decisions (see the resolved-findings table below). The newest spec, `scaling-qos.md`
(SaaS scaling, fairness & QoS), is decision-ready: its §0 decision table (D1–D10) is final and binding, it
reuses the established adapter axis (`DeploymentProfile.Mode`) and the existing `IFairQueue<T>`/`IRunOnceGuard`/
`IEventBus` primitives, and its only schema addition (`CommandLogEntry`, Domain O) sits beside `EventJournal`.
Findings below were the cross-spec seams that needed a deliberate owner call; all are resolved, and trivial
divergences were fixed inline (see "Fixed inline").

> **Status (2026-06-16): all cross-spec/schema seams RESOLVED.** The 6 interface-ownership/aliasing findings
> below (A1–A3, B4, B5, G10) plus the 12 subsystem-readiness owner gaps (`_READINESS.md`) were resolved by
> targeted edits — one canonical owner per seam, every other spec references it. The set is now **fully
> implementable**. The 12 readiness resolutions are summarized after the findings table; all were re-verified
> to resolve against the spec files and the locked schema (`DomainEventBase` `Guid BroadcasterId` in
> platform-conventions §2.0; `EventSubTransportKind` vs `EventSubTransportMode` disambiguated;
> `IEventJournal.AppendAsync` single-owned by event-store; `IOverlayClient.TtsSpeak` in widgets-overlays §7;
> `twitch:diagnostics:read` / `code:script:author` / `eventsub:*` seed rows present;
> `CreatePrincipalAsync`, `BuiltinCommandContext`, `IFederationInboundTranslator` defined; schema
> `EventJournal.Source` carries `federation` and `TtsConfig` carries the BYOK AEAD envelope columns). One
> stale `Origin=shared_chat` leftover in `moderation.md` §3 was corrected inline to `Origin=federation`.

### A. Contradictions — same interface/event defined differently in two specs

1. **`CryptoKey` DEK-lifecycle interface — THREE names, two real shapes. [RESOLVED → owner `gdpr-crypto.md`.]**
   Canonical set: `ISubjectKeyService` (DEK lifecycle incl. `DestroyKeyAsync` crypto-shred), `IFieldCipher` (AES-256-GCM
   + HKDF AEAD), `IKeyVault` (local-AES / envelope-KMS KEK custody), `IKdf`. `identity-auth.md` §3.5 and
   `event-store.md` §3.9/§7/§8 now **reference** these and dropped the divergent `ICryptoKeyService.ShredAsync` /
   `ICryptoShredService` / standalone `IEncryptionService` names; `IIntegrationTokenVault` builds on
   `ISubjectKeyService` + `IFieldCipher` (one AEAD primitive platform-wide). Original finding below.
   `identity-auth.md` §3.5 defines
   **`ICryptoKeyService`** (`GetOrCreateTenantKeyAsync(Guid)`, `GetOrCreateSubjectKeyAsync(string subjectIdHash)`,
   `UnwrapAsync`, `RotateAsync`, **`ShredAsync(cryptoKeyId, erasureRequestId)`**) and registers
   `LocalAesCryptoKeyService`/`KmsEnvelopeCryptoKeyService`. `gdpr-crypto.md` §3.4 defines **`ISubjectKeyService`**
   for the *same* `CryptoKey` + `KeyUsageBinding` ownership (`GetOrCreateSubjectKeyAsync(Guid subjectUserId, string
   subjectIdHash)`, `GetOrCreatePlatformKeyAsync`, `ProtectAsync`/`UnprotectAsync`, `RotateKeyAsync`,
   **`DestroyKeyAsync(cryptoKeyId, erasureRequestId)`**, `ResolveSubjectKeysAsync`) and explicitly says it "owns
   `CryptoKey`". `event-store.md` §3.x references a third name, **`ICryptoShredService`**, plus
   `ICryptoKeyService` (in its §7 table). gdpr-crypto further splits the crypto data-plane into `IFieldCipher` +
   `IKeyVault` + `IKdf`, while identity-auth keeps an extended `IEncryptionService` (AEAD) + `IIntegrationTokenVault`.
   **Conflict:** two specs claim ownership of `CryptoKey` lifecycle with non-matching interface names and
   signatures (`ShredAsync` vs `DestroyKeyAsync`, `GetOrCreateSubjectKeyAsync` arity differs). They cannot both be
   authored. **Action:** pick one owner (gdpr-crypto is the natural home — it owns Q.1/Q.2 and the erasure
   pipeline). Identity-auth should consume that interface (rename `ICryptoKeyService`→`ISubjectKeyService`, map
   `ShredAsync`→`DestroyKeyAsync`) rather than redefine it; event-store should reference the same name (drop the
   third alias `ICryptoShredService`). Decide whether `IIntegrationTokenVault`'s AEAD sits on `IFieldCipher`
   (gdpr) or the extended `IEncryptionService` (identity) — only one AEAD primitive should exist.

2. **Platform IAM authorization — two interfaces, overlapping responsibility. [RESOLVED → owner `roles-permissions.md`.]**
   Canonical: `IPlatformIamService.AuthorizePlatformAsync` (authorizes **and** writes `IamAuditLog` in one call, so no
   decision goes un-audited). `stream-admin.md` §3.2 drops the split public `IIamAuthorizationService` path and routes
   `PlatformAdminController` through `IPlatformIamService`; `IIamAuditWriter` is retained only as the internal sink
   `IPlatformIamService` writes through (not a second public entry point). Original finding below.
   `roles-permissions.md` §3.7
   defines **`IPlatformIamService`** (`AuthorizePlatformAsync(principalId, permissionKey, targetBroadcasterId,
   breakGlass, justification)` — *authorizes AND writes `IamAuditLog`*), with profile adapters
   `PlatformIamService`/`OwnerIsFullIamService`. `stream-admin.md` §3.2 instead defines **`IIamAuthorizationService`**
   (`AuthorizeAsync` — *pure decision, writes no audit*) **plus** a separate **`IIamAuditWriter`**, and routes
   `PlatformAdminController` through them. Both gate Plane-C (`tenant:read`/`tenant:suspend`/`tenant:access`/
   `featureflag:write`/`audit:read`). **Conflict:** same Plane-C gate, two interface designs (combined vs
   split-decision-and-audit). Pick one. (roles-permissions owns Domain C, so its `IPlatformIamService` is the
   natural canonical; stream-admin's split is a reasonable refinement — reconcile to a single pair.)

3. **`IScriptExecutor` — referenced shape ≠ authoritative shape. [RESOLVED → owner `custom-code.md` §3.1.]**
   `platform-conventions.md` §3.9 now cites custom-code's exact signature (`ScriptRuntimeKind Runtime`, `CompileAsync`,
   `ExecuteAsync(request, grant, ct) → Result<ScriptExecutionOutcomeResult>`) and no longer shows a diverging body.
   `CodeExecutorKind` (deployment-profile enum, P.12 — selects the adapter) and `ScriptRuntimeKind` (the adapter's
   self-reported runtime) are intentionally two enums. Original finding below.
   `custom-code.md` §3.1 is the authoritative
   owner: `ScriptRuntimeKind Runtime { get; }`, **`CompileAsync(sourceCode, ct)`** + **`ExecuteAsync(request,
   grant, ct)` → `Result<ScriptExecutionOutcomeResult>`**. `platform-conventions.md` §3.9 (adapter-registry
   reference) shows a *different* surface: **`CodeExecutorKind Kind { get; }`** and `ExecuteAsync(request, ct) →
   Result<ScriptExecutionResult>` (no `CompileAsync`, no `grant`, placeholder types `ScriptExecutionRequest`/
   `ScriptExecutionResult` that custom-code defines differently). platform-conventions self-labels its block
   "referenced surface only; full contract owned by the sandbox subsystem", which softens it — but the property
   name (`Kind` vs `Runtime`) and the placeholder return type still conflict literally. **Action:** make
   platform-conventions §3.9 cite custom-code's exact signature (or just name the interface and stop showing a
   diverging body). Confirm `CodeExecutorKind` (deployment enum, P.12) vs `ScriptRuntimeKind` (custom-code enum)
   are intentionally two enums or unify them.

### B. Schema gaps — spec defines an entity NOT in the locked schema

4. **`TtsApprovalQueueEntry` (tts.md). [RESOLVED → owner `tts.md`; added to LOCKED schema as Domain P **P.1a**.]**
   Tenant-scoped (`BroadcasterId guid`), UUIDv7 PK, status enum (`pending`\|`approved`\|`rejected`\|`expired`),
   requested/decided timestamps, soft-delete. tts.md §1 references the real table (resolved in `tts.md` §2; no
   longer provisional). Original finding below.
   `TtsApprovalQueueEntry` is required by the TTS mod-approval flow but **was**
   **not** in the locked schema's enumerated P.1–P.13 tables — now added as **P.1a** and bound by the spec.

5. **`Channels.SongRequestPageToken` (music-sr.md §3.7). [RESOLVED → owner `music-sr.md`; column added to LOCKED schema A.2.]**
   Added as `SongRequestPageToken string(64) Null Unique` (rotatable, mirrors `OverlayToken`). music-sr.md §3.7
   uses the column (resolved in `music-sr.md` §3.7); the `SongRequestPageTokens` fallback table is dropped.
   Original finding below.
   The public SR-page token **was** assumed but **not** a column on `Channels` (A.2) in the locked schema (which only had `OverlayToken`).

   *(Both gaps are now resolved into binding schema additions in their specs — listed here so the schema owner has
   the full gap history. No other spec invents a table: all remaining specs map 1:1 to locked tables.)*

### C. Naming — `BroadcasterId` / `UserId` / Twitch-id discipline

6. **Clean.** Every spec uses `Guid BroadcasterId` as the tenant key, demotes raw Twitch ids to indexed
   `*TwitchUserId`/`TwitchChannelId` attribute columns, and acknowledges the `ITenantScoped.BroadcasterId`
   `string→Guid` widening as the one load-bearing rebuild. `UserId`/`*UserId` are `Guid` surrogate FKs everywhere.
   One **intentional** nuance, correctly documented: `event-store.md` exposes `Guid? BroadcasterId` (nullable,
   platform-global rows) and deliberately does **not** implement `ITenantScoped` (enforces isolation in the
   service layer instead) — consistent with the schema's "journal/audit tables with nullable `BroadcasterId`".
   Domain-event base `BroadcasterId` staying `string?` (bus serialization key, tenant guid carried as
   `.ToString()` or in explicit `Guid` fields) is uniformly noted across identity-auth, economy, federation,
   moderation, event-store — consistent.

### D. Endpoint request/response DTO completeness

7. **Clean.** Every controller-endpoint row names both a request DTO (or `—` for bodyless GET/DELETE) and a
   `StatusResponseDto<T>`/`PaginatedResponse<T>` response. Webhook/overlay/handshake endpoints that return raw
   `2xx`/`text`/`challenge` to an external caller (Twitch EventSub webhook, Stripe webhook, federation inbound,
   OAuth callbacks) correctly document the non-envelope response — these are deliberate exceptions, not omissions.

### E. Service-method return-type convention

8. **Clean.** Every fallible service method returns `Task<Result>` / `Task<Result<T>>`; pure synchronous
   accessors that cannot fail return the value directly per the stated convention (`IDeploymentProfileService.Current`,
   `ICurrentTenantService.BroadcasterId`, `IFeatureFlagService.IsEnabledAsync→Task<bool>` read-only eval,
   `IEncryptionService.Encrypt`/`IFieldCipher.Encrypt` pure cipher ops returning `Result<T>` non-async,
   `ITwitchRateLimiter.Observe` void). Hub methods and pipeline-action `ExecuteAsync` follow their own
   (`ActionResult`) contracts as specified. No method returns bare `T`/`null` for an expected-failure path.

### F. Deployment-profile adapter-pair completeness

9. **Clean.** Every profile-selected abstraction names both impls: DB provider (Npgsql/Sqlite), `ICacheService`
   (Hybrid L1+L2 / L1), `IEventBus` (`RedisEventBus`/`EventBus`), `IScriptExecutor` (Wasmtime/Jint), token vault
   (`kms_envelope`/`local_aes`), EventSub transport (conduit/WebSocket), `IRateLimiterPartitionStore`
   (Redis/in-memory), `IRunOnceGuard` (Postgres-advisory/no-op), `IPlatformIamService` (DB/owner-is-full),
   `ITenantSequenceAllocator` (Postgres-FOR-UPDATE/SQLite-BEGIN-IMMEDIATE), `ITtsAudioStore`
   (disk/object-store/inline), `IRemoteEventBus` (Redis/WebSocket), `IKeyVault` (Azure-HSM/local-AES-file),
   `IStripeGateway`/billing (`StripeGateway`/`NullBillingGateway`). `IRunOnceGuard` is consistently named as the
   multi-instance guard across timers, moderation sweep, scheduler, metering, replay, federation dispatch.

   **Minor inconsistency (fixed inline):** two specs injected the `DeploymentProfile` **entity** directly
   (`sp.GetRequiredService<DeploymentProfile>().DbProvider`, `deploymentProfile.TokenVault`) where
   platform-conventions §3.3 establishes the runtime accessor as `IDeploymentProfileService.Current` →
   `DeploymentProfileSnapshot` (with enum fields). Realigned (see "Fixed inline").

### G. Cross-spec type references — all resolvable

10. **Clean.** Shared types referenced across specs all resolve to a definition: `Result<T>`/`PagedList<T>`/
    `PaginationParams`/`StatusResponseDto<T>`/`PaginatedResponse<T>` (existing app primitives); `ICommandAction`/
    `ActionContext`/`ActionResult`/`ITemplateEngine` (commands-pipelines, consumed by moderation/economy/music/tts/
    discord/billing/stream-admin/custom-code/widgets actions); `IChatProvider`/`ITwitchHelixClient`/`IEventBus`/
    `ICacheService`/`IUnitOfWork`/`ICurrentTenantService`/`IChannelAccessService`/`IRunOnceGuard` (platform/identity
    primitives); `IScriptExecutor` (custom-code, consumed by commands-pipelines `run_code`); `EventJournal`/
    `EventSubjectKeys`/`TenantSequences`/`CryptoKey`/`ConsentRecords` (consumed by economy/eventsub/federation/gdpr).
    **`ICommandAction` contract caveat — [RESOLVED → owner `commands-pipelines.md` §3.13].** The contract existed in
    two forms in the live code (Application-side `Type/Category/Description/ExecuteAsync(ActionContext)` vs
    Infrastructure-side `ActionType/ExecuteAsync(PipelineExecutionContext, ActionDefinition)`); commands-pipelines §0
    collapses to ONE canonical contract (`Type`/`Category`/`Description`/`Task<ActionResult> ExecuteAsync(ActionContext,
    CancellationToken)`). The four specs that targeted the divergent Infrastructure shape — **moderation §6, economy §6,
    custom-code §6, stream-admin §6** — have been retargeted to the canonical contract (params from
    `context.Parameters`, tenant from `context.BroadcasterId` as a `Guid`, no `PipelineExecutionContext`/`ActionDefinition`).
    The others (widgets/tts/roles-permissions/billing/discord) already matched and now carry an explicit
    "owned by commands-pipelines §3.13" reference. One action interface survives.

### H. Fixed inline (trivial — already applied to the offending specs)

- **`FeatureFlagChangedEvent` name collision** → `stream-admin.md` §2 renamed its operator-action event to
  **`FeatureFlagAdministeredEvent`** (carries `PrincipalId`) to stop colliding with `platform-conventions.md` §2's
  cache-invalidation `FeatureFlagChangedEvent` (same name + namespace, different members). Admin service now emits
  both (audit + cache-invalidation). §3.2 behavior notes updated to match.
- **`ITenantSequenceService` (platform-conventions §3.6)** → renamed to **`ITenantSequenceAllocator`** and pointed
  at `event-store.md` §3.7 as the authoritative owner (the `Result<long>` + `NextBlockAsync` shape that economy
  and event-store already agree on), superseding the diverging `Task<long>` draft. DI table row updated.
- **`IChannelAccessService` truncated in roles-permissions §3.1** → added the `ResolveOwnChannelAsync` member +
  a note that `platform-conventions.md` §3.2 is the single owner of the full two-method surface (roles-permissions
  had shown only `CanResolveTenantAsync`).
- **`DeploymentProfile` entity injected directly** → `tts.md` §7 and `gdpr-crypto.md` §7 realigned to the
  `IDeploymentProfileService.Current` snapshot accessor with the `DbProviderKind`/`TokenVaultKind` enums
  (platform-conventions §3.3 pattern), instead of resolving the EF entity and string-comparing.

---

### Summary

| # | Finding | Severity | Status |
|---|---|---|---|
| A1 | `CryptoKey` lifecycle: `ICryptoKeyService` vs `ISubjectKeyService` vs `ICryptoShredService` (3 names, 2 shapes, 2 ownership claims) | High | **RESOLVED** — owner **`gdpr-crypto.md`** (`ISubjectKeyService`/`IFieldCipher`/`IKeyVault`/`IKdf`); identity-auth + event-store now reference it (dropped `ICryptoKeyService`/`ICryptoShredService`/standalone `IEncryptionService`) |
| A2 | Plane-C IAM: `IPlatformIamService` vs `IIamAuthorizationService`+`IIamAuditWriter` (overlapping) | High | **RESOLVED** — owner **`roles-permissions.md`** (`IPlatformIamService.AuthorizePlatformAsync`, authorize+audit in one call); stream-admin drops the split public `IIamAuthorizationService`, keeps `IIamAuditWriter` only as IPlatformIamService's internal sink |
| A3 | `IScriptExecutor`: platform-conventions reference shape ≠ custom-code authoritative shape (`Kind`/`Runtime`, return type) | Medium | **RESOLVED** — owner **`custom-code.md`** (`Runtime`/`CompileAsync`/`ExecuteAsync(request, grant, ct)→Result<ScriptExecutionOutcomeResult>`); platform-conventions §3.9 cites it (two enums kept intentionally) |
| B4 | `TtsApprovalQueueEntry` not in locked schema | Medium | **RESOLVED** — owner **`tts.md`**; added to LOCKED schema as **P.1a** (tenant-scoped, soft-delete); tts.md references the real table |
| B5 | `Channels.SongRequestPageToken` not in locked schema | Medium | **RESOLVED** — owner **`music-sr.md`**; `Channels.SongRequestPageToken string(64) Null Unique` added to LOCKED schema (A.2); fallback table dropped |
| G10 | Two live `ICommandAction` contracts; action-bearing specs split across both | Medium | **RESOLVED** — owner **`commands-pipelines.md`** §3.13 (canonical Infrastructure-collapsed `ICommandAction`: `Type`/`Category`/`Description`/`ExecuteAsync(ActionContext, ct)`); every action-bearing spec now references it (economy/stream-admin/moderation/custom-code retargeted off the divergent shape) |
| — | `FeatureFlagChangedEvent` name collision | Low | **Fixed inline** |
| — | `ITenantSequenceService`/`ITenantSequenceAllocator` name drift | Low | **Fixed inline** |
| — | `IChannelAccessService` truncated surface | Low | **Fixed inline** |
| — | `DeploymentProfile` entity injected vs snapshot accessor | Low | **Fixed inline** |

No dangling DTOs, no missing request/response pairs, no unwrapped return types, no incomplete adapter pairs,
clean `BroadcasterId`/`UserId` naming, and full entity→table mapping. The 6 cross-subsystem
ownership/aliasing seams above (3 interface-ownership picks, 2 schema additions, 1 action-contract
consolidation owned by commands-pipelines) are all **RESOLVED** with one canonical owner each.

### 12 subsystem-readiness owner gaps — RESOLVED (see `_READINESS.md` for full one-liners)

These are the cross-spec/schema gaps from `_READINESS.md` (distinct from the interface-aliasing findings above),
now resolved by targeted edits and re-verified against the spec/schema files:

| # | Subsystem | Resolution (canonical owner) |
|---|---|---|
| 1 | roles-permissions | `CreatePrincipalAsync` added to `IPlatformIamService` (provisioning path for employee/service-account principals). |
| 2 | twitch-helix | `[RequireManagementRole]` dropped; service-body `AuthorizeActionAsync("twitch:diagnostics:read")`; seed row added (`management`, Mod 10). |
| 3 | twitch-eventsub | `EventSubTransportKind` (wire DTO, eventsub) vs `EventSubTransportMode` (profile selector, platform-conventions) kept as two enums, disambiguated. |
| 4 | twitch-eventsub | Journal-append single-owned by event-store `IEventJournal.AppendAsync` + `ITenantSequenceAllocator`; local writer removed; eventsub references it. |
| 5 | commands-pipelines | `BuiltinCommandContext` record defined (§3, line 421); every built-in `ExecuteAsync` binds it. |
| 6 | custom-code | Events inherit canonical `DomainEventBase`; `BroadcasterId` is the base-supplied `Guid`, not redeclared. |
| 7 | custom-code | `code:script:author` seed row re-planed to `management` (Broadcaster 40 / `critical`); owns the seed insertion point. |
| 8 | tts | `IOverlayClient.TtsSpeak(TtsSpeakPayload)` + DTOs added to widgets-overlays §7; tts `client_edge` consumes them. |
| 9 | federation-oidc | `EventJournal.Source` enum extended with `federation` in the LOCKED schema (O.1). |
| 10 | federation-oidc | `IFederationInboundTranslator` owns envelope→typed-event mapping (`moderation.ban.shared`→`SharedChatBanIssuedEvent`). |
| 11 | federation-oidc | Inbound federated apply persists `Origin=federation` (distinct from Twitch-native `shared_chat`); moderation §3 leftover fixed inline. |
| 12 | federation-oidc | Active-shared-chat-session precondition declared at `ApplyInboundSharedBanAsync` (enforced there, not by the caller). |

**Verdict: fully implementable.** All originally-reviewed subsystem specs are buildable now, every one decision-complete with no
open or pending questions (`scaling-qos.md`, `webhooks.md`, and `code-execution-sandbox.md` are decision-ready on the same conventions). The 12 resolutions are provisional
(owner asleep) and logged for override-on-wake in `../2026-06-16-decisions-pending-confirmation.md`; only gap #7
(re-planing the T3 custom-code action `community`→`management`) changes a security plane and warrants a deliberate
owner confirmation.
