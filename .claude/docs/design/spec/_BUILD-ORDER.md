# Build Order — the single-fire implementation sequence

Closes gap **X3**. The stack doc deferred cross-subsystem sequencing to "the task board"; this **is** that order. It is the dependency-topological sequence a single autonomous implementation pass follows so a consumer is never built before its owner. Per-spec §14 vertical-slice checklists still govern *within* a subsystem; this governs the order *between* them.

The repo is a clean-slate `master` with the existing tested backend as the working tree (uncommitted). The pass evolves that base toward the specs — it does not start blank.

---

## Per-slice acceptance gate (the "green" bar)

A slice is the smallest end-to-end change (entity + service + data access + test, or one endpoint). Before a slice is committed, **all four** must hold:

1. The slice **compiles**.
2. The slice's **own tests pass** — and they prove behavior (state change, emitted events, side effects), not surface (`spec/backend-structure.md` testing standard).
3. The **whole solution builds** (no downstream break).
4. The **app boots in the CI lite profile** — `self_host_lite` + SQLite + in-memory cache/bus + EventSub-connect disabled + dummy Twitch creds (catches DI/wiring/startup breaks a unit test misses).

CI is **hermetic** — no slice hits live Twitch (twitch-helix §10 fakes/fixtures). Security-critical slices carry mandatory tests: cross-tenant IDOR, tenant query-filter/RLS, AES-GCM AAD non-transplantability, fail-closed authz.

---

## Phases

Phases are ordered; **slices within a phase** may run in parallel where they don't share a contract. A later phase may begin only once the contracts it consumes are green.

### Phase 0 — Scaffolding & structure
- Module-first reorganization of the existing tree per `backend-structure.md` §8 (mechanical, guarded by the existing 327 tests — rename/move, fix namespaces, build+test green). Everything after lands in its final home.
- CI lite boot profile + hermetic Twitch fakes/fixtures (twitch-helix §10) wired so the gate's step 4 is runnable.
- `TimeProvider` registered as the single clock (platform-conventions §3.11); ban direct `UtcNow`.
- **Exit:** solution builds module-first, 327 baseline tests green, CI boots the lite profile.

### Phase 1 — Domain + persistence base
- Locked-schema entities, enums, `ITenantScoped` (**`BroadcasterId` string→Guid widen** — load-bearing), canonical `DomainEventBase` (record).
- EF model; **EF10 named query filters** (tenant + soft-delete); Postgres RLS connection interceptor; one **greenfield initial migration per provider** (Postgres + SQLite). Repository + `IUnitOfWork`.
- Seeders + ordering (backend-structure §5.2): ActionDefinitions/IamPermissions/IamRoles/IamRolePermissions, TtsVoice, BillingTier, FeatureFlag.
- **Exit:** migrations apply on both providers; tenant filter + RLS proven by an IDOR/isolation test; seeders run idempotently.

### Phase 2 — Platform foundation (cross-cutting)
- `IDeploymentProfileService` detector + adapter registry; `ICacheService`/`IEventBus`/`IRunOnceGuard`/`IRateLimiterPartitionStore` adapters.
- **`TenantResolutionMiddleware` IDOR fix** + `CurrentTenantService` (Guid).
- **Crypto rebuild** (security): `IFieldCipher` (AES-256-GCM + AAD) over `ISubjectKeyService` (per-subject DEK), OS-vault KEK custody, envelope/crypto-shred — replaces the legacy AES-CBC `EncryptionService`.
- SignalR hubs + auth + audience lanes; problem details, versioning, health, OpenTelemetry.
- **Exit:** the 3 live security defects (IDOR, tenant filter, transplantable crypto) are closed with passing tests; lite + saas adapters both resolve.

### Phase 3 — Identity & authorization
- `AuthService` (Twitch OAuth, **RS256/ES256 JWT**, JWKS), `IScopeGrantService` (progressive scopes + drop detection).
- Roles & permissions: 3 planes / 2 gates — `IActionAuthorizationService` (+ the `[Authorize(Policy=key)]` policy provider), `IRoleResolver`, `IMembershipService`, `ICommunityStandingService`, `IPermitService`, `IPlatformIamService`. Seed catalogue (§7.1) already loaded in Phase 1.
- **Exit:** a gated endpoint returns 200 for an authorized caller and 403 for an under-level one; Gate-2 fails closed on an unknown key; effective-level MAX rule tested.

### Phase 4 — Event store + Twitch ingestion
- Event store: `IEventJournal`, `ITenantSequenceAllocator`, projections (+ `ResetAsync`/replay), `IEventUpcaster` registry, `IJournalPostCommitHook`.
- Twitch Helix client (codegen DTOs, `ITwitchRateLimiter` adaptive limiter, resilience handler); EventSub transport (WS lite / conduit+webhook SaaS) → dispatcher → journal → bus; `IChatTransport` (Helix send / IRC).
- **Exit:** a faked EventSub notification flows source→dedupe→journal→projection→bus; replay rebuilds a projection deterministically.

### Phase 5 — Command & pipeline engine
- Commands, pipelines, conditions, the canonical `ICommandAction`, `ITemplateEngine`, built-ins (`BuiltinCommandContext`), timers, event responses.
- **Exit:** a custom command + an event-response pipeline execute end-to-end on a faked chat message; template variables resolve.

### Phase 6 — Feature subsystems (parallel; each depends on 1–5)
- economy · **rewards (M1)** · moderation · music-sr · tts · widgets-overlays · discord · stream-admin.
- Rewards bridges Phase 4 redemptions → Phase 5 pipelines; economy/music consume it.
- **Exit:** each subsystem's headline flow passes a behavior test (e.g. redemption→pipeline, points debit+ledger+leaderboard atomically, SR fair-queue ordering).

### Phase 7 — Commerce, platform ops & analytics
- monetization-billing · federation-oidc · webhooks · custom-code/sandbox.
- **analytics (M2)** projections (per-viewer profiles, channel daily, moderation projections, usage rollups, HeatScore).
- **rollout-updates (M3)** — feature-flag staged rollout + zero-downtime migration sequencing.
- **Exit:** billing entitlement gates a feature; a sandboxed script runs deny-by-default; analytics projections rebuild from the journal.

### Phase 8 — API surface & integration
- Controllers + hub wiring complete; Scalar docs; end-to-end hermetic integration tests across the gated surface.
- **Exit:** the lite profile serves the full v1 API + public web pages (song-request, overlays, OAuth landing), boots clean, all tests green.

---

## Dependency rules (invariants the order encodes)

- Nothing consumes `IActionAuthorizationService` (Phase 3) before the seed catalogue + gates exist.
- No subsystem writes the journal before event-store (Phase 4) owns `IEventJournal`.
- No feature (Phase 6+) is built before the engine (Phase 5) and auth (Phase 3).
- The crypto + IDOR + tenant-filter fixes (Phase 2) precede anything that stores tokens or reads tenant data.
- Federation (Phase 7) does not start before RS256 signing (Phase 3) lands.

Scope of the pass: **backend + public web pages**, headless-functional on Twitch. The KMP dashboard is out (frontend spec phase). "Working bot" = Phase 8 exit.
