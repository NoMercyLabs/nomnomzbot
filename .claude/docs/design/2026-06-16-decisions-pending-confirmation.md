# Design Decisions — Resolved

Source: the 10 "Open tensions for owner sign-off" in
`2026-06-16-stack-and-dependencies.md` §5, now resolved into binding decisions.

**Status of every item below: `RESOLVED — final binding decision`.** Every numbered item is a settled,
authoritative decision for the project. There is nothing pending, open, or deferred here; where an item
names a prerequisite, that prerequisite is a hard ordering **dependency**, not a deferral.

Provenance: items 1, 2, 6, 3, 9 were decided directly by the owner (carried in verbatim). Items
4, 5, 7, 8, 10 follow project convention and best practice. Each carries a one-line rationale.

---

## 1. Distributed rate limiting (SaaS) — tension #1

**DECISION:** Adopt a **profile-adapter rate limiter** behind one `IRateLimiter` abstraction:
**SaaS = Redis-backed distributed limiter** (custom glue over the already-present StackExchange.Redis);
**self-host/lite = in-box ASP.NET Core `RateLimiter`, in-memory** (per-instance is correct for single-node).
Rationale: closes the N× brute-force/auth-protection correctness gap on multi-node SaaS without forcing a
Redis dependency onto the zero-dep lite profile.

## 2. Jint is not a security boundary — tension #2

**DECISION:** **Lite = single-trusted-operator only.** Jint stays the lite
executor; the instant the lite profile becomes multi-user/shared it is unsafe and must move to
Wasmtime + per-tenant process isolation. Committed: **Jint (self-host) + Wasmtime x86_64-Cranelift (SaaS)
behind one `IScriptExecutor`.**
Rationale: Jint's resource-safety threat model only holds for a single trusted operator; the profile split
keeps the no-native-deps no-Docker self-host while giving SaaS a real isolation boundary.

## 3. OpenIddict bus-factor vs. running an issuer at all — tension #3

**DECISION:** **OIDC issuer = OpenIddict, FEATURE-GATED** — stood up *only* when
federation or multi-user-dashboard SSO is enabled. **Basic single-user self-host = JWT (resource-server)
only, no issuer, no authorize/token/JWKS surface.**
Rationale: avoids hand-rolling the auth protocol where an issuer is genuinely needed, while keeping the
attack surface and the maintainer dependency out of the common single-user path entirely.

## 4. HS256 → asymmetric migration blocks federation — tension #4

**DECISION:** **The HS256 → RS256/ES256 signing migration is a hard DEPENDENCY of any federation/SSO
work** (migrate token issuance to `JsonWebTokenHandler` 8.19.1 with asymmetric keys; publish JWKS).
Federation is gated on this and does not start before it.
Rationale: HS256 is unfederatable (sharing the secret = sharing signing power); there is no JWKS/SSO key
path until signing is asymmetric, so this is a hard ordering constraint, not a preference.

## 5. StackExchange.Redis 3.0 RESP2→RESP3 default flip — tension #5

**DECISION:** **Stay pinned at 2.13.17.** Do not adopt the 3.x line until it bakes; any later bump
requires an explicit reply-shape audit (array→map changes) before merge.
Rationale: the 3.0 default flip changes some reply shapes; holding avoids silent breakage in HybridCache
L2, the SignalR backplane, and the run-once lock for zero current benefit.

## 6. Wasmtime escape class is recurring — tension #6

**DECISION:** **An advisory-subscription + fast-patch SLA for the SaaS edge is MANDATORY.**
Run **only x86_64-Cranelift**; never enable Winch or aarch64-Cranelift. Keep the
compensating controls (host-call budget, wall-clock watchdog, WIT value-in/value-out, fail-closed unknown
action/condition, SSRF egress allowlist).
Rationale: x86_64-Cranelift dodged the April-2026 *Critical* escapes but is not advisory-free
(CVE-2026-24116, CVE-2026-34944) and compiler-miscompilation escapes are inherent — patch latency is a live
operational risk, so the SLA is a hard requirement.

## 7. No file logging after dropping Serilog — tension #7

**DECISION:** **Console-only for lite** (`AddConsole()`), documented as "pipe to file /
local OTel Collector". Do **not** build a custom file `ILoggerProvider` (YAGNI).
Rationale: OTel has no file sink, but console output is tail-able/redirectable and keeps the lite profile
dependency-free; a ~50 LOC file provider is speculative and out of scope.

## 8. Multi-instance double-fire is latent today — tension #8

**DECISION:** **`IRunOnceGuard` (no-op on lite, `pg_try_advisory_lock` /
DistributedLock.Postgres on SaaS) is a hard DEPENDENCY of any multi-instance SaaS deploy.** Single-instance
deploys are safe without it; multi-instance is gated on it shipping.
Rationale: current `BackgroundService`s (stream poll, token refresh, timers) will duplicate on multi-node
SaaS; making the guard a release gate prevents shipping the latent double-fire.

## 9. DataProtection CVE-2026-40372 remediation — tension #9

**DECISION:** **Pin DataProtection ≥ 10.0.7 on all profiles.** **Persist the key ring
to the database** (shared across instances) and **rotate it on every deploy.** SaaS-on-Linux is the exposed
config (pin + rotation mandatory); a Windows self-host lite is not primary-affected (pin defensively;
rotation only if exposed).
Rationale: a version bump alone leaves forged-eligible tokens valid until rotation; DB-backed key ring +
deploy-time rotation closes the window for the affected Linux/non-Windows SaaS edge.

## 10. Crypto-shred completeness — tension #10

**DECISION:** **O(1) crypto-shred is the committed erasure for the `[PII-shred]` ciphertext path.**
Row-level scrub + one-subject-per-event enforcement is committed scheduled work and is a hard DEPENDENCY
of the plaintext and multi-subject paths — the ciphertext path ships on O(1) DEK-destroy without waiting
for it. Plaintext snapshot columns (`[PII-scrub]`) and multi-subject events (gift sub, raid) get explicit
row-level erasure and a one-subject-per-event invariant under that scheduled work.
Rationale: O(1) DEK-destroy erasure genuinely holds for `[PII-shred]` ciphertext; plaintext snapshots and
multi-subject events need row-level handling, which is real but separable work — it gates only those paths,
not the vault rebuild.

---

## Summary of decisions

| # | Tension | Decision | Source |
|---|---------|----------|--------|
| 1 | SaaS distributed rate limiting | Profile adapter: Redis limiter (SaaS) / in-box in-memory (lite) | owner |
| 2 | Jint not a security boundary | Lite = single-operator; Jint+Wasmtime, one `IScriptExecutor` | owner |
| 3 | OIDC issuer | OpenIddict, feature-gated (federation/SSO only); else JWT-only | owner |
| 4 | HS256 → asymmetric | RS256/ES256 migration is a dependency of federation | best practice |
| 5 | StackExchange.Redis 3.0 | Stay on 2.13.17 until 3.x bakes; audit before bump | best practice |
| 6 | Wasmtime escape class | Advisory-sub + fast-patch SLA mandatory; x86_64-Cranelift only | owner |
| 7 | No file logging | Console-only for lite; no custom file provider (YAGNI) | best practice |
| 8 | Multi-instance double-fire | `IRunOnceGuard` is a dependency of multi-instance SaaS deploy | best practice |
| 9 | DataProtection CVE-2026-40372 | Pin ≥10.0.7 all profiles; DB key ring; rotate on deploy | owner |
| 10 | Crypto-shred completeness | Ciphertext O(1) committed; row-level scrub gates plaintext/multi-subject | best practice |

---

## Seams resolved — binding

Six cross-subsystem ownership/aliasing seams in the interface-spec set are resolved as targeted edits (one canonical owner per seam; other specs reference it). These ownership assignments are final.

- **A1 — CryptoKey DEK lifecycle → owner `gdpr-crypto.md`.** Canonical `ISubjectKeyService` / `IFieldCipher` / `IKeyVault` / `IKdf`; `identity-auth.md` + `event-store.md` now reference them and dropped `ICryptoKeyService.ShredAsync` / `ICryptoShredService` / standalone `IEncryptionService`.
- **A2 — Plane-C IAM authorization → owner `roles-permissions.md`.** Canonical `IPlatformIamService.AuthorizePlatformAsync` (authorize + audit in one call); `stream-admin.md` dropped the split public `IIamAuthorizationService`, kept `IIamAuditWriter` only as the internal audit sink.
- **A3 — `IScriptExecutor` → owner `custom-code.md`.** Canonical `Runtime` / `CompileAsync` / `ExecuteAsync(request, grant, ct)→Result<ScriptExecutionOutcomeResult>`; `platform-conventions.md` §3.9 cites it (kept `CodeExecutorKind` and `ScriptRuntimeKind` as two intentional enums).
- **B4 — `TtsApprovalQueueEntry` → owner `tts.md`.** Added to the LOCKED schema as Domain P **P.1a** (tenant-scoped, UUIDv7 PK, status enum, soft-delete) + changelog; `tts.md` references the real table.
- **B5 — `Channels.SongRequestPageToken` → owner `music-sr.md`.** Added `string(64) Null Unique` rotatable column to LOCKED schema A.2 + changelog; `music-sr.md` uses it and dropped the fallback table.
- **G10 — `ICommandAction` contract → owner `commands-pipelines.md` §3.13.** One canonical contract (`Type`/`Category`/`Description`/`ExecuteAsync(ActionContext, ct)`); economy/stream-admin/moderation/custom-code retargeted off the divergent Infrastructure shape, the rest carry an explicit reference.

---

## Owner gaps resolved — binding

The 12 cross-spec/schema **subsystem-readiness** owner gaps from `spec/_READINESS.md` (distinct from the 6 interface seams above) are resolved by targeted edits — one canonical owner/shape per gap, re-verified to resolve against the spec files and the locked schema. All flip the 6 previously-blocked subsystems to Ready and the overall verdict to **implementable**. These resolutions are final.

1. **roles-permissions — `IamPrincipal` provisioning.** Added `IPlatformIamService.CreatePrincipalAsync(actingPrincipalId, CreatePrincipalRequest, ct) → Result<IamPrincipalDto>` (`roles-permissions.md` §3). Principals are created through this gated call, not silently seeded.
2. **twitch-helix — diagnostics authz mechanism.** Dropped the non-existent `[RequireManagementRole]`; gate moved to the service body via `AuthorizeActionAsync("twitch:diagnostics:read")`; new seed row `twitch:diagnostics:read` (`management`, Moderator 10, read-only). Matches the per-action ladder used everywhere.
3. **twitch-eventsub — transport enum collision.** Kept two enums: `EventSubTransportKind { WebSocket, Conduit, Webhook }` (wire DTO, owned in eventsub) vs `EventSubTransportMode { WebSocket, ConduitWebhook }` (deployment-profile selector, platform-conventions). Disambiguation note added; SaaS `ConduitWebhook` drives both `Conduit` and `Webhook` wire kinds.
4. **twitch-eventsub — journal-append owner.** Single-owned by event-store's `IEventJournal.AppendAsync` + `ITenantSequenceAllocator`; the local `IEventJournalWriter` was removed. The eventsub dispatcher journals through event-store.
5. **commands-pipelines — `BuiltinCommandContext`.** Defined: `record BuiltinCommandContext(Guid BroadcasterId, Guid TriggeringUserId, CommunityRole TriggeringUserRole, …)` (§3). Every built-in `ExecuteAsync` binds it.
6. **custom-code — domain-event base.** Events inherit the canonical `DomainEventBase`; `BroadcasterId` is the base-supplied `Guid` (locked UUIDv7 key, platform-conventions §2.0), not redeclared per-event. Binds to the bus contract.
7. **custom-code — `code:script:author` plane vs floor.** Resolved to `Plane=management` (where Broadcaster 40 actually exists), `FloorLevel=40 (Broadcaster)`, `FloorTier=critical`; custom-code §5.1 owns the seed insertion point. **(Plane change — final: the T3 custom-code escape-hatch is re-planed `community`→`management`.)**
8. **tts — client-edge dispatch surface.** Added `IOverlayClient.TtsSpeak(TtsSpeakPayload)` + `TtsSpeakPayload`/`TtsSpeakOptions` DTOs to widgets-overlays §7 (the `IOverlayClient` owner). tts `client_edge` consumes them — utterance event pushed, audio rendered edge-side, no bytes on the wire.
9. **federation-oidc — `EventJournal.Source=federation`.** Extended the LOCKED schema O.1 enum with `federation` (+ changelog). Federated events are first-class, distinct from `eventsub|domain|irc|import`.
10. **federation-oidc — envelope→typed-event mapping.** Added `IFederationInboundTranslator.TranslateAndApplyAsync` (§3.6) deserializing `FederationEventEnvelope.PayloadJson` by `FederatedEventType` (`moderation.ban.shared`→`SharedChatBanIssuedEvent`) and invoking moderation's `ISharedBanService.ApplyInboundSharedBanAsync`. Fails closed on unknown type/schema.
11. **federation-oidc — `Origin` value conflict.** Settled on `Origin=federation` for the cross-instance inbound apply (distinct from Twitch-native same-instance `Origin=shared_chat`). The stale `origin=shared_chat` on `ApplyInboundSharedBanAsync` in `moderation.md` §3 was corrected inline to `origin=federation`.
12. **federation-oidc — shared-chat-session precondition.** Declared at the owning method: `ApplyInboundSharedBanAsync` verifies an active shared-chat session (`SharedChatSessionId` carried on `SharedChatBanIssuedEvent`) and enforces the predicate itself — "not by the caller" (`moderation.md` §3).
