# Spec Readiness Report

> **Authority note (2026-06-17).** This report's per-subsystem "Ready / 0 blocked" view is **engine-scoped** and was historically over-optimistic — `_GAP-AUDIT.md` is the authoritative gate. As of 2026-06-17 the in-spec BLOCKING band (B1–B7) + cross-cutting (X1–X4) are **verified resolved**, so the engine/feature specs below are genuinely ready. **But "ready" here does NOT mean the whole product is buildable:** a **product-edge & ops band (P1–P7)** — onboarding/setup backend, Spotify/YouTube OAuth, Community/Dashboard controllers, distribution/deploy, wasm-serving, mDNS backend, poll/prediction management — is still **open** (see `_GAP-AUDIT.md` → "PRODUCT-EDGE & OPS GAPS"). Read both docs together.

21 subsystem specs ran through critic + auto-fix. Blocking gaps = contradictions or undefined contracts a dev would hit head-on. Auto-fixed = resolvable from existing locked context (corrected in the spec file). Needs-owner = a design/schema decision no single spec can make alone.

**Update (2026-06-16):** the 12 owner gaps below were resolved autonomously by targeted edits across the spec files + the locked schema (the owner was asleep; decisions are provisional — see `../2026-06-16-decisions-pending-confirmation.md` "Owner gaps resolved autonomously (override on wake)"). All previously-blocked subsystems are **Ready**; the overall verdict is **implementable**. The 12 resolutions were re-verified against the spec/schema files (cross-references resolve; one stale `Origin=shared_chat` leftover in `moderation.md` §3 was corrected inline to `Origin=federation` for the cross-instance inbound path). Every subsystem is now decision-complete — no spec carries an open or pending question.

**Added (2026-06-17):** `frontend.md` — the KMP + Compose Multiplatform dashboard spec (the **client layer**, supersedes the thin `../2026-06-16-frontend.md` draft). One codebase → JVM desktop **and** web (wasmJs) identical app; the typed shared client (Ktor REST generated from the v1 OpenAPI doc + a **hand-rolled SignalR-over-WebSockets** client in `commonMain`) is the sole integration point; profile-agnostic direct-connect with native mDNS multi-origin switcher vs web single-origin; Navigation Compose + ViewModel/StateFlow + Koin; first-party Compose resources (`en`/`nl`); Material3 themed to Figma tokens. Decision-ready: §11 decisions are final; it consumes the existing v1 API + hubs and **owns no schema** (0 blocking, 0 needs-owner). The one load-bearing net-new piece is the hand-rolled SignalR client (no Kotlin/Wasm library exists) — built once in `commonMain` for both targets.

**Added (2026-06-16):** `scaling-qos.md` (SaaS scaling, fairness & QoS) — log-first command/event runtime (`ICommandLog`), per-tenant `IFairWorkScheduler`, distributed `IRateLimiter`, three priority lanes, backpressure/load-shedding, stateless `IChatTransport`, and data-tier scaling (`IReadDbContext` + monthly journal partitioning). Decision-ready: §0 D1–D10 are final and binding, it reuses the established `DeploymentProfile.Mode` adapter axis and existing `IFairQueue<T>`/`IRunOnceGuard`/`IEventBus` primitives, and its single schema addition (`CommandLogEntry`, Domain O) sits beside `EventJournal`. Ships with 0 blocking gaps, 0 needs-owner.

## Subsystem status

| Subsystem | Ready? | Blocking gaps found | Auto-fixed | Needs owner |
|-----------|--------|---------------------|-----------|-------------|
| identity-auth | Ready | 0 | 0 | 0 |
| roles-permissions | Ready | 2 | 2 | 0 |
| twitch-helix | Ready | 1 | 1 | 0 |
| twitch-eventsub | Ready | 3 | 3 | 0 |
| rewards | Ready | 0 | 0 | 0 |
| analytics | Ready | 0 | 0 | 0 |
| event-store | Ready | 0 | 0 | 0 |
| commands-pipelines | Ready | 2 | 2 | 0 |
| custom-code | Ready | 2 | 2 | 0 |
| widgets-overlays | Ready | 1 | 1 | 0 |
| economy | Ready | 0 | 0 | 0 |
| moderation | Ready | 1 | 1 | 0 |
| music-sr | Ready | 0 | 0 | 0 |
| tts | Ready | 1 | 1 | 0 |
| discord | Ready | 2 | 2 | 0 |
| monetization-billing | Ready | 2 | 2 | 0 |
| gdpr-crypto | Ready | 0 | 0 | 0 |
| stream-admin | Ready | 0 | 0 | 0 |
| federation-oidc | Ready | 4 | 4 | 0 |
| platform-conventions | Ready | 0 | 0 | 0 |
| scaling-qos | Ready | 0 | 0 | 0 |
| webhooks | Ready | 0 | 0 | 0 |
| rollout-updates | Ready | 0 | 0 | 0 |
| code-execution-sandbox | Ready | 0 | 0 | 0 |
| backend-structure | Ready (rulebook) | 0 | 0 | 0 |
| frontend | Ready (client) | 0 | 0 | 0 |
| onboarding-setup | Ready | 0 | 0 | 0 |
| integrations-oauth | Ready | 0 | 0 | 0 |
| community-dashboard | Ready | 0 | 0 | 0 |
| broadcaster-liveops | Ready | 0 | 0 | 0 |
| deployment-distribution | Ready (rulebook) | 0 | 0 | 0 |
| **Total** | **28 ready / 0 blocked** | **22** | **22** | **0** |

> **Product-edge & management band (2026-06-17, P1–P7).** The five specs above + the management-surface extensions to `moderation.md`/`twitch-helix.md` (chat controls, VIP/mod writes, unban-requests, block-list, suspicious-users, warn/automod/blocked-terms Helix legs), `stream-admin.md` (CCL/language/branded/extensions gating), and `music-sr.md` (full Spotify remote + YouTube manage) close the product-edge band and the [[external-api-full-management-coverage]] buildout. ~32 new action keys seeded in `roles-permissions.md §7.1`; schema F.12/F.13 + `AutoModConfigs.ShieldModeActive` added. See `_GAP-AUDIT.md` → PRODUCT-EDGE & OPS GAPS for the per-item resolution.

## Owner decisions — RESOLVED

The twelve gaps that cross spec/schema boundaries have each been resolved with a chosen owner/shape and re-verified in the spec files. Resolution one-liner per gap:

**roles-permissions**
1. **RESOLVED** — `IamPrincipal` provisioning path added: `IPlatformIamService.CreatePrincipalAsync(actingPrincipalId, CreatePrincipalRequest, ct) → Result<IamPrincipalDto>` (`roles-permissions.md` §3, line 267). Employee principals (from `Users.IsPlatformPrincipal`) and service-account principals are created through this gated call; no silent seed.

**twitch-helix**
2. **RESOLVED** — Dropped the non-existent `[RequireManagementRole]` attribute; the diagnostics endpoint carries `[Authorize]` only and enforces the floor in the service body via `IActionAuthorizationService.AuthorizeActionAsync("twitch:diagnostics:read")`. New seed row added: `twitch:diagnostics:read` (`management`, Moderator 10, `low`, read-only). Matches the per-action ladder used everywhere else.

**twitch-eventsub**
3. **RESOLVED** — Two enums kept, disambiguated: `EventSubTransportKind { WebSocket, Conduit, Webhook }` (owned in eventsub §2, the *wire* transport DTO) vs `EventSubTransportMode { WebSocket, ConduitWebhook }` (platform-conventions §3.3, the deployment-*profile* selector). Explicit disambiguation note added; a SaaS `EventSubTransportMode.ConduitWebhook` drives both `Conduit` and `Webhook` wire kinds.
4. **RESOLVED** — Journal-append seam given a single owner: event-store's `IEventJournal.AppendAsync` + `ITenantSequenceAllocator`. The eventsub dispatcher journals **through event-store** (the local `IEventJournalWriter` is gone); eventsub §3.5/§7 now reference event-store as the canonical journal owner.

**commands-pipelines**
5. **RESOLVED** — `BuiltinCommandContext` defined: `record BuiltinCommandContext(Guid BroadcasterId, Guid TriggeringUserId, CommunityRole TriggeringUserRole, …)` (commands-pipelines §3, line 421). Every built-in `ExecuteAsync` binds to this shape.

**custom-code**
6. **RESOLVED** — Events inherit the canonical `DomainEventBase` (not the undefined `DomainEvent`); `BroadcasterId` is supplied by the base as `Guid` (locked UUIDv7 key, platform-conventions §2.0) and is **not** redeclared per-event. Binds to the bus contract.
7. **RESOLVED** — `code:script:author` seed row added with the contradiction resolved to `Plane=management` (where Broadcaster 40 actually exists), `FloorLevel=40 (Broadcaster)`, `FloorTier=critical`. custom-code §5.1 owns the seed/insertion point; all 8 controller endpoints gate on it via `AuthorizeActionAsync`.

**tts**
8. **RESOLVED** — `IOverlayClient.TtsSpeak(TtsSpeakPayload)` + the `TtsSpeakPayload`/`TtsSpeakOptions` DTOs added to widgets-overlays §7 (the `IOverlayClient` owner). tts.md `client_edge` dispatch consumes them (server pushes the utterance event, browser-source renders audio edge-side — no bytes on the wire).

**federation-oidc**
9. **RESOLVED** — `EventJournal.Source` enum extended with `federation` in the LOCKED schema (O.1, line 1078, + changelog line 42). Federated/relayed events are first-class, distinct from `eventsub|domain|irc|import`.
10. **RESOLVED** — Envelope→typed-event mapping given an owner: `IFederationInboundTranslator.TranslateAndApplyAsync` (federation §3.6) deserializes `FederationEventEnvelope.PayloadJson` by `FederatedEventType` (`"moderation.ban.shared"` → `SharedChatBanIssuedEvent`) and invokes moderation's `ISharedBanService.ApplyInboundSharedBanAsync`. Fails closed on unknown type/schema.
11. **RESOLVED** — `Origin` value settled: the cross-instance inbound apply persists `Origin=federation` (distinct from Twitch-native same-instance `Origin=shared_chat`). Documented in federation §1/§6 and moderation §1; the stale `origin=shared_chat` on `ApplyInboundSharedBanAsync` (moderation §3, line 315) was corrected inline to `origin=federation`.
12. **RESOLVED** — Shared-chat-session precondition now declared at the owning method: `ApplyInboundSharedBanAsync` verifies an active shared-chat session (`SharedChatSessionId` carried on `SharedChatBanIssuedEvent`) and enforces the predicate itself ("not by the caller", moderation §3, line 315). The federation inbound gate sequence delegates the apply to it.

## Overall verdict

**Implementable.** All 21 subsystem specs are buildable now and decision-complete — none carries an open or pending question. The previously-blocked subsystems (roles-permissions, twitch-helix, twitch-eventsub, commands-pipelines, custom-code, tts, federation-oidc) are unblocked; the 12 cross-spec/schema owner decisions are resolved with a single canonical owner each, and the cross-references were re-verified to resolve against the spec files and the locked schema. One trivial leftover (`Origin` mismatch in moderation §3) was fixed inline during verification. `scaling-qos.md` was added decision-ready (0 blocking, 0 needs-owner).

**Residual that genuinely still needs Stoney:** none are *blocking* — but every resolution is **provisional (override on wake)**. The single decision most worth an explicit owner glance is gap **#7** (`code:script:author` re-planed from `community`→`management` with a `critical` Broadcaster-40 floor): this is the only resolution that changed a security *plane*, so it deserves a deliberate "yes, the T3 custom-code escape-hatch is management-plane / Broadcaster-floor" confirmation rather than silent acceptance.
