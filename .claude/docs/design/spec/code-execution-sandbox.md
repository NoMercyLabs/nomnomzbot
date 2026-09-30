# Security & Sandboxing Deep-Dive — `code-execution-sandbox`

**Status:** design spec, **partly built** — the *Implementation status* table below is the truth about what the code does today; the sections after it are the design and carry an *As built* note wherever the code differs. **Owner of the *feature* contract:** [`custom-code.md`](./custom-code.md)
(the `IScriptExecutor` / `IScriptCapabilityBroker` / `IScriptExecutionMeter` / `IScriptRunner` interfaces, the
`CodeScript`/`CodeScriptVersion`/`HttpEgressAllowlist` entities, the `run_code` action, the `code:script:author`
authz gate, and the profile DI switch are all **defined there** and only **referenced** here).

**This document is the SECURITY deep-dive `custom-code.md` defers to.** It does not re-declare the interfaces or
DTOs in that spec — it specifies *how the sandbox actually contains untrusted code*: the threat model, the
per-executor isolation guarantees with exact runtime API knobs, the concrete DoS budgets, the capability-broker
safety pattern, network egress hardening, secrets/tenant isolation, observability, and the escape-attempt test
plan. Where a type from `custom-code.md` is named here (`ScriptExecutionRequest`, `ScriptCapabilityGrant`,
`ScriptResourceBudget`, `ScriptExecutionOutcome`, …), it is the **same** type — never a divergent copy.

> **Read order:** `custom-code.md` (what the feature is) → **this doc** (how it is contained) →
> [`gdpr-crypto.md`](./gdpr-crypto.md) (token-at-rest crypto the broker depends on) →
> [`commands-pipelines.md`](./commands-pipelines.md) (the fail-closed engine that drives `run_code`) →
> [`platform-conventions.md`](./platform-conventions.md) §3.7 (the `IRateLimiterPartitionStore` distributed
> counters the SaaS rate/concurrency limits route through, §5.3) / §3.9 / §7 (the profile-adapter seam that
> selects the executor).

**Grounding (verified research):** Wasmtime security model + wasmtime-dotnet v44 API surface; Jint v4.10/4.9.2
in-process limits and escape literature; OWASP SSRF Prevention; isolated-vm / node:vm escape lore;
Firecracker/gVisor multi-tenant isolation guidance; the four upstream specs above and the live-code state in
`server/src/NomNomzBot.Infrastructure/…`.

## Implementation status (as of 2026-09-30)

Code lives in `server/src/NomNomzBot.Infrastructure/CustomCode/` and `…/Sandbox/`; contracts in `NomNomzBot.Application/Contracts/CustomCode/`. Items marked **owner question** are design controls the code does not implement; whether and how to build each one is the owner's decision, so they are recorded here and in the section they touch rather than decided.

| Area | Status | As built / gap |
|---|---|---|
| Jint executor (self-host) | **Built — in-process** | `Jint/JintScriptExecutor.cs`, `Jint/JintEngineFactory.cs`, Jint **4.10.0** (`Directory.Packages.props`). Hardened per §4.2 (CLR interop off, string compilation off, recursion/stack/array/regex caps, statement/time/memory limits). Runs on the calling thread. **No separate worker process, no dedicated bounded-stack thread, no OS limits** — *owner question: worker process* (§3.3, §5.5, D5). |
| Wasmtime executor (SaaS) | **Harness only — fails closed** | `Wasmtime/WasmtimeScriptExecutor.cs`, wasmtime-dotnet **44.0.0**. Builds the hardened `Engine` (Cranelift, threads/SIMD/relaxed-SIMD/multi-memory/bulk-memory/reference-types off, fuel + epoch on) and `RunHardenedModule` runs a raw wasm export under `Store.SetLimits` + fuel. `ExecuteAsync` always returns `Faulted` (`"…JS engine module is not configured."` or `"…pending the engine-module ABI binding."`). No epoch watchdog thread, no host-import linking. |
| Executor selection | **Not built** | `IScriptExecutor` is bound to `JintScriptExecutor` unconditionally (Scoped). `DeploymentProfileSnapshot.CodeExecutor` / `CodeExecutorKind` exist and are persisted, but nothing selects the executor from them. `WasmtimeScriptExecutor` is never registered. |
| Capability broker + host bridge | **Built** | `ScriptCapabilityBroker` (22 catalogued capabilities, §6.2), `ScriptHostBridge`, `IScriptHostBridge` / `HostImportDelegate` seam (D9 satisfied). Deny-by-default grant; `critical` never granted. No `HttpEgressAllowlist` owner-approval check for `http.fetch`. |
| Budgets | **Built — compile-time constants** | `ScriptResourceBudget.Baseline` in `ScriptContracts.cs`: **8000 ms** / 64 host calls / 200,000 statements / 64 MiB / 8 KiB output / 256 KiB egress. No `AppSetting` surface, no tier headroom. `MaxEgressBytes` is declared but nothing enforces it. |
| Metering | **Built — check-then-record** | `ScriptExecutionMeter` over `IUsageMeteringService`, metric `sandbox_exec_ms`; refused `Denied(QuotaExceeded)` before execution, recorded after. **Not** reserve-then-settle (T14b open). Quotas beyond this (global admission, per-tenant import/egress buckets, circuit breaker, N-in-window script auto-disable) are **not built** — *owner question: quotas* (§5.3, §5.4). |
| SSRF egress client | **Built (core)** | `Sandbox/EgressHttpClient.cs` + `Sandbox/EgressAddressGuard.cs`: resolve-then-pin `ConnectCallback`, https-only, no redirects, blocked-range guard. Gaps: multicast not blocked (**S-EGRESS-GUARD-MULTICAST**); no body clamp; no `MaxConnectionsPerServer`/header timeout. See §7.1. |
| `http.fetch` allowlist | **Not built** | `ScriptHostBridge.Fetch` accepts any absolute `https` URL (GET only, 256 KiB body cap). It does **not** consult `HttpEgressAllowlist`. *Owner question: fetch allowlist.* |
| `http_request` pipeline action | **Does not exist** | Nothing in the code registers an action of that name. The design's "two front-ends" collapse to the callers listed in §6.3. |
| Runner + `run_code` | **Built** | `ScriptRunner` (disabled → quota → valid version → grant → execute → record) and `RunCodeAction` (`code_script_id`, decoded with `OwnedIdCodec`). The engine is fail-closed (§10.4). `ScriptExecutedEvent` / `ScriptExecutionDeniedEvent` are defined but **no code publishes them**. |
| PII allowlist on `Variables` | **Not built** | `RunCodeAction` copies `ctx.Variables` wholesale into `ScriptInputs.Variables`. `user.get` returns the public profile only (no email/IP). *Owner question: PII allowlist* (§8.5, T9b). |
| Postgres RLS | **Not built** | The `ITenantScoped` query filter exists; no `app.tenant_id` interceptor or RLS policies. *Owner question: RLS* (§8.3). |
| TypeScript → JS transpile | **Not built** | `CompileAsync` parses JavaScript only (`Engine.PrepareScript`), computes `CompiledHash`, extracts declared capabilities by regex. There is no TypeScript step and no `forbidden_global` / `undeclared_capability` static check. *Owner question: TS transpile* (§9.2, §9.4). |
| Authoring gate | **Built, differs from D1** | `code:script:author`: floor **Moderator**, `DangerTier.Critical`, **not** permit-grantable (`ActionDefinitionSeeder`), plus the per-channel `custom_code` feature toggle (default off) on every `/code-scripts` route (`FEATURE_DISABLED`). *Owner question: author floor* (§9.3, D1). |
| §8.4 prerequisites | **Satisfied** | AES-256-GCM + AAD token crypto, sealed `Configuration.SecureValue`, fail-closed engine — see §8.4. |
| Security tests (§12) | **Partial** | See the status table at the end of §12. T1 is partial; **T18 and T19 do not exist**; there are no architecture tests in the tree. |

---

## 0. Conventions, namespaces, non-negotiables

- C# namespace root is **`NomNomzBot.*`**. All sandbox types live under
  `NomNomzBot.Application.Contracts.CustomCode` (boundary value types, owned by `custom-code.md`),
  `NomNomzBot.Infrastructure.CustomCode.*` (the executor adapters, broker, meter, egress handler — **new**),
  and `NomNomzBot.Domain.*` (entities/events). Clean Architecture: dependencies flow inward only; the
  executor adapters are Infrastructure and depend on Application contracts, never the reverse.
- **`Result<T>` over exceptions / null.** The executor **never throws a sandbox escape outward** — every fault,
  denial, timeout, or budget breach becomes a `ScriptExecutionOutcome` value (`custom-code.md` §3.1).
- **No Roslyn.** User scripts are **TypeScript transpiled to JS**, executed as JS in Jint or JS-in-Wasm in
  Wasmtime. We never compile, emit, or reflect over user-supplied **C#**. (Roslyn is banned for codegen/analysis
  platform-wide; it has no role here.)
- **No MediatR.** Audit/abuse events flow over the existing `IEventBus` (`custom-code.md` §2 events).
- **.NET 10 / C# 14.** AES/SHA via in-box `System.Security.Cryptography`; HTTP via in-box `IHttpClientFactory` +
  `Microsoft.Extensions.Http.Resilience`. The only owner-accepted 3rd-party deps are **Jint** (self-host) and
  **Wasmtime/wasmtime-dotnet** (SaaS) — both already in the stack doc. Microsoft packages are 2nd-party.
- **Deployment-profile axis selects the adapter by DI** (`platform-conventions.md` §7). One boot-time switch,
  never a runtime `if`. The profile field `CodeExecutor` (`{wasmtime|jint}`) picks the security tier.
  **As built the switch does not exist** — Jint is registered unconditionally (§11.2).

---

## 1. Purpose & scope

### 1.1 What runs in the sandbox

The sandbox executes exactly one thing: an **author-written TypeScript script** (the T3 *code* escape-hatch),
compiled to JS at save time and run on demand through the **single** `run_code` pipeline action. Two trigger
shapes reach it, both through the same path:

| Trigger | How it reaches the sandbox |
|---|---|
| **Command (T3 code tier)** | A custom command of `Tier == Code` whose pipeline contains a `run_code` step (`commands-pipelines.md` §4.2 `CommandTier.Code`). |
| **Event response / timer pipeline** | Any pipeline (`TriggerKind = event|timer|command|manual`) containing a `run_code` step. Event handlers and timers are *just pipelines*; there is no separate sandbox entry point. |

Every entry funnels through `RunCodeAction → IScriptRunner.RunAsync → IScriptExecutor.ExecuteAsync`. There is
**no HTTP endpoint that executes a script** (`custom-code.md` §5). Authoring is REST; execution is pipeline-only.

### 1.2 What does **not** run in the sandbox

- **Template (T1) and pipeline (T2) tiers** — string interpolation and the built-in action set run in-host with
  no script engine. Only the `code` tier touches the sandbox.
- **The TypeScript→JS transpile** is *validation-time* (`CompileAsync`), deterministic, side-effect-free, and
  **never executes user code** (`custom-code.md` §3.1). It is in-scope for *this* doc only as a hardening surface
  (§9.4), not as untrusted execution.
- **Host services** — chat send, music queue, economy reads, HTTP egress, variable I/O — run **host-side** behind
  the capability broker. The guest never holds the client, token, or connection (§6).

### 1.3 In/out-of-scope for this document

| In scope | Out of scope (owned elsewhere) |
|---|---|
| Executor isolation guarantees, DoS budgets, capability brokering safety, egress hardening, secrets/tenant unreachability, escape-test plan | The feature interfaces & DTOs (`custom-code.md`); the fail-closed engine change (`commands-pipelines.md`); token-at-rest crypto (`gdpr-crypto.md`); RLS + tenant query filter + IDOR fix (`platform-conventions.md`); billing quota table ownership (`monetization-billing.md`) |

---

## 2. Threat model

### 2.1 Assets (what we protect, ranked by impact)

| # | Asset | Worst-case loss |
|---|---|---|
| A1 | **OAuth tokens / API keys / DB creds** (Twitch, Spotify, Discord, YouTube; Postgres conn string; `ENCRYPTION_KEY`) | Full account takeover of the streamer. The shared-key transplant defect that once widened this to *every* streamer (§8.4) is **closed**: token-at-rest crypto is AES-256-GCM with per-field AAD. |
| A2 | **Other tenants' data** (variables, chat, economy balances, viewer lists, scripts) | Cross-tenant confidentiality/integrity breach — the defining multi-tenant SaaS failure. |
| A3 | **The host process / host machine** | RCE, cloud-metadata credential theft, lateral movement, platform-wide outage. |
| A4 | **This tenant's own data & quota** | Self-inflicted is acceptable; we still bound it so a buggy script can't melt the channel. |
| A5 | **Platform availability** (CPU, RAM, file handles, sockets, threads) | Noisy-neighbor / deliberate DoS taking down all tenants. |

### 2.2 Adversaries

| Adversary | Capability | Primary goal |
|---|---|---|
| **Malicious script author** (a real Broadcaster who passed `code:script:author`) | Writes arbitrary TS/JS, controls loops/allocations/recursion *within one expression*, controls which capabilities are declared, controls egress FQDN requests they own. | Read another tenant's data (A2), steal a token (A1), reach cloud metadata (A3), DoS the platform (A5). **This is the design-driving adversary.** |
| **Compromised Broadcaster account** | Same as above, plus the attacker is not the legitimate owner — but is *indistinguishable* at the sandbox boundary. | Same. Mitigated upstream by auth/MFA, not by the sandbox; the sandbox assumes the author is hostile regardless. |
| **Co-located tenant on shared hardware** (SaaS) | Runs their own legitimate script on the same core/cache as a victim. | Microarchitectural side-channel leak (A2/A1). Accepted residual (§2.5). |
| **Confused-deputy via a host import** | Not a separate actor — a *technique*: a malicious script feeds crafted indices/keys/paths to an over-trusting host function. | Make the host act on another tenant's resource (A2/A1). Closed by §6 broker invariants. |

We do **not** trust the script author. We **do** trust: the host process code, the EF query filter + RLS, the JWT
issuer, and (after the live-code fixes in §13) the token-at-rest crypto.

### 2.3 Trust boundaries (ASCII)

```
                          ╔═══════════════════════════════════════════════════════════════╗
                          ║                    HOST PROCESS  (TRUSTED)                      ║
  Authenticated          ║                                                                 ║
  Broadcaster ──JWT──►  ┌─────────────┐   tenant = JWT.sub (NEVER from DTO/route)          ║
  (author / trigger)     ║  │ TenantResol.│──►IChannelAccessService.CanResolveTenantAsync  ║
                          ║  │ Middleware  │   fail-closed 403 on mismatch (IDOR fix #1)     ║
                          ║  └──────┬──────┘                                                 ║
                          ║         │ ctx.BroadcasterId : Guid  (unforgeable, host-derived)  ║
                          ║         ▼                                                        ║
                          ║  ┌──────────────┐   ┌──────────────────┐   ┌──────────────────┐ ║
                          ║  │  RunCodeAction│──►│  IScriptRunner   │──►│ IScriptExecutor  │ ║
                          ║  │ (pipeline)    │   │ meter→grant→exec │   │  Compile/Execute │ ║
                          ║  └──────────────┘   └───────┬──────────┘   └────────┬─────────┘ ║
                          ║                              │ grant (value)         │           ║
                          ║   ┌──────────────────────────▼──────────┐           │           ║
                          ║   │ IScriptCapabilityBroker             │           │           ║
                          ║   │  binds host-side resource owners,   │           │           ║
                          ║   │  injects pre-authorized clients,    │           │           ║
                          ║   │  TOKEN STAYS HOST-SIDE              ◄──────────┐ │           ║
                          ║   └─────────────┬───────────────────────┘          │ │           ║
                          ║ ════════════════│════════ SANDBOX BOUNDARY ════════│═│═════════  ║
                          ║                 │ value-in / value-out only        │ │           ║
                          ║      ┌──────────▼──────────────────────────────────┴─▼────────┐ ║
                          ║      │            GUEST  (UNTRUSTED user JS)                    │║
   SaaS  → Wasmtime Store │      │  • own linear memory / own Realm — no host heap        │║
   self  → Jint Engine    │      │  • NO ambient authority: only the granted `bot.*` API  │║
                          ║      │  • NO fs, NO net, NO clock-of-record, NO reflection     │║
                          ║      │  • host imports: chat.send, vars.read/write, music.q,  │║
                          ║      │    http.fetch(allowlist), economy.read … (each audited)│║
                          ║      └──────────┬──────────────────────────────────────────────┘║
                          ║                 │ host import call (validated, tenant-scoped)    ║
                          ║   ┌─────────────▼──────────────┐   ┌──────────────────────────┐ ║
                          ║   │ SSRF-hardened egress client │   │ pre-authorized Spotify/  │ ║
                          ║   │ FQDN-pin · no-redirect ·    │   │ Twitch/economy clients   │ ║
                          ║   │ block 169.254/RFC1918/loop  │   │ bound to ONE BroadcasterId│║
                          ║   └─────────────┬──────────────┘   └────────────┬─────────────┘ ║
                          ╚═════════════════│═══════════════════════════════│═══════════════╝
                                            ▼                               ▼
                                   approved external host          Postgres (RLS: app.tenant_id)
                                   (allowlisted FQDN only)         + per-tenant EF query filter
```

Two boundaries matter most: **(1)** the sandbox boundary — only value types and the granted facade cross it; and
**(2)** `ctx.BroadcasterId` being host-derived and unforgeable — every other layer assumes it.

### 2.4 STRIDE-to-control map

| STRIDE | Concrete vector | Control (section) |
|---|---|---|
| **S**poofing | Guest names another tenant's resource owner | BroadcasterId host-derived only; broker binds owner host-side (§6, §8) |
| **T**ampering | Guest corrupts another tenant's vars/memory | Per-Store/per-Engine isolation (§4); RLS + query filter (§8) |
| **R**epudiation | "I didn't run that script" | Append-only `CodeScriptVersion` + `CompiledHash`; append-only audit (§9, §10) |
| **I**nfo disclosure | Token theft, cross-tenant read, metadata SSRF | Broker (token host-side), egress allowlist, no fs (§6, §7, §8) |
| **D**enial of service | Infinite loop, memory bomb, host-call flood, stack overflow, blocking-host-call park, slow-loris egress, multi-instance fan-out, transpile-time bomb, self-sustaining worker crash | Fuel/epoch/wall-clock watchdog + per-import timeout & plumbed token, StoreLimits, MaxHostCalls (API-side on Jint), separate disposable worker, **distributed** global admission + global egress concurrency, budgeted off-thread transpile, worker-crash→auto-disable (§3.3.1, §4, §5, §7, §9.4) |
| **E**levation | JIT miscompile escape, CLR reflection escape, confused-deputy import | x86_64-Cranelift-only + fast-patch SLA; CLR hard-off; import value-validation (§3, §4, §6) |

> **STRIDE status, as built.** *Satisfied:* Spoofing (BroadcasterId host-derived; the bridge is created per tenant), Elevation-by-CLR (interop hard-off in `JintEngineFactory`), the token-at-rest transplant (§8.4), and the fail-closed engine (§10.4). *Partial:* DoS (Jint statement/time/memory/recursion caps and a wall-clock `CancellationToken`, but in-process — no worker, no global admission, no per-tenant rate buckets), Repudiation (append-only `CodeScriptVersion` + `CompiledHash` exist; the audit events are not published). *Not built:* Wasmtime as the SaaS boundary (harness only), Postgres RLS, the fast-patch SLA tooling, x86_64-only startup assertion (T18). These are recorded as owner questions in the Implementation status table.

### 2.5 Out-of-scope residuals (explicit, per profile)

| Residual | Profile | Why accepted / compensating control |
|---|---|---|
| **Wasmtime JIT miscompile escape** (Cranelift/Winch full bounds-check bypass — CRITICALs landed Apr 2026: RUSTSEC-2026-0095/0096) | SaaS | Accepted residual mitigated by **config-hardening** (x86_64-Cranelift only; Winch + aarch64-Cranelift disabled) + a **mandatory fast-patch SLA** (§5.6). The design keeps the boundary **in-process** (no per-execution Firecracker/gVisor); the in-process boundary is the accepted risk posture (§13 D4). This is the **highest** SaaS residual. |
| **Microarchitectural side channels** (Spectre beyond mitigated patterns, cache/timing, Rowhammer) | SaaS | Out of Wasmtime's guarantee. Accepted low-bandwidth residual; bounded by §10 timing caps + rate limits. Stronger closure (core/SMT pinning, KVM Spectre mitigations) is left to ops hardening, not a structural control (§13 D4). |
| **Jint is not an isolation boundary** | self-host only | Accepted **only** under the single-trusted-operator threat model (the operator authors their own scripts). **MUST NOT** be used multi-tenant. If self-host ever becomes multi-user, Jint is instantly unsafe and must move to Wasmtime + per-tenant process isolation (§3.3). |
| **In-statement constraint bypass** (Jint timeout/memory checked only between statements) | self-host | Compensated by the external wall-clock kill + OS memory cap on the worker (§3.3, §5.5). Single-operator model makes deliberate abuse a non-goal. |
| **Lower-bandwidth covert channels** (chat-output timing, host-call-count timing, error microleaks) | both | Bounded (output size caps, rate limits, generic errors, PII-excluded logs) but not provably eliminated. Monitored via per-tenant denial/usage telemetry (§10), not structurally removed. |
| **Second-order / confused-deputy SSRF** (guest reaches metadata/internal via an allowlisted host that is itself a proxy/URL-fetcher/redirector, e.g. `?url=http://169.254.169.254/...`) | both | The IP-revalidation is on the **egress client**, not the far side of an owner-approved FQDN. Reduced by per-row body/query opt-in + optional path/method scoping (§7.1 step 9) and owner guidance "don't allowlist generic fetch/proxy services" (§7.3); bandwidth metered by the cumulative egress budget (§7.4). Not structurally closed — a deliberately-bad allowlist entry re-opens it. |

---

## 3. Two-executor strategy & WHY

One Application abstraction — **`IScriptExecutor`** (`custom-code.md` §3.1) — two Infrastructure adapters,
profile-selected. The split is deliberate and security-driven, not convenience.

### 3.1 The split

| | **SaaS / `full`** | **self-host / `lite`** |
|---|---|---|
| Adapter | `WasmtimeScriptExecutor` | `JintScriptExecutor` |
| Runtime | wasmtime-dotnet **44.0.0** (`Directory.Packages.props`), **x86_64-Cranelift only** | Jint **4.10.0** (`Directory.Packages.props`; the design text cites 4.9.2 — the shipped package is 4.10.0) |
| Isolation class | **OS-grade memory isolation** — separate linear memory per Store, guest cannot synthesize a host pointer | **In-process** — JS objects are CLR objects on the shared GC heap; JS frames are real CLR frames |
| Threat model | **Untrusted-by-default** (anonymous-ish multi-tenant authors) | **Single trusted operator** (operator authors their own scripts) |
| Is it a security boundary? | **Yes** (memory-safe, deny-by-default) | **No** — a cooperative resource limiter only |
| Profile field | `DeploymentProfileSnapshot.CodeExecutor == wasmtime` | `… == jint` |

> **As built.** `JintScriptExecutor` is registered **unconditionally** as `IScriptExecutor` (Scoped) — on every profile, including one whose snapshot says `wasmtime`. `WasmtimeScriptExecutor` is a **harness that fails closed**: its hardened `Engine`/`Store` isolation is real and tested with raw wasm (`RunHardenedModule`), but `ExecuteAsync` returns `Faulted` because the JS-in-wasm engine module and its ABI are not wired, and the class is not registered in DI. Nothing runs untrusted multi-tenant code today; the only working boundary is Jint's resource limiter, which §3.3 says is acceptable only for a single trusted operator.

### 3.2 WHY two, and WHY these two

1. **SaaS needs a real memory boundary; .NET 10 no longer offers one in-process.** CAS and AppDomains are gone;
   there is no managed way to sandbox untrusted code inside the host process. **WebAssembly via Wasmtime is the
   only memory-safe, deny-by-default boundary available on .NET 10** without standing up a separate runtime per
   call. Each tenant invocation gets its own `Store` (own linear memory, zero-initialized on reuse), the guest's
   only channel out is the host imports we define, and every resource bound is opt-in and enforceable (§4.1).

2. **Self-host is a single operator running their own code** — the multi-tenant adversary does not exist. Standing
   up Wasmtime (native binary, larger footprint) on a hobbyist's box is friction for zero security gain in that
   threat model. **Jint** is a pure-managed interpreter, no native deps, no Docker, trivial to ship. It gives
   **resource-safety** (bounded CPU/memory/recursion against an *accidental* runaway) which is all the
   single-operator model requires. It is explicitly **not** sold as an isolation boundary.

3. **One interface, profile-selected, keeps the call sites identical.** `RunCodeAction`/`IScriptRunner` never know
   which executor they got. The security tier is chosen **once** at boot by the profile registry
   (`platform-conventions.md` §7), so a self-host box cannot accidentally run the weaker engine in a SaaS context.

### 3.3 The honest Jint residual + compensating controls (self-host)

> **Owner question — worker process: not built.** As built, `JintScriptExecutor.ExecuteAsync` runs the script **in the API process**, on the calling thread, with `MaxRecursionDepth = 64`, `MaxExecutionStackCount = 500`, statement/time/memory limits and a linked wall-clock `CancellationToken`. There is no script-worker process, no IPC host-import contract (§3.3.1), no OS limits (§5.5), and a stack-overflow or in-statement OOM would take the API process down. Whether to build the worker (the design's compensating control) or accept the single-operator residual is an open owner decision; nothing below should be read as shipped.

Jint **cannot** contain three things, by construction (in-process on the CLR):

- **`StackOverflowException` is uncatchable in .NET and kills the whole process.** Jint overflows the native stack
  at ~730 nested JS calls (vs ~14k in V8) because one JS call maps to many CLR frames. `MaxRecursionDepth` /
  `MaxExecutionStackCount` *reduce* but do not *guarantee* prevention (observed crash even with a recursion limit
  set, Jint #572).
- **In-statement OOM / CPU bypass.** `TimeoutInterval`/`LimitMemory`/`MaxStatements` are checked **between**
  statements only; a single expression (`new Array(2**31).join('*')`, `a = a.concat(a)` in a loop header) runs
  unbounded to OOM/CPU-pin before any check fires.
- **No syscall filter, no address-space separation, no cgroup** — none of that exists for an in-process
  interpreter.

**Therefore, even in the single-operator model, the host — not Jint — supplies the real boundary.** Mandatory
compensating controls for `JintScriptExecutor` (§5.5 has the numbers):

1. Run script execution in a **separate, low-privilege worker process** (a "script worker") over IPC, supervised
   and auto-restarted, so a `StackOverflow`/OOM crashes **only** the worker.
2. Apply **OS-level limits** to that worker: Linux container/cgroup (`memory.max`, CPU quota, `pids.max`,
   read-only rootfs, dropped capabilities, seccomp) or systemd (`MemoryMax`/`CPUQuota`/`NoNewPrivileges`); on
   Windows a **Job Object** (memory/CPU/process caps). The worker holds **no** ambient authority — no DB creds,
   no network egress, no secrets in its environment — so even a full Jint escape lands in an already-empty,
   network-isolated sandbox.
3. Run the script on a **dedicated thread with a bounded stack size**; treat thread/process death as expected.
4. Enforce a **hard external wall-clock kill** (cancel + kill the worker), since `TimeoutInterval` is unreliable
   mid-statement.

> **Hard constraint (carried from research must-fix #8):** if Jint is ever multiplexed across co-tenants, it must
> be **one confined process per tenant per execution** — never a shared Jint `Engine` between tenants. A shared
> Engine is Jint-as-sole-boundary between co-tenants, an **unaccepted** risk.

#### 3.3.1 Self-host host-import IPC contract (and where `MaxHostCalls` is enforced)

> **As built (no worker, so no IPC):** the Jint engine and the `IScriptHostBridge` share the API process. `MaxHostCalls` is enforced by the `__call` delegate inside `JintScriptExecutor` (`++hostCalls > Budget.MaxHostCalls` → `HostBudgetExceeded`), authoritative because the guest cannot reach the counter. The IPC contract below is design intent that applies only if the worker (§3.3) is built.

§3.3 mandates the worker holds **no ambient authority** — no DB, no egress, no secrets. But the §6.2
side-effecting imports (`chat.send`, `music.queue`, `http.fetch`, `vars.write`) must reach host services that live
in the **API process**. These two facts force an explicit IPC contract (without it, T3's `HostCallCount ≤
MaxHostCalls` is unwritable for the Jint executor, and a worker-side counter would be forgeable by a Jint escape):

- **The worker holds NO host clients.** Each guest `bot.*` side-effecting call inside the worker marshals
  `(capabilityKey, validated args)` over IPC to the **API-process broker dispatch** (the `IScriptHostBridge` of
  §6.4) — the worker is just a transport for primitive-in/primitive-out.
- **The API-process broker** authorizes the call (declared + granted), tenant-scopes it to `grant.BroadcasterId`,
  **increments the authoritative `HostCallCount`**, executes against the host client, and returns the primitive
  value (or `HostBudgetExceeded`).
- **`MaxHostCalls` is enforced API-side.** The worker's own count is **untrusted**; exceeding `MaxHostCalls`
  returns `HostBudgetExceeded` to the worker and the next call is refused host-side. This keeps the §3.3 "empty,
  network-isolated worker" invariant intact (the worker never gains a host client) while making the budget
  unforgeable. The same per-execution `CancellationToken` (§6.4 / §4.1.2) is honored on the API side of each IPC
  round-trip. Test: §12 T3 (host-call budget) is thereby writable for **both** executors.

---

## 4. Isolation guarantees, per executor (exact API knobs)

### 4.1 Wasmtime (SaaS) — memory, CPU/time, capability model

> **As built — harness only.** `WasmtimeScriptExecutor` implements the hardened `Engine` and the per-execution `Store` below and proves them with raw wasm (`RunHardenedModule`, exercised by `WasmtimeScriptExecutorTests`), but `ExecuteAsync` fails closed without dispatching any JS (§3.1 note). Deltas from the text below: `Store.SetLimits(memorySize 64 MiB, tableElements 100_000, instances 1, tables 1, memories 1)` is applied exactly; `store.Fuel` and `SetEpochDeadline(1)` are set, but **no watchdog thread advances the epoch**, so fuel is the only live CPU bound and the wall-clock-including-host watchdog (below) does not exist; `WithBulkMemory(false)` (the design allowed `true` if the JS runtime needs it), `WithRelaxedSIMD(false, false)` is added (relaxed-SIMD depends on SIMD); `WithMaximumStackSize` and NaN canonicalization are **not** set (core defaults stand); nothing is linked (no `DefineWasi`, no `bot.*` imports). No test asserts the `Config` invariants (T18 open).

All knobs below are **opt-in**; an unconfigured Store is a trivial DoS. The `WasmtimeScriptExecutor` constructor
builds **one** hardened `Engine`/`Config` (and compiles+caches each tenant `Module` once); **each execution gets a
fresh `Store`**.

#### 4.1.1 Memory isolation

- **Per-`Store` linear memory**, bounds-checked; guest pointers are offsets, never host addresses. The host managed
  heap is never exposed. **Reused linear memory is zero-initialized** → no cross-tenant residue.
- **Hard cap via `Store.SetLimits`** (wasmtime-dotnet v44 — the single call that maps to Rust
  `StoreLimits`/`ResourceLimiter`):
  ```csharp
  // verified signature (wasmtime-dotnet v44.0.0 src/Store.cs):
  store.SetLimits(
      memorySize:    budget.MaxMemoryBytes,   // hard linear-memory cap (e.g. 64 MiB) — NOT OS-bounded
      tableElements: 100_000,                  // cap indirect-call table growth
      instances:     1,                        // one instance per Store
      tables:        1,
      memories:      1);
  ```
  **Default if omitted:** instances/tables/memories default to **10,000 each** and memory growth is **only
  OS-bounded** → an unconfigured guest `memory.grow`-loops the host to OOM. `SetLimits` is mandatory on every
  untrusted Store.
- **Defense-in-depth (Engine `Config`, Rust-core):** large memory guard region (default 32 MiB on 64-bit) +
  native-stack guard pages catch sign-extension/codegen bugs. `Config.WithMaximumStackSize(...)` caps the wasm
  stack (core default 512 KiB). Where a knob is surfaced through the wasmtime-dotnet binding we set it explicitly;
  where it is not, the hardened Rust-core default stands as the design (it is already defense-in-depth on top of
  the mandatory `SetLimits`/fuel/epoch controls).

#### 4.1.2 CPU / time bounding — **both** mechanisms, layered

Neither fuel nor epoch is on by default. We enable **both**: epoch is the real-time hard kill; fuel is the
deterministic instruction ceiling.

```csharp
// On Config (once, in the executor ctor):
config.WithFuelConsumption(true);     // deterministic instruction metering
config.WithEpochInterruption(true);   // wall-clock kill switch

// Per execution, on the fresh Store:
store.Fuel = (ulong)budget.MaxFuelOrStatements;   // v44: Store.Fuel get/set (NOT AddFuel/ConsumeFuel — doc site stale)
store.SetEpochDeadline(ticksBeyondCurrent: 1);    // trap after the watchdog advances the epoch once
```

- **Fuel** — every instruction costs ~1 fuel; exhaustion traps the guest immediately and **deterministically**
  (same program + same fuel → same trap point). Higher per-instruction overhead.
- **Epoch** — cheaper (~10% slowdown). A **dedicated watchdog thread** holds a min-heap of `(deadlineUtc, Engine)`
  and calls `engine.IncrementEpoch()` when a deadline passes, trapping any guest past its epoch deadline. This is
  the practical runaway kill in the **synchronous** .NET binding (there is no async-yield/cancellation-token path).
- **The wall-clock-including-host watchdog (must-fix #7 / `ScriptResourceBudget.WallClockMs`).** Epoch
  interruption does **not** preempt a guest stuck *inside a host call* (Wasmtime issue #9188 / CVE-2026-27195): a
  tight loop of host calls starves the epoch timer. **Enforcement:** the runner records `startUtc` and registers
  the execution with the watchdog under a **wall-clock** deadline that counts host-call time too. The watchdog,
  on deadline:
  1. advances the epoch (`IncrementEpoch`) to trap any in-wasm code, **and**
  2. signals the host-import dispatcher's per-execution `CancellationTokenSource` so any **in-progress host call**
     (e.g. an HTTP fetch) is cancelled, **and**
  3. if the call still has not returned within a short grace window, abandons the Store (the execution is already
     failed; the Store is discarded, never reused), **and**
  4. on grace-window expiry, **releases that execution's GLOBAL admission permit** (§5.3) and raises an ops alert,
     so a host thread still blocked in managed code cannot pin a permit indefinitely.
  This is the live DoS surface on the SaaS path; the watchdog is the control that closes it.

  **Hard invariant — cancellation is only as strong as the slowest import (closes the synchronous-host-call gap).**
  `IncrementEpoch` traps **wasm** code only; it does **not** preempt the **host** frame currently executing, and
  the wasmtime-dotnet binding is **synchronous** (no async-yield/cancellation path). Signalling a CTS the import
  *ignores* is worthless — a guest looping a host import whose backing service does even a few-ms uncancellable
  synchronous unit (a DB round-trip not wired to the token) can hold the host thread past `WallClockMs`, and under
  the §5.3 GLOBAL semaphore a fleet of tenants each parking one such thread exhausts the global pool and stalls
  **all** sandbox execution (a liveness/DoS break against A5). Therefore **every** `bot.*` host import MUST:
    - (a) be bounded by its **own short internal timeout** (DB `CommandTimeout`, `HttpClient` per-request timeout,
      chat-provider send timeout — provider-agnostic, the same bound for every platform `IChatProvider`) — never rely solely on the watchdog, and
    - (b) **actually plumb the per-execution `CancellationToken`** into the underlying I/O (the DB command, the
      `HttpClient` call, the chat-provider send) — not merely "signal a CTS" the import may drop.
  `MaxHostCalls` is capped (§5.1) such that worst-case `MaxHostCalls × per-import-timeout` stays **below** a hard
  ceiling (< the grace-window-extended `WallClockMs`). A stuck thread that survives the grace window forfeits its
  GLOBAL permit (step 4 above). Test: §12 T3 covers an import whose backing service blocks — it must trip within
  the ceiling and must **not** consume a permit past the grace window.

#### 4.1.3 Capability model — WASI **default-deny**, link nothing

- wasmtime-dotnet exposes **WASI preview1 only** (no preview2/components). A guest reaches the OS **only** through
  WASI imports we link. **We link none of the filesystem/clock/argv surface** — no `DefineWasi()` with fs preopens.
  The guest's *entire* host surface is the **explicit `bot.*` host functions** we define via
  `Linker.Define` / `Function.FromCallback` (§6).
- **Never `WithPreopenedDirectory('/')` or any shared tenant dir.** There are **no WASI fs preopens** in our config
  (§7.4 test asserts this). Result: **zero filesystem, zero ambient OS authority** for the guest.
- **Gate wasm proposals to the minimum** — disable what we don't need (each enabled proposal widens the
  miscompile surface):
  ```csharp
  config.WithWasmThreads(false);        // shared-memory has had unsoundness (RUSTSEC-2025-0118)
  config.WithReferenceTypes(false);
  config.WithSIMD(false);               // CVE-2026-34944 f64x2.splat history
  config.WithMultiMemory(false);
  config.WithBulkMemory(true);          // enable ONLY if the TS→JS runtime needs it; default off
  config.WithCraneliftNanCanonicalization(true);   // determinism — set where the binding surfaces it; core default stands otherwise
  ```
- **Compiler choice is a security control:** `config.WithCompilerStrategy(Cranelift)` — **Cranelift, never
  Winch** (Winch carried CRITICAL escapes Apr 2026). Plus the host pins **x86_64** only (aarch64-Cranelift also
  carried a CRITICAL Apr 2026). §7.5 asserts both at startup.

#### 4.1.4 Wasmtime knob summary

| Concern | Knob | Default if omitted | Our setting |
|---|---|---|---|
| Memory cap | `Store.SetLimits(memorySize:…)` | OS-bounded (DoS) | `budget.MaxMemoryBytes` |
| Table/instance cap | `Store.SetLimits(tables/instances/memories:…)` | 10,000 each | 1/1/1, table 100k |
| Deterministic CPU ceiling | `Config.WithFuelConsumption(true)` + `Store.Fuel` | OFF (infinite loop) | `MaxFuelOrStatements` |
| Wall-clock kill | `Config.WithEpochInterruption(true)` + `Store.SetEpochDeadline` + watchdog | OFF | epoch + `WallClockMs` watchdog |
| Host-call DoS (epoch gap #9188) | wall-clock-incl-host watchdog + `MaxHostCalls` | none | §4.1.2 + §5 |
| Stack cap | `Config.WithMaximumStackSize` | 512 KiB | bounded |
| Filesystem | **omit** `DefineWasi`/preopens | none granted | **none** |
| Proposal surface | `WithWasmThreads/SIMD/…(false)` | varies | minimized |
| Compiler | `WithCompilerStrategy(Cranelift)` + x86_64 host | Cranelift | Cranelift/x86_64 **only** |

### 4.2 Jint (self-host) — memory, CPU/time, CLR hard-off

Jint's defaults are **dangerous on three of four axes**; a hardened factory is mandatory and ad-hoc `new Engine()`
for untrusted input is banned by architecture test (§7.4). One central `JintEngineFactory` builds a **fresh
`Engine` per execution** (never pooled across tenants/requests — prevents prototype-pollution / leaked-global
carryover).

```csharp
// NomNomzBot.Infrastructure.CustomCode.Jint — the ONLY place an untrusted Jint Engine is constructed.
// Verified against Jint Options.cs / ConstraintsOptionsExtensions.cs (v4.9.2 / v4.10.0).
var engine = new Engine(options =>
{
    // --- Resource constraints (best-effort, between-statement; the OS worker is the real bound) ---
    options.MaxStatements((int)budget.MaxFuelOrStatements);   // statement-count cap
    options.TimeoutInterval(TimeSpan.FromMilliseconds(budget.WallClockMs));
    options.LimitMemory(budget.MaxMemoryBytes);
    options.CancellationToken(ct);                            // external kill switch (worker-driven)

    // --- Constraint PROPERTIES — DANGEROUS DEFAULTS, MUST set explicitly ---
    options.Constraints.MaxRecursionDepth     = 64;          // DEFAULT -1 = NO CHECK
    options.Constraints.MaxExecutionStackCount = 500;        // DEFAULT Disabled = no native-stack guard
    options.Constraints.MaxArraySize          = 10_000;      // DEFAULT uint.MaxValue
    options.Constraints.RegexTimeout          = TimeSpan.FromSeconds(1);  // DEFAULT 10s — lower it

    // --- Kill code-from-string (DEFAULT StringCompilationAllowed = TRUE) ---
    options.DisableStringCompilation();                      // no eval() / new Function(code)
    options.Strict();                                        // force strict mode
    // ⚠ DisableStringCompilation must gate ALL FOUR dynamic-function constructors, not just Function/eval.
    //   ECMAScript exposes the dynamic-code constructor off ordinary literals' .constructor chain:
    //     (function(){}).constructor        → Function
    //     (function*(){}).constructor       → GeneratorFunction
    //     (async function(){}).constructor  → AsyncFunction
    //     (async function*(){}).constructor → AsyncGeneratorFunction
    //   Jint has historically tracked these separately. The factory MUST verify (and the factory test assert)
    //   that all four are non-callable / throw under DisableStringCompilation — else `(async()=>{}).constructor
    //   ('return this')()` synthesizes a function from a string and reaches the global object. (CLR escape is
    //   still blocked by AllowGetType=false, so this alone is not RCE — but it defeats the "no code-from-string"
    //   guarantee the save-time forbidden_global static check leans on.)

    // --- CLR INTEROP: HARD OFF. Never call AllowClr(). Keep ALL interop defaults (false). ---
    // Interop.Enabled = false (default). AllowGetType = false. AllowSystemReflection = false.
    // AllowClrWrite — never enabled. No SetTypeResolver, no SetWrapObjectHandler.
});
// Inputs as PRIMITIVES only: engine.SetValue("bot", <value-facade>) where the facade exposes ONLY
// primitive-in/primitive-out delegates (the same broker grant). NEVER SetValue a rich host object/type.
```

**The CLR escape hatch is the catastrophic surface.** `AllowClr()` (or exposing any rich host object/`TypeReference`)
lets script reach arbitrary .NET — `obj.GetType()` → reflection → `System.IO.File` / `Process.Start` → full host
RCE. The rule for untrusted Jint: **interop OFF, expose only primitives.** The capability facade handed to Jint is
the **same broker grant** as Wasmtime (§6) — a set of primitive-in/primitive-out delegates, never a DbContext,
HttpClient, token, or CLR type.

> **As built:** `JintEngineFactory.CreateHardened(budget, ct)` sets exactly the options in the listing above (`MaxStatements`, `TimeoutInterval`, `LimitMemory`, `CancellationToken`, `MaxRecursionDepth = 64`, `MaxExecutionStackCount = 500`, `MaxArraySize = 10_000`, `RegexTimeout = 1 s`, `DisableStringCompilation()`, `Strict()`, interop untouched). A fresh `Engine` is built per execution and the executor itself is Scoped. The tests assert only that `eval(...)` and `(function(){}).constructor('…')()` fail (`Eval_is_blocked`, `The_function_constructor_code_from_string_is_blocked`); the other three dynamic-function constructors and CLR-probe scripts are not asserted (T1 partial).

| Concern | Jint knob | Default | Our setting |
|---|---|---|---|
| CLR access | (do not call `AllowClr`); `Interop.Enabled` | **false** ✅ | stays false |
| `GetType`/reflection | `Interop.AllowGetType` / `AllowSystemReflection` | **false** ✅ | stays false |
| eval/Function **+ Generator/Async/AsyncGenerator function constructors** | `DisableStringCompilation()` (assert all four constructors non-callable) | enabled ❌ | disabled (all four) |
| Recursion | `Constraints.MaxRecursionDepth` | -1 (off) ❌ | 64 |
| Native stack | `Constraints.MaxExecutionStackCount` | Disabled ❌ | 500 |
| Array size | `Constraints.MaxArraySize` | uint.Max ❌ | 10,000 |
| ReDoS | `Constraints.RegexTimeout` | 10s ⚠ | 1s |
| Statements/time/memory | `MaxStatements`/`TimeoutInterval`/`LimitMemory` | none ❌ | from budget |
| **Real boundary** | separate worker process + OS cgroup/Job Object | — | §3.3 / §5.5 — **not built (owner question: worker process)** |

---

## 5. Resource & DoS budgets

The per-execution clamp is **`ScriptResourceBudget`** (`custom-code.md` §4 — **do not** invent a parallel type).
This doc fixes the **default values**, **where each is configured**, and the **kill switch**.

### 5.1 Default budget values (per single `run_code` invocation)

**Posture (binding): safety-first baseline for everyone, then headroom tier-scaled by subscription.** The values
in the table below are the **safety baseline applied to every tenant** — they exist to contain a hostile or runaway
script (A3/A5), and the **baseline safety is never lowered for anyone**. On SaaS, subscription tier grants
**headroom above the baseline** across **three levels — Base / Pro / Premium** — by scaling the *generous-but-still-bounded*
fields (`WallClockMs`, `MaxFuelOrStatements`, `MaxHostCalls`, `MaxEgressBytes`); the safety floors
(`MaxMemoryBytes`, `MaxOutputBytes`, no-fs, no-ambient-authority, the watchdog, the global admission/egress caps,
and every §4/§6/§7 containment control) are **identical** across all three tiers. On self-host the budget is
**host-sized** — bounded by the operator's own `memory.max`/CPU quota (§5.5) rather than a subscription tier — and
the same safety floors apply. Resolution: the per-tenant tier-scaled headroom multiplies the baseline field, clamped
to the hard profile ceiling; the table value is the **Base / baseline** row.

| Field | SaaS (Wasmtime) default | self-host (Jint) default | Maps to |
|---|---|---|---|
| `WallClockMs` | 2000 ms design value — **not wired** (Wasmtime executor is a harness) | **8000 ms as built** (`ScriptResourceBudget.Baseline`, `ScriptContracts.cs`), wall-clock **incl. host calls** | epoch deadline + watchdog / `TimeoutInterval` + the linked `CancellationToken` |
| `MaxHostCalls` | **64** host calls | 64 | per-execution host-call budget. SaaS: dispatcher-incremented `ctx.HostCallCount` in-process. **Self-host: enforced API-side** — the worker's count is untrusted; the API-process broker increments the authoritative count and returns `HostBudgetExceeded` past 64 (§3.3.1) |
| `MaxFuelOrStatements` | 50,000,000 fuel design value — **not wired** | **200,000 statements** (as built, the single `Baseline` value) | `Store.Fuel` / `MaxStatements` |
| `MaxMemoryBytes` | **64 MiB** | 64 MiB (OS `memory.max` is the real cap) | `Store.SetLimits(memorySize)` / `LimitMemory` — **field type MUST be `long`** (see note) |
| `MaxOutputBytes` | **8 KiB** | 8 KiB | **chat** output truncation in the runner (NOT egress) |
| `MaxEgressBytes` | **256 KiB** (cumulative request+response over all fetches in one run) | 256 KiB | per-execution egress counter in the `DelegatingHandler` (§7.4) — **new** `ScriptResourceBudget` field |

> **As built — budgets are compile-time constants.** `ScriptResourceBudget.Baseline` is one `static` record: `WallClockMs 8000`, `MaxHostCalls 64`, `MaxFuelOrStatements 200_000`, `MaxMemoryBytes 64 MiB`, `MaxOutputBytes 8 KiB`, `MaxEgressBytes 256 KiB`. `ScriptRunner` passes it unchanged. There is no per-profile column, no `AppSetting` override and no tier headroom. The wall-clock figure was raised from the 2000 ms design value on 2026-08-26: a real script that sent one chat message and one edge-tts synthesis (≈1089 ms for 28 characters) was killed at 2000 ms with `PartiallyFailed`, because the budget covers every granted host call, not just JS statements; 8000 ms covers one slow synthesis plus one chat send while a genuine runaway is still caught quickly. `MaxMemoryBytes`, `MaxOutputBytes` and `MaxEgressBytes` are already `long` (D8a satisfied). `MaxOutputBytes` is enforced (chat output is truncated in the executor); `MaxEgressBytes` is declared but **not enforced** by anything.

> Fuel-vs-time are independent ceilings: a CPU-heavy script may exhaust fuel before 2 s; a host-call-heavy script
> hits `MaxHostCalls` or the wall-clock watchdog first. All three can trip; whichever trips first wins.

> **Byte-count fields must be `long`, not `int` (type contradiction, coordinated edit to `custom-code.md` §4,
> §13 D8).** `ScriptResourceBudget.MaxMemoryBytes` is currently declared `int` in `custom-code.md` §4, but it is
> fed to `Store.SetLimits(memorySize:…)` (Rust `StoreLimits`, 64-bit byte count) and Jint `LimitMemory(...)` —
> both 64-bit — and its sibling `MaxFuelOrStatements` is already `long`. 64 MiB fits in `int`, but an operator
> tightening/loosening via `AppSetting` (§5.2) or any future cap above ~2 GiB silently overflows or won't compile
> against the long-typed knob, and it forces an `int→long` widening at the one place the cap matters. **Change
> `MaxMemoryBytes` (and `MaxEgressBytes`; `MaxOutputBytes` if it could ever exceed `int`) to `long`** in
> `custom-code.md` §4 to match the runtime signatures. This doc requires `long`; `custom-code.md` declares it.

### 5.2 Where each is configured

> **As built:** the runner takes `ScriptResourceBudget.Baseline` (§5.1) — no tier scaling, no `AppSetting`. The `sandbox_exec_ms` quota is `IScriptExecutionMeter` → `IUsageMeteringService` (metric `sandbox_exec_ms`): `CheckSandboxBudgetAsync` before execution (over limit → `Denied(QuotaExceeded)`), `RecordSandboxUsageAsync` after (skips a 0 ms run; **not** idempotent per `ExecutionId`, not reserve-then-settle). The `HttpEgressAllowlist.MaxRequestBytes` / `AllowRequestBody` columns exist on the entity and **nothing reads them**. **The gate that turns Custom Code on is the per-channel `custom_code` feature toggle** (`FeatureService` catalogue: label *Custom Code*, default **off**): every `/code-scripts` endpoint returns `FEATURE_DISABLED` while it is off (`CodeScriptsController.FeatureGateAsync`), and `ScriptCapabilityBroker.BuildGrantAsync` refuses every capability of a channel with the toggle off. The runner itself does not check the toggle, so a saved script that declares no capabilities can still execute.

- **Per-execution budget** is assembled by `IScriptRunner` from the **safety baseline** (§5.1 table) plus the
  tenant's **subscription-tier headroom multiplier** (SaaS: Base/Pro/Premium; self-host: host-sized, no
  subscription axis) and is **not** author-editable — a script cannot raise its own ceiling. The headroom
  multiplier is resolved from the billing-owned tier (the same `TierLimit` read path used for `sandbox_exec_ms`,
  §5.2 next bullet) and applies **only** to the generous-but-bounded fields; the safety-floor fields are never
  scaled. The resulting per-execution value is clamped to the hard profile ceiling.
- **Per-tenant quota** (`sandbox_exec_ms`) lives in the **billing-owned** tables (`custom-code.md` §1): `TierLimit`
  (`LimitKey = "sandbox_exec_ms"`, `-1 = unlimited`) and `UsageRecord` (`MetricKey = "sandbox_exec_ms"`). The
  sandbox subsystem **reads** `TierLimit` and **increments** `UsageRecord` via `IScriptExecutionMeter`
  (`custom-code.md` §3.3). **No new quota columns are added by this subsystem.**
  - **The gate is reserve-then-settle, not check-then-record** (closes the concurrent-first-run race). A plain
    PRE-run *read* + POST-run *increment* lets up to GLOBAL-concurrency (`min(CPUcount, configured)`) executions
    all pass the PRE-run check simultaneously when remaining quota is ~0 (none has recorded yet), each then burns a
    full `WallClockMs` before any `RecordSandboxUsageAsync` fires — overshooting the ceiling by ~N × `WallClockMs`
    every cycle (a quota/billing bypass and noisy-neighbor lever; idempotency-per-`ExecutionId` stops *replay*
    double-counting but does nothing about *concurrent first-runs*). Therefore admission **atomically debits the
    projected max cost (`WallClockMs`) into an in-flight reservation** (Redis `INCR` / atomic `UsageRecord` delta)
    **before** `ExecuteAsync`, fails closed if the reservation would breach the limit, and **reconciles the
    reservation to actual elapsed** after the run. Test: §12 T14b.
- **Per-tenant egress response cap** is `HttpEgressAllowlist.MaxResponseBytes` (per-FQDN row, §7); the matching
  **request-body cap** is `HttpEgressAllowlist.MaxRequestBytes` (per-row, default a few KiB, §7.1 step 6b) and the
  per-execution **cumulative** clamp is `ScriptResourceBudget.MaxEgressBytes` (§7.4). The two `HttpEgressAllowlist`
  fields (`MaxRequestBytes`, plus the §7.1-step-9 `AllowRequestBody`/`AllowQuery`/path-method-allowlist columns)
  and the `ScriptResourceBudget.MaxEgressBytes` field are **owned by `custom-code.md` §4** — this doc requires
  them; that spec declares them (coordinated edit, §13 D8).
- **Profile defaults** are surfaced as `AppSetting` rows (category `custom_code`) so an operator can tighten (never
  loosen beyond the hard profile ceiling) without a deploy.

### 5.3 Per-tenant rate + concurrency quotas

> **Owner question — quotas: only the `sandbox_exec_ms` row is built** (check-then-record, §5.2). The side-effecting import bucket, the egress rate bucket, the global sandbox admission semaphore, the global egress-concurrency semaphore and the distributed `IRateLimiterPartitionStore` counters described below are **not built**; per-channel pipeline concurrency is the existing `PipelineEngine` cap. Note the egress rows name an `http_request` pipeline action that does not exist (§6.3), so the "two front-ends share one bucket" argument reduces to the callers listed there. Whether and how to build the rest is an open owner decision.

| Quota | Scope | Mechanism | Default |
|---|---|---|---|
| **Side-effecting host-import rate** (non-egress: `chat.*`, `music.*`, `vars.write`) | per tenant | token bucket keyed by `(BroadcasterId, capabilityKey)` in the broker; only descriptors with `SideEffecting == true` count | 20 calls / 10 s burst, 2/s refill |
| **Egress rate** (`http.fetch`; the design also named an `http_request` pipeline action, which does not exist) | per tenant | token bucket keyed by `(BroadcasterId, 'egress')` enforced **inside the shared egress `DelegatingHandler`** (§6.3), **not** the broker — so **both** front-ends are charged to **one** bucket (see note below) | 20 calls / 10 s burst, 2/s refill |
| **`sandbox_exec_ms` quota** | per tenant per billing period | **reserve-then-settle** (NOT check-then-record): `IScriptExecutionMeter` atomically **debits the projected max cost (`WallClockMs`) into an in-flight reservation** at admission (Redis `INCR` / atomic `UsageRecord` delta), fail-closed `BILLING_LIMIT` if the reservation would exceed the limit, then **reconciles the reservation to actual elapsed** POST-run (idempotent per `ExecutionId`) | tier-resolved (`-1` = unlimited) |
| **Per-channel pipeline concurrency** | per channel | existing `PipelineEngine` cap | 5 concurrent |
| **GLOBAL sandbox concurrency / admission control** | whole host (fleet on SaaS) | **new** global semaphore + admission queue (must-fix #7) — a fleet of tenants cannot collectively exhaust the host even though each is under its per-channel 5. **In-process on lite/self-host; distributed via `IRateLimiterPartitionStore` (`sandbox:global:concurrency`) on SaaS** (see note below) | `min(CPUcount, configured)` per the fleet; (N+1)th queued or `RATE_LIMITED` |
| **GLOBAL outbound-egress concurrency** | whole host | **new, separate** semaphore **in the egress `DelegatingHandler`** (§7.1) bounding **total in-flight fetches across all tenants** independently of execution slots — an admitted execution must **not** be able to park an in-flight fetch against a slow allowlisted host for the full `WallClockMs`, exhausting the connection pool / ephemeral ports while other slots starve (slow-loris amplification, the connection-pool analog of #9188) | sized to the connection-pool budget; past it → queue briefly or `Denied(EgressBlocked)` |

The **GLOBAL** limit is the difference between one abusive tenant and a platform-wide outage; per-channel-only is
insufficient for SaaS. The admission gate sits in `IScriptRunner` **before** `ExecuteAsync`.

> **Why egress rate lives in the handler, not the broker (closes the §6.3 two-front-end bypass).** The broker
> only governs the **sandbox `http.fetch`** path; the `http_request` **pipeline action** (T2; *does not exist as built, §6.3*) would not be a brokered
> capability and is never charged against `(BroadcasterId, 'http.fetch')`. If the egress rate limiter sat in the
> broker, an author could drive unlimited egress through a pipeline full of `http_request` steps (or alternate the
> two front-ends) and out-pace the intended per-tenant rate — turning the platform into an SSRF/DoS amplifier. The
> §6.3 shared `DelegatingHandler` is the **single policy owner** both front-ends pass through, so the egress rate
> bucket — keyed by `(BroadcasterId, 'egress')` — is enforced **there**, charging both paths to one budget. Test:
> §12 T15b (N `http_request` steps + M `http.fetch` calls in one run share a single rate budget).

> **Distributed on SaaS — in-process limiters are NOT global across a fleet (closes the multi-instance hole).**
> `platform-conventions.md` §3.7 establishes that **all** rate/limit counters in SaaS must read through
> `IRateLimiterPartitionStore` (Redis-backed) precisely because SaaS runs **K** API instances behind a load
> balancer. A process-local `System.Threading.RateLimiter` / `SemaphoreSlim` sized `min(CPUcount, configured)`
> gives a real ceiling of **K×** the configured limit, and the per-tenant import bucket **resets per instance** —
> an attacker spreads concurrent `run_code` triggers across instances (ordinary LB fan-out, no special effort) and
> collectively exhausts host CPU/threads while **every** per-instance limiter reads "under budget." That is exactly
> "the difference between one abusive tenant and a platform-wide outage" this section names — so the control must
> span the fleet. **Therefore, on the SaaS profile**, the per-tenant side-effecting-import bucket, the
> `(BroadcasterId,'egress')` egress bucket, the GLOBAL admission counter, and the GLOBAL outbound-egress
> concurrency limiter are **distributed counters** resolved through `IRateLimiterPartitionStore` with partition
> keys:
>   - `sandbox:import:{broadcasterId}:{capabilityKey}`
>   - `sandbox:egress:{broadcasterId}`
>   - `sandbox:global:concurrency` (the global admission semaphore is a **distributed** counter in SaaS)
>   - `sandbox:global:egress-concurrency`
> The in-process `RateLimiter`/`SemaphoreSlim` is used **only on lite/self-host** (single process). Test: §12 T15d
> asserts fleet-wide (distributed) enforcement, not single-instance.

### 5.4 Kill switch / circuit breaker

> **As built:** per-execution stop = the Jint `TimeoutInterval` + linked `CancellationToken` (in-process, no worker kill). **Per-script auto-disable and the per-tenant circuit breaker are not built** — the runner records `CodeScript.LastRanAt` / `LastRuntimeError` and never flips `IsEnabled`; only the author does (owner question: quotas). The *global feature kill* is **not platform-wide**: `custom_code` is a **per-channel** feature toggle (§5.2), so disabling it for the platform means disabling it per channel; and `run_code` does not itself fail `FEATURE_DISABLED` — the authoring routes and capability grants do.

- **Per-execution:** the epoch watchdog (SaaS) / external worker kill (self-host) hard-stops a single runaway
  (§4.1.2 / §3.3).
- **Per-script auto-disable:** N consecutive `Faulted`/`Timeout` outcomes within a window flips
  `CodeScript.IsEnabled = false` (recorded in `LastRuntimeError`), and subsequent `run_code` fails closed with
  `Denied(ScriptDisabled)`. The author must re-enable after fixing. Default **N = 5 in 5 min**.
  - **A worker crash MUST count toward this counter (closes the worst-DoS-class gap).** On self-host, a stack-bomb
    or in-statement OOM (the accepted Jint residuals, §3.3) kills the **worker process** — the execution may die
    mid-IPC and never return a clean `Faulted` to the runner. If a crashed worker is **not** mapped to a counted
    `Faulted` for the exact triggering `CodeScriptId`, a script that reliably crashes the worker is **never**
    auto-disabled and keeps being re-triggered on every event/timer, repeatedly killing and respawning the worker —
    a self-sustaining DoS the kill-switch was meant to stop. Therefore `IScriptRunner` **MUST** treat a worker
    **crash / IPC-abort / kill-timeout** as a `Faulted` `ScriptExecutionOutcome` **attributed to the exact
    `CodeScriptId` + `ExecutionId` in flight**, and it counts toward the N-in-window auto-disable. Test: §12 T5b.
- **Per-tenant circuit breaker:** when a tenant's sandbox denial/fault **rate** crosses a threshold, the runner
  short-circuits new executions to `Denied(QuotaExceeded)` for a cool-off and emits an ops notification (§10).
- **Global feature kill:** the `custom_code` feature flag (`platform-conventions.md`) is the platform-wide off
  switch — disabling it makes every `run_code` fail `FEATURE_DISABLED` instantly, no deploy.

### 5.5 Self-host worker OS limits (concrete)

> **Owner question — worker process: not built** (see §3.3). The limits below apply only if a worker exists; today nothing sets a cgroup, Job Object or process cap on script execution.

| Limit | Linux (cgroup v2 / systemd) | Windows (Job Object) |
|---|---|---|
| Memory | `memory.max = 128M` (above the 64 MiB Jint budget for headroom) | `JOBOBJECT_EXTENDED_LIMIT_INFORMATION.ProcessMemoryLimit` |
| CPU | `cpu.max = 50000 100000` (½ core) / `CPUQuota=50%` | `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` |
| PIDs | `pids.max = 16` | `JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 1` |
| FS | read-only rootfs, no preopens | (no host-object surface; n/a) |
| Caps/syscalls | drop all caps, seccomp default-deny, `NoNewPrivileges=yes` | `JOB_OBJECT_LIMIT_BREAKAWAY_OK` off |
| Network | no egress from the worker (egress runs in the **API** process, not the worker) | same |
| Stack | dedicated thread, bounded stack (e.g. 1 MiB) | same |

### 5.6 Fast-patch SLA (SaaS, non-functional requirement)

> **As built:** `Wasmtime` is pinned to `44.0.0` in `server/Directory.Packages.props` (never floated). Because the Wasmtime executor is a fail-closed harness (§3.1), no tenant code runs on it yet; the SLA becomes binding when it is wired.

x86_64-Cranelift is **not** advisory-free (CVE-2026-24116 `f64.copysign`, CVE-2026-34944 `f64x2.splat`,
CVE-2026-27195 `call_async` DoS). The single mitigation for the residual escape class on the mandated backend is
**patch latency**, encoded here as a hard NFR:

- **Subscribe** to GHSA + RustSec advisories for `wasmtime` (automated dependency-advisory alerts in CI).
- **Patch SLA:** CRITICAL/HIGH wasmtime advisory → patched build deployed within **72 h** of disclosure;
  MEDIUM within **2 weeks**. A CRITICAL with an active PoC triggers an immediate `custom_code` feature-flag
  kill (§5.4) until patched.
- **Pin** a `wasmtime-dotnet` version that maps to a patched core; never float.

---

## 6. Capability brokering (the core safety pattern)

**No ambient authority.** The guest has *no* global `fetch`, `eval`, `require`, `process`, `fs`, clock-of-record,
or rich host object. The **only** host surface is the **`bot.*` facade** the broker builds for **one** execution.
A capability is callable **iff** its key is in `CodeScriptVersion.DeclaredCapabilities` **and**
`IScriptCapabilityBroker.BuildGrantAsync` validated it (feature flag + tier + owner approval). An undeclared/
disallowed capability is a **hard deny**, never a default-allow.

The broker and grant types are owned by `custom-code.md` (§3.2, §4: `IScriptCapabilityBroker`,
`ScriptCapabilityGrant`, `ScriptCapabilityDescriptor`). This section specifies **the host-call API surface** and,
for each call, **how the host authorizes + tenant-scopes + audits it, and how the credential never enters guest
memory.**

### 6.1 The three broker invariants (every capability obeys all three)

1. **Intent, not credential.** The guest names an *action* bound to a resource owner it already controls
   (`bot.chat.send("hi")`) — never a token, URL, connection string, or another tenant's id. The host injects the
   pre-authorized client (bound to `grant.BroadcasterId`, host-side only) so **the credential is never serialized
   across the sandbox boundary**.
2. **Tenant from the Store, never from the guest.** The host derives the acting tenant from
   `grant.BroadcasterId` (set host-side, **never readable by user code**). A host import **never** accepts a
   tenant id / broadcaster id / channel id / path / url-to-an-arbitrary-host as a guest argument that selects
   *whose* resource to act on. This closes the confused-deputy class.
3. **Validate every guest value.** Wasmtime/Jint do **not** validate guest-supplied indices/lengths/keys. Every
   host import validates its arguments (length caps, key existence within the tenant's own namespace, enum
   membership) **before** acting. Treat every import as an attacker-controlled syscall.

### 6.2 The host-call API surface (`bot.*` / `nnz.api.*`)

> **Regenerated from `ScriptCapabilityBroker.CatalogEntries`** (`NomNomzBot.Infrastructure/CustomCode/ScriptCapabilityBroker.cs`) — 22 capabilities, every one gated by the per-channel `custom_code` feature (`FeatureFlagKey = "custom_code"`), none `critical`. The guest calls a capability either as `bot.call(key, ...stringArgs)` or through the typed `nnz.api.<group>.<method>(…)` wrappers in the executor's bootstrap (`JintScriptExecutor.Bootstrap`); both resolve to the same broker key. `bot.call` marshals every argument as a string and every return is `string?` (JSON for structured results). Host code: `ScriptHostBridge.Resolve` (`NomNomzBot.Infrastructure/CustomCode/ScriptHostBridge.cs`). Keys with no branch in `Resolve` are a granted **no-op** (return `null`).

Each entry: the guest-visible signature (value-in/value-out), the danger `FloorTier`, the `FeatureFlagKey` gate,
whether it is `SideEffecting` (counts against the per-tenant import rate limit), how the host **authorizes +
tenant-scopes + audits**, and where the **credential** lives.

| Capability key | Guest surface | Tier | SideEff. | Host behaviour as built | Credential location |
|---|---|---|---|---|---|
| `vars.read` | `bot.getVar(key)` | low | no | Served by the executor from the in-memory `Variables` snapshot; **not** routed through the broker (`bot.call('vars.read')` is a no-op) | none |
| `vars.write` | `bot.setVar(key, val)` | low | yes | Written to the executor's variable copy and returned as `VariablesOut`, merged into this pipeline run only; served by the executor, not the bridge | none |
| `args.get` | `bot.args` (array) | low | no | The invocation's args as a JSON array; executor-served | none |
| `user.get` | `nnz.api.user.get(id?)` → object / `null` | low | no | Resolves by login, Twitch id or internal Guid (default: the trigger user); returns the **public profile only** — `id`, `username`, `displayName`, `avatarUrl`, optional 7TV `paint`; no email/IP | none |
| `chat.send` | `nnz.api.chat.send(text)` | tos | yes | `SendChat` → the platform chat provider for **this** `broadcasterId`; text passed as given | bot token — host-side, in the chat provider |
| `chat.reply` | `nnz.api.chat.reply(text)` | tos | yes | Same `SendChat` handler as `chat.send` (no reply-thread linkage as built) | host-side |
| `music.queue` | `nnz.api.music.queue(uri)` → bool | tos | yes | `IMusicService.RequestTrackAsync` for this channel, attributed to the trigger user; refusal → `"false"` | provider token — host-side |
| `music.nowPlaying` | `nnz.api.music.nowPlaying()` → object / `null` | low | no | This channel's current-track snapshot (track, artist, album, durations, `isPlaying`, `requestedBy`, `provider`) | host-side |
| `economy.read` | `nnz.api.economy.balance(userId?)` → number | low | no | This channel's balance for `userId` (a Guid; default the trigger user); a non-Guid or failure → `"0"` | none (host service) |
| `http.fetch` | `nnz.api.http.fetch(url)` → string / `null` | tos | yes | **`https` only, GET only**, via the shared `egress-allowlisted` client (§7); body capped at **256 KiB**; non-2xx or any fault → `null`. **Does not consult `HttpEgressAllowlist`** (owner question: fetch allowlist) | none |
| `storage.get` | `nnz.api.storage.get(key)` | low | no | Per-channel script KV store (`IScriptStorageService`) | none |
| `storage.set` | `nnz.api.storage.set(key, value)` → bool | low | yes | Bounded by the service: key length, **64 KB** value, **200 keys** per channel; an over-cap write changes nothing | none |
| `storage.delete` | `nnz.api.storage.delete(key)` → bool | low | yes | Deletes one key in this channel's store | none |
| `storage.list` | `nnz.api.storage.list(prefix?)` → string[] | low | no | Lists this channel's keys, optionally by prefix | none |
| `tts.speak` | `nnz.api.tts.speak(text, voiceId?, ratePercent?, pitchPercent?)` → `{durationMs,…}` / `null` | low | yes | Routed through the gated TTS dispatcher (channel enable, caps, censor run host-side); rate/pitch are per-call overrides, never persisted; a refusal → `null` | none |
| `widget.emit` | `nnz.api.widget.emit(widget, eventType, data?)` → bool | low | yes | Pushes an event to one of **this** channel's widgets, resolved by case-insensitive name; unknown or disabled widget → refused, nothing pushed | none |
| `reward.get` | `nnz.api.reward.get(idOrTitle)` → object / `null` | low | no | Public projection of one channel-point reward, resolved by id or title | host-side |
| `reward.update` | `nnz.api.reward.update(idOrTitle, patch)` → bool | tos | yes | Patches cost/title through the rewards service (Helix); a non-manageable reward is refused without calling update | host-side |
| `stats.viewer` | `nnz.api.stats.viewer(userIdOrLogin?)` → object / `null` | low | no | The per-viewer M.1 analytics snapshot (default the trigger user); a never-seen viewer gets honest zeros | none |
| `tts.voice.get` | `nnz.api.tts.getVoice(userIdOrLogin)` → object / `null` | low | no | The viewer's assigned voice with its catalogue display name; `null` = channel default | none |
| `tts.voice.set` | `nnz.api.tts.setVoice(userIdOrLogin, voiceId?)` → bool | low | yes | Validated against the voice catalogue, scoped to the bridge's own tenant; an empty voice clears back to the channel default; a rejected voice fails closed | none |
| `schedule.pipeline` | `nnz.api.schedule.pipeline(name, delaySeconds, variables?, dedupeKey?)` → bool | low | yes | Enqueues a saved pipeline (by name) to run once after a delay via the scheduler, for this tenant; a malformed variables payload or unknown pipeline name schedules nothing | none |

**Tier rule:** `FloorTier == critical` capabilities are **never granted to T3** scripts
(`ScriptCapabilityDescriptor.FloorTier`; `BuildGrantAsync` returns `FORBIDDEN`). The catalog above is `low`/`tos` only (`The_catalogue_exposes_no_critical_capability` asserts it); any future `critical` capability
is broker-rejected for `run_code`.

**Notably absent (no ambient authority):** there is **no** `bot.fs.*`, no `bot.exec`, no `bot.eval`, no
`bot.token`, no `bot.db`, no `bot.tenant(id)`, no generic `bot.http` without an allowlist, and **no PII reader**
(viewer email/IP are never in the facade).

### 6.3 Single SSRF-hardened egress client (as built)

> **There is no `http_request` pipeline action.** The design resolved a cross-spec contradiction by giving one egress client two front-ends (an `http_request` action and the `http.fetch` capability). Only one of those exists: nothing in the code registers an action named `http_request`, and the same correction applies to `webhooks.md`. What exists is **one** SSRF-hardened client shared by several callers.

- **The client.** A single named `HttpClient`, `EgressHttpClient.Name` = `"egress-allowlisted"`, registered once in `NomNomzBot.Infrastructure/DependencyInjection.cs`. Its primary handler is `EgressHttpClient.CreateHandler()` (a `SocketsHttpHandler` with a `ConnectCallback`, `AllowAutoRedirect = false`, `ConnectTimeout = 5 s`, `PooledConnectionLifetime = 1 min`) and an outer `EgressSchemeHandler` (a `DelegatingHandler` that rejects any non-`https` request). Location: **`NomNomzBot.Infrastructure/Sandbox/EgressHttpClient.cs`** and **`…/Sandbox/EgressAddressGuard.cs`** (not `…CustomCode.Egress`).
- **Callers (as built):**
  - `ScriptHostBridge.Fetch` — the sandbox `http.fetch` capability (`Infrastructure/CustomCode/ScriptHostBridge.cs`).
  - `OutboundWebhookDispatcher` — signed outbound webhook delivery (`webhooks.md`).
  - `LinkPreviewService` — chat link previews (`Infrastructure/Chat/LinkPreviewService.cs`).
  - `CustomDataEgressFetcher` and `CustomDataPollService` — custom-data pull sources (`Infrastructure/CustomEvents/`).
- **What the client owns:** scheme (https only), no redirects, resolve-then-pin, and the blocked-range guard (§7.1 steps 1, 3, 4, 5). **It does not own the FQDN allowlist** — matching a host against `HttpEgressAllowlist` is each caller's job: `OutboundWebhookEndpointService.CreateAsync` matches at endpoint creation, `CustomDataEgressFetcher` matches on every fetch, and **`ScriptHostBridge.Fetch` and `LinkPreviewService` do not match at all** (owner question: fetch allowlist). It also does not own body/response clamps or rate limits; `ScriptHostBridge.Fetch` applies its own fixed 256 KiB response cap.

### 6.4 Broker interface (referenced, not redefined)

> **As built.** `BuildGrantAsync` performs, per declared key: catalogue membership (unknown → `FORBIDDEN`), `FloorTier != critical`, and the per-channel `custom_code` feature check (off → `FORBIDDEN`), then returns `ScriptCapabilityGrant(BroadcasterId, Granted)`. **It does not check owner-approval rows** (`HttpEgressAllowlist` for `http.fetch`) and **does not publish `ScriptExecutionDeniedEvent`** — `ScriptRunner` maps a failed grant to `Denied(CapabilityDenied)` and records the message in `CodeScript.LastRuntimeError`. The per-execution bridge is a fresh `ScriptHostBridge` built by `ScriptHostBridgeFactory` for one `broadcasterId` (invariant 3 of the list below holds).

The broker is `IScriptCapabilityBroker` (`custom-code.md` §3.2). This doc adds **no** method; it specifies the
**enforcement obligations** of `BuildGrantAsync`:

1. For each declared key: confirm it exists in `Catalog`, its `FeatureFlagKey` is enabled for the channel, its
   `FloorTier != critical`, and (where applicable) owner-approval rows exist (`HttpEgressAllowlist` for
   `http.fetch`). Any failure → `Result.Failure(FORBIDDEN)` (fail-closed) + `ScriptExecutionDeniedEvent`.
2. Bind each granted capability to a **host-side resource owner** keyed by `broadcasterId` (the pre-authorized
   client). The grant handed across the boundary contains **only** value-typed descriptors + the host-side
   dispatch table — **never** a client, token, or `DbContext`.
3. The grant is **per execution** — a fresh closure set bound to one `broadcasterId`; never shared/pooled across
   tenants (a shared closure leaks captured host state).

> **Required boundary type — SATISFIED as built (D9).** `HostImportDelegate(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)`, `IScriptHostBridge.Resolve(key)` and `IScriptExecutor.ExecuteAsync(request, grant, bridge, ct)` all exist in `ScriptContracts.cs` exactly as specified below; `ScriptCapabilityGrant` stays a pure value record. The text that follows is the original argument, kept for the record.
>
> **Required boundary type — the load-bearing seam (was: currently missing; critical, coordinated edit to
> `custom-code.md` §3.1/§4, §13 D9).** This doc repeatedly says the grant "contains the host-side dispatch table"
> the guest's `bot.*` calls invoke — but `ScriptCapabilityGrant` as declared in `custom-code.md` §4 is
> `(Guid BroadcasterId, IReadOnlyList<ScriptCapabilityDescriptor> Granted)` and a descriptor is
> `(string Key, string FloorTier, string FeatureFlagKey, bool SideEffecting)` — **pure value types, zero
> delegates**. There is **no member the `Linker.Define` / `engine.SetValue("bot", …)` callback can call to reach
> the host**, so as written **not a single host import is wireable**. The contract must carry a non-serialized
> dispatch surface. **Resolution (this doc specifies the seam; `custom-code.md` declares the types):**
>   - Define a host-import delegate with a **primitive-in / primitive-out** signature (it is the trampoline the
>     `Linker`/`SetValue` binds to):
>     ```csharp
>     // NomNomzBot.Application.Contracts.CustomCode
>     public delegate string? HostImportDelegate(
>         string capabilityKey, IReadOnlyList<string> args, CancellationToken ct);
>     ```
>   - Hand the executor the dispatch surface via **`IScriptHostBridge`** as a **separate parameter** on
>     `IScriptExecutor.ExecuteAsync(ScriptExecutionRequest request, ScriptCapabilityGrant grant,
>     IScriptHostBridge bridge, CancellationToken ct)` — the chosen shape, because it keeps `ScriptCapabilityGrant`
>     a pure value type. The bridge exposes `HostImportDelegate Resolve(string capabilityKey)` (or the dispatch
>     map). **This member never crosses the sandbox memory boundary** — it is the host trampoline the
>     `Linker.Define`/`SetValue` callbacks invoke from the host side; only primitives flow into and out of the
>     guest.
>   - The `CancellationToken` passed to every `HostImportDelegate.Invoke` is the **per-execution** token the §4.1.2
>     watchdog cancels, satisfying the §4.1.2 plumb-the-token invariant.
> `custom-code.md` defines this seam (the `IScriptHostBridge` parameter shape) once; this doc consumes it
> unchanged. This is a **hard prerequisite**: the `bot.*` facade is un-implementable until the seam lands, so it is
> a dependency on the `custom-code.md` §3.1/§4 boundary-type edit, sequenced with §8.4 (§13 D9).

---

## 7. Network egress (SSRF + cloud-metadata defense)

**Deny-by-default.** A script cannot reach the network at all unless `http.fetch` is granted **and** the target
FQDN is an enabled `HttpEgressAllowlist` row for the tenant. This is the canonical OWASP SSRF control set.

### 7.1 The egress `DelegatingHandler` (single owner, §6.3)

> **As built — status of each step.** Built: step 1 (`EgressSchemeHandler`), step 3 (resolve-then-pin `ConnectCallback`), step 4 (`EgressAddressGuard`, minus multicast and the metadata hostnames), step 5 (`AllowAutoRedirect = false`). **Not built in the client:** step 2 (FQDN allowlist — callers only, §6.3), step 6 (per-row `MaxResponseBytes` — `ScriptHostBridge.Fetch` uses a fixed 256 KiB), step 6b (request-body clamp — `http.fetch` sends GET with no body), step 7 beyond `ConnectTimeout = 5 s` (no response-header timeout, no `MaxConnectionsPerServer`, no global egress-concurrency semaphore), step 8's header allowlist (the client attaches no credentials; the only caller that sets headers is the trusted webhook dispatcher), step 9 (`AllowRequestBody`/`AllowQuery`/path-method columns are unread). Steps 2, 6, 6b, 7 and 9 are *owner questions* (fetch allowlist, quotas). The two-front-end wording below refers to `http_request`, which does not exist; read "every outbound request" as "every request through the `egress-allowlisted` client" (§6.3 callers).

Pipeline the design applies to **every** outbound request through the client:

1. **Scheme allowlist:** **`https` only.** Reject `http://`, `file://`, `gopher://`, `ftp://`, `data:`, `ws://`.
2. **FQDN allowlist match** *(design; not in the client as built — callers match, §6.3; `http.fetch` does not, owner question: fetch allowlist)*: the host must equal an enabled `HttpEgressAllowlist.Fqdn` for **this** tenant
   (exact match, no wildcard, no suffix tricks). Miss → `Denied(EgressBlocked)`.
3. **Resolve-then-pin (DNS-rebind / TOCTOU defense, must-fix #6) — built.** The pin cannot live in a `DelegatingHandler` (it runs above the connection pool, and the inner `SocketsHttpHandler` re-resolves at connect time), so it lives in the inner handler's **`ConnectCallback`** (`EgressHttpClient.ConnectValidatedAsync`).
   - **As built:** the callback (a) resolves the target host **once** with `Dns.GetHostAddressesAsync` (an empty result throws `Egress host did not resolve.`), (b) runs `EgressAddressGuard.IsBlocked` on **every** resolved address and throws `HttpRequestException("Egress to a non-public address is blocked.")` if **any** is blocked (fail-closed against split-horizon answers), then (c) opens a plain TCP `Socket` to the **first** validated address (`resolved[0]`, `NoDelay = true`) and returns a `NetworkStream` over it. The hostname is never re-resolved at connect time.
   - **TLS as built:** the callback returns the raw stream; it does **not** wrap it in an `SslStream`. `SocketsHttpHandler` layers TLS over the returned stream itself for `https`, using the **request's original host** as the TLS target host — so SNI and certificate/SAN validation run against the FQDN, not the pinned IP. `CreateHandler` sets **no** `SslOptions` and no `RemoteCertificateValidationCallback`, so default certificate validation stays on. The forbidden practices in the next bullet are not present in the code, but **no test asserts it** (T7/T7b open).
   - **Forbidden:** rewriting the request URI to `https://<pinned-ip>/path` (breaks SNI + SAN → pressures a validation bypass), and **any** `SslClientAuthenticationOptions.RemoteCertificateValidationCallback` / `SocketsHttpHandler.SslOptions` override that returns `true`/weakens validation. A cert mismatch is a hard failure, never a bypass.
   - *Design text superseded by the above:* the original spec had the callback take a pre-validated IP from per-request state and build the `SslStream` itself with `AuthenticateAsClientAsync(TargetHost = FQDN)`. The as-built callback re-derives the IP from DNS inside the callback (resolve and validate in one place), and leaves TLS to the handler.
4. **Resolved-IP re-validation (block internal/link-local/metadata) — built, with one gap.** `EgressAddressGuard.IsBlocked(IPAddress)` is pure (no DNS). An IPv4-mapped IPv6 address is first normalized with `MapToIPv4()` so `::ffff:169.254.169.254` cannot smuggle a blocked v4; an unknown address family is blocked. **Blocked as built:** IPv4 `0.0.0.0/8`, `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16` (incl. `169.254.169.254`), `100.64.0.0/10` (CGNAT), and the limited broadcast `255.255.255.255`; IPv6 `::1`, `::` (unspecified), `fc00::/7` (ULA) and `fe80::/10` (link-local). **Gap — multicast is not blocked** (`224.0.0.0/4`, `ff00::/8`); tracked as **S-EGRESS-GUARD-MULTICAST**. The metadata **hostnames** in the original list (`metadata.google.internal`, `metadata.azure.com`, `metadata.aws`) are not blocked by name — they are caught only if they resolve to a blocked address. A blocked address surfaces as an `HttpRequestException` (the callers treat it as a failed fetch), not as a `Denied(EgressBlocked)` outcome or event. Tests: `EgressAddressGuardTests` (internal/metadata addresses blocked, public allowed), `EgressSchemeHandlerTests` (https-only).
5. **No redirects:** `HttpClientHandler.AllowAutoRedirect = false`. A `3xx` is returned as-is to policy, **never
   followed** — a redirect cannot bounce a validated host to an internal one. (If a front-end wants to follow a
   redirect, it must re-submit the new URL through steps 1–4.)
6. **Response-size cap:** stream the body; abort once `HttpEgressAllowlist.MaxResponseBytes` (per-row) is exceeded
   → truncated/`Faulted` with a size error. Prevents memory-bomb-via-download.
6b. **Request-body-size cap (exfil clamp, finding §7.4):** the response cap does **nothing** about *outbound*
   bytes — the guest reads everything it legitimately holds (`bot.vars.read` over the whole bag, `bot.args`,
   `bot.economy.balance`, accumulated results), concatenates, and `POST`s it. The handler **clamps `init.body`**
   to a small per-row/per-execution ceiling — a new `HttpEgressAllowlist.MaxRequestBytes` (mirroring
   `MaxResponseBytes`, **default a few KiB**); over it → `Denied(EgressBlocked)` (reject, do not silently
   truncate). This applies to **every caller** of the client (design text named two front-ends, the `http_request` action — which does not exist — and `http.fetch`).
7. **Per-request + connect/header timeouts (slow-loris defense):** the **full** request is bounded by the
   remaining `WallClockMs` budget (the watchdog also cancels the in-flight call, §4.1.2), **but** a slow responder
   could otherwise hold a socket for nearly the whole 2 s. So the `egress-allowlisted` `SocketsHttpHandler` also
   sets an **aggressive `ConnectTimeout`** and a short **response-headers timeout** (`HttpClient.Timeout` /
   per-request response-read), and caps **`MaxConnectionsPerServer`**, so a slow-loris responder cannot park a
   connection for the full budget or monopolize the pool. Combined with the **GLOBAL outbound-egress concurrency**
   semaphore (§5.3), total in-flight fetches across all tenants stay bounded independently of execution slots.
   Test: §12 T15c.
8. **No credential attachment:** the handler attaches **no** `Authorization`/cookies/host secrets; the guest
   cannot inject auth headers (the `init` object's header set is allowlisted to a safe subset — `Accept`,
   `Content-Type` — never `Authorization`/`Cookie`). Egress carries **no** ambient authority.
   > **Per-caller header/body policy (not hard-wired in the handler).** This `Accept`/`Content-Type` allowlist and the
   > step-9 body opt-in are the **guest** policy, selected by the *untrusted* front-end (`http.fetch`; the design's `http_request` does not exist).
   > A **trusted first-party caller** (`webhooks.md`'s outbound dispatcher) traverses the **same** `ConnectCallback`
   > SSRF core but under a **trusted policy** that injects server-controlled signature/`webhook-*` headers + a
   > webhook-sized body (its H.7 row sets `AllowRequestBody=true`, `MaxRequestBytes`=256 KiB). The policy is a
   > **caller-selected parameter**, never reachable from across the sandbox boundary — the guest can never select it.
   > (`webhooks.md` §8.1 specifies this; an architecture test asserts the webhook path uses this same handler.)
9. **Request-body + query opt-in (second-order SSRF reduction, finding §7.3):** the guest **cannot** attach a
   request body or an arbitrary query string unless the `HttpEgressAllowlist` row **opts in**
   (`AllowRequestBody` / `AllowQuery` flags). A row may optionally carry a **per-row path + method allowlist**, so
   an approved FQDN can be scoped to specific safe endpoints rather than arbitrary `path+method+body`. This shrinks
   the confused-deputy surface (§7.3) — an allowlisted image-resizer/link-preview/proxy cannot be driven with a
   `?url=http://169.254.169.254/...` query unless the owner explicitly opened that door.

### 7.2 Why each control

> As built: the rows for https-only, resolved-IP re-validation (except multicast, S-EGRESS-GUARD-MULTICAST), resolve-then-pin and no-redirects are implemented; the FQDN allowlist, size caps and body/query opt-in rows are not enforced by the client (§7.1 status note).

| Control | Threat closed |
|---|---|
| FQDN allowlist (deny-by-default) | Arbitrary-*direct*-destination data exfiltration (NOT second-order via an allowlisted proxy — see §7.3) |
| Resolved-IP re-validation incl. `169.254.169.254` | **Cloud-metadata IAM credential theft** *via the egress client itself* (the #1 cloud-sandbox escape) + direct internal-service SSRF (NOT confused-deputy reach through an allowlisted host — §7.3) |
| Resolve-then-pin via `ConnectCallback` (TLS `TargetHost` = FQDN) | DNS-rebind TOCTOU bypass of the allowlist — **and** the TLS-bypass trap (URI-rewrite-to-IP breaking SAN, then disabling cert validation) |
| No redirects | `3xx` bounce from a validated host to an internal one |
| Response-size cap | Memory-exhaustion via large download |
| https-only + no-credential | Plaintext interception; token smuggling via headers |
| Request-body/query opt-in + per-row path/method allowlist (§7.1 step 9) | Reduces **second-order/confused-deputy SSRF** through an allowlisted fetch/proxy/redirector (§7.3) — not fully closed |

### 7.3 Residual: second-order (confused-deputy) SSRF through an allowlisted host

The §7.1 IP-revalidation + no-redirect controls stop the **egress client itself** from touching
`169.254.169.254`/RFC-1918. They do **not** stop the *far side* of an allowlisted FQDN from doing so on the
guest's behalf. The allowlist is per-FQDN and owner-approved, but the guest controls the full path/query/body and
receives the response body back (capped at `MaxResponseBytes`). If an owner allowlists **any** host with a
server-side-request feature — an open proxy, a URL-fetcher, a webhook-echo, an image-resizer that fetches a
user-supplied `?url=`, a "preview this link" API, or a corporate proxy the box can reach — the guest sends
`?url=http://169.254.169.254/latest/meta-data/...` to the *allowlisted* host and the metadata/internal content
comes back in the response body. This is a **confused-deputy SSRF the egress hardening cannot see** (the dangerous
fetch happens on the far side of an allowed FQDN), one hop removed from the A1/A3 metadata-theft class.

- **Explicit residual** (also recorded in §2.5): owner-approved allowlist rows are trusted; a deliberately-bad
  allowlist entry (a generic fetch/proxy/redirector) re-opens internal reach. The sandbox cannot prove an
  arbitrary allowlisted host is not a proxy.
- **Structural mitigation** (§7.1 step 9): body/query are **opt-in per row** and a row may pin a path+method
  allowlist, so an FQDN can be scoped to specific safe endpoints. **Owner guidance (must surface in the allowlist
  UI):** do **not** allowlist generic fetch/proxy/redirector/link-preview services.
- **Containment that still holds:** the `http.fetch` response body is the **only** channel back to the guest (it
  already is), so the exfil bandwidth is metered, rate-limited (§5.3), and clamped by the cumulative egress budget
  (§7.4). Test: §12 T6b.

### 7.4 Egress byte budgets (outbound exfil clamp)

> **As built: neither clamp is enforced.** `HttpEgressAllowlist.MaxRequestBytes` is unread and `ScriptResourceBudget.MaxEgressBytes` is declared (256 KiB) but not counted by any handler. The guest's only egress today is `http.fetch`, a body-less GET, so the outbound-exfil path the clamps target is bounded by the request URL (which the guest controls) and `MaxHostCalls = 64`. Owner question (fetch allowlist / quotas).

The single-call request-body cap (§7.1 step 6b) is **not enough** on its own: with `MaxHostCalls = 64`, a script
can ship up to 64 capped `POST`s per run, and the per-run loop repeats across triggers. The `MaxOutputBytes` budget
(§5.1) throttles **chat** output only — it never touched egress. Two clamps, both owned by the egress handler:

1. **Per-call:** `HttpEgressAllowlist.MaxRequestBytes` (per-row, default a few KiB) caps a single request body
   (§7.1 step 6b).
2. **Per-execution cumulative:** a new `ScriptResourceBudget.MaxEgressBytes` (sum of **request + response** bytes
   across **all** fetches in one run) is enforced in the `DelegatingHandler` against the runner's per-execution
   egress counter; exceeding it → `Denied(EgressBlocked)`. Default sized to the legitimate workload (e.g. a few
   hundred KiB), well below what a structured-exfil loop needs.

`T8` is extended to assert that data the guest **does** legitimately hold cannot be shipped past the cumulative
egress cap (the original T8 only reasoned about secrets the guest *never* had).

---

## 8. Secrets & tenant isolation

### 8.1 Secrets are unreachable from the guest

- **No host import returns or accepts a token / secret / connection-string-shaped value.** Every `bot.*` argument
  and return is a value type from `ScriptInputs` / `ScriptCapabilityDescriptor` (string/int/bool/struct) — never a
  `CipherPayload`, `HttpClient`, `DbContext`, or OAuth token (§6 table "Credential location" = host-side for all).
- The broker injects **pre-authorized clients** host-side (§6.1); the bearer of authority lives in host memory and
  is never serialized into the Store/Engine.
- **No fs (§4.1.3 / §4.2)** → the guest cannot reach the OS-native key store (`OsSecureStoreKeyVault`) or its
  encrypted-file KEK fallback or any on-disk secret.

### 8.2 The executor binds to exactly one `BroadcasterId`

- `ScriptCapabilityGrant.BroadcasterId` is **host-derived** (from `ICurrentTenantService` ← JWT `sub`, never a
  DTO/route/query — IDOR fix #1) and **never readable by user code**.
- The grant's host-side dispatch table is a **fresh closure set bound to that one `broadcasterId`**. Every host
  import acts on that tenant's resources only; the guest cannot name another.
- A **fresh `Store`/`Engine` and fresh grant per execution** ensures no host state (or another tenant's binding)
  is captured across executions.

### 8.3 Cross-tenant data access is impossible (defense-in-depth, not just discipline)

> **Owner question — Postgres RLS: not built.** As built the boundary and service layers hold (`ScriptHostBridge` is constructed for one `broadcasterId`; `CodeScript` is `ITenantScoped` with a global query filter, and the script KV store is keyed by tenant — `Storage_is_tenant_isolated_channel_b_cannot_read_channel_a` proves it). The **Database** row below — `SET app.tenant_id` per connection, `TenantRlsConnectionInterceptor`, RLS policies — does not exist in the code, and there is no architecture test banning `IgnoreQueryFilters()`. A SQLite deployment (the SelfHostLite default) has no RLS at all.

| Layer | Control |
|---|---|
| Boundary | Guest cannot name a tenant; BroadcasterId host-only (§8.2) |
| Service | All sandbox-reachable host services run under the **`ITenantScoped` global query filter** bound to `CurrentTenantService` |
| Database | **Postgres RLS** (`SET app.tenant_id` per connection, `TenantRlsConnectionInterceptor`) — cross-tenant reads fail even if a service forgets `.Where(BroadcasterId==…)` |
| Process | `IgnoreQueryFilters()` is **banned** outside the GDPR re-encryption path (architecture test) |

### 8.4 Live-code dependency (ordering blocker) — **all three satisfied**

The broker model was void until three live defects were fixed. **`run_code` was not to ship until they landed; they have:**

1. **Token-at-rest crypto — SATISFIED.** The legacy `EncryptionService` (AES-256-**CBC**, `SHA256(rawKey)`, no MAC, no AAD, one platform-wide key) is gone: no such class and no `CipherMode.CBC` remain in `server/src`. Secrets are sealed by `ITokenProtector` (`Infrastructure/Platform/Security/TokenProtector.cs`) over `AesGcmFieldCipher` — **AES-256-GCM**, 96-bit random nonce, 128-bit tag, with `CipherAad` (tenant / provider / token type / key version) bound as associated data, and a per-subject DEK (`gdpr-crypto.md`). A ciphertext copied to another tenant's row no longer authenticates.
2. **`Configuration.SecureValue` — SATISFIED.** Platform client secrets are written through `ITokenProtector.ProtectAsync` (`SystemController`), not stored as plaintext.
3. **Fail-closed `PipelineEngine` — SATISFIED.** See §10.4.

`custom-code.md` and `platform-conventions.md` own (1)/(3) and the IDOR/RLS fixes respectively. Brokering tokens host-side is therefore no longer unsafe on account of transplantable token crypto. (RLS, §8.3, remains an owner question.)

### 8.5 PII never crosses the boundary

> **Owner question — PII allowlist: not built.** As built, `RunCodeAction` builds `ScriptInvocation` with `new Dictionary<string,string>(ctx.Variables)` and `ScriptRunner` forwards it into `ScriptInputs.Variables` **unfiltered**: whatever template-variable namespaces the pipeline resolved into `ctx.Variables` (including any `user.*` beyond name/id) reach the guest through `bot.getVar`. What *is* true: `user.get` returns the public profile only, `TriggeredByUserId` is the internal id, and no capability returns email or IP. The default-deny prefix allowlist below, and test T9b, are design intent whose adoption is an open owner decision.

`ScriptInputs` deliberately carries an **internal guid** `TriggeredByUserId` (not Twitch user id/email/IP) and a
display name. **The `Variables` snapshot handed to the sandbox must be the PII-scrubbed set** — display name only,
no email/IP — even though the template-variable system (`commands-pipelines.md` §6.3) can resolve richer
`{{user.*}}` values into `ctx.Variables`. The runner **filters** the variable bag to the PII-safe subset before
building `ScriptInputs` (§10.3 logging rule mirrors this).

> **The filter predicate is concrete — `ctx.Variables` is an untyped `string→string` namespaced map, so "PII-safe"
> must be enumerated or the filter is unimplementable (and T9, which is cross-tenant only, never tests it).** The
> runner applies a **default-deny prefix allowlist**: only these namespaces cross into `ScriptInputs.Variables` —
>   - `args.*` (positional command args),
>   - `channel.*` (title, game, category — channel-public, no viewer PII),
>   - `user.name` (display name) and `user.id` (the **internal** guid `TriggeredByUserId`),
>   - author-set **custom** variables (the `var.*` / channel-custom namespace the author themselves writes via
>     `bot.vars.write` / the dashboard).
>   **Everything else is dropped.** In particular the PII-bearing template keys `commands-pipelines.md` §6.3
>   resolves from Twitch — `user.email`, `user.ip`, raw `user.twitchId`, and any `{{user.*}}` beyond `name`/`id` —
>   are **never** in the allowlist and never reach the guest. (Allowlist, not denylist, so a future PII template
>   key fails closed by default.) Test: §12 T9b asserts a known-PII variable key (e.g. `user.email` seeded into
>   `ctx.Variables`) is **absent** from `ScriptInputs.Variables`.

---

## 9. Script lifecycle & authz (security view)

The lifecycle entities/services are owned by `custom-code.md` (§1, §3.4); the **security-relevant** properties:

### 9.1 Immutable, versioned, tamper-evident

- `CodeScriptVersion` is **APPEND-ONLY** (unique `(CodeScriptId, Version)`, no `UpdatedAt`/`DeletedAt`); a
  correction is a **new version**. `CompiledHash` (SHA-256 of `CompiledJs`) is the per-unit cache key and proves
  **exactly which bytes ran**.
- `CodeScript.CurrentVersionId` is the active-version pointer → hot-swap with no restart; **rollback** =
  `PublishVersionAsync` repointing to a prior **valid** version.

### 9.2 Validate-on-save (rejection at authoring time, never mid-stream)

> **Owner question — TS transpile: not built.** As built, `CompileAsync` is JavaScript-only: `Engine.PrepareScript(source)` (parse, never execute) rejects syntax errors as `VALIDATION_FAILED`; then it computes `CompiledHash` (SHA-256 of the source, hex) and `DeclaredCapabilities` by **regex** over `bot.call("key", …)` and `nnz.api.<group>.<method>` — `CompiledJs` equals the submitted source. There is no TypeScript transpile, and no `forbidden_global` / `undeclared_capability` static check (an undeclared capability is caught only by the runtime broker). Relative `import` statements in a multi-file project are resolved and flattened before `CompileAsync` (`CodeScriptService`, `ScriptImportResolutionTests`); an unresolvable import persists no version. A rejected version is persisted and `run_code` on it fails closed (`VersionInvalid`), as designed.

`CompileAsync` (deterministic, side-effect-free, **never executes user code**) transpiles TS→JS, computes
`CompiledHash`, statically extracts `DeclaredCapabilities`, and sets `ValidationStatus = valid|rejected`. A
rejected version is **persisted for audit** but `CurrentVersionId` stays null → `run_code` on it fails closed
`Denied(VersionInvalid)`. Static validation also rejects **forbidden globals** (`forbidden_global`) and
**undeclared capabilities** (`undeclared_capability`) — see `ScriptValidationError.Code` (`custom-code.md` §4).

> **Static validation is best-effort; the runtime broker is the real boundary.** The `forbidden_global` /
> `undeclared_capability` static check cannot see a **string-built reference** (a capability name assembled at
> runtime, e.g. via a dynamically-constructed function — see the §4.2 four-constructor note). It is a fast
> save-time reject, **not** the security boundary. The boundary is the **runtime deny-by-default broker**: a
> capability is callable **iff** its key is in `DeclaredCapabilities` *and* `BuildGrantAsync` validated it
> (§6) — an undeclared/dynamically-named capability call **fails closed** at runtime (`Denied(CapabilityDenied)`,
> T12) even if static analysis was evaded. The two layers are belt-and-suspenders; the runtime layer is
> load-bearing.

### 9.3 The single authz gate — Broadcaster-floor, per-user delegable only

> **As built differs (owner question — author floor).** `ActionDefinitionSeeder` seeds `code:script:author` with a **Moderator** floor, `DangerTier.Critical`, and **`IsGrantableViaPermit = false`**; the seeder comment records that the owner asked for moderators to be able to author scripts on channels they do not own, and that a broadcaster may still raise the requirement through a `ChannelActionOverride`. Every `/code-scripts` route carries `[RequireAction("code:script:author")]` and, in addition, the per-channel `custom_code` feature toggle (`FEATURE_DISABLED` while off). The Broadcaster(40) floor, per-user `!permit` delegation and role-tier override rejection described below (and D1, T17) are the design; which of the two floors is right is the owner's call.

All eight `/code-scripts` endpoints (reads included) enforce **`code:script:author`** — `Plane=management`,
`FloorLevel=Broadcaster(40)`, **`FloorTier=critical`**, **`IsGrantableViaPermit=true`** (Broadcaster-delegable as a
per-user `!permit` capability grant only) — behind `FeatureFlag custom_code` (`custom-code.md` §5). Authoring/running
sandboxed code is a channel-**owner** capability by default; the Broadcaster is fully trusted over their own channel
and **MAY delegate it to a named individual user** via `!permit @user code:script:author`. It is **never** role-tier
delegable: the `Broadcaster(40)` floor blocks any `ChannelActionOverride` from dropping it onto an Editor or lower
tier (`SetActionOverrideAsync` rejects below-floor), and the no-escalation guardrail keeps the grantor at/above the
action — so only the Broadcaster can issue this Critical grant, and only to one named user at a time
(`roles-permissions.md` §0.2/§3.6). **The seed row must ship** — a missing `ActionDefinitions` row makes the
resolver fail closed and **every** `/code-scripts` call 403 (a fail-closed but broken state). Tenant comes from the
principal, never the route (IDOR fix #1).

> **Plane placement (decided):** authoring/running sandboxed code is placed on the
> `management / Broadcaster(40) / critical / IsGrantableViaPermit=true` plane. This is the design; it keeps the
> capability owner-floored while letting the fully-trusted Broadcaster delegate it to a specific named user (never a
> whole role tier). Recorded in §13 D1.

### 9.4 Transpile surface (security framing)

> **Owner question — TS transpile: not built** (§9.2). With no TypeScript step there is no transpiler engine parsing untrusted source, so the API-thread transpile DoS surface this section closes does not currently exist; `Engine.PrepareScript` (a parse) runs on the API request thread inside `CompileAsync` and its cost is bounded only by the request size. T21 is open.

The TS→JS transpiler inside `CompileAsync` is a large JS program (`tsc`/`sucrase`/`babel-standalone`) that
**parses fully attacker-controlled source** — so although `CompileAsync` "never executes user code," it *does* run
untrusted-influenced code on a JS engine. **The design is build-time / committed-artifact transpilation** (decided
below), so no in-process JS engine parses untrusted source on the API request thread. The framing here is a
containment surface, not a free pass:

- **The transpiler-engine config is NOT the execution-engine config, and the difference is dangerous.** The
  execution Jint engine sets `DisableStringCompilation()` (kills `eval`/`Function`) and tight
  recursion/statement caps; a real TS transpiler frequently needs `Function`/`eval` and deep recursion to run at
  all, so its engine **must** be configured more permissively. If the looser transpiler engine ever parses
  adversarial source, a crafted string can drive the parser into eval-reachable paths, a parser-level ReDoS, or a
  native-stack overflow — and `CompileAsync` is a **save-time REST call on the API thread**, *not* the IPC worker
  of §3.3, so a `StackOverflowException` there kills the **whole API process** (platform DoS from an authenticated
  save, no execution needed).

**Required model:**

1. **Build-time / committed-artifact transpilation is the design.** Untrusted TS is transpiled in a build step and
   `CompiledJs` is a committed artifact, so **no in-process JS engine ever parses untrusted source on the API
   thread.** This removes the entire surface and is the chosen approach.
2. **Confined-runtime transpile is the sanctioned fallback** for any path that must transpile at runtime:
   `CompileAsync` runs the transpiler **inside the same confined unit as execution** — the §3.3 Jint **worker
   process** (self-host) or a **fresh Wasmtime `Store`** (SaaS) — **never on the API request thread**. It gets its
   **own** `ScriptResourceBudget` (statement/time/memory/recursion caps + a **regex/parse timeout**), and its
   transpiler-engine config is **pinned and arch-tested** (string-compilation choice, `MaxRecursionDepth`,
   `RegexTimeout`). A transpile that exceeds budget yields a bounded `ValidationStatus = rejected`
   (`VALIDATION_FAILED`), and the API process survives.

Either way `CompileAsync` stays deterministic (stable `CompiledHash`), gets **no** capability grant, **no** host
imports. **Test (new §12 T21):** a save with adversarial source (deep nesting, catastrophic-backtracking regex,
10 MB body) yields a **bounded** `VALIDATION_FAILED` and the **API process survives** (no crash, no OOM, no CPU
pin past the transpile budget). Recorded in §13 D2.

---

## 10. Observability, audit & abuse response

### 10.1 Per-execution audit record

> **As built:** the pipeline engine writes its own execution telemetry (`PipelineExecution`, step logs, failure-shaped rows retained 30 days). `ScriptRunner` sets `CodeScript.LastRanAt` / `LastRuntimeError`. The domain events below (`ScriptExecutedEvent`, `ScriptExecutionDeniedEvent`, …) are **defined** in `Domain/CustomCode/Events/CodeScriptEvents.cs` but **no code publishes the execution/denial events**; the denial reason travels in `ScriptRunResult.DenialReason` and the run result.

Every `run_code` invocation contributes to the **append-only** `PipelineExecution` (H.4):
`HostCallCount`, `DurationMs`, `Status` (`success|failed|timeout|denied`), `ErrorMessage` (≤1000, generic),
**bounded, PII-excluded** `StepLogsJson` (TTL-purged). Plus domain events on `IEventBus` (`custom-code.md` §2):

- `ScriptExecutedEvent(CodeScriptId, VersionId, ExecutionId, Outcome, HostCallCount, DurationMs, ErrorMessage)` —
  after **every** run (success or failure).
- `ScriptExecutionDeniedEvent(CodeScriptId, VersionId, ExecutionId, Reason, Detail)` — on **every** refusal
  (`ScriptDenialReason`: `CapabilityDenied|QuotaExceeded|EgressBlocked|ScriptDisabled|VersionInvalid`). `Detail`
  carries the **denied capability key / blocked FQDN / quota name** — enough to investigate, never a secret.
- `CodeScriptValidatedEvent` / `CodeScriptVersionPublishedEvent` — tamper-evident authoring trail.

### 10.2 Quota-breach & abuse response

- **PRE-run quota exhausted** → run refused with `BILLING_LIMIT`, `HostCallCount == 0`, one
  `ScriptExecutionDeniedEvent(QuotaExceeded)` (§5.3).
- **Repeated faults** → auto-disable the script (§5.4); next trigger fails `Denied(ScriptDisabled)`.
- **Per-tenant denial-rate threshold crossed** → circuit-breaker cool-off + **ops notification** (ops paging hook).
- **Idempotent metering** (`RecordSandboxUsageAsync` keyed by `ExecutionId`) → a retried run cannot double-bill or
  be replayed to evade quota.

### 10.3 What MUST be logged vs MUST NOT

| MUST log | MUST NOT log |
|---|---|
| `ExecutionId`, `CodeScriptId`, `VersionId`, `CompiledHash`, `Outcome`, `HostCallCount`, `DurationMs`, denied capability key, blocked FQDN, generic error code | **Any token/secret/connection string/`ENCRYPTION_KEY`** |
| BroadcasterId (internal guid), internal `TriggeredByUserId` guid | **Twitch PII** (viewer email/IP/raw Twitch user id) |
| Bounded `StepLogs` (PII-excluded, TTL-purged) | Full script source in execution logs (it's in `CodeScriptVersion`, not the run log) |
| Capability-denial reason enum | Internal stack traces / host exception details reflected to the **author-visible** `ErrorMessage` |

The executor **never reflects an internal exception/secret into the guest-visible `ErrorMessage`** — it returns a
**generic, bounded** outcome (`GlobalExceptionMiddleware` already returns generic errors in prod). Test: §12 T8.

### 10.4 The fail-closed engine (the linchpin) — **SATISFIED**

> **As built.** `NomNomzBot.Infrastructure/Platform/Pipeline/PipelineEngine.cs` is fail-closed at all three places the design named: an **unknown condition type blocks the step** ("Unknown condition type … blocking step (fail-closed)"); an **unknown action type** returns `ActionResult.Failure` and aborts; an **action that throws or returns failure** aborts the run as `PartiallyFailed` unless the step explicitly opts in to continue. `RunCodeAction` returns `ActionResult.Failure` for every non-`Success` `ScriptExecutionOutcome` (`A_non_success_outcome_fails_the_step`). There is no separate `PipelineOutcome.Denied`; a denied script fails the step. The description below is the original problem statement (the fail-open engine it describes no longer exists).

Every layer's denial is meaningless unless the engine **halts** on it. The live `PipelineEngine` is **fail-OPEN**
in three places (`server/src/NomNomzBot.Infrastructure/Pipeline/PipelineEngine.cs`): unknown action → skip &
continue; unknown condition → **treated true**; action throw → log & continue. **`run_code` MUST be on the
fail-CLOSED path** (must-fix #4, owned by `commands-pipelines.md`): disabled/rejected/missing-version script,
denied capability, quota breach, timeout, or host-budget breach → failing `ActionResult` and the engine **halts
that run** (`StopPipeline`; `PipelineOutcome` gains `Denied`). An attacker must not be able to trigger an unknown
condition to make a guarded step run, nor rely on continue-on-error to push a malicious chain past a denied step.
Tests: §12 T10, T11.

---

## 11. Host integration & DI

### 11.1 Where the code lives (Clean Architecture)

> **As built (differs from the table):** `JintScriptExecutor`, `ScriptRunner`, `ScriptCapabilityBroker`, `ScriptExecutionMeter` and `ScriptHostBridgeFactory` are all registered **Scoped** (D3's Singleton was not applied; `JintEngineFactory` still builds a fresh `Engine` per call, so nothing is shared). `WasmtimeScriptExecutor` is **not registered**. There is no worker-process wrapper and no epoch watchdog. The egress handlers live in `NomNomzBot.Infrastructure.Sandbox` (§6.3), not `…CustomCode.Egress`. `RunCodeAction` is in `Infrastructure/CustomCode/PipelineActions/`.

| Type | Layer / namespace | Lifetime | Notes |
|---|---|---|---|
| `IScriptExecutor` (contract) | `NomNomzBot.Application.Contracts.CustomCode` | — | owned by `custom-code.md` |
| `WasmtimeScriptExecutor` | `NomNomzBot.Infrastructure.CustomCode.Wasmtime` | **Singleton** | one hardened `Engine`/`Config` + module cache; fresh `Store` per call (thread-safe; per-exec context value-passed) |
| `JintScriptExecutor` (+ `JintEngineFactory`) | `NomNomzBot.Infrastructure.CustomCode.Jint` | **Singleton** | factory builds fresh `Engine` per call; worker-process wrapper |
| `IScriptCapabilityBroker` → `ScriptCapabilityBroker` | `…Infrastructure.CustomCode` | Scoped | binds host-side clients per tenant |
| `IScriptExecutionMeter` → `ScriptExecutionMeter` | `…Infrastructure.CustomCode` | Scoped | reads `TierLimit`, increments `UsageRecord` |
| egress `DelegatingHandler` + inner `SocketsHttpHandler.ConnectCallback` (`egress-allowlisted`) | `…Infrastructure.CustomCode.Egress` | (handler) | single SSRF policy owner (§6.3, §7); the `ConnectCallback` is the **only** correct IP-pin mechanism (§7.1 step 3) — connects to the validated IP, TLS `TargetHost` = FQDN |
| epoch watchdog thread | `…Infrastructure.CustomCode.Wasmtime` | Singleton hosted | advances epoch + cancels host calls past deadline |
| `IScriptRunner` → `ScriptRunner` | Application | Scoped | meter→admission→grant→executor→record→events |
| `RunCodeAction : ICommandAction` | Infrastructure (beside `SongRequestAction`) | Transient | thin; drives `IScriptRunner` |

> **Lifetime reconciliation (cross-spec contradiction):** `custom-code.md` §7 registers the executor as
> **Singleton** (reusable thread-safe engine/config pools, per-execution context value-passed);
> `platform-conventions.md` §7 lists it as **Scoped**. **Decision: Singleton** — `custom-code.md` is the owning
> spec and the rationale (one compiled-module cache + hardened Config reused across calls; a fresh `Store`/grant
> per execution carries all per-tenant state) is correct and cheaper. `platform-conventions.md` §7 should be
> updated to Singleton (§13 D3).

### 11.2 Executor registration (as built) — no profile switch

**Jint is registered unconditionally.** In `NomNomzBot.Infrastructure/DependencyInjection.cs`:

```csharp
// The sandbox script executor — Jint on self-host (Wasmtime SaaS adapter is a separate profile binding).
services.AddScoped<IScriptExecutor, JintScriptExecutor>();
```

`WasmtimeScriptExecutor` is a **harness that fails closed** (§3.1): it is constructible from `IConfiguration` (`Sandbox:WasmJsEnginePath`) and its hardened `Engine`/`Store` are tested, but no registration binds it and `ExecuteAsync` returns `Faulted`. `DeploymentProfileSnapshot.CodeExecutor` (`CodeExecutorKind`) is resolved and persisted by `DeploymentProfileService` and is **not read** by anything that chooses the executor.

*Design intent (owner question, not built):* selecting the adapter once at boot on `DeploymentProfileSnapshot.CodeExecutor` inside `AddDeploymentAdapters(snapshot)` — `Wasmtime` → `WasmtimeScriptExecutor`, otherwise `JintScriptExecutor` — so a `SelfHostFull` profile could never silently fall back to the weaker engine. The original text corrected `custom-code.md` §7's `DeploymentMode.Lite` switch (a non-existent enum member keyed on the wrong field); that correction stays valid for whenever the switch is built.

### 11.3 How `run_code` drives it (the runtime path)

```
PipelineEngine (fail-CLOSED)
  → RunCodeAction.ExecuteAsync(ActionContext ctx)         // ctx.BroadcasterId host-derived, unforgeable
    → build ScriptInvocation (PII-scrubbed Variables, §8.5)
    → IScriptRunner.RunAsync(codeScriptId, invocation)
        1. IScriptExecutionMeter.CheckSandboxBudgetAsync  → PRE-run gate; atomically debit projected WallClockMs
                                                             (reserve-then-settle, §5.2); over limit? fail-closed Denied(QuotaExceeded/BILLING_LIMIT)
        2. GLOBAL admission gate (§5.3, distributed on SaaS)→ over capacity? RATE_LIMITED / queue
        3. load active CodeScriptVersion (tenant-scoped)   → not valid/disabled? Denied(VersionInvalid/ScriptDisabled)
        4. IScriptCapabilityBroker.BuildGrantAsync (Bridge) → disallowed cap? Denied(CapabilityDenied)
        5. IScriptExecutor.ExecuteAsync(request, grant, bridge, ct) → run under budget + watchdog; value-out only
                                                             (bridge = IScriptHostBridge dispatch seam, §6.4)
        6. IScriptExecutionMeter.RecordSandboxUsageAsync    → reconcile reservation → actual elapsed; idempotent per ExecutionId
        7. raise ScriptExecuted / ScriptExecutionDenied; set CodeScript.LastRanAt/LastRuntimeError
           (a worker crash / IPC-abort counts as Faulted for THIS CodeScriptId → §5.4 auto-disable, finding §5.4)
    → map ScriptRunResult → ActionResult (Success → merge VariablesOut, honor StopPipeline;
                                          any non-Success → ActionResult.Fail + engine HALTS)
```

The executor **never throws a sandbox escape outward**; every fault is a `ScriptExecutionOutcome` value, and the
action surfaces it as a fail-closed `ActionResult`.

> **As built, the runtime path is:** `PipelineEngine` → `RunCodeAction` (decodes `code_script_id` with `OwnedIdCodec`, splits `ctx.RawMessage` into args, copies `ctx.Variables`) → `ScriptRunner.RunAsync`: (1) load the `CodeScript` (`NOT_FOUND` if absent; the `ITenantScoped` query filter scopes the load; the runner adds no explicit `BroadcasterId` predicate) → (2) `!IsEnabled` → `Faulted`/`ScriptDisabled` → (3) `meter.CheckSandboxBudgetAsync` → over budget → `Denied`/`QuotaExceeded` → (4) load the current version; not `valid` or no `CompiledJs` → `Faulted`/`VersionInvalid` → (5) `broker.BuildGrantAsync` over the version's `DeclaredCapabilitiesJson` → failure → `Denied`/`CapabilityDenied`, recorded in `LastRuntimeError` → (6) `IScriptHostBridgeFactory.Create(script.BroadcasterId, triggeringUserId)` → (7) `IScriptExecutor.ExecuteAsync` (Jint, in-process, `ScriptResourceBudget.Baseline`) → (8) set `LastRanAt` / `LastRuntimeError` → (9) `meter.RecordSandboxUsageAsync(elapsedMs)`. There is **no global admission step**, no domain-event publication, and no crash-attribution step. The per-channel `custom_code` toggle is enforced at authoring time and inside the broker (§5.2), not by the runner. `CodeScriptId` is read from the action's own parameters, not from a typed `CompiledStep.CodeScriptId`.

> **`CodeScriptId` source + tenant-scoped load (closes the config-DTO IDOR/bypass).** The id a `run_code` step
> executes is double-declared — `RunCodeActionConfig.CodeScriptId` (the step's untyped `ConfigJson`, surfaced as
> `ctx.Parameters`) **and** `CompiledStep.CodeScriptId` (the typed field the compiler populates from
> `PipelineStep.CodeScriptId`, `commands-pipelines.md` §4.4 H.2). **`RunCodeAction` MUST read it from the typed,
> compiler-validated `CompiledStep.CodeScriptId`, never from `ctx.Parameters`** — the untyped path bypasses the
> §190 architecture invariant that strips tenant/credential/url config keys and is not the value the save-time
> validator vetted. `RunCodeActionConfig` is the **authoring/storage** shape only. And `IScriptRunner.RunAsync`
> **MUST re-load the `CodeScript` + `CurrentVersion` via the tenant-scoped repository** (the `ITenantScoped`
> global query filter, §8.3) before trusting `CurrentVersionId`, so a step naming **another tenant's** script id
> resolves `NOT_FOUND` (cross-tenant IDOR fails closed) — the defense must not rest on an unstated load. Test: §12
> T19 (arch: config has only `CodeScriptId`) + T9 (cross-tenant resolution → not found).

---

## 12. Security test plan

Each test = **the attack → the expected containment → the assertion**. A test **must fail if its control
regresses** (it is not a smoke test). Tests live in `tests/NomNomzBot.Infrastructure.Tests/CustomCode/` and
`tests/NomNomzBot.Application.Tests/CustomCode/`; architecture tests in `…Domain.Tests`.

| # | Attack (the malicious script / request) | Expected containment | Assertion (fails on regression) |
|---|---|---|---|
| **T1** | Jint: script attempts CLR access **and code-from-string via all four dynamic-function constructors** — `clr('System.IO.File')`, `this.GetType()`, `({}).constructor.constructor('return process')()`, `(function*(){}).constructor('return this')()`, `(async function(){}).constructor('return this')()`, `(async function*(){}).constructor('return this')()` | `ReferenceError` / constructor non-callable / no host type reachable | `ScriptExecutionOutcome == Faulted`; result contains **no** `System.*` type, **no** `Process`/`File`/global reference; **all four** of `Function`/`GeneratorFunction`/`AsyncFunction`/`AsyncGeneratorFunction` throw/non-callable under `DisableStringCompilation` (factory test); engine never instantiated with `AllowClr`/`AllowGetType` |
| **T2** | Infinite loop `while(true){}` | time budget trips | SaaS: `Timeout` via epoch at `WallClockMs ± tolerance` (assert elapsed ≈ budget, **not** 5 min); self-host: worker killed, `Timeout`. **Not** unbounded |
| **T3** | Tight host-call loop `for(;;) bot.chat.send('x')` (starves epoch — #9188); **and** a loop over a host import whose backing service blocks/ignores cancellation | host-call budget / wall-clock-incl-host watchdog trips **before** wall-clock-in-wasm; per-import timeout + plumbed token bound the blocking case | `HostBudgetExceeded` at `MaxHostCalls`, **or** `Timeout` via the host-inclusive watchdog; `HostCallCount ≤ MaxHostCalls`; in-flight host call cancelled; **the blocking-import variant trips within `MaxHostCalls × per-import-timeout` (< the hard ceiling) and does NOT hold its GLOBAL admission permit past the grace window** (§4.1.2) |
| **T4** | Memory bomb `let a=[1,2,3]; for(;;) a=a.concat(a)` | memory cap trips, **host does not OOM** | SaaS: `Faulted` at `Store.SetLimits(memorySize)` (assert the **Store** trapped, host RSS bounded); self-host: worker `memory.max` kills worker, host survives |
| **T5** | Stack bomb `function f(){return f()} f()` | contained to a disposable unit | self-host: **worker process** dies, **API process survives** (supervisor restarts worker), outcome `Faulted`; SaaS: wasm stack cap traps, `Faulted` |
| **T5b** | Self-sustaining worker-crash DoS: a script that **reliably crashes the worker** (stack-bomb/OOM) re-triggered N times within the window | worker death attributed to the `CodeScriptId` → auto-disable | each crash is counted as a `Faulted` for **that** `CodeScriptId`; after N in window `CodeScript.IsEnabled = false`, subsequent `run_code` → `Denied(ScriptDisabled)` (**not** re-run/respawn indefinitely) — fails if a worker death isn't mapped to the triggering script |
| **T6** | SSRF to metadata `bot.http.fetch('https://169.254.169.254/latest/meta-data/iam/...')` (FQDN allowlisted but resolves to metadata IP, via test resolver) | resolved-IP re-validation blocks at connect | `Denied(EgressBlocked)`; **no** connection to `169.254.169.254`; `ScriptExecutionDeniedEvent(EgressBlocked)` with the IP/FQDN in `Detail` |
| **T6b** | Second-order SSRF + exfil: script POSTs harvested tenant data, and queries an allowlisted **proxy** host with `?url=http://169.254.169.254/...`; also exceeds the per-call and cumulative egress caps | request-body/query opt-in + egress byte budgets bound it | a request body over `MaxRequestBytes` → `Denied(EgressBlocked)`; cumulative request+response over `MaxEgressBytes` → `Denied(EgressBlocked)`; body/query rejected when the row did not opt in; the second-order reach is acknowledged residual (§7.3) but bandwidth-clamped |
| **T7** | DNS-rebind: allowlisted FQDN whose resolver returns a public IP on check, internal IP on connect | resolve-then-pin connects to the **validated** IP only (via `SocketsHttpHandler.ConnectCallback`) | `ConnectCallback` opens the socket to the validated public IP; the inner handler never re-resolves; rebind to `10.x`/`127.x` never reached; TLS `TargetHost` == the FQDN (SNI/SAN validate against the FQDN, not the IP) |
| **T7b** | Cert authentication: pinned-IP connect whose presented certificate's SAN does **not** match the original FQDN (on-path/MITM attempt), and a script-config attempt to weaken validation | FQDN-cert validation is mandatory, cannot be disabled | the handshake `AuthenticateAsClientAsync(TargetHost=FQDN)` **fails** → `Denied(EgressBlocked)`, **not** a bypass; arch test asserts the `egress-allowlisted` client has **no** `RemoteCertificateValidationCallback`/`SslOptions` override that returns `true` |
| **T8** | Secret exfil: script reads a sentinel secret injected into host config and tries to return/throw it; also `bot.http.fetch` to an allowlisted attacker host with the secret in the body; **and** the script harvests data it legitimately holds (vars/args/balance) and tries to ship it in a large/looping POST | secret unreachable **and** legitimately-held data clamped by egress budgets | the secret value never appears in `ScriptExecutionOutcomeResult`/`ChatOutput`/`ErrorMessage`/logs; **no** host import returns a token/secret-shaped value (fuzz/inspect import surface); egress body cannot contain a host secret the guest never had; **data the guest DOES hold cannot be shipped past `MaxRequestBytes` (per call) or `MaxEgressBytes` (cumulative)** → `Denied(EgressBlocked)` |
| **T9** | Cross-tenant read: UserA's script enumerates `bot.vars.read(k)` for every key; UserB has a variable with a known sentinel | tenant-scoped vars only | sentinel **never** appears in A's reads; A's `bot.economy.balance`/`bot.music.*` only ever touch A's resources (RLS + host-bound owner) |
| **T9b** | PII-in-own-variables: a known-PII template key (e.g. `user.email`, `user.ip`, raw `user.twitchId`) is resolved into `ctx.Variables` for the **author's own** trigger | runner PII-scrub allowlist (§8.5) | the PII key/value is **absent** from `ScriptInputs.Variables` (only `args.*`/`channel.*`/`user.name`/`user.id`/custom vars cross); `bot.vars.read('user.email')` → `undefined`/`null` — fails if the filter is empty/guessed |
| **T10** | Pipeline with a `run_code` step whose script is **disabled** | engine fail-CLOSED | the **whole pipeline halts** (`StopPipeline`), `Denied(ScriptDisabled)` — **fails against the current fail-open engine** (that is the point) |
| **T11** | Pipeline with an **unknown condition** before a guarded step | unknown condition does **not** pass | step does **not** run; pipeline halts/denies — **fails against the current "treat-as-true"** |
| **T12** | Capability escalation: script calls `bot.http.fetch` **without** `http.fetch` in `DeclaredCapabilities` | broker deny | `Denied(CapabilityDenied)`; `ScriptExecutionDeniedEvent` carries the denied key |
| **T13** | Declares a `critical`-tier capability for a T3 script | broker rejects critical-to-T3 | `BuildGrantAsync` → `FORBIDDEN`; never granted |
| **T14** | Quota evasion: tenant over `sandbox_exec_ms`; then retry the same `ExecutionId` | PRE-run fail-closed + idempotent meter | run refused with `BILLING_LIMIT`, `HostCallCount == 0`; replayed `ExecutionId` does **not** double-increment `UsageRecord` |
| **T14b** | Concurrent-first-run quota race: tenant with ~0 remaining quota fires N concurrent runs that all pass the gate before any records | reserve-then-settle | only as many runs as the **reservation** allows admit; the others get `Denied(QuotaExceeded)`; total metered cost does **not** overshoot by ~N × `WallClockMs` — **fails against a plain check-then-record meter** |
| **T15** | Global DoS: N+1 concurrent executions across many tenants (each under per-channel 5) | global admission control | the (N+1)th is queued or `RATE_LIMITED` — asserts a **GLOBAL** limiter exists, not only per-channel; on SaaS the counter is **distributed** (§5.3, T15d) |
| **T15b** | *(void as written — the `http_request` T2 front-end does not exist, §6.3)* Egress rate bypass via the T2 front-end: a run mixes N `http_request` pipeline steps + M `http.fetch` capability calls | one shared `(BroadcasterId,'egress')` bucket in the handler | both front-ends decrement the **same** bucket; N+M past the burst → `Denied(EgressBlocked)`/`RATE_LIMITED` — **fails if the limiter sits in the broker** (which sees only the T3 path) |
| **T15c** | Slow-loris egress: many executions each fetch a slow allowlisted responder that dribbles headers | global egress concurrency + connect/header timeouts | total in-flight fetches ≤ the egress semaphore (independent of execution slots); a dribbling responder trips the `ConnectTimeout`/response-headers timeout, **not** the full `WallClockMs`; connection pool / ephemeral ports not exhausted |
| **T15d** | Multi-instance global DoS (SaaS): N concurrent `run_code` triggers spread across K API instances behind the LB, each instance reading "under budget" | distributed global admission + distributed import bucket | the **fleet-wide** ceiling holds (not K× the per-instance limit); counters resolve through `IRateLimiterPartitionStore` (Redis); asserts distributed enforcement, not single-instance (§5.3) |
| **T16** | Forbidden-global / undeclared-capability at **save** time | validate-on-save rejects | `ValidationStatus == rejected`, `CurrentVersionId` stays null; `run_code` → `Denied(VersionInvalid)`; `ScriptValidationError.Code ∈ {forbidden_global, undeclared_capability}` |
| **T17** | (a) Editor/Moderator **without** a per-user grant calls any `/code-scripts` endpoint → `403`. (b) A specific user the Broadcaster granted via `!permit @user code:script:author` → `200`. (c) A `ChannelActionOverride` attempting to drop the floor below `Broadcaster(40)` → rejected (`VALIDATION_FAILED`) | per-user delegable, never role-tier | (a) `403`; (b) `200` for the named user only; (c) override rejected — asserts `IsGrantableViaPermit == true` reaches **only** the named user, never a role tier |
| **T18** | Config: assert the SaaS Wasmtime `Config` is **x86_64-Cranelift**, **Winch disabled**, **no WASI fs preopen**, threads off | hardened-config invariants | startup/arch test fails if Winch selected, aarch64 host, any preopen present, or `WithWasmThreads(true)` |
| **T19** | Architecture: any inbound `/code-scripts` DTO or `run_code` config declares `BroadcasterId`/tenant/credential/url | broker invariant | arch test **fails** if any such field exists; `RunCodeActionConfig` has **only** `CodeScriptId` |
| **T20** | Determinism: same source compiled twice (and across hosts) | stable hash | identical `CompiledHash`; `CompileAsync` executes **no** user code (no host import touched) |
| **T21** | Transpile-time DoS at **save**: adversarial source (deeply nested AST, catastrophic-backtracking regex literal, 10 MB body) submitted to `CompileAsync` | transpile is budgeted + off the API request thread (§9.4) | `ValidationStatus == rejected` with bounded `VALIDATION_FAILED`; the **API process survives** (no `StackOverflow`/OOM/CPU-pin past the transpile budget); if in-runtime, the transpile ran in the worker/`Store`, not the API thread (arch test) |

### 12.1 As-built test status (verified 2026-09-30)

Tests live in `server/tests/NomNomzBot.Infrastructure.Tests/CustomCode/` and `…/Sandbox/`. **There are no architecture tests anywhere in the tree** (no NetArchTest or equivalent), so every "arch test asserts …" clause in this plan is open.

**Verified explicitly**

- **T1 — partial.** `JintScriptExecutorTests.Eval_is_blocked` (`eval('1+1')` → `Faulted`) and `The_function_constructor_code_from_string_is_blocked` (`(function(){}).constructor('return 1')()` → `Faulted`) exist. **Missing:** the CLR probes (`clr(...)`, `this.GetType()`, `({}).constructor.constructor(...)`), the `GeneratorFunction` / `AsyncFunction` / `AsyncGeneratorFunction` constructors, the factory-level assertion that all four are non-callable, and the assertion that the engine is never built with `AllowClr` / `AllowGetType`.
- **T18 — does not exist.** `WasmtimeScriptExecutorTests` has five tests (`Runtime_is_wasmtime`, `A_benign_module_runs_to_completion`, `A_runaway_loop_is_fuel_bounded_and_contained`, `ExecuteAsync_without_an_engine_module_fails_closed`, `Compile_declares_the_capabilities_the_script_calls`). None asserts Cranelift-only, Winch off, x86_64, no WASI preopen or threads off; `BuildHardenedEngine` sets them but nothing would fail if it regressed.
- **T19 — does not exist.** No test inspects `/code-scripts` DTOs or the `run_code` config for `BroadcasterId` / tenant / credential / url fields.

**Full status of the plan**

| Test | Status | Evidence / gap |
|---|---|---|
| T1 | Partial | see above |
| T2 | Partial | `A_runaway_loop_is_contained`, `A_script_that_exceeds_its_wall_clock_is_timed_out` (Jint, in-process; no worker kill, no epoch path) |
| T3 | Partial | `The_host_call_budget_is_enforced`, `The_production_baseline_survives_one_slow_host_call`; **open:** the blocking-import variant and the permit-release assertion (no permits exist) |
| T4, T5, T5b | **Open** | no memory-bomb, stack-bomb or worker-crash test; no worker |
| T6, T6b, T7, T7b, T8 | **Open** | only `EgressAddressGuardTests` (blocked/allowed address classes) and `EgressSchemeHandlerTests` (https-only); no end-to-end metadata-SSRF, rebind, cert-mismatch or exfil-clamp test. `ScriptHostBridgeTests.Http_fetch_rejects_a_non_https_url` and `Http_fetch_returns_the_capped_response_body` cover the bridge |
| T9 | Partial | `Storage_is_tenant_isolated_channel_b_cannot_read_channel_a`, `Tts_voice_set_is_scoped_to_the_bridges_own_tenant`; no cross-tenant `vars.read` / economy / music enumeration test |
| T9b | **Open** | the PII allowlist is not built (§8.5) |
| T10 | Partial | `ScriptRunnerTests.A_disabled_script_fails_closed` + `RunCodeActionTests.A_non_success_outcome_fails_the_step` cover the two halves; no single pipeline-level test |
| T11 | **Open** | the engine blocks an unknown condition (§10.4) but no test located asserts it |
| T12 | Covered | `JintScriptExecutorTests.An_ungranted_capability_is_denied` |
| T13 | Partial | `ScriptCapabilityBrokerTests.The_catalogue_exposes_no_critical_capability`; no `BuildGrantAsync`-rejects-critical test |
| T14 | Partial | `ScriptExecutionMeterTests` (verdict mapping, accumulation, zero-ms skip) and `ScriptRunnerTests`; **open:** replayed `ExecutionId` idempotency (the meter ignores it) |
| T14b, T15, T15b, T15c, T15d | **Open** | the mechanisms (reserve-then-settle, global admission, shared egress bucket, egress semaphore, distributed counters) are not built |
| T16 | Partial | `CodeScriptServiceTests.Create_with_invalid_source_rejects_but_persists_the_version` (syntax errors only); `forbidden_global` / `undeclared_capability` are not implemented |
| T17 | **Open** | the design's scenarios (Broadcaster floor, `!permit` grant, override rejection) are not tested, and the as-built floor differs (§9.3); `Identity/` tests exercise the seeded action separately |
| T18, T19 | **Do not exist** | see above |
| T20 | **Open** | no determinism test (the hash is a plain SHA-256 of the source, so it is stable by construction) |
| T21 | **Open** | no transpile exists (§9.4) |

---

## 13. Decisions (resolved)

Every item below is a **binding decision** — the design, not a pending choice. Where a decision is a real
business/judgment call (security-plane placement, risk acceptance, budget ceilings, sequencing), it is **still
decided here**, and flagged in the last column for owner review against the committed default. Items marked as a
coordinated edit to another spec are **dependencies** (the named spec declares the type/fix this doc consumes), not
deferrals — implementation order is the §14 checklist's job.

| # | Decision | Where specified | Owner-review flag |
|---|---|---|---|
| **D1** | `code:script:author` is placed on the `management / Broadcaster(40) / critical / IsGrantableViaPermit=true` plane. Authoring/running sandboxed code is owner-floored, but the fully-trusted Broadcaster MAY delegate it to a **named individual user** via `!permit` (never role-tier). | §9.3 | Business call — confirm the plane placement against `custom-code.md` §5. |
| **D2** | TS→JS transpilation inside `CompileAsync` is **build-time / committed-artifact** (No-Roslyn stands): no in-process JS engine parses untrusted source on the API thread. The sanctioned runtime-transpile fallback is confined to the §3.3 worker / a fresh `Store` with its own `ScriptResourceBudget`, never the API request thread. | §9.4 | Business call — build-time is the chosen approach; it sets the dep surface and removes the API-thread DoS surface. |
| **D3** | `IScriptExecutor` is registered **Singleton** (one hardened `Engine`/`Config` + module cache; fresh `Store`/grant per execution). `platform-conventions.md` §7 is corrected from Scoped to Singleton. | §11.1 | Dependency — apply the Singleton correction in `platform-conventions.md` §7. |
| **D4** | SaaS microarchitectural side channels are an **accepted residual**: the boundary stays **in-process** (no per-execution Firecracker/gVisor), mitigated by config-hardening + the §5.6 fast-patch SLA + §10 timing caps/rate limits. Core/SMT pinning and KVM Spectre mitigations are ops hardening on top, not a structural control. | §2.5 | Business call — risk acceptance of the in-process boundary. |
| **D5** | Self-host worker isolation runs **full OS limits on day one** (§5.5: cgroup/Job-Object, CPU/memory/PID caps, dropped caps, seccomp, no worker egress). The minimum bar (process + bounded thread + external kill alone) is **not** acceptable, and is mandatory before any future multi-user self-host. | §5.5 | Business call — confirm the full day-one bar for single-operator self-host. |
| **D6** | Default per-execution budgets are `2000 ms / 64 host calls / 64 MiB / 8 KiB chat-out / 256 KiB egress / 50M fuel / 200k statements`, surfaced as tightenable `AppSetting` rows (operator may tighten, never loosen past the profile ceiling). **As built:** `WallClockMs` is **8000 ms** (`ScriptContracts.cs`, raised 2026-08-26 after a real script with one chat send + one TTS synthesis was killed at 2000 ms), the fuel/statement value is 200,000 statements only (50M fuel is Wasmtime-only and unwired), and the budgets are **compile-time constants** in `ScriptResourceBudget.Baseline` — no `AppSetting` rows. | §5.1, §5.2 | Business call — the constants are set; making them operator-tunable is an open owner decision. |
| **D7** | **SATISFIED (§8.4).** `run_code` does **not** ship until the §8.4 live-code prerequisites land: AES-GCM+AAD token-at-rest crypto, encrypted `Configuration.SecureValue`, and the fail-closed `PipelineEngine`. This is a hard ordering dependency, not an optional gate. | §8.4 | Sequencing dependency — owned by `gdpr-crypto.md` / `commands-pipelines.md` / `platform-conventions.md`. |
| **D8** | `custom-code.md` §4 boundary types are widened/added as this doc requires: (a) `ScriptResourceBudget.MaxMemoryBytes`/`MaxEgressBytes` are **`long`** (match `Store.SetLimits`/`LimitMemory`); (b) `ScriptResourceBudget.MaxEgressBytes` is added (§7.4); (c) `HttpEgressAllowlist.MaxRequestBytes` + `AllowRequestBody`/`AllowQuery`/path-method-allowlist columns are added (§7.1 step 6b/9). | §5.1, §5.2, §7.1, §7.4 | Dependency — declared in `custom-code.md` §4. |
| **D9** | The host-call **dispatch seam** is added to `custom-code.md` §3.1/§4: a `HostImportDelegate` (primitive-in/primitive-out) plus an `IScriptHostBridge` **parameter** on `IScriptExecutor.ExecuteAsync` (§6.4). Without it the `bot.*` facade is un-implementable, so it is a hard prerequisite sequenced with §8.4. | §6.4 | Dependency — blocking; `custom-code.md` declares the seam, sequenced with D7. |
| **D10** | Executor selection is on `DeploymentProfileSnapshot.CodeExecutor` (`CodeExecutorKind`) inside `AddDeploymentAdapters(snapshot)` (§11.2). `custom-code.md` §7's `DeploymentMode.Lite` switch is broken (non-existent enum member, wrong field) and is corrected to match this doc's §11.2. | §11.2 | Dependency — correction in `custom-code.md` §7. |

### 13.1 As-built status of the decisions

- **D1** (author gate on the Broadcaster plane, permit-delegable) — **differs.** Built as Moderator floor / Critical / not permit-grantable, plus the per-channel `custom_code` toggle (§9.3). Owner question.
- **D2** (build-time TS transpile) — **not built**; `CompileAsync` is a JS parse (§9.2, §9.4). Owner question.
- **D3** (Singleton executor) — **not applied**; everything is Scoped (§11.1).
- **D4** (accept in-process SaaS side channels) — a risk acceptance for a boundary that does not carry tenant code yet (§3.1).
- **D5** (full OS limits on the self-host worker) — **not built**; there is no worker (§3.3, §5.5). Owner question.
- **D6** — see the row above (8000 ms, compile-time constants).
- **D7** — **satisfied** (§8.4).
- **D8** (boundary types) — **satisfied**: `MaxMemoryBytes`, `MaxEgressBytes`, `MaxOutputBytes` are `long`, `MaxEgressBytes` exists, and `HttpEgressAllowlist` carries `MaxRequestBytes`, `AllowRequestBody`, `AllowQuery`, `AllowedMethods`, `PathPrefix`. **The columns are unread** by any code (§7.1).
- **D9** (`HostImportDelegate` + `IScriptHostBridge` seam) — **satisfied** (§6.4).
- **D10** (select on `CodeExecutorKind`) — **not built**; Jint is registered unconditionally (§11.2).

---

## 14. Implementation checklist (vertical slices)

> **Status:** slices 4 (broker), 6 (runner + fail-closed `run_code`, minus the PII allowlist and crash attribution) and 7 (authz seed, with a different floor) are built; slice 1 (egress) is built for the SSRF core only; slice 2 (Wasmtime) is the fail-closed harness; slice 3 (Jint) is built in-process without the worker; slices 5 (global admission / reserve-then-settle), 8 (audit events) and 9 (transpile) are not built. Slice 0's boundary types are satisfied.

0. **Boundary-type prerequisites (coordinated edits to `custom-code.md`, §13 D8/D9/D10)** — add the
   `HostImportDelegate` + `IScriptHostBridge` dispatch seam (§6.4 — without it nothing below is implementable),
   widen byte-count budget fields to `long`, add `MaxEgressBytes` + `HttpEgressAllowlist.MaxRequestBytes`/opt-in
   columns, and fix the `DeploymentMode.Lite` selection switch. *(Blocking; lands before any adapter.)*
1. **Egress policy** — `DelegatingHandler` + inner `SocketsHttpHandler.ConnectCallback` (pinned-IP connect, TLS `TargetHost`=FQDN) (`egress-allowlisted`) + `HttpEgressAllowlist` repo (incl. `MaxRequestBytes`/opt-in flags) + request-body/cumulative-egress clamps + global egress-concurrency semaphore + connect/header timeouts; tests T6/T6b/T7/T7b/T15c. *(Independent; lands first.)*
2. **Wasmtime adapter** — hardened `Config`/`Engine`, `Store.SetLimits`, fuel+epoch, watchdog thread (host-call cancel + permit-release), per-import timeout/token plumbing, no-WASI; tests T2/T3/T4/T18/T20.
3. **Jint adapter + worker** — `JintEngineFactory` (CLR-off, all-four-constructor string-compilation off, hardened constraints) + separate worker process + OS limits + **host-import IPC contract** (§3.3.1, API-side `MaxHostCalls`); tests T1/T5/T5b.
4. **Capability broker** — catalog (§6.2), `BuildGrantAsync` enforcement (§6.4), `IScriptHostBridge` host-side client binding; tests T8/T9/T9b/T12/T13.
5. **Meter + budgets + global admission** — `IScriptExecutionMeter` (**reserve-then-settle**), profile budget defaults, GLOBAL semaphore + **distributed counters via `IRateLimiterPartitionStore` on SaaS**, shared `(BroadcasterId,'egress')` bucket in the handler; tests T14/T14b/T15/T15b/T15d.
6. **Runner + fail-closed `run_code`** — `IScriptRunner` orchestration, typed `CompiledStep.CodeScriptId` + tenant-scoped load, PII-scrub allowlist, fail-closed mapping, worker-crash→auto-disable attribution; tests T10/T11/T16.
7. **Authz seed + endpoints** — `code:script:author` seed row (`IsGrantableViaPermit=true`), Broadcaster-floor gate, per-user `!permit` delegation only; tests T17/T19.
8. **Observability** — audit events, generic errors, MUST-NOT-log enforcement; test T8 (log assertion).
9. **Transpile confinement** — build-time/committed artifact (preferred) or off-API-thread budgeted in-runtime transpile (§9.4); test T21.

Each slice: implemented → its tests prove the **containment behavior** (not "no exception") → committed. Never
pile up uncommitted changes.
