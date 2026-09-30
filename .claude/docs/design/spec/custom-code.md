# Interface Specification — `custom-code` (T3 sandboxed script execution)

Scope: the T3 code escape-hatch. A single `run_code` pipeline action runs an author's TypeScript script
(compiled to JS) inside a sandbox behind one
`IScriptExecutor` (Jint everywhere today; a Wasmtime adapter exists but is not registered, §7). The capability broker injects a deny-by-default, value-in/value-out `bot` facade —
never a credential, URL, or another tenant. Execution time is metered against per-tier quotas;
scripts are immutably versioned with validate-on-save and hot-swappable active versions.

Grounding: `2026-06-16-custom-command-execution.md` (decisions + red-team must-fixes 4/5/6/7/8),
`2026-06-16-stack-and-dependencies.md` §"Sandbox execution", `2026-06-16-decisions-resolved.md`
items 2 & 6, schema Domain H (H.2/H.4/H.5/H.6/H.7), N.2/N.5.

This subsystem **consumes** the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13
(`Application/Pipeline`: `string Type` + `Category`/`Description`; `Task<ActionResult> ExecuteAsync(ActionContext
context, CancellationToken ct)`, with `ActionResult.Ok/Fail/Stop`). It does **not** introduce a second action
contract. The `RunCodeAction` is one more `ICommandAction` registered alongside `SongRequestAction`. (The
pre-consolidation Infrastructure `ActionType`/`ExecuteAsync(PipelineExecutionContext, ActionDefinition)` shape is
collapsed away per commands-pipelines §0.)

Binding correction this spec encodes (red-team must-fix #4 — current `PipelineEngine` is fail-OPEN:
unknown action → skip, unknown condition → true, action throw → continue): the `run_code` path is
**fail-CLOSED** — unknown/rejected/disabled script, denied capability, or quota-exceeded returns a failing
`ActionResult` and the engine MUST halt that pipeline run (`StopPipeline`). The engine change is owned by the
pipeline subsystem; this spec only defines the `run_code` action's fail-closed return contract.

---

## 1. Entities (locked schema — owned by this subsystem)

These tables are **defined in** `2026-06-16-database-schema.md`; reproduced here as field references only —
the schema doc is the source of truth. EF entities live in `NomNomzBot.Domain/Entities/`; configurations in
`NomNomzBot.Infrastructure/Persistence/Configurations/`. Surrogate PKs = `Guid` via `Guid.CreateVersion7()`.

### `CodeScript` (schema H.5) `[soft-delete]` — owned
`Id guid PK`; `BroadcasterId guid FK→Channels Index` (tenant); `Name string(100)`; `Description string(500)?`;
`Language string(20)` (`typescript` [VC:enum]); `CurrentVersionId guid? FK→CodeScriptVersions Index`
(active-version pointer — hot-swap); `IsEnabled bool Index`; `AuthorUserId guid? FK→Users Index`;
`LastRuntimeError string? (text)`; `LastRanAt timestamp?`; `CreatedAt/UpdatedAt/DeletedAt`.
Platform-content provenance (S-ADMIN-2e, same shape as `ChannelBuiltinCommand`/`Widget`/`Pipeline`):
`PlatformSourceDefinitionId guid?` (the `PlatformContentDefinition`, Kind=`code_script`, this row was installed
from), `PlatformSourceVersion int?`, `PlatformSourceHash string?` (content hash at install/last sync — compared to the
live current version's source to decide "untouched"), `PlatformSourceSyncedAt timestamp?`.
**Unique** `(BroadcasterId, Name)`.

### `CodeScriptVersion` (schema H.6) `[APPEND-ONLY content, soft-delete]` — owned
`Id guid PK`; `CodeScriptId guid FK→CodeScripts Index`; `BroadcasterId guid FK→Channels Index`;
`Version int`; `SourceCode string (text)` (authored TypeScript — the compiled entry's content);
`FilesJson string? (text)` (multi-file project: raw JSON `path → content` map; a single-file save is a one-entry
map; null only on legacy rows); `ManifestJson string? (text)` (project manifest `{ entry, kind, framework,
dependencies[] }`, dev-platform.md §4.2); `CompiledJs string? (text)` (transpiled JS,
populated on `valid`); `CompiledHash string? (64) Index` (SHA-256 of `CompiledJs` — per-unit cache key
`tenant+version`); `ValidationStatus string(20) Index` (`valid`|`rejected`|`pending` [VC:enum]);
`ValidationErrorsJson string? (text)` [VC:JSON] (`IReadOnlyList<ScriptValidationError>`);
`DeclaredCapabilitiesJson string (text)` [VC:JSON] (`IReadOnlyList<string>` capability keys);
`PublishedAt timestamp?`; `AuthorUserId guid? FK→Users`; `CreatedAt`.
**Unique** `(CodeScriptId, Version)`. Append-only in content: a version is never edited, corrections = new version.
The entity is `SoftDeletableEntity` (S-OWN06): the owner may prune a non-published version from the history list, which
only sets `DeletedAt` (the row is retained for audit); the current published version cannot be deleted.

### `HttpEgressAllowlist` (schema H.7) `[soft-delete]` — owned (consumed by the `bot.http` capability)
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Fqdn string(253)`; `ApprovedByUserId guid? FK→Users`;
`IsEnabled bool Index`; `MaxResponseBytes int`; `MaxRequestBytes int` (default 8192 — outbound request-body cap;
**reject** when exceeded, not truncate); `AllowRequestBody bool` (default false); `AllowQuery bool` (default false —
guest may attach an arbitrary query string only when opted in; §7.1 step 9 second-order SSRF reduction);
`AllowedMethods string(100)` (CSV of permitted HTTP methods, default `GET`); `PathPrefix string(255)?` (optional
path-prefix restriction, null = any path); `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, Fqdn)`. SSRF boundary for `bot.http.fetch` (request-cap columns: §7.1 step 6b/step 9).

### Referenced, NOT owned (read-only here)
- `PipelineStep` (H.2): `ActionType == "run_code"` rows carry `CodeScriptId guid? FK→CodeScripts`. Pipeline subsystem owns it; this spec defines the `run_code` action it dispatches to.
- `PipelineExecution` (H.4) `[APPEND-ONLY]`: `HostCallCount int`, `DurationMs int`, `Status` (`success`|`failed`|`timeout`|`denied`), `StepLogsJson` (bounded, PII-excluded). The pipeline subsystem writes this; the `run_code` action contributes `HostCallCount`, denial reason, and bounded logs via its return.
- `TierLimit` (N.2) `[GLOBAL]`: `LimitKey == "sandbox_exec_ms"`, `LimitValue bigint` (-1 = unlimited). Billing subsystem owns the table; this subsystem **reads** the resolved limit.
- `UsageRecord` (N.5) `[APPEND-ONLY semantics]`: `MetricKey == "sandbox_exec_ms"`, `Quantity bigint`, `(BroadcasterId, MetricKey, PeriodStart)` unique. This subsystem **increments** `Quantity` for the current period after each run (the metering write).

---

## 2. Domain events

Namespace `NomNomzBot.Domain.CustomCode.Events`; `sealed class … : DomainEventBase` with `required` init-only
properties (as built). `BroadcasterId` (the `Guid` tenant key) comes from the base — it is NOT redeclared on these
events. Emitted via `IEventBus.PublishAsync`.

**Only two events exist:** `CodeScriptValidatedEvent` and `CodeScriptVersionPublishedEvent`. There is no
`ScriptExecutedEvent` and no `ScriptExecutionDeniedEvent`; a run's outcome is recorded on the instance row
(`CodeScript.LastRanAt` / `LastRuntimeError`) and in the pipeline execution log, not on the bus.

```csharp
// Raised after a new version is persisted with ValidationStatus == valid|rejected (validate-on-save outcome).
public sealed class CodeScriptValidatedEvent : DomainEventBase
{
    public required Guid CodeScriptId { get; init; }
    public required Guid CodeScriptVersionId { get; init; }
    public required int Version { get; init; }
    public required string ValidationStatus { get; init; }                       // "valid" | "rejected"
    public required IReadOnlyList<string> DeclaredCapabilities { get; init; }
    public required IReadOnlyList<ScriptValidationError> Errors { get; init; }   // empty when valid
}

// Raised when CurrentVersionId is repointed (hot-swap; old version stays immutable).
public sealed class CodeScriptVersionPublishedEvent : DomainEventBase
{
    public required Guid CodeScriptId { get; init; }
    public required Guid CodeScriptVersionId { get; init; }
    public required int Version { get; init; }
    public Guid? PreviousVersionId { get; init; }
    public Guid? PublishedByUserId { get; init; }
}
```

---

## 3. Service interface(s)

All in `NomNomzBot.Application.Contracts.CustomCode` unless noted. `Result<T>` =
`NomNomzBot.Application.Common.Models.Result<T>`. Async all the way; never null. Errors use the
`BaseController.ResultResponse` error-code vocabulary (`VALIDATION_FAILED`, `NOT_FOUND`, `ALREADY_EXISTS`,
`FORBIDDEN`, `BILLING_LIMIT`, `RATE_LIMITED`, `FEATURE_DISABLED`).

### 3.1 `IScriptExecutor` — Application contract; Jint adapter registered (Wasmtime adapter built, not registered)
The single sandbox boundary. The implementation receives **only** the resolved `ScriptExecutionRequest`
(value types, copied), a `ScriptCapabilityGrant` (value-typed capability descriptors), and an
`IScriptHostBridge` (the per-execution host-dispatch seam the granted `bot.*` imports invoke). No `DbContext`,
no tokens, no `HttpClient`, no tenant context cross the boundary — must-fix #5.

#### Host-dispatch bridge (the seam the `bot.*` facade is wired to)

`ScriptCapabilityGrant` (§4) is a **pure value type** — descriptors only, zero delegates — so on its own there
is **no member** the `Linker.Define` / `engine.SetValue("bot", …)` callback can call to reach host code, and
**not a single host import is wireable**. The dispatch surface is carried out-of-band by `IScriptHostBridge`,
passed as a **separate parameter** to `ExecuteAsync` (keeps the grant a pure value type). The bridge is the
**only** path from a guest host-import to host code: it is **per-execution**, **capability-key-gated**, and
**never crosses the sandbox memory boundary** — it is the host-side trampoline the `Linker.Define`/`SetValue`
callbacks invoke; only primitives flow into and out of the guest (`code-execution-sandbox.md` §6.4).

```csharp
// The host trampoline a single granted bot.* import binds to: primitive-in / primitive-out only.
// The CancellationToken is the per-execution token the wall-clock watchdog cancels (sandbox §4.1.2).
public delegate string? HostImportDelegate(
    string capabilityKey, IReadOnlyList<string> args, CancellationToken ct);

// Resolves the host-side dispatch delegate for one granted capability key. Host-side only; the returned
// delegate is what the Linker/SetValue callback invokes — it never crosses into guest memory.
public interface IScriptHostBridge
{
    HostImportDelegate Resolve(string capabilityKey);
}
```

```csharp
public interface IScriptExecutor
{
    // The runtime backend this adapter provides ("jint" | "wasmtime"); surfaced for diagnostics/health.
    ScriptRuntimeKind Runtime { get; }

    // Transpile+validate TypeScript at SAVE time (must-fix #4 fail-closed at save). Returns the compiled JS,
    // SHA-256 hash, and statically declared capabilities, or a rejected result with structured errors.
    // No side effects: never executes user code, never touches host imports. Deterministic, sandbox-bounded.
    Task<Result<ScriptCompilation>> CompileAsync(
        string sourceCode,
        CancellationToken cancellationToken = default);

    // Execute compiled JS once inside the sandbox under the supplied grant + resource budget.
    // Enforces wall-clock-incl-host watchdog + host-call budget (must-fix #7); host imports are the only
    // surface (must-fix #5) and reach host code ONLY through `bridge` — the per-execution, capability-key-gated
    // host-dispatch seam (the bridge never crosses the sandbox memory boundary). Returns the script's
    // value-typed result + accounting; NEVER throws sandbox escapes outward. A denied capability / exceeded
    // budget / runtime fault → ScriptOutcome with the matching ScriptExecutionOutcome
    // (Denied/Timeout/HostBudgetExceeded/Faulted) — fail-closed.
    Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge,
        CancellationToken cancellationToken = default);
}
```

### 3.2 `IScriptCapabilityBroker` — Infrastructure; per-tenant grant assembly + enforcement
Builds the deny-by-default `bot` facade for one execution, binding each capability to a resource owner the
tenant already controls (token injected host-side, never in the sandbox — decision #4). Enforces the
declared-capability allowlist (deny anything not in `CodeScriptVersion.DeclaredCapabilities`) and per-tenant
rate limits on side-effecting imports.

```csharp
public interface IScriptCapabilityBroker
{
    // Resolve the grant for this tenant+version: validate every declared capability is allowed for the
    // channel (feature flag + tier + owner approval), bind host-side resource owners, and produce the
    // value-in/value-out import set. Returns FORBIDDEN-coded failure on any disallowed capability (fail-closed).
    Task<Result<ScriptCapabilityGrant>> BuildGrantAsync(
        Guid broadcasterId,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken cancellationToken = default);

    // The full catalog of capability keys this build exposes (UI + save-time validation read this).
    // Each describes its import surface, danger tier, and the feature flag / tier gate that governs it.
    IReadOnlyList<ScriptCapabilityDescriptor> Catalog { get; }
}
```

### 3.3 `IScriptExecutionMeter` — Infrastructure; quota check + usage metering
Single owner of the `sandbox_exec_ms` quota gate (reads `TierLimit`) and the `UsageRecord` increment.

```csharp
public interface IScriptExecutionMeter
{
    // PRE-run gate: is the tenant under its sandbox_exec_ms quota for the current period?
    // Reads the resolved TierLimit (-1 = unlimited) vs the period UsageRecord. BILLING_LIMIT-coded failure
    // when exhausted (fail-closed — run is refused before execution).
    Task<Result<QuotaCheck>> CheckSandboxBudgetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default);

    // POST-run write: increment UsageRecord(MetricKey="sandbox_exec_ms", current period) by elapsedMs.
    // Idempotent per ExecutionId. Append-only window semantics; never decrements.
    Task<Result> RecordSandboxUsageAsync(
        Guid broadcasterId,
        long elapsedMs,
        string executionId,
        CancellationToken cancellationToken = default);
}
```

### 3.4 `ICodeScriptService` — Application; authoring CRUD + versioning + hot-swap
Owns `CodeScript` / `CodeScriptVersion` lifecycle through `IUnitOfWork` + repository (no raw `DbContext`).
Tenant taken from `ICurrentTenantService` — callers pass no `BroadcasterId` in DTOs (broker invariant).

```csharp
public interface ICodeScriptService
{
    // List the tenant's scripts (active-version status projected). Soft-deleted excluded by global filter.
    Task<Result<PagedList<CodeScriptSummaryDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default);

    // Get one script with its active version's source + validation state. NOT_FOUND if missing/other tenant.
    Task<Result<CodeScriptDetailDto>> GetAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default);

    // Create a named script + its Version 1. Compiles+validates synchronously (validate-on-save):
    // valid → persists CompiledJs/hash/capabilities, sets CurrentVersionId, raises CodeScriptValidatedEvent;
    // rejected → persists the rejected version (audit), leaves CurrentVersionId null, returns VALIDATION_FAILED
    // with errors. ALREADY_EXISTS on duplicate (BroadcasterId, Name).
    Task<Result<CodeScriptDetailDto>> CreateAsync(
        CreateCodeScriptRequest request,
        CancellationToken cancellationToken = default);

    // Append a new immutable Version (never edits prior rows). Compiles+validates; on valid does NOT auto-publish
    // unless request.Publish — pure save vs save-and-swap is the caller's choice. Raises CodeScriptValidatedEvent.
    Task<Result<CodeScriptVersionDto>> CreateVersionAsync(
        Guid codeScriptId,
        CreateCodeScriptVersionRequest request,
        CancellationToken cancellationToken = default);

    // Repoint CurrentVersionId to an existing valid version (hot-swap, no restart; per-unit cache invalidated
    // by tenant+version). Raises CodeScriptVersionPublishedEvent. VALIDATION_FAILED if the target is not valid.
    Task<Result<CodeScriptDetailDto>> PublishVersionAsync(
        Guid codeScriptId,
        Guid codeScriptVersionId,
        CancellationToken cancellationToken = default);

    // List immutable version history (newest first), each with validation status + declared capabilities.
    Task<Result<PagedList<CodeScriptVersionDto>>> ListVersionsAsync(
        Guid codeScriptId,
        PaginationParams paging,
        CancellationToken cancellationToken = default);

    // Soft-delete one saved version (S-OWN06). NOT_FOUND if the script/version is not in this tenant;
    // VERSION_IS_PUBLISHED if it is the current hot-swap target (publish a different version first).
    Task<Result> DeleteVersionAsync(
        Guid codeScriptId,
        Guid codeScriptVersionId,
        CancellationToken cancellationToken = default);

    // Load the current multi-file project (file set + manifest) for the editor (dev-platform.md §8).
    // A legacy version without a stored project is projected as its one-file scaffold.
    Task<Result<ProjectDto>> GetProjectAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default);

    // Save a multi-file project: validate file set + manifest (entry present, safe paths, allowlisted
    // dependencies), compile the manifest entry, and ONLY on a valid compile append a new version, store the
    // whole project and hot-swap it live. A validation/compile failure persists NO version.
    Task<Result<CodeScriptVersionDto>> SaveProjectAsync(
        Guid codeScriptId,
        ProjectDto project,
        CancellationToken cancellationToken = default);

    // Toggle IsEnabled. Disabled script → run_code fails closed at execution. Mutates instance row only.
    Task<Result> SetEnabledAsync(
        Guid codeScriptId,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    // Soft-delete the script (sets IsDeleted/DeletedAt; versions remain as append-only audit under the FK).
    Task<Result> DeleteAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default);

    // The real, counted blast radius of deleting this script (S-CONSEQ): its saved versions and the run_code
    // pipeline steps that would stop running. The dashboard calls this before the delete confirm.
    Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default);
}
```

`ProjectDto` lives in `NomNomzBot.Application.DevPlatform.Dtos`; `BlastRadiusDto` in
`NomNomzBot.Application.Common.Consequences`. `PaginationParams` (page, take, sort, order) is the paging input the
controller builds from `PageRequestDto`.

A sibling service, `IScriptTestRunService.RunAsync(codeScriptId, ScriptTestRunRequest)` →
`Result<TestRunResultDto>`, DRY-RUNs the current version through the real sandbox with sample inputs: every
outward or mutating effect (chat, TTS, widgets, storage writes, rewards, schedules, …) is CAPTURED and returned as
`CapturedEffectDto` instead of performed; reads run live. It leaves no trace (no state, no chat, no quota).

### 3.5 `IScriptRunner` — Application; the orchestration `RunCodeAction` calls
One method composing meter-gate → load active version → broker grant → executor → meter-record →
events → instance `LastRanAt`/`LastRuntimeError`. Keeps `RunCodeAction` thin.

```csharp
public interface IScriptRunner
{
    // Run the script bound to a pipeline step. Fail-closed at every gate (disabled/rejected/missing version →
    // Faulted; quota → Denied(QuotaExceeded); capability → Denied(CapabilityDenied)). Side effects: meters usage,
    // updates CodeScript.LastRanAt/LastRuntimeError (no bus event is raised for a run). Returns the
    // value-typed outcome (variables to merge back, chat output, stop flag) for the action to surface.
    Task<Result<ScriptRunResult>> RunAsync(
        Guid codeScriptId,
        ScriptInvocation invocation,
        CancellationToken cancellationToken = default);
}
```

---

## 4. DTOs / contracts

Namespace `NomNomzBot.Application.Contracts.CustomCode`. App JSON = **Newtonsoft.Json** (per binding
conventions for app-facing contracts). Records, init-only. No `BroadcasterId`/credential/url field on any
inbound request (broker invariant; save-time validator rejects them).

### Enums (Domain or Application as noted)
```csharp
public enum ScriptRuntimeKind { Jint, Wasmtime }                                   // Application

public enum ScriptExecutionOutcome { Success, Faulted, Timeout, HostBudgetExceeded, Denied }   // Domain

public enum ScriptDenialReason { CapabilityDenied, QuotaExceeded, EgressBlocked, ScriptDisabled, VersionInvalid } // Domain
```

### Authoring requests/responses
```csharp
public sealed record CreateCodeScriptRequest(
    string Name,                 // ≤100, unique per tenant
    string? Description,         // ≤500
    string SourceCode);          // TypeScript; Language fixed to "typescript"

public sealed record CreateCodeScriptVersionRequest(
    string SourceCode,
    bool Publish);               // true = save-and-swap CurrentVersionId on valid

public sealed record CodeScriptSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsEnabled,
    int? CurrentVersion,         // null if no published version
    string CurrentValidationStatus,   // "valid" | "rejected" | "pending" | "none"
    string? LastRuntimeError,
    DateTime? LastRanAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CodeScriptDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsEnabled,
    string Language,
    Guid? CurrentVersionId,
    CodeScriptVersionDto? CurrentVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CodeScriptVersionDto(
    Guid Id,
    Guid CodeScriptId,
    int Version,
    string SourceCode,
    string CompiledHash,
    string ValidationStatus,
    IReadOnlyList<ScriptValidationError> ValidationErrors,
    IReadOnlyList<string> DeclaredCapabilities,
    DateTime? PublishedAt,
    DateTime CreatedAt);

public sealed record ScriptValidationError(
    string Code,                 // "syntax" | "transpile" | "forbidden_global" | "undeclared_capability" | "banned_param"
    string Message,
    int? Line,
    int? Column);
```

### Execution contracts (cross the executor boundary — value types only, must-fix #5)
```csharp
// Built by IScriptRunner from the pipeline step; passed to IScriptExecutor. Copied/owned values only.
public sealed record ScriptExecutionRequest(
    string ExecutionId,                  // == PipelineExecutionContext.ExecutionId
    string CompiledJs,
    string CompiledHash,
    ScriptInputs Inputs,                 // value snapshot — NO PII (no viewer email/IP), must-fix #5
    ScriptResourceBudget Budget);

public sealed record ScriptInputs(
    string TriggeredByUserId,            // internal guid string, not Twitch PII
    string TriggeredByDisplayName,
    IReadOnlyList<string> Args,          // bot.args
    IReadOnlyDictionary<string, string> Variables);   // bot.vars snapshot in

public sealed record ScriptResourceBudget(
    int WallClockMs,                     // seconds-not-minutes; wall-clock incl. host calls (covers Wasmtime #9188)
    int MaxHostCalls,                    // per-execution host-call budget (must-fix #7)
    long MaxFuelOrStatements,            // Wasmtime fuel / Jint statement cap
    long MaxMemoryBytes,                 // long: fed to Store.SetLimits(memorySize) (Rust StoreLimits, 64-bit) + Jint LimitMemory (sandbox §5.1)
    long MaxOutputBytes,                 // chat-output truncation cap (long: may exceed int for large outputs)
    long MaxEgressBytes);                // cumulative request+response bytes over ALL fetches in one run; reject when exceeded (sandbox §7.4)

// The capability surface handed to the sandbox; each import is value-in/value-out, host-side bound.
public sealed record ScriptCapabilityGrant(
    Guid BroadcasterId,                  // host-side only; never readable by user code
    IReadOnlyList<ScriptCapabilityDescriptor> Granted);

public sealed record ScriptCapabilityDescriptor(
    string Key,                          // e.g. "chat.send", "music.queue", "vars.read", "vars.write", "http.fetch"
    string FloorTier,                    // "low" | "tos" | "critical" — critical never granted to T3
    string FeatureFlagKey,               // gate that must be enabled
    bool SideEffecting);                 // true → counts against per-tenant import rate limit

public sealed record ScriptExecutionOutcomeResult(
    ScriptExecutionOutcome Outcome,
    long ElapsedMs,
    int HostCallCount,
    IReadOnlyDictionary<string, string> VariablesOut,   // bot.vars writes to merge back
    string? ChatOutput,                                 // value the script asked to send (subject to chat.send grant)
    bool StopPipeline,
    string? ErrorMessage);

// Returned by IScriptRunner to RunCodeAction.
public sealed record ScriptRunResult(
    ScriptExecutionOutcome Outcome,
    IReadOnlyDictionary<string, string> VariablesOut,
    string? Output,
    bool StopPipeline,
    string? ErrorMessage,
    ScriptDenialReason? DenialReason);

public sealed record ScriptInvocation(
    string ExecutionId,
    string TriggeredByUserId,
    string TriggeredByDisplayName,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Variables);

// Returned by IScriptExecutor.CompileAsync.
public sealed record ScriptCompilation(
    string CompiledJs,
    string CompiledHash,                 // SHA-256 of CompiledJs
    IReadOnlyList<string> DeclaredCapabilities);

public sealed record QuotaCheck(
    bool Allowed,
    long LimitMs,                        // -1 = unlimited
    long UsedMs,
    DateTime PeriodStart,
    DateTime PeriodEnd);
```

---

## 5. Controller endpoints

`CodeScriptsController : BaseController` in `NomNomzBot.Api/Controllers/`.
`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/code-scripts")] [Authorize]`. All responses
`StatusResponseDto<T>` / `PaginatedResponse<T>` via `ResultResponse(...)`. Tenant resolved from the
authenticated principal (NOT route/header/query — IDOR fix #1); every action operates on the current tenant.
Gate-2 is the class-level `[RequireAction("code:script:author")]`, so every action below inherits it; every action
also runs the `custom_code` feature gate first.

Auth plane = **management plane, Broadcaster floor, critical danger tier** (authoring/running sandboxed code
is a channel-owner-level operational capability, never a per-viewer one — the earlier "community plane +
critical floor" framing was self-contradictory and is resolved here). `ActionDefinitions` key
`code:script:author` (`Plane=management`, `FloorLevel=Broadcaster(40)`, `FloorTier=critical`,
`IsGrantableViaPermit=true` → **Broadcaster-delegable as a per-user `!permit` grant only**, never role-tier
based — see below). All thirteen endpoints — reads included (source can embed logic the owner authored) —
enforce the **same** key.

Role gate:
- Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's).
- Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below).
- The keys are seeded global `ActionDefinitions` (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|------|-------|-------------|--------------|------------------|
| GET | `/api/v1/code-scripts` | `PageRequestDto` (query) | `PaginatedResponse<CodeScriptSummaryDto>` | management / Broadcaster · `code:script:author` |
| GET | `/api/v1/code-scripts/{id}` | — | `StatusResponseDto<CodeScriptDetailDto>` | management / Broadcaster · `code:script:author` |
| POST | `/api/v1/code-scripts` | `CreateCodeScriptRequest` | `StatusResponseDto<CodeScriptDetailDto>` | management / Broadcaster · `code:script:author` |
| POST | `/api/v1/code-scripts/{id}/versions` | `CreateCodeScriptVersionRequest` | `StatusResponseDto<CodeScriptVersionDto>` | management / Broadcaster · `code:script:author` |
| GET | `/api/v1/code-scripts/{id}/versions` | `PageRequestDto` (query) | `PaginatedResponse<CodeScriptVersionDto>` | management / Broadcaster · `code:script:author` |
| DELETE | `/api/v1/code-scripts/{id}/versions/{versionId}` | — | `StatusResponseDto<object>` | management / Broadcaster · `code:script:author` |
| POST | `/api/v1/code-scripts/{id}/versions/{versionId}/publish` | — | `StatusResponseDto<CodeScriptDetailDto>` | management / Broadcaster · `code:script:author` |
| GET | `/api/v1/code-scripts/{id}/project` | — | `StatusResponseDto<ProjectDto>` | management / Broadcaster · `code:script:author` |
| PUT | `/api/v1/code-scripts/{id}/project` | `ProjectDto` | `StatusResponseDto<CodeScriptVersionDto>` | management / Broadcaster · `code:script:author` |
| POST | `/api/v1/code-scripts/{id}/test-run` | `ScriptTestRunRequest` | `StatusResponseDto<TestRunResultDto>` | management / Broadcaster · `code:script:author` |
| PATCH | `/api/v1/code-scripts/{id}/enabled` | `SetCodeScriptEnabledRequest(bool IsEnabled)` | `StatusResponseDto<object>` | management / Broadcaster · `code:script:author` |
| GET | `/api/v1/code-scripts/{id}/blast-radius` | — | `StatusResponseDto<BlastRadiusDto>` | management / Broadcaster · `code:script:author` |
| DELETE | `/api/v1/code-scripts/{id}` | — | `StatusResponseDto<object>` | management / Broadcaster · `code:script:author` |

Controller actions (`CodeScriptsController`): `List`, `Get`, `Create`, `CreateVersion`, `ListVersions`,
`DeleteVersion`, `Publish`, `GetProject`, `SaveProject`, `TestRun`, `SetEnabled`, `GetDeleteBlastRadius`, `Delete`.
`Publish` and `TestRun` carry the `WriteExpensive` rate-limit policy. `GetDeleteBlastRadius` and `Delete` are
`[DestructiveAction(HasCountedBlastRadius = true)]`: the confirm step MUST call the blast-radius route and show the
counted dependents before the delete runs.

The class-level `[RequireAction("code:script:author")]` enforces Gate-2 before any action body runs; a denial
returns `FORBIDDEN`, fail-closed.

#### 5.1 Seeded `ActionDefinitions` row (this subsystem owns this seed entry)

The `code:script:author` key the table above resolves is **not** defined elsewhere — this subsystem adds it
to the `[GLOBAL, seed]` `ActionDefinitions` seed set in the existing `DataSeeder` (the same seed pass that
owns Domain-B B.3 rows, per `roles-permissions.md`, mirroring the eventsub seed). Without this row the
resolver fails closed and every `/code-scripts` call 403s.

| `ActionKey` | `Plane` | `DefaultLevel` | `FloorLevel` | `FloorTier` | `IsGrantableViaPermit` | `Description` |
|---|---|---|---|---|---|---|
| `code:script:author` | `management` | 40 (Broadcaster) | 40 (Broadcaster) | `critical` | `true` | Author, version, publish, toggle, delete, and read this channel's sandboxed custom-code scripts (T3 escape-hatch). |

`Id` is `Guid.CreateVersion7()` at seed time; the seed is idempotent on the `ActionKey` unique index (upsert,
no duplicate on re-run). `Plane` uses the `AuthPlane` `[VC:enum]`, `FloorTier` the `DangerTier` `[VC:enum]`
(matching B.3). `critical` + `IsGrantableViaPermit=true` means authority sits with the Broadcaster by default,
and the Broadcaster — fully trusted over their own channel — **MAY delegate it to a named individual user** via a
per-user `!permit @user code:script:author` capability grant (`roles-permissions.md` §0.2/§3.6). It is **never**
role-tier delegable: the `Broadcaster(40)` floor means a `ChannelActionOverride` cannot drop it onto an Editor or
any lower tier (`SetActionOverrideAsync` rejects below-floor, `VALIDATION_FAILED`); reach below the owner is the
per-user `PermitGrant` path **only**. The no-escalation guardrail keeps the grantor at/above the action — so in
practice only the Broadcaster can issue this Critical grant (the canonical "one mod is a developer" case → grant
that one person, not all Editors).

Feature gate: whole controller behind `FeatureFlag.Key == "custom_code"` (`MinTierId` → tier-gated;
`DeploymentMode` null = both). Disabled flag → `FEATURE_DISABLED`. No HTTP endpoint runs a script —
execution happens only via the `run_code` pipeline action.

---

## 6. Pipeline actions

One action, implementing the single canonical `ICommandAction` (owner `commands-pipelines.md` §3.13; `Type`-based).

### `RunCodeAction : ICommandAction`
- **`Type`** = `"run_code"`.
- **Config DTO** (the `ActionDefinition.Parameters` shape; bound to `PipelineStep.CodeScriptId` per H.2):
  ```csharp
  public sealed record RunCodeActionConfig(
      Guid CodeScriptId);          // FK→CodeScripts; the only param — NO tenant/credential/url (broker invariant)
  ```
- **Behavior**: resolves `CodeScriptId` + the live `PipelineExecutionContext` into a `ScriptInvocation`,
  calls `IScriptRunner.RunAsync`, maps the result to the engine's `ActionResult`:
  - `Success` → `ActionResult.Success(output)`, merging `VariablesOut` back into `ctx.Variables`; honors `StopPipeline`.
  - any non-`Success` outcome (`Faulted`/`Timeout`/`HostBudgetExceeded`/`Denied`) → `ActionResult.Failure(reason)`
    and signals the engine to **halt** (fail-closed, must-fix #4 — unlike the current fail-open default).
  - never lets a sandbox exception propagate; the executor returns an outcome, the action returns a `Failure`.
- Registered by the transient `AddImplementationsOf<ICommandAction>(infrastructure, …)` assembly scan beside the
  other actions (§7) — there is no hand-written `AddTransient<ICommandAction, RunCodeAction>()` line.
- Save-time validator forbids any param other than `CodeScriptId` on a `run_code` step (architecture-test enforced).

No new conditions. (`HttpRequest` egress is exposed as the `http.fetch` **capability** inside the sandbox,
gated by `HttpEgressAllowlist` — not a separate pipeline action in this subsystem.)

---

## 7. DI registration

`NomNomzBot.Application/DependencyInjection.cs` (contracts/orchestration) and
`NomNomzBot.Infrastructure/DependencyInjection.cs` (impls + profile adapter). Lifetimes match the existing
pipeline registrations: actions/executors transient (stateless), broker/meter/service scoped
(use scoped DbContext + tenant).

```csharp
// Application — AddApplication()
services.AddScoped<ICodeScriptService, CodeScriptService>();
services.AddScoped<IScriptRunner, ScriptRunner>();

// Infrastructure — AddInfrastructure(configuration)
services.AddScoped<IScriptCapabilityBroker, ScriptCapabilityBroker>();
services.AddScoped<IScriptExecutionMeter, ScriptExecutionMeter>();
services.AddScoped<IScriptHostBridgeFactory, ScriptHostBridgeFactory>();
services.AddScoped<CodeScriptRepository>();                       // GenericRepository<CodeScript>-derived
services.AddScoped<HttpEgressAllowlistRepository>();

// Pipeline action (transient — stateless): RunCodeAction is picked up by
// services.AddImplementationsOf<ICommandAction>(infrastructure, ServiceLifetime.Transient, …), not a manual line.

// The executor — registered UNCONDITIONALLY, no profile switch (as built):
services.AddScoped<IScriptExecutor, JintScriptExecutor>();
```

`DeploymentProfileSnapshot.CodeExecutor` (`CodeExecutorKind`) is still resolved and persisted on the
`DeploymentProfile` row, but **nothing in DI branches on it**: there is no `if (CodeExecutor == Wasmtime)` and no
`AddDeploymentAdapters` executor switch. `WasmtimeScriptExecutor` exists as a class (the hardened Cranelift harness,
proven by tests against real wasm; `Sandbox:WasmJsEnginePath` names the QuickJS engine module, and until that module
is present `ExecuteAsync` fails closed as `Faulted`) but it is **not registered** — no deployment mode resolves it.
The `Wasmtime` package reference stays in the Infrastructure project for that class.

Adapter variants (decision #2, tension #6):
- **every deployment (today)** → `JintScriptExecutor` (Jint 4.10.0, the version pinned in `server/Directory.Packages.props`).
  Threat model = single trusted operator; resource-safety only. Registered `Scoped`.
- **full/SaaS (not wired)** → `WasmtimeScriptExecutor` (Wasmtime 44.0.0, x86_64-Cranelift). Real isolation boundary;
  Winch + aarch64-Cranelift MUST stay disabled (April-2026 critical escapes); fast-patch SLA mandatory. The class is
  built and tested, but no DI registration selects it (see above); wiring it is a separate SaaS-deployment slice.

---

## 8. Dependencies (from the stack doc)

| Dependency | Party | Used by | Note |
|------------|-------|---------|------|
| **Jint** 4.10.0 (`Directory.Packages.props`) | 3rd (BSD-2) | `JintScriptExecutor` (registered for every deployment) | Managed JS interpreter; NOT a security boundary — single-operator threat model only |
| **Wasmtime (wasmtime-dotnet)** 44.0.0 | 3rd (Apache-2.0 WITH LLVM-exception) | `WasmtimeScriptExecutor` (class exists, **not registered**) | x86_64-Cranelift only; fuel + epoch + wall-clock watchdog; no DI branch loads it today |
| `System.Security.Cryptography` (`SHA256`) | 2nd / in-box | `CompiledHash` computation | No 3rd-party crypto |
| EF Core 10 + repository/`IUnitOfWork` | 2nd | `CodeScriptService`, meter, repos | Named query filters = soft-delete + tenant isolation |
| `IEventBus` (in-box `EventBus` lite / `RedisEventBus` SaaS) | 1st/2nd | domain-event emission | Existing bus; no MediatR |
| `Microsoft.Extensions.Http.Resilience` 10.7.0 + `IHttpClientFactory` | 2nd | `http.fetch` capability host-side | SSRF-hardened `DelegatingHandler`: FQDN-pin, no redirects, block loopback/RFC-1918/169.254.169.254, response-size cap |
| `System.Threading.RateLimiter` | 1st / in-box | per-tenant side-effecting-import limit | Adaptive throttle, in-box |
| `Newtonsoft.Json` | (app JSON convention) | inbound/outbound contracts | App-facing DTO serialization per binding rules |
| TypeScript transpile | (in-runtime) | `IScriptExecutor.CompileAsync` | TS→JS handled inside the executor adapter (Jint/Wasmtime-hosted); no Roslyn, no separate 3rd-party transpiler dep at the .NET layer |

No new owner-chosen 3rd-party beyond Jint + Wasmtime (both already accepted in the stack doc). No MediatR, no Roslyn.

---

## 9. Decisions (resolved)

1. **TypeScript→JS transpile mechanism inside `CompileAsync`.** The stack doc fixes the *runtimes*
   (Jint/Wasmtime) and the *No-Roslyn* rule, and states authoring is TS executed as JS. Transpilation runs
   **inside the executor adapter** — the TypeScript compiler is hosted on the same JS engine that backs the
   adapter (Jint for lite/self-host, Wasmtime for full/SaaS), so TS→JS happens entirely within the sandbox
   boundary. No new .NET-layer 3rd-party transpiler dependency is introduced, and no build-time/committed-JS
   artifact step exists: source is authored as TypeScript, transpiled at save time by `CompileAsync`, and the
   resulting `CompiledJs` + SHA-256 `CompiledHash` are persisted on the `CodeScriptVersion` (§1, §8). This is
   the design, consistent with the §8 Dependencies "TypeScript transpile (in-runtime)" row.
