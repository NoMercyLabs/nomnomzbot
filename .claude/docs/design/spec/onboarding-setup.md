# Onboarding & Setup — Interface Specification

**Status:** Implementable. Build the first-run flow from this directly.
**Subsystem:** First-run/setup state machine + the system-status surface the clients gate on. Owns `SystemController`'s setup endpoints, the `ISetupService` state machine, and the basics-configuration step (deployment finalization, guidance level, bot prefix/language/timezone). Closes gap **P1**. **One onboarding logic, three presentations** (KMP desktop app · web wizard · headless CLI) — all drive the *same* setup API; this spec owns that API, not the UI.

## Grounding & locked decisions (binding)

- **Setup status is the client entry-gate.** `frontend.md §5` routes to Connect → Setup wizard → Main based on a probe of `SystemController` setup-status. This spec defines that endpoint and its `SetupStatusDto`. It is reachable **before** a streamer is configured (the only pre-auth system surface besides `/health`).
- **Three surfaces, one logic** (`2026-06-16-onboarding.md`): desktop app (KMP), web wizard (self-host local page / SaaS signup), headless CLI. The backend exposes one setup API; each surface renders it. No surface-specific business logic.
- **Adaptive guidance level** (`2026-06-16-deployment-profile.md`): the wizard asks **Simple vs Advanced** with no silent default; **Docker present = sophistication signal** → pre-select Advanced (`expert`), else Simple (`novice`). Persisted as `DeploymentProfile.DefaultGuidanceLevel` (seed) → live per-user `UserPreferences.GuidanceLevel` (adjustable anytime, whole-app). Bypassed/non-interactive setup falls back to Simple.
- **Deployment profile drives the DB recommendation** (self-host): Docker reachable → recommend **Postgres + Docker** (pre-selected); else **SQLite, no Docker**. The host-capability probe (CPU/memory → worker-pool/concurrency sizing) is owned by `platform-conventions.md §3.3` + `scaling-qos.md §9` — referenced here, not redefined.
- **Two-account bot model** (`2026-06-16-onboarding.md`): **shared bot** (default, free on SaaS) vs **custom bot name** (premium on SaaS via `IBillingTierService.AllowsCustomBotName`, always free + always custom on self-host). The OAuth flows themselves are owned by `identity-auth.md` (streamer + bot, progressive scopes); this spec only **sequences** them and reports status.
- **SaaS instant value:** on streamer-connect the bot joins chat immediately and responds to default-enabled built-ins (e.g. `!followage`) before any further config — setup completion is not a prerequisite for basic value.
- **AuthZ:** setup-status read is anonymous (no tenant yet); every *mutating* setup step requires the connected streamer principal (Broadcaster on their own channel) — first connect establishes that principal, subsequent steps gate on it. Self-host single-operator collapses to that one principal.
- Conventions: `NomNomzBot.*`, .NET 10, `Result<T>`, `Guid` keys, async-all-the-way, `StatusResponseDto<T>`, `[ApiVersion("1.0")]`.

---

## 1. Entities (schema deltas — apply to the living schema)

No new domain; small additive columns on existing tables (schema is a living artifact, one greenfield migration). Setup status is otherwise **derived** from existing state, not stored redundantly.

| Table | Schema ref | Delta |
|---|---|---|
| `DeploymentProfile` | P.12 | **add** `SetupCompletedAt timestamp Null` (null = setup incomplete; set once, monotonic). `DefaultGuidanceLevel string(10)` [VC:enum `novice`\|`expert`] already present per deployment-profile design; confirm/add. |
| `Channels` | A.2 | **confirm/add** the basics: `BotPrefix string(8)` (default `!`), `DefaultLanguage string(5)` [VC:enum `en`\|`nl`], `Timezone string(64)` (IANA, default `Etc/UTC`). If a `ChannelSettings` row already owns these, reference it instead — do not duplicate. |
| `UserPreferences` | (existing) | `GuidanceLevel string(10)` [VC:enum `novice`\|`expert`] — the **live** per-user value (seeded from `DeploymentProfile.DefaultGuidanceLevel`). Referenced, owned by identity-auth/platform. |

**Derived, not stored:** "streamer connected", "bot connected", "basics configured" are computed from `Channels` / `IntegrationConnections` (E.1) / `DeploymentProfile` at read time — only the terminal `SetupCompletedAt` marker is persisted.

**Read dependencies (owned elsewhere):** `IntegrationConnections` (E.1, streamer/bot OAuth state — identity-auth), `DeploymentProfile` (P.12 + `IDeploymentProfileService.Current` — platform-conventions), `BillingTier`/`TierLimit` (N.1 — `AllowsCustomBotName`), host-capability probe (platform-conventions §3.3).

---

## 2. Domain events

`NomNomzBot.Domain.Events`, `sealed record : DomainEventBase` (canonical base — inherits `EventId`/`BroadcasterId`/`OccurredAt`, never redeclared).

```csharp
namespace NomNomzBot.Domain.Events;

/// Raised when a setup step transitions to completed (audit + dashboard progress + SaaS analytics funnel).
public sealed record SetupStepCompletedEvent : DomainEventBase
{
    public required SetupStage Stage { get; init; }     // the step just completed
    public required SetupStage NextStage { get; init; } // resulting current stage
}

/// Raised once when the wizard finishes (SetupCompletedAt set). Drives "welcome to the dashboard" + funnel-complete.
public sealed record SetupCompletedEvent : DomainEventBase
{
    public required string DeploymentMode { get; init; } // saas | self_host_lite | self_host_full
    public required bool UsesCustomBot { get; init; }
}
```

Guidance-level changes are a `UserPreferences` update (identity-auth/platform owns that event), not re-emitted here. Streamer/bot connection events (`ChannelOnboardedEvent`, `BotAccountAuthorizedEvent`) are owned by `identity-auth.md §2` and consumed here to advance the state machine.

---

## 3. Service interfaces

`NomNomzBot.Application.Services.System`; impls in `NomNomzBot.Infrastructure/Services/System/`. Async, `Result`/`Result<T>`, repositories + `IUnitOfWork`.

### 3.1 `ISetupService` — the first-run state machine

```csharp
namespace NomNomzBot.Application.Services.System;

public interface ISetupService
{
    // The entry-gate probe. Anonymous-reachable. Computes the current stage from live state (no auth required to read
    // whether the instance is set up). On a fresh instance returns Stage=NeedsStreamerAuth.
    Task<Result<SetupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);

    // Recommended first-run defaults for the wizard to pre-select: detected DeploymentMode, Docker-present →
    // guidance=expert + db=postgres else novice + sqlite, host-capability summary (cores/mem), custom-bot allowance.
    // Pure read; drives "pre-select, always overridable".
    Task<Result<SetupRecommendationDto>> GetRecommendationAsync(CancellationToken cancellationToken = default);

    // Step: persist the guidance level (Simple→novice / Advanced→expert). Writes DeploymentProfile.DefaultGuidanceLevel
    // AND seeds the acting user's UserPreferences.GuidanceLevel. No silent default — the caller must pass an explicit choice.
    Task<Result<SetupStatusDto>> SetGuidanceLevelAsync(Guid broadcasterId, GuidanceLevel level, CancellationToken cancellationToken = default);

    // Step: configure basics (self-host: confirm/switch DB provider before first migration; all: bot prefix, default
    // language, timezone). Validates IANA timezone + language enum + prefix length. Idempotent (re-runnable until complete).
    Task<Result<SetupStatusDto>> ConfigureBasicsAsync(Guid broadcasterId, ConfigureBasicsRequest request, Guid actingUserId, CancellationToken cancellationToken = default);

    // Step: choose the bot identity. shared → bind the platform bot; custom → require AllowsCustomBotName (SaaS premium /
    // self-host always allowed) then hand off to identity-auth's bot OAuth. Returns the bot-OAuth start URL when custom.
    Task<Result<BotIdentityChoiceResultDto>> ChooseBotIdentityAsync(Guid broadcasterId, BotIdentityChoice choice, Guid actingUserId, CancellationToken cancellationToken = default);

    // Terminal step: mark setup complete (sets DeploymentProfile.SetupCompletedAt, emits SetupCompletedEvent). Fails if a
    // required prior stage is unmet (fail-closed: cannot complete without a connected streamer). Idempotent (no-op if already complete).
    Task<Result<SetupStatusDto>> CompleteAsync(Guid broadcasterId, Guid actingUserId, CancellationToken cancellationToken = default);
}

public enum SetupStage { NeedsStreamerAuth = 0, NeedsBotIdentity = 1, NeedsBasics = 2, ReadyToComplete = 3, Complete = 4 }
public enum GuidanceLevel { Novice, Expert }
public enum BotIdentityChoice { Shared, Custom }
```

**State-machine rule (fail-closed, monotonic):** `GetStatusAsync` computes `Stage` as the **lowest unmet** requirement — `NeedsStreamerAuth` (no streamer `IntegrationConnection`) → `NeedsBotIdentity` (no bot bound / not chosen) → `NeedsBasics` (`SetupCompletedAt` null AND basics unset) → `ReadyToComplete` (all met, not yet completed) → `Complete` (`SetupCompletedAt` set). Steps may be revisited until `Complete`; `Complete` is terminal. Streamer connect is always first (it establishes the gating principal).

---

## 4. DTOs / contracts

`NomNomzBot.Application/DTOs/System/`. App JSON = Newtonsoft.

```csharp
namespace NomNomzBot.Application.DTOs.System;

public sealed record SetupStatusDto(
    SetupStage Stage,
    bool StreamerConnected,
    bool BotConnected,
    bool BasicsConfigured,
    bool IsComplete,
    string DeploymentMode,          // saas | self_host_lite | self_host_full
    string GuidanceLevel,           // novice | expert (current default)
    string? StreamerDisplayName,    // null until connected
    string? BotDisplayName);        // null until bound

public sealed record SetupRecommendationDto(
    string DeploymentMode,
    bool DockerDetected,
    GuidanceLevel RecommendedGuidance,     // expert if Docker else novice
    string RecommendedDbProvider,          // postgres if Docker else sqlite (self-host); fixed postgres on saas
    int HostCpuCores,
    int HostMemoryMb,
    bool CustomBotAllowed,                 // AllowsCustomBotName for this tier/profile
    string SharedBotLogin);                // the platform bot login offered as the default

public sealed record ConfigureBasicsRequest(
    string? DbProvider,             // self-host only: "postgres" | "sqlite"; ignored on saas
    string BotPrefix,               // 1–8 chars, default "!"
    string DefaultLanguage,         // "en" | "nl"
    string Timezone);               // IANA, validated

public sealed record BotIdentityChoiceResultDto(
    BotIdentityChoice Choice,
    string? BotOAuthStartUrl,       // non-null only when Custom (hand-off to identity-auth bot OAuth)
    string? BoundBotLogin);         // non-null when Shared (already bound) 
```

---

## 5. Controller endpoints

`SystemController` (`NomNomzBot.Api/Controllers/V1/`), `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/system")]`, responses `StatusResponseDto<T>`. This spec owns the **setup** surface of `SystemController`; `/health*` is the health-check middleware (platform-conventions, not a controller action) and `tts-voices` is `TtsController`'s (tts.md) — they are **not** redefined here.

| Route | Verb | Request | Response | Auth |
|---|---|---|---|---|
| `/system/setup-status` | GET | — | `StatusResponseDto<SetupStatusDto>` | **Anonymous** (the client entry-gate; reveals only setup progress, no secrets) |
| `/system/setup/recommendation` | GET | — | `StatusResponseDto<SetupRecommendationDto>` | Anonymous (first-run defaults; self-host LAN / SaaS pre-auth) |
| `/system/setup/guidance` | PUT | `{ level }` | `StatusResponseDto<SetupStatusDto>` | `[Authorize]` streamer principal |
| `/system/setup/basics` | PUT | `ConfigureBasicsRequest` | `StatusResponseDto<SetupStatusDto>` | `[Authorize]` streamer · `setup:write` |
| `/system/setup/bot-identity` | POST | `{ choice }` | `StatusResponseDto<BotIdentityChoiceResultDto>` | `[Authorize]` streamer · `setup:write` |
| `/system/setup/complete` | POST | — | `StatusResponseDto<SetupStatusDto>` | `[Authorize]` streamer · `setup:write` |

> The streamer/bot **OAuth login + callback** endpoints are owned by `identity-auth.md §5` (`AuthController`) — the wizard links to them and then polls `setup-status`; they are not re-declared here. New management action key **`setup:write`** (`management`, Broadcaster 40, Low, non-grantable) is added to `roles-permissions.md §7.1` (self-host single-operator is Broadcaster by definition).

---

## 6. Pipeline actions

**None.** Setup is an instance-lifecycle concern, not a per-command step.

---

## 7. DI registration

`AddSystemSetup(this IServiceCollection)` from `AddInfrastructure`.

| Interface | Impl | Lifetime | Notes |
|---|---|---|---|
| `ISetupService` | `SetupService` | Scoped | Reads `IDeploymentProfileService.Current` + host-capability probe; writes `DeploymentProfile`/`Channels`/`UserPreferences` via `IUnitOfWork`; emits events via `IEventBus`. |
| `IEventHandler<ChannelOnboardedEvent>` | `SetupAdvanceOnStreamerConnectHandler` | Scoped | Recomputes/advances stage when identity-auth reports a streamer connect (SaaS instant-value: ensures the bot-join + default built-ins fire). |
| `IEventHandler<BotAccountAuthorizedEvent>` | `SetupAdvanceOnBotConnectHandler` | Scoped | Advances stage when a custom bot finishes OAuth. |

No deployment-profile adapter pair — the setup flow is identical across profiles; only the *recommendations* differ (computed from the live profile + probe).

---

## 8. Dependencies

In-box + existing primitives only — no new third-party. `IDeploymentProfileService` + host-capability probe (platform-conventions), `IIntegrationService`/OAuth (identity-auth), `IBillingTierService.AllowsCustomBotName` (monetization-billing), `IEventBus`, `IUnitOfWork`, `TimeProvider` (for `SetupCompletedAt`).

---

## 9. Decisions (resolved)

- **Setup status is the single client entry-gate**, anonymous-readable, computed (lowest-unmet-stage), with one persisted terminal marker (`DeploymentProfile.SetupCompletedAt`). No redundant per-step state table.
- **One setup API, three presentations** (desktop/web/CLI) — surfaces render the same `ISetupService` flow; no surface-specific logic.
- **Guidance level is an explicit Simple/Advanced choice** (Docker-pre-selected, never silent), seeded to `DeploymentProfile.DefaultGuidanceLevel` and the live per-user `UserPreferences.GuidanceLevel`.
- **Bot identity choice is sequenced here but OAuth is owned by identity-auth**; custom-bot allowance gates on `AllowsCustomBotName` (SaaS premium / self-host free).
- **Fail-closed completion** — cannot mark complete without a connected streamer; streamer connect is always step one (establishes the gating principal).
