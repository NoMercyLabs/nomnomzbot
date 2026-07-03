# Interface Specification — External Automation API

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** the Streamer.bot WebSocket/HTTP API (`docs.streamer.bot/api/websocket` — the ecosystem reference whose `request`/`id`/`status` + `event` message shape third-party tools, Stream Deck plugins, Touch Portal and `@streamerbot/client`-style libraries already speak; modeled, not copied). Corpus: `commands-pipelines.md` (§3.3 `IPipelineEngine.ExecuteAsync(PipelineRequest)`, §3.4 `IPipelineService`/`ICommandService` list, `TriggerKind=manual`); `event-store.md` (§3.1 `IEventJournal`, event naming); `platform-conventions.md` (§2.0 `DomainEventBase`, §3.3 `IDeploymentProfileService.Current`, `IEventBus`, `ICacheService`); `scaling-qos.md` (§4.1 `IRateLimiter`, §6 `IChatTransport`); `identity-auth.md` (`RefreshToken.TokenHash` hashing + `Channels.OverlayToken` opaque-token patterns); `roles-permissions.md` (Gate-2, §5 cell format, `DangerTier`); locked schema `2026-06-16-database-schema.md` (Domain P — platform integrations).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way (never `.Result`/`.Wait()`); `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs (`Guid.CreateVersion7()`); `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json for app JSON; secrets via `IFieldCipher` / hashed-token pattern.

> **Why.** Streamer.bot's biggest extensibility win is its WebSocket/HTTP server: external surfaces — Stream Deck, Touch Portal, Bitfocus Companion, mobile remotes, custom scripts in any language — connect, **run any action**, **subscribe to the event stream**, read state, and send chat. Our SignalR hubs (`DashboardHub`/`OverlayHub`/`OBSRelayHub`/`AdminHub`) serve our own frontend/overlays over a Microsoft-client transport; they are **not** a language-agnostic third-party surface, and `webhooks.md` is a passive event-adapter layer, not a command/query/subscribe API. This subsystem adds the missing active control surface: a documented **plain WebSocket + REST** API, authed by per-channel scoped tokens, that drives pipelines and streams events to any tool. It is the foundation the first-party **Stream Deck** integration and any community control surface ride on.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Plain WebSocket + REST — deliberately NOT SignalR.** The whole point is language-agnostic third-party tooling; SignalR would lock integrators into the MS client. The existing hubs stay internal (frontend/overlay); this surface is independent. Message shape (`op`/`id` request → correlated `response`, plus pushed `event`) mirrors Streamer.bot's proven shape so existing tools/clients port with minimal effort. |
| D2 | **Per-channel scoped API tokens.** A token is a channel-owned credential (Plane-B / broadcaster automation — **not** a Plane-C IAM principal). High-entropy opaque secret, **stored hashed** (`TokenHash`, the `RefreshToken` pattern), shown **once** on creation; a non-secret `TokenPrefix` identifies it in the UI afterward. Default-deny **scopes** (`invoke` \| `read` \| `events` \| `chat`) + optional `AllowedPipelineIds` invoke-allowlist. **Auth is always required** — no auth-optional mode (default-deny), even on localhost self-host. |
| D3 | **Auth transport: header or first-message — never query-param.** Native tools send `Authorization: Bearer <token>` on the REST request / WS handshake. Browser clients (which cannot set WS handshake headers) send a first `{ "op":"authenticate", "token":"…" }` within a short auth-timeout, else the socket closes. Tokens never travel in a query string (contrast the SignalR `?access_token=` hubs — acceptable for those, not here). |
| D4 | **Two planes.** **Management plane** (token CRUD) lives under `/api/v1/automation/*`, JWT-authed, Gate-2 `automation:tokens:*` (Broadcaster / `Critical`). **Data plane** (the external API itself) lives under `/automation/v1/*` + WS `/automation/v1/stream`, authed by `IAutomationTokenAuthenticator` against the token + its scopes. |
| D5 | **Invocation = `TriggerKind=manual`.** `POST /automation/v1/invoke` resolves the pipeline (by id or name), enforces scope `invoke` + the token allowlist, and calls `IPipelineEngine.ExecuteAsync` with a synthetic **automation actor** (`TriggeredByDisplayName` = token name; `{{trigger.source}}` = `automation`). No new execution path. |
| D6 | **Event stream is a curated public catalog, default-deny.** Only domain events with a registered `IAutomationEventDescriptor` (stable `PublicName` + **PII-safe** payload projection) are exposed; everything else is invisible (no internal/PII leakage). Descriptors are **auto-discovered** — exposing a new event = drop a descriptor, no engine edit. The bridge consumes `IEventBus`, fans out to locally-connected sessions holding scope `events`; cluster-wide for free because the bus is cluster-wide (`RedisEventBus` on SaaS), so each node serves its own sessions with no cross-node session routing. |
| D7 | **Profile-agnostic surface; exposure differs, API identical.** Self-host binds the data plane on localhost + LAN (remote via the operator's existing opt-in tunnel — `ExposureModel.OptInTunnel`); SaaS exposes it at the managed edge per-tenant. Same routes, same protocol, same auth on both — only the bind/exposure (owned by the existing `ExposureModel`) changes. |
| D8 | **Rate-limited + audited.** Every data-plane call passes `IRateLimiter` on a per-token bucket (tier-scaled: safe baseline + headroom). Token lifecycle (`created`/`rotated`/`revoked`) is journaled (Critical-tier credential). |
| D9 | **Schema:** **P.17 `AutomationApiToken`** (tenant-scoped, soft-delete). WS sessions are ephemeral (in-memory, per node) — **no session table**. Boundary vs `webhooks.md`: webhooks = passive event adapters (in/out); this = active command/query/subscribe. No overlap. |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`AutomationApiToken`** | **P.17 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(100)` (label); `TokenHash string(64)` **Unique** (SHA-256 of the secret — the secret is never stored); `TokenPrefix string(16)` (non-secret display id, e.g. `nnzb_ak_AB12`); `ScopesJson text` **[VC:JSON]** (`string[]` ⊆ `invoke`\|`read`\|`events`\|`chat`); `AllowedPipelineIdsJson text?` **[VC:JSON]** (`Guid[]`; null/empty ⇒ any pipeline when `invoke` granted); `LastUsedAt DateTime?`; `ExpiresAt DateTime?` (null ⇒ no expiry); `RevokedAt DateTime?`; `CreatedByUserId Guid` FK→`Users.Id`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Name)`. |

No table for WS sessions or subscriptions (D9) — they live in `IAutomationSessionRegistry` (in-memory, per node).

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0: `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`). Published via `IEventBus`; journaled for audit.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record AutomationTokenCreatedEvent : DomainEventBase
{
    public required Guid TokenId { get; init; }
    public required string TokenName { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public required Guid CreatedByUserId { get; init; }
}

public sealed record AutomationTokenRevokedEvent : DomainEventBase
{
    public required Guid TokenId { get; init; }
    public required Guid RevokedByUserId { get; init; }
}
```

These are **internal audit** events (no `IAutomationEventDescriptor` — a token's own lifecycle is not streamed to automation clients, D6).

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.AutomationApi`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/AutomationApi/`.

```csharp
// ── Management plane (JWT, Gate-2) ──────────────────────────────────────────
public interface IAutomationApiTokenService
{
    Task<Result<PagedList<AutomationTokenDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    // Returns the one-time plaintext secret in IssuedAutomationTokenDto.Secret; never retrievable again.
    Task<Result<IssuedAutomationTokenDto>> CreateAsync(Guid broadcasterId, Guid actorUserId, CreateAutomationTokenRequest request, CancellationToken ct = default);
    Task<Result<IssuedAutomationTokenDto>> RotateAsync(Guid broadcasterId, Guid tokenId, Guid actorUserId, CancellationToken ct = default);
    Task<Result> RevokeAsync(Guid broadcasterId, Guid tokenId, Guid actorUserId, CancellationToken ct = default);

    // The subscribable public event types (for integrators building against the stream).
    Task<Result<IReadOnlyList<AutomationEventCatalogItem>>> GetEventCatalogAsync(CancellationToken ct = default);
}

// ── Data plane (API-token authed) ───────────────────────────────────────────
public interface IAutomationTokenAuthenticator
{
    // Resolves a presented secret → principal (validates hash, expiry, revocation, soft-delete); touches LastUsedAt.
    Task<Result<AutomationPrincipal>> AuthenticateAsync(string presentedSecret, CancellationToken ct = default);
}

public interface IAutomationCommandService
{
    // Each method enforces principal.Scopes (and the invoke allowlist) + IRateLimiter before acting.
    Task<Result<AutomationInvokeResult>> InvokePipelineAsync(AutomationPrincipal principal, AutomationInvokeRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AutomationPipelineRef>>> ListPipelinesAsync(AutomationPrincipal principal, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AutomationCommandRef>>> ListCommandsAsync(AutomationPrincipal principal, CancellationToken ct = default);
    Task<Result<AutomationInfo>> GetInfoAsync(AutomationPrincipal principal, CancellationToken ct = default);
    Task<Result> SendChatAsync(AutomationPrincipal principal, AutomationChatRequest request, CancellationToken ct = default);
}

// ── Event stream ────────────────────────────────────────────────────────────
// One descriptor per externally-exposed domain event; auto-discovered at startup (D6).
public interface IAutomationEventDescriptor
{
    string PublicName { get; }            // stable wire name, e.g. "Twitch.ChatMessage", "Supporter.Received", "Custom.HeartRate"
    Type DomainEventType { get; }         // the source DomainEventBase subtype
    object ProjectPayload(DomainEventBase domainEvent);   // PII-safe public projection
}

public interface IAutomationEventRegistry
{
    bool TryGet(Type domainEventType, out IAutomationEventDescriptor descriptor);
    IReadOnlyList<AutomationEventCatalogItem> Catalog { get; }
}

// Consumes IEventBus; pushes matching public events to locally-connected sessions with scope `events`.
public interface IAutomationEventBridge { /* hosted; no public methods */ }

// In-memory per-node session + subscription tracking (filters by event PublicName / wildcard, by token scope).
public interface IAutomationSessionRegistry
{
    void Register(AutomationSession session);
    void Unregister(string sessionId);
    IReadOnlyCollection<AutomationSession> SubscribersOf(string publicEventName);
}

public sealed record AutomationPrincipal(Guid BroadcasterId, Guid TokenId, string TokenName, IReadOnlyList<string> Scopes, IReadOnlyList<Guid>? AllowedPipelineIds);
public sealed record CreateAutomationTokenRequest(string Name, IReadOnlyList<string> Scopes, IReadOnlyList<Guid>? AllowedPipelineIds, DateTime? ExpiresAt);
public sealed record IssuedAutomationTokenDto(Guid Id, string Name, string TokenPrefix, string Secret, IReadOnlyList<string> Scopes, DateTime? ExpiresAt);
public sealed record AutomationTokenDto(Guid Id, string Name, string TokenPrefix, IReadOnlyList<string> Scopes, IReadOnlyList<Guid>? AllowedPipelineIds, DateTime? LastUsedAt, DateTime? ExpiresAt, DateTime CreatedAt);
public sealed record AutomationInvokeRequest(Guid? PipelineId, string? PipelineName, IReadOnlyList<string>? Args, IDictionary<string, string>? Variables);
public sealed record AutomationInvokeResult(Guid PipelineId, Guid ExecutionId, bool Accepted);
public sealed record AutomationChatRequest(string Text, string? ReplyToMessageId, string? WhisperToTwitchUserId);
public sealed record AutomationEventCatalogItem(string PublicName, string Description);
```

**Invocation (D5):** `InvokePipelineAsync` resolves the pipeline within `principal.BroadcasterId`, rejects (scope/allowlist) with a typed `Result` failure, then builds a `PipelineRequest { TriggerKind = "manual", BroadcasterId = principal.BroadcasterId, PipelineId, TriggeredByDisplayName = principal.TokenName, Args, InitialVariables }` and calls `IPipelineEngine.ExecuteAsync`. `Accepted=true` once enqueued (fire-and-forget execution; the API does not block on full pipeline completion).

---

## 4. The data-plane protocol

### 4.1 REST (`/automation/v1`, API-token authed)

JSON in/out, `StatusResponseDto<T>` envelope (it is still our API). Auth: `Authorization: Bearer <secret>`. `429` with `Retry-After` when `IRateLimiter` denies.

| Verb | Path | Scope | Purpose |
|---|---|---|---|
| GET | `/automation/v1/info` | `read` | broadcaster + instance summary (`AutomationInfo`) |
| GET | `/automation/v1/pipelines` | `read` | invocable pipelines (id, name) |
| GET | `/automation/v1/commands` | `read` | commands (name, aliases) |
| POST | `/automation/v1/invoke` | `invoke` | run a pipeline (`AutomationInvokeRequest` → `AutomationInvokeResult`) |
| POST | `/automation/v1/chat` | `chat` | send message / reply / whisper (`AutomationChatRequest`) |
| GET | `/automation/v1/stream` | `events` | **WebSocket upgrade** (see §4.2) |

### 4.2 WebSocket (`/automation/v1/stream`)

Text frames, one JSON object per frame. Client requests carry `op` + `id` (correlation) + payload; server replies with a `response` carrying the same `id` + `status`; subscribed events arrive as unsolicited `event` frames.

```jsonc
// server → client, on connect
{ "op": "hello", "data": { "instanceId": "…", "apiVersion": "1.0", "authRequired": true, "authTimeoutSeconds": 10 } }

// client → server (browser clients only; native clients authed via handshake header)
{ "op": "authenticate", "id": "1", "token": "nnzb_ak_…" }

// client → server: subscribe to event names or "*"; run a pipeline; send chat
{ "op": "subscribe",   "id": "2", "events": ["Twitch.ChatMessage", "Supporter.Received", "Custom.*"] }
{ "op": "invoke",      "id": "3", "pipelineName": "shoutout", "args": ["@someone"] }
{ "op": "sendChat",    "id": "4", "text": "hello chat" }

// server → client: correlated reply
{ "op": "response", "id": "3", "status": "ok", "data": { "executionId": "…", "accepted": true } }
{ "op": "response", "id": "3", "status": "error", "error": { "code": "scope_denied", "message": "token lacks scope 'invoke'" } }

// server → client: a subscribed public event
{ "op": "event", "type": "Supporter.Received", "broadcasterId": "…", "occurredAt": "…", "data": { "kind": "tip", "from": "…", "amount": 5.00, "currency": "USD", "message": "…" } }
```

**Rules:** unauthenticated sockets accept only `authenticate` and are closed after `authTimeoutSeconds`; each op enforces the token's scope (`subscribe`→`events`, `invoke`→`invoke` + allowlist, `sendChat`→`chat`); subscriptions are filtered server-side against the **public catalog** (D6) and the token scope — an event with no descriptor is never sent. Wildcards (`Custom.*`, `*`) match catalog `PublicName`s. The server pushes only events for the token's `BroadcasterId`.

### 4.3 Exposure (D7)

The routes/protocol are identical on every profile. Binding is owned by the existing `ExposureModel` (`platform-conventions`): self-host binds localhost + LAN (the operator's own tunnel for remote); SaaS serves it at the managed per-tenant edge. No profile-specific surface.

---

## 5. REST surface (management plane)

Controller `AutomationTokensController`, `[Route("api/v{version:apiVersion}/automation")]`. `[Authorize]` (JWT). Gate-1 management entry + Gate-2 per-action via `IActionAuthorizationService`; middleware in the pipeline, not the cells.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/tokens` | — | `PaginatedResponse<AutomationTokenDto>` | management / Editor · `automation:tokens:read` |
| POST | `/tokens` | `CreateAutomationTokenRequest` | `StatusResponseDto<IssuedAutomationTokenDto>` | management / Broadcaster · `automation:tokens:write` |
| POST | `/tokens/{id}/rotate` | — | `StatusResponseDto<IssuedAutomationTokenDto>` | management / Broadcaster · `automation:tokens:write` |
| DELETE | `/tokens/{id}` | — | `StatusResponseDto<bool>` | management / Broadcaster · `automation:tokens:write` |
| GET | `/events/catalog` | — | `StatusResponseDto<IReadOnlyList<AutomationEventCatalogItem>>` | management / Editor · `automation:tokens:read` |

Seed in `roles-permissions.md`: **`automation:tokens:read`** (`management`, Editor 30, `Low`) and **`automation:tokens:write`** (`management`, Broadcaster 40, **`Critical`** — a token grants programmatic pipeline-invoke + chat-send + the event firehose, so issuing/rotating/revoking is a channel-control action).

The **data plane** (§4) is **not** in this Gate-2 table: it is authed by `IAutomationTokenAuthenticator` (the API token + its scopes), via a dedicated `ApiTokenAuthenticationHandler` on the `/automation/v1` route group — documented as token-scope-gated, not JWT/Gate-2.

---

## 6. DI & testing

`NomNomzBot.Infrastructure/AutomationApi/DependencyInjection.cs` (`AddAutomationApi()`): `IAutomationApiTokenService`→`AutomationApiTokenService` (Scoped); `IAutomationTokenAuthenticator`→`AutomationTokenAuthenticator` (Scoped); `IAutomationCommandService`→`AutomationCommandService` (Scoped); `AutomationApiTokenRepository` (Scoped); `IAutomationSessionRegistry`→`AutomationSessionRegistry` (Singleton, in-memory); `IAutomationEventRegistry`→`AutomationEventRegistry` (Singleton, **auto-discovers** all `IAutomationEventDescriptor`); `IAutomationEventBridge`→`AutomationEventBridge` (Singleton `IHostedService`, subscribes `IEventBus`). The `/automation/v1` route group uses `ApiTokenAuthenticationHandler`; rate-limit buckets resolve through `IRateLimiter` per token.

**Tests (prove behavior):** creating a token returns the plaintext **once** and persists only the hash, with the requested scopes (a second read never exposes the secret); `AuthenticateAsync` accepts a valid secret and rejects an expired/revoked/soft-deleted one and stamps `LastUsedAt`; `InvokePipelineAsync` with scope `invoke` enqueues a `manual`-trigger execution attributed to the token name, while a token lacking `invoke` (or invoking a pipeline outside its `AllowedPipelineIds`) is rejected and **no execution is enqueued**; a `chat`-scoped call reaches `IChatTransport.SendAsync`, a token without `chat` does not; the event bridge delivers a `Supporter.Received` public event **only** to sessions that subscribed to it **and** hold scope `events`, with the **PII-safe projection** (no raw domain-event internals), and never delivers an event lacking a descriptor; rate-limit denial returns `Retry-After` and performs no side effect; rotating/revoking emits the audit events and invalidates the old secret.

---

## 7. Decisions (resolved)

Plain WebSocket + REST, not SignalR (D1); per-channel scoped hashed API tokens, default-deny scopes + invoke-allowlist, always required (D2); header/first-message auth, never query-param (D3); two planes — JWT management vs token-scoped data (D4); invocation via `TriggerKind=manual` with a synthetic automation actor (D5); curated auto-discovered public event catalog, PII-safe, default-deny, cluster-wide via the shared bus (D6); profile-agnostic surface with exposure owned by `ExposureModel` (D7); rate-limited + audited (D8); schema delta **P.17 `AutomationApiToken`**, no session table, no overlap with `webhooks.md` (D9).
