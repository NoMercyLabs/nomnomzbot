# Interface Specification — Custom Events & Data Sources

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Streamer.bot's custom-event + integration-trigger model (Pulsoid/HypeRate heart-rate, custom WebSocket triggers — the ecosystem reference) generalized into one mechanism. Corpus: `commands-pipelines.md` (trigger registry, `event` `TriggerKind`, `ITemplateEngine`, I/O-free resolver rule, `HttpEgressAllowlist` egress); `webhooks.md` (inbound endpoint H.10 + adapter-kind enum — the push ingress); `supporter-events.md` (the per-channel outbound `SocketIOClient`/`ClientWebSocket` hosted-service pattern — `SupporterSocketHostedService`, the socket ingress); `widgets-overlays.md` (`IOverlayClient`/`widget_event` — the overlay feed); `platform-conventions.md` (§2.0 `DomainEventBase`, `IEventBus`, `ICacheService`, `IRunOnceGuard`, `IDeploymentProfileService`); `scaling-qos.md` (`IRateLimiter`); `gdpr-crypto.md` (`IFieldCipher` AEAD); `automation-api.md` (exposes `Custom.<name>` to the external event stream); locked schema `2026-06-16-database-schema.md` (Domain G — channel content/automation).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json; secrets via `IFieldCipher`.

> **Why.** Heart-rate (Pulsoid/HypeRate) is the obvious ask — but it's a special case of a far more useful primitive: **an arbitrary external data source pushes data → the bot fires a trigger, exposes the data, and feeds an overlay.** Speccing "heart rate" as bespoke code would be a missed generalization (and against the project's extensibility line). This subsystem is the generic hook: a streamer defines a named **custom data source**; each datum becomes a normalized **`custom.<name>` event** that fires a pipeline trigger, surfaces as `{{custom.<name>.*}}` template variables, and is pushed to overlays as a live value. **Pulsoid and HypeRate ship as presets on top — not as separate subsystems** — and so does anything else (a sensor, a companion app's state, a REST ticker, a subathon feed).

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **One generic mechanism.** A `CustomDataSource` produces a normalized **`custom.<name>` event**. Each event (a) fires the pipeline trigger `custom.<name>` (registered as an `event` trigger kind, like the engagement triggers — **no new `TriggerKind` enum value**), (b) exposes `{{custom.<name>.<field>}}` + `{{custom.<name>.raw}}` + `{{custom.<name>.at}}`, and (c) updates the source's **latest value** for overlays. Heart rate is just a source named e.g. `heartrate`. |
| D2 | **Three ingress kinds, decided per source (`SourceKind`):** **`push`** — an inbound webhook (reuses `webhooks.md` H.10 via a new `CustomData` adapter kind: verify→dedup→journal→ingest, no parallel ingress); **`poll`** — an interval HTTP fetch, FQDN-constrained by the tenant's existing `HttpEgressAllowlist` (no new SSRF surface), one runner per source via `IRunOnceGuard`; **`socket`** — an outbound WS/Socket.IO client (the `supporter-events.md` hosted-service pattern), one connection per source via `IRunOnceGuard`. |
| D3 | **Arbitrary payload, structured access via a field-mapping.** Each source carries a JSON-path **field map** (`bpm <- $.data.heartRate`) extracting named fields from the raw payload; the bounded raw payload stays available as `{{custom.<name>.raw}}`. The mechanism never assumes a schema. |
| D4 | **Latest value is cached, events are journaled — no event/snapshot table.** The latest extracted value per source lives in `ICacheService` (transient, fast). Every `CustomDataReceivedEvent` flows through `IEventBus` and is journaled by the existing decorator (full history, replay). The pipeline **dispatcher seeds the `custom.*` latest values into the run's `InitialVariables`** at dispatch (one cache read at dispatch — keeps the template resolver I/O-free per the corpus rule), so a `!heartrate` command can read `{{custom.heartrate.bpm}}` even when it wasn't the trigger. |
| D5 | **Overlay feed via the existing surface.** A first-party **"Custom Data" widget** (catalogue entry in `widgets-overlays.md`) binds a source name and renders its live value (number, gauge, text); updates ride the existing `IOverlayClient`/`widget_event` push. No bespoke overlay transport. |
| D6 | **Presets are auto-discovered.** An `ICustomDataSourcePreset` descriptor (Key, DisplayName, `SourceKind`, default endpoint, auth shape, default field-map) is discovered at startup. **Pulsoid and HypeRate ship as `socket` presets** (their WS endpoints + token/OAuth + `bpm` mapping prefilled). Adding a preset = drop a descriptor — no engine edit. |
| D7 | **Exposed to the Automation API.** One `IAutomationEventDescriptor` over `CustomDataReceivedEvent` maps it to the public name **`Custom.<name>`** (PII-safe projection: source name, fields, timestamp), so external tools subscribe to custom data just like any other event (`automation-api.md` §4.2, `Custom.*`). |
| D8 | **Safe + opt-in.** Source auth secrets (socket/push) are AEAD via `IFieldCipher`; ingest is rate-limited (`IRateLimiter`, per-source inbound bucket, tier-scaled); sources are **disabled until configured** (default-deny). Schema delta **G.13 `CustomDataSource`**; `webhooks.md` adapter-kind enum gains `CustomData`. |

---

## 1. Entities

Domain G. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`CustomDataSource`** | **G.13 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(50)` (the `<name>` in `custom.<name>`; lowercase, slug); `DisplayName string(100)`; `SourceKind string(20)` **[VC:enum]** (`push`\|`poll`\|`socket`); `PresetKey string(50)?` (the `ICustomDataSourcePreset` key, null for a hand-rolled source); `EndpointUrl string(500)?` (poll URL / socket URL; null for push); `AuthSecretCipher text?` **[PII]** (AEAD via `IFieldCipher` — bearer token / OAuth access token for socket/poll/push auth); `FieldMapJson text` **[VC:JSON]** (`{ "<field>": "<jsonpath>" }`); `PollIntervalSeconds int?` (poll only; clamped to a tier-scaled floor); `InboundWebhookEndpointId Guid?` FK→`InboundWebhookEndpoint.Id` (push only — the H.10 endpoint backing this source); `IsEnabled bool` (default false); `LastReceivedAt DateTime?`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Name)`. |

A "latest value" is **not** a table — it is `ICacheService` key `customdata:{broadcasterId}:{name}` (D4). Event history is the event journal (D4).

---

## 2. Domain events (→ pipeline trigger)

Inherit `DomainEventBase`. Published via `IEventBus`; consumed by `CustomEventTriggerSource` (§3) and the Automation API bridge.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record CustomDataReceivedEvent : DomainEventBase
{
    public required string SourceName { get; init; }                       // the `<name>`
    public required IReadOnlyDictionary<string, string> Fields { get; init; } // extracted per the field-map
    public required string RawPayload { get; init; }                       // bounded raw (size-capped)
}
```

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.CustomEvents`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/CustomEvents/`.

```csharp
public interface ICustomDataSourceService
{
    Task<Result<PagedList<CustomDataSourceDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<CustomDataSourceDto>> GetAsync(Guid broadcasterId, Guid id, CancellationToken ct = default);
    Task<Result<CustomDataSourceDto>> CreateAsync(Guid broadcasterId, Guid actorUserId, UpsertCustomDataSourceRequest request, CancellationToken ct = default);
    Task<Result<CustomDataSourceDto>> UpdateAsync(Guid broadcasterId, Guid id, Guid actorUserId, UpsertCustomDataSourceRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid broadcasterId, Guid id, Guid actorUserId, CancellationToken ct = default);

    // Fires a CustomDataReceivedEvent from a sample payload so the streamer can wire/preview a pipeline.
    Task<Result> TestAsync(Guid broadcasterId, Guid id, string samplePayload, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CustomDataSourcePresetDto>>> ListPresetsAsync(CancellationToken ct = default);
}

// The single ingest path — extracts fields per the field-map, publishes the event, updates the cached latest value.
// Called by: the CustomData webhook adapter (push), CustomDataPollHostedService (poll), CustomDataSocketHostedService (socket).
public interface ICustomDataIngestService
{
    Task<Result> IngestAsync(Guid broadcasterId, string sourceName, string rawPayload, CancellationToken ct = default);
}

// Auto-discovered presets (Pulsoid, HypeRate, …). One per supported provider.
public interface ICustomDataSourcePreset
{
    string Key { get; }                 // e.g. "pulsoid", "hyperate"
    string DisplayName { get; }
    CustomDataSourceTemplate Template { get; }  // SourceKind, default endpoint, auth kind, default field-map
}

public sealed record UpsertCustomDataSourceRequest(string Name, string DisplayName, string SourceKind, string? PresetKey, string? EndpointUrl, string? AuthSecret, IReadOnlyDictionary<string, string> FieldMap, int? PollIntervalSeconds, bool IsEnabled);
public sealed record CustomDataSourceDto(Guid Id, string Name, string DisplayName, string SourceKind, string? PresetKey, string? EndpointUrl, bool HasAuthSecret, IReadOnlyDictionary<string, string> FieldMap, int? PollIntervalSeconds, bool IsEnabled, DateTime? LastReceivedAt);
public sealed record CustomDataSourcePresetDto(string Key, string DisplayName, string SourceKind);
```

`CustomEventTriggerSource` (Singleton, consumes `CustomDataReceivedEvent`) matches bound pipelines/event-responses whose trigger kind is `custom.<SourceName>` and dispatches with the event fields as variables. `CustomDataSocketHostedService` / `CustomDataPollHostedService` (`IHostedService`, `IRunOnceGuard`-guarded) own the socket/poll ingress and call `ICustomDataIngestService`. Push sources ingest through the new `CustomData` webhook adapter (`webhooks.md`), which resolves the source by `InboundWebhookEndpointId` and calls the same `IngestAsync`.

---

## 4. Pipeline triggers, variables, overlay

- **Trigger:** `custom.<name>` (registered with the trigger registry, `commands-pipelines.md §4.1`, as an `event` kind). The streamer binds any pipeline.
- **Variables:** `{{custom.<name>.<field>}}` (mapped fields), `{{custom.<name>.raw}}` (bounded raw), `{{custom.<name>.at}}` (last-received timestamp) — seeded into the run at dispatch (D4), available to **any** pipeline, not just the triggered one.
- **Overlay:** the "Custom Data" widget (`widgets-overlays.md` catalogue) binds `name` and renders the live value via `IOverlayClient`/`widget_event`. A heart-rate gauge is this widget bound to `heartrate.bpm`.

No new pipeline **actions** — responses use existing actions (`send_message`, `play_tts`, `widget_event`, …).

---

## 5. REST surface

Controller `CustomDataSourcesController`, `[Route("api/v{version:apiVersion}/custom-data-sources")]`. `[Authorize]`; Gate-2 keys.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | — | `PaginatedResponse<CustomDataSourceDto>` | management / Moderator · `customdata:read` |
| GET | `/{id}` | — | `StatusResponseDto<CustomDataSourceDto>` | management / Moderator · `customdata:read` |
| POST | `/` | `UpsertCustomDataSourceRequest` | `StatusResponseDto<CustomDataSourceDto>` | management / Editor · `customdata:write` |
| PUT | `/{id}` | `UpsertCustomDataSourceRequest` | `StatusResponseDto<CustomDataSourceDto>` | management / Editor · `customdata:write` |
| DELETE | `/{id}` | — | `StatusResponseDto<bool>` | management / Editor · `customdata:write` |
| POST | `/{id}/test` | `{ string SamplePayload }` | `StatusResponseDto<bool>` | management / Editor · `customdata:write` |
| GET | `/presets` | — | `StatusResponseDto<IReadOnlyList<CustomDataSourcePresetDto>>` | management / Moderator · `customdata:read` |

Seed in `roles-permissions.md`: **`customdata:read`** (`management`, Moderator 10, `Low`), **`customdata:write`** (`management`, Editor 30, `Low`). The trigger *bindings* are ordinary pipeline config (`pipelines:write`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/CustomEvents/DependencyInjection.cs` (`AddCustomEvents()`): `ICustomDataSourceService`→`CustomDataSourceService` (Scoped); `ICustomDataIngestService`→`CustomDataIngestService` (Scoped); `CustomDataSourceRepository` (Scoped); `CustomEventTriggerSource` (Singleton); `CustomDataSocketHostedService` + `CustomDataPollHostedService` (`IHostedService`, `IRunOnceGuard`-guarded, started for enabled `socket`/`poll` sources); all `ICustomDataSourcePreset` impls **auto-discovered** (Pulsoid, HypeRate). The `CustomData` webhook adapter registers with `webhooks.md`'s adapter registry. One `IAutomationEventDescriptor` (`CustomDataReceivedEvent`→`Custom.<name>`) registers with the automation event registry.

**Tests (prove behavior):** ingesting a raw payload through `IngestAsync` extracts the mapped fields (`$.data.heartRate`→`bpm`), publishes exactly one `CustomDataReceivedEvent`, updates the cached latest value, and stamps `LastReceivedAt`; the `custom.<name>` trigger fires the bound pipeline with `{{custom.<name>.bpm}}` populated, and a **non-triggered** pipeline referencing the same var reads the cached latest (resolver stays I/O-free — the value is in `InitialVariables`); a `poll` source only fetches an allowlisted FQDN (a non-allowlisted URL is rejected at create/update); a disabled source ingests nothing and starts no socket/poll runner; the `CustomData` webhook adapter routes a verified inbound delivery to `IngestAsync` for the right source; the Pulsoid/HypeRate presets resolve to a `socket` template with the `bpm` field-map; the Automation API receives the event as `Custom.<name>` with the PII-safe projection; rate-limit denial drops the datum without publishing.

---

## 7. Decisions (resolved)

One generic `custom.<name>` mechanism — trigger + variables + overlay, heart rate as a preset (D1); three ingress kinds push/poll/socket reusing webhooks/egress-allowlist/socket-hosted-service (D2); arbitrary payload + JSON-path field-map (D3); cached latest + journaled events, dispatcher-seeded vars keep the resolver I/O-free (D4); overlay via the existing Custom Data widget + `widget_event` (D5); auto-discovered presets, Pulsoid + HypeRate ship (D6); exposed to the Automation API as `Custom.<name>` (D7); AEAD secrets, rate-limited, opt-in; schema delta **G.13 `CustomDataSource`** + `webhooks` adapter-kind `CustomData` (D8).
