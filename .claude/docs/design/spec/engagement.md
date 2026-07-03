# Interface Specification — Engagement Triggers Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (Domain G — channel content); pipeline `commands-pipelines.md` (trigger kinds §4.1, `ITemplateEngine`); chat (the decorated `channel.chat.message` stream — `chat-decoration.md`); streams `stream-admin.md` (current live session); platform `platform-conventions.md` (`IEventBus`); roles `roles-permissions.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; UUIDv7 `Guid` PKs; `BroadcasterId` `Guid`; soft-delete filter; AGPL header on every source file.

> **Why.** "Welcome a first-time chatter", "shout out a returning regular", "reward a 50-stream watch streak" is the auto-greeting/loyalty-recognition layer every serious bot ships (StreamElements, Wizebot, Mix It Up) and the corpus had no *detect → act* layer over viewer activity. This subsystem detects three engagement moments from the chat stream and fires them as **pipeline triggers**, so the streamer attaches whatever response they want (a message, bonus points, a TTS welcome). It owns the small per-viewer state needed to detect them; it adds no greeting *policy* — the pipeline decides. Fits the project's "personality is a core value" line.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Three trigger moments**, each a pipeline trigger kind: **`engagement.first_time_chatter`** (a viewer's first-ever message in this channel), **`engagement.returning_chatter`** (their first message *this stream*, having chatted before), **`engagement.watch_streak`** (they hit a configured consecutive-stream milestone). |
| D2 | **No greeting policy here — pure triggers.** The subsystem fires the event; the bound pipeline does the greeting/reward. Event fields surface as `{{engagement.*}}` / `{{viewer.*}}` vars. |
| D3 | **Self-contained streak counter.** Engagement owns `ViewerEngagementState.ConsecutiveStreams` — incremented when a viewer is first seen in a *new* stream after being seen in the *previous* one, reset when they miss a stream. It does not depend on analytics (avoids a cross-spec read on the chat hot path). |
| D4 | **Dedup + opt-in.** Each moment fires **once per viewer per stream** (greet-dedup via `LastGreetedStreamSessionId`); a per-channel `GreetCooldownSeconds` rate-limits bursts. Every trigger is **off by default** (`EngagementConfig`) — opt-in per the default-deny rule. |
| D5 | **Schema additions (Domain G):** **G.11 `EngagementConfig`** (per channel), **G.12 `ViewerEngagementState`** (per channel+viewer). |

---

## 1. Entities

Domain G. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`EngagementConfig`** | **G.11 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` **Unique** (one per channel); `FirstTimeChatterEnabled bool` (default false); `ReturningChatterEnabled bool` (default false); `WatchStreakEnabled bool` (default false); `StreakMilestonesJson text?` **[VC:JSON]** (`int[]`, e.g. `[5,10,25,50,100]`, or empty = every stream); `GreetCooldownSeconds int` (default 5); `ConfigSchemaVersion int`; `CreatedAt/UpdatedAt/DeletedAt`. |
| **`ViewerEngagementState`** | **G.12 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `ViewerUserId Guid` FK→`Users.Id`; `ViewerTwitchUserId string(50)` **[PII-hash]**; `FirstChatAt DateTime`; `LastChatAt DateTime`; `LastSeenStreamSessionId Guid?` (the stream they last chatted in); `LastGreetedStreamSessionId Guid?` (dedup, D4); `ConsecutiveStreams int` (D3); `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, ViewerUserId)`. |

A "stream session" id is the current live `Stream` row (`stream-admin.md`); `null` when offline (engagement moments only fire while live).

---

## 2. Domain events (→ pipeline triggers)

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`; consumed by the trigger sources (§4).

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record FirstTimeChatterDetectedEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
}

public sealed record ReturningChatterDetectedEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
    public required int DaysSinceLastSeen { get; init; }
}

public sealed record WatchStreakMilestoneEvent : DomainEventBase
{
    public required Guid ViewerUserId { get; init; }
    public required string ViewerDisplayName { get; init; }
    public required int StreakCount { get; init; }
}
```

---

## 3. Service interface

Namespace `NomNomzBot.Application.Engagement`. Returns `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Engagement/`.

```csharp
public interface IEngagementService
{
    // The chat hot-path hook (called once per inbound decorated chat message, while live). Upserts
    // ViewerEngagementState in one tx and fires at most one engagement event per the rules (D1/D3/D4):
    //  • no prior row              → FirstTimeChatterDetectedEvent
    //  • prior row, new stream      → ReturningChatterDetectedEvent (+ streak update; milestone → WatchStreakMilestoneEvent)
    //  • same stream / on cooldown  → state update only, no event
    // No-op (fast return) when the matching trigger is disabled in EngagementConfig.
    Task<Result> OnChatActivityAsync(Guid broadcasterId, EngagementSignal signal, CancellationToken ct = default);

    Task<Result<EngagementConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<EngagementConfigDto>> UpdateConfigAsync(Guid broadcasterId, UpdateEngagementConfigRequest request, CancellationToken ct = default);
}

public sealed record EngagementSignal(Guid ViewerUserId, string ViewerTwitchUserId, string DisplayName, Guid? CurrentStreamSessionId, DateTime At);
public sealed record EngagementConfigDto(bool FirstTimeChatterEnabled, bool ReturningChatterEnabled, bool WatchStreakEnabled, IReadOnlyList<int> StreakMilestones, int GreetCooldownSeconds);
public sealed record UpdateEngagementConfigRequest(bool FirstTimeChatterEnabled, bool ReturningChatterEnabled, bool WatchStreakEnabled, IReadOnlyList<int>? StreakMilestones, int GreetCooldownSeconds);
```

**Streak logic (D3):** on the first activity of a *new* `CurrentStreamSessionId`: if `LastSeenStreamSessionId` was the immediately-previous live session → `ConsecutiveStreams += 1`, else reset to `1`; if `ConsecutiveStreams` ∈ `StreakMilestones` (or milestones empty) → fire `WatchStreakMilestoneEvent`. "Immediately-previous" is resolved against the channel's stream-session order (`stream-admin.md`).

---

## 4. Pipeline triggers

Three `TriggerKind`s registered with the pipeline trigger registry (commands-pipelines §4.1): `engagement.first_time_chatter`, `engagement.returning_chatter`, `engagement.watch_streak`. Each engagement domain event (§2) is matched to bound pipelines/event-responses by a trigger source (`EngagementTriggerSource`, consumes the events). Template vars exposed to the bound pipeline: `{{viewer.name}}`, `{{engagement.streak}}`, `{{engagement.daysSinceLastSeen}}`. No new pipeline *actions* — the greeting/reward is whatever actions the streamer wires (send_message, grant_currency, play_tts, …).

---

## 5. REST surface

Controller `EngagementController`, `[Route("api/v{version:apiVersion}/engagement")]`. `[Authorize]`; Gate-2 keys.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/config` | — | `StatusResponseDto<EngagementConfigDto>` | management / Moderator · `engagement:read` |
| PUT | `/config` | `UpdateEngagementConfigRequest` | `StatusResponseDto<EngagementConfigDto>` | management / Editor · `engagement:write` |

Seed `engagement:read` (Moderator), `engagement:write` (Editor) in `roles-permissions.md`. The trigger *bindings* themselves are ordinary pipeline/event-response config (gated by the pipeline editor's `pipelines:write`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Engagement/DependencyInjection.cs` (`AddEngagement()`): `IEngagementService` → `EngagementService` (Scoped); `EngagementConfigRepository` + `ViewerEngagementStateRepository` (Scoped); `EngagementTriggerSource` (Singleton, consumes the §2 events). `OnChatActivityAsync` is invoked from the chat-message processing path (after decoration, `chat-decoration.md`) — a single call per message, guarded by the enabled-flags fast-path so a fully-disabled channel adds ~one cache read.

**Tests (prove behavior):** a viewer's **first-ever** message fires `FirstTimeChatterDetectedEvent` exactly once and creates the state row (a second message fires nothing); a first message in a **new** stream after a prior stream fires `ReturningChatterDetectedEvent` with the right `DaysSinceLastSeen` and increments `ConsecutiveStreams`; **consecutive** streams raise the streak and a configured milestone fires `WatchStreakMilestoneEvent`, while a **missed** stream resets it to 1; greet-dedup (`LastGreetedStreamSessionId`) and `GreetCooldownSeconds` suppress repeats within a stream; a disabled trigger fires **nothing** (state still updates).

---

## 7. Decisions (resolved)

Three engagement trigger moments (D1); pure triggers, no greeting policy (D2); self-owned streak counter, no analytics dependency (D3); once-per-viewer-per-stream dedup + cooldown, off by default (D4); schema deltas G.11 `EngagementConfig` + G.12 `ViewerEngagementState` (D5).
