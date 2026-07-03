# Stack & Dependencies — Decision Document

Source: lead-architect synthesis of 12 per-subsystem research findings, 2026-06-16. Version/CVE/license facts verified against NuGet, GitHub advisories, and vendor security bulletins on 2026-06-16 (see Sources per subsystem). This is the binding dependency policy for the backend.

## Governing rule

**Use as few third-party packages as possible.** Microsoft / .NET-Foundation-adjacent packages are treated as **second-party** and are acceptable by default. Every genuine **third-party** dependency must *earn its place*. Hard caveat that overrides minimalism: **do not hand-roll crypto, auth, or the sandbox boundary** — for those, a vetted dependency beats homegrown code.

Party legend: **1st** = our own code / in-box BCL; **2nd** = Microsoft or .NET-Foundation-governed (Asp.Versioning, OpenTelemetry, Polly, xUnit); **3rd** = genuine external maintainer.

Profile axis (drives every swappable adapter, per `2026-06-16-deployment-profile.md`): **lite** = SQLite + in-memory cache/bus + WebSocket EventSub, no Docker; **full/SaaS** = Postgres + Redis + conduits/webhooks. One boot-time `App__DeploymentMode` switch reconfigures DB provider, cache/pub-sub, EventSub transport, code executor, token vault.

---

## 1. Master dependency table

### 1a. Second-party (Microsoft / .NET Foundation) — taken freely

| Name | Party | Version | License | Known CVEs | Maintenance | Why it earns its place |
|------|-------|---------|---------|-----------|-------------|------------------------|
| Microsoft.AspNetCore.* (host, MVC controllers, CORS, RateLimiting, OutputCaching, HealthChecks, ProblemDetails) | 2nd | in-box .NET 10 | MIT | DataProtection transitive — see below | .NET 10 servicing train | Host needs are nearly 100% in-box; controllers fit the per-action authz ladder |
| Microsoft.AspNetCore.OpenApi | 2nd | **10.0.9** (repo pins 10.0.8 — bump) | MIT | none | 2026-06-09 | OpenAPI 3.1 generation; MS replacement for Swashbuckle |
| Asp.Versioning.Mvc + .Mvc.ApiExplorer | 2nd (.NET Foundation) | 10.0.0 GA | MIT | none | GA 2026-04-21 | De-facto API versioning; already wired (must add `AddApiExplorer()`) |
| Microsoft.AspNetCore.DataProtection | 2nd | **≥ 10.0.7 REQUIRED** | MIT | **CVE-2026-40372 (CVSS 9.1)** in 10.0.0–10.0.6 — **platform-scoped** (see below) | OOB patch 2026-04 | Underpins cookie/token protection; verify resolved ≥10.0.7 + rotate key ring (rotation required only where the affected config applies) |
| Microsoft.AspNetCore.Authentication.JwtBearer | 2nd | 10.0.9 (repo 10.0.8) | MIT | none direct | .NET 10 train | Inbound JWT resource-server validation |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | 2nd | 10.0.x | MIT | none direct | .NET 10 train | OIDC *client* / SSO relying party; auto-fetches peer JWKS |
| Microsoft.IdentityModel.JsonWebTokens (+ .Tokens) | 2nd | **8.19.1** | MIT | none on 8.19.1 | active | Modern JWT create/validate — **replaces** legacy `System.IdentityModel.Tokens.Jwt` |
| Microsoft.EntityFrameworkCore | 2nd | **10.0.9** (10.0 GA 2025-11-11, LTS→2028) | MIT | none | monthly patch | Core ORM; EF10 **named query filters** = soft-delete + tenant isolation natively |
| Microsoft.EntityFrameworkCore.Sqlite + Microsoft.Data.Sqlite | 2nd | 10.0.9 | MIT | engine CVE — see SQLitePCLRaw | .NET 10 train | Self-host (lite) provider; no server, no Docker |
| SQLitePCLRaw.bundle_e_sqlite3 | 2nd | **pin ≥ 3.0.3** (package version; bundles engine 3.50.4.5) | Apache-2.0 | bundles SQLite — **CVE-2025-6965** (CVSS up to 9.8) if engine < 3.50.2 | MS/Eric Sink | Carries native SQLite; explicit pin forces patched engine. NB: `3.0.3` is the *package* version (2026-05-07), not the engine version |
| Microsoft.Extensions.Caching.Hybrid | 2nd | 10.7.0 (GA since .NET 9) | MIT | none | active | L1(+L2 Redis) cache behind `ICacheService`; stampede protection + tag invalidation free |
| Microsoft.Extensions.Caching.StackExchangeRedis | 2nd | 10.0.8 | MIT | none | .NET 10 train | IDistributedCache Redis L2 (SaaS); pulls StackExchange.Redis transitively |
| Microsoft.AspNetCore.SignalR.StackExchangeRedis | 2nd | **10.0.9** (repo bump from 10.0.8) | MIT | none in pkg (server Redis is infra) | 2026-06-09 | SaaS-only SignalR backplane; self-host uses none |
| Microsoft.AspNetCore.SignalR.Protocols.MessagePack | 2nd | **10.0.9** (repo pins 10.0.8 — bump) | MIT | none in pkg; heed MessagePack DoS advisory | 2026-06-09 | Compact frames for high-volume chat/event fan-out; JSON stays enabled too |
| MessagePack (MessagePack-CSharp) | 2nd-adjacent | ≥ 2.5.301 (transitive) | MIT | GHSA-4qm4-8hg2-g2xm DoS (patched ≥2.5.301); **never enable Typeless** | active | Serializer behind `AddMessagePackProtocol`; keep default safe resolver |
| Microsoft.Extensions.Http.Resilience | 2nd | 10.7.0 | MIT | none | 2026-06-09 | Retry/circuit-breaker/timeout on Helix `HttpClient` (Polly v8); **don't hand-roll a breaker** |
| Polly (App-vNext) | 2nd (.NET Foundation) | 8.7.0 (transitive) | BSD-3 | none | 2026-06-10 | Engine under MS.Resilience; no direct reference needed |
| System.Security.Cryptography (AesGcm, HKDF, RNG, HMACSHA256, CryptographicOperations) | 2nd | in-box .NET 10 | MIT | none on current line; `AesGcm(byte[])` now `[Obsolete]` — pass `tagSizeInBytes:16` | dotnet/runtime | All crypto **primitives** for the token vault; only glue is hand-rolled |
| System.Security.Cryptography.ProtectedData | 2nd | 10.0.9 | MIT | none | 2026-06-09 | Windows DPAPI KEK-at-rest (opt-in hardening; machine-bound, not the default) |
| Azure.Security.KeyVault.Keys | 2nd | 4.10.0 | MIT | none current | 2026-05-06 | SaaS-only KEK custody (WrapKey/UnwrapKey, Managed-HSM, EU residency) |
| OpenTelemetry.Extensions.Hosting / .Exporter.OpenTelemetryProtocol | 2nd (.NET Foundation/CNCF) | 1.16.0 | Apache-2.0 | none | 2026-06-10 | Logs+traces+metrics export pipeline; default in .NET 10 / Aspire ServiceDefaults |
| OpenTelemetry.Instrumentation.AspNetCore / .Http / .Runtime | 2nd | 1.15.2 / 1.15.1 / 1.15.1 | Apache-2.0 | none | 2026-04-21 | Auto request/dependency/runtime telemetry |
| Npgsql.OpenTelemetry | 2nd | 10.0.2 | PostgreSQL | none | 2026-03-12 | DB-command spans (Postgres profile); stable alt to beta EFCore instrumentation |
| System.Threading.RateLimiter / Channels / PeriodicTimer / Net.WebSockets / Net.Http | 1st (in-box) | .NET 10 BCL | MIT | none | in-box | Adaptive throttle, fire-and-forget queue, recurring ticks, EventSub WS, Helix client |
| Microsoft.AspNetCore.Mvc.Testing | 1st (MS) | 10.0.x | MIT | none | .NET 10 train | `WebApplicationFactory<Program>` for full-stack auth/tenant/middleware tests |
| Microsoft.Testing.Extensions.CodeCoverage | 1st/2nd (MS) | ships with .NET 10 MTP | MIT-style | none | 1st-party | MTP-native coverage (`dotnet test --coverage`); replaces incompatible coverlet.collector |

### 1b. Third-party — deliberately accepted (each earns its place)

| Name | Party | Version | License | Known CVEs | Maintenance | Why it earns its place |
|------|-------|---------|---------|-----------|-------------|------------------------|
| **Npgsql.EntityFrameworkCore.PostgreSQL** (+ **Npgsql**) | 3rd (MS-adjacent — EF team's PG lead) | **10.0.2** (Npgsql **10.0.3**) | PostgreSQL License (OSI permissive) | none on 10.x (driver CVE-2024-32655 fixed in 8.0.3) | active, 2026-05-27 | **Sole** credible PG EF provider; MS ships none. Enables SaaS Postgres + RLS |
| **StackExchange.Redis** | 3rd (MIT; pulled transitively by MS pkgs) | **2.13.17** (pin; the 2.x line is the current stable release, 3.0 is still pre-stable) | MIT | **none against the client**; the 2025-26 CVSS-10 RCEs are Redis **server** bugs | 2026-05-27; ~1.1B downloads | The .NET Redis client; required by HybridCache L2, SignalR backplane, run-once lock. Not a *new* owner dep |
| **Scalar.AspNetCore** | 3rd | 2.14.14 | MIT | none found | active through 2026 | API reference UI over MS-generated spec. Dev-only, one-line swap; optional vs .NET 10 built-in UI |
| **Wasmtime (wasmtime-dotnet)** | 3rd | 44.0.0 (pin ≥ patched line 43.0.1) | Apache-2.0 WITH LLVM-exception | April-2026 **Critical sandbox escapes** hit **Winch** + **aarch64-Cranelift**; **x86_64-Cranelift escaped those Criticals but was still hit by lower-severity issues** (CVE-2026-24116 f64.copysign, CVE-2026-34944 f64x2.splat); CVE-2026-27195 call_async DoS | very active (Bytecode Alliance) | Only memory-safe deny-by-default in-process sandbox on .NET 10. **SaaS** code executor. x86_64-Cranelift is **not advisory-free** → fast-patch SLA required (tension #6) |
| **Jint** | 3rd | **4.9.2** (4.10 line very recent) | BSD-2 | none published; **not a security boundary by its own docs** | very active (sebastienros/lahma) | Pure-managed JS interp, zero native deps → fits no-Docker self-host. **Lite** executor only (single trusted operator) |
| **OpenIddict** (.AspNetCore + .EntityFrameworkCore) | 3rd | 7.5.0 (2026-04-22) | Apache-2.0 | none open | active; led by Kévin Chalet, **commercially sponsored by Rock Solid Knowledge** (lower bus-factor than a lone-hobby project) | **Only if** we run an OIDC/OAuth2 *issuer*. Avoids hand-rolling the auth protocol; Duende is license-encumbered |
| **AwesomeAssertions** | 3rd (community fork) | 9.4.0 | Apache-2.0 | none | active (9.0→9.4 across 2026) | Rich object-graph asserts without the paid FluentAssertions license; namespace-compatible drop-in |
| **NSubstitute** | 3rd | 5.3.0 | BSD-3 / Castle.Core Apache-2.0 | none | active | No-telemetry mocking (Moq disqualified by SponsorLink). Grandfathered, already in use |
| **xunit.v3** | 3rd (.NET-Foundation-adjacent) | 3.2.2 | Apache-2.0 | none | active | Test framework; native MTP on .NET 10, lets us drop VSTest runner + Test.Sdk |
| **Testcontainers** (+ .PostgreSql, .Redis) | 3rd | 4.12.0 | MIT | none | active | **SaaS test subset only**: real Postgres RLS + real Redis pub-sub that SQLite/in-memory can't fake |
| **SocketIOClient** | 3rd | current stable | MIT | none | active | .NET Socket.IO client for the supporter-event sockets (StreamElements/Streamlabs/Tipeee/TreatStream) (Engine.IO v3, `EIO=V3`); `supporter-events.md` |

### 1c. Dev-time only (not shipped at runtime)

| Name | Party | Version | License | Why |
|------|-------|---------|---------|-----|
| NSwag / NJsonSchema | 3rd | NSwag 14.x / NJsonSchema 11.x | MIT | Generate Helix DTOs from Twitch OpenAPI spec at build; committed, domain-foldered. **Not Roslyn**, not a runtime dep |
| Cronos *(if cron needed)* | 3rd | current stable | MIT | Cron next-fire calc with correct DST/leap handling — the one part of scheduling worth not hand-rolling |
| DistributedLock.Postgres *(SaaS run-once / leader-election)* | 3rd | 1.3.1 | MIT | **Decided:** SaaS run-once / leader-election lock. Wraps `pg_try_advisory_lock` with async RAII/renewal behind `IRunOnceGuard` (no-op on lite) |

---

## 2. AVOIDED — third-party we deliberately do NOT take

| Avoided package | License/risk | What we use instead |
|-----------------|--------------|---------------------|
| **FluentAssertions ≥ 8** | **Xceed proprietary, $130/dev/yr commercial** — live license violation for a monetized SaaS | **AwesomeAssertions 9.4.0** (Apache-2.0, drop-in) or Shouldly 4.3.0 (MIT) |
| **coverlet.collector 10.0.0** | VSTest-only — **broken** under .NET 10 native `dotnet test`/MTP | Microsoft.Testing.Extensions.CodeCoverage (`dotnet test --coverage`) |
| **Moq** | SponsorLink email-hash exfiltration (4.20.0) | NSubstitute |
| **Newtonsoft.Json** | dead reference (pinned 13.0.4, zero source usage) | System.Text.Json (in-box) + native .NET 10 JsonPatch |
| **AspNetCore.HealthChecks.NpgSql** | stale (net8-targeted, last 2024-12-19); wraps a trivial connection open | In-box `AddCheck` over Npgsql / `AddDbContextCheck` |
| **Swashbuckle** | dropped from MS templates; heavier | Microsoft.AspNetCore.OpenApi (+ Scalar UI) |
| **Serilog** (+ sinks/enrichers) | 5 genuine 3rd-party pkgs duplicating ILogger+OTel; two pipelines | `ILogger` + `[LoggerMessage]` source-gen + OpenTelemetry (gains traces+metrics) |
| **MediatR** | forbidden by project rule; adds indirection | Direct typed-interface DI calls |
| **MassTransit** | heavyweight bus; duplicates in-process EventBus | Existing `EventBus` (lite) + thin `RedisEventBus` over `ISubscriber` (SaaS) |
| **FusionCache** | genuine 3rd-party hybrid cache now subsumed by MS | Microsoft.Extensions.Caching.Hybrid |
| **Finbuckle.MultiTenant** | resolution/store we already have | `CurrentTenantService` + EF10 named query filters |
| **FluentMigrator** | duplicates EF Migrations; doesn't remove two-dialect reality | EF Migrations (two provider assemblies, CI-gated) |
| **EFCore.NamingConventions** | cosmetic snake_case only (YAGNI) | Defer; hand-roll a convention if ever wanted |
| **Innofactor/EfCore.Json converters** | ~30 LOC trivially hand-rolled, need full `ValueComparer` control | Hand-rolled `ValueConverter<T,string>` + `ValueComparer` convention |
| **Quartz.NET / Hangfire** | force shared-RDBMS store + 2nd self-host config path; Hangfire is LGPL | `BackgroundService` + `PeriodicTimer` + `Channels`; event-store covers durability |
| **TwitchLib** (umbrella + IRC client) | umbrella stale since 2022; IRC client = dead-end (100-channel join cap); doesn't solve conduits/rate-limit | Hand-rolled Helix client + EventSub/conduits (in-box WS/HMAC) |
| **ClearScript.V8** | interop bridge, **not** a multi-tenant isolation boundary; heavy native V8 | Wasmtime (SaaS) / Jint (lite) |
| **Duende IdentityServer** | Community license excludes redistribution **and** customer-facing SaaS — both apply here (we'd otherwise clear the **< $1M USD annual gross revenue** Community gate while pre-revenue, but those two exclusions disqualify us regardless; revenue ceiling bites once monetized). License effective date **2026-06-02** — re-read before quoting | OpenIddict (if an issuer is needed at all) |
| **NSec / BouncyCastle** | only needed for Ed25519 (no native EdDSA on .NET 10) | Choose `rsa-sha256` for federation peer keys → in-box `System.Security.Cryptography` |
| **MiniValidation** | niche closed in .NET 10 (built-in `AddValidation` now recurses) | In-box `AddValidation()` source generator |
| **AWS KMS SDK** | 3rd-party, large | Azure Key Vault (2nd-party, MIT) by default; AWS only if hosting on AWS |

---

## 3. Per-subsystem decisions

### Web API host (controllers, versioning, errors, rate-limit, caching, health, CORS, OpenAPI)
**Decision:** stay ~100% in-box. Keep **controllers** (better than minimal APIs for the per-action min-level authz ladder, both GA in .NET 10). Keep Asp.Versioning 10.0.0; **add the missing `AddApiExplorer(GroupNameFormat="'v'VVV")`** and hand-roll an `IOpenApiDocumentTransformer` for per-version docs (avoid the RC `Asp.Versioning.OpenApi` until GA). **Re-check NuGet for a GA tag before committing the hand-roll:** the RC (`10.0.0-rc.1`) already ships the final API surface and `WithDocumentPerVersion()` — the exact per-version-doc feature being hand-rolled here. If a GA tag has landed, drop the custom `IOpenApiDocumentTransformer` and use `WithDocumentPerVersion()` instead (don't build what MS now ships). Replace the custom `GlobalExceptionMiddleware` with **`AddProblemDetails()` + `IExceptionHandler` + `UseExceptionHandler()`** → RFC 9457 (supersedes 7807) for free. Drop the stale `AspNetCore.HealthChecks.NpgSql` for an in-box `AddCheck`. **Accept one 3rd-party: Scalar** (dev-only UI). Best practice (2026-06): rate-limit counters are **per-instance** — SaaS multi-node needs a Redis-backed limiter (no in-box distributed limiter exists); output caching ≠ response caching, never use `IDistributedCache` as the output-cache store.

### Persistence — EF Core 10, one model across Postgres + SQLite
**Decision:** in-box EF + the one genuine 3rd-party **Npgsql** provider. Hand-roll four small portable pieces: (1) `System.Text.Json` `ValueConverter`+`ValueComparer` convention for all `[VC:JSON]` columns — **NOT** EF10 `ToJson()`/`jsonb` (provider-specific, breaks the one-model rule; SQLite has no `ToJson`); (2) **EF10 named query filters** for soft-delete + tenant (fixes the no-op `ApplyTenantFilter`); (3) Postgres-only `DbConnectionInterceptor` for `SET/RESET app.tenant_id` RLS (caveat: pooling leaks tenant context if not `RESET`); (4) `TenantSequences` table for per-tenant monotonic IDs (SQLite has no sequences; identity is global on both). Two migration assemblies, CI-gated against drift. **Pin `SQLitePCLRaw.bundle_e_sqlite3 ≥ 3.0.3`** (package version; bundles engine 3.50.4.5 — CVE-2025-6965; `3.50.x` is the engine line, not the package version). Remove the live Postgres-only `.HasColumnType("jsonb")` in `ChannelConfiguration.cs`.

### Distributed cache + pub/sub (`ICache` / `IEventBus`)
**Decision:** all-Microsoft. Re-implement `ICacheService` on **HybridCache** — L1-only for self-host (no Redis), L1+Redis-L2 for SaaS (`AddStackExchangeRedisCache`). Keep the in-process `EventBus` for lite; add a thin `RedisEventBus : IEventBus` over `ISubscriber` (StackExchange.Redis, transitive) for SaaS — one publish-once / subscribe-and-redispatch boundary to avoid double-delivery. **Pin StackExchange.Redis at 2.13.17** until the 3.0 RESP3-default line bakes. **SaaS cache/pub-sub server = Garnet (MIT, Microsoft)** — chosen for its permissive MIT license, which keeps the SaaS deployment off the AGPLv3 obligations that Redis 8 would impose; the .NET client stays **StackExchange.Redis (2.13.17)** pointed at Garnet's RESP endpoint (Garnet speaks RESP, so the client is unchanged). Patch the server for the 2025-26 RCEs per its own advisory track.

### Realtime — SignalR
**Decision:** server stays 100% Microsoft. `AddSignalR().AddMessagePackProtocol()` (run JSON + MessagePack both enabled); add `SignalR.StackExchangeRedis` **only in the SaaS profile**; self-host is single-process in-memory. **Bump MessagePack protocol 10.0.8 → 10.0.9** to avoid a split MessagePack version. Auth: do **not** use the long-lived user JWT for OBS overlay sockets — mint a per-channel capability **OverlayToken**; on WebSocket the token is validated once at connect, so use `WithStatefulReconnect` + fresh-token-on-reconnect, and **scrub `access_token` from logs**. Clients: `@microsoft/signalr` for OBS overlays (2nd-party JS); KMP dashboard → **SignalRKore 0.9.13 (Apache-2.0)** is the decided realtime client (single KMP-common client across targets), with a hand-rolled SignalR-over-Ktor-WebSocket client as the documented fallback if SignalRKore goes unmaintained (`2026-06-16-frontend-structure.md` §9).

### AuthN / OIDC federation
**Decision:** split the three jobs. **Resource server + OIDC client = 100% MS** (JwtBearer + OpenIdConnect with `Authority`/`MetadataAddress`). Two immediate security fixes: migrate off legacy `System.IdentityModel.Tokens.Jwt` `JwtSecurityTokenHandler` → **`JsonWebTokenHandler` 8.19.1**, and move signing **HS256 → RS256/ES256** (HS256 is unfederatable — sharing the secret = sharing signing power). **Pin DataProtection ≥ 10.0.7 and rotate the key ring** (CVE-2026-40372, CVSS 9.1) — but the affected configuration is **platform-scoped**: it hits .NET 10 on **Linux/macOS/non-Windows**, OR .NET Framework / netstandard2.0 targets on any OS. A .NET 10 app on **Windows is *not* in the primary affected config**. So the **SaaS profile on Linux containers IS exposed** (pin + rotate mandatory); a **Windows self-host of the lite profile likely is not** (still pin defensively; rotation only required if exposed). **OIDC provider/issuer:** adopt **OpenIddict 7.5.0** *only if* first-party SSO or a real OIDC handshake is needed — bus-factor risk is moderate (led by Kévin Chalet but **commercially sponsored by Rock Solid Knowledge**, not a lone hobby project), further mitigated by Apache-2.0 forkability; reject Duende. (Duende rejection: a **pre-revenue** NomNomzBot *would* qualify for the free Community Edition today — its eligibility gate is **< $1M USD annual gross revenue** — but the **redistribution + customer-facing-SaaS exclusions** disqualify us regardless, and the revenue ceiling would bite once monetized. Re-read the Duende license before quoting; it has a fresh effective date of **2026-06-02**.) **Cross-instance federation** = the schema's signed-event model (peer public keys, KeyId rotation), **not** OIDC-mTLS: use **`rsa-sha256` → zero 3rd-party** (avoid Ed25519/NSec); mTLS transport via native Kestrel/HttpClient client certs.

### Crypto / secrets — token vault, envelope DEKs, crypto-shred
**Decision:** hand-roll the **AEAD + envelope + crypto-shred data-plane** on in-box `System.Security.Cryptography` (glue, not primitives — permitted). AES-256-GCM with `tagSizeInBytes:16`; **AAD = tenantId‖provider‖tokenType‖keyVersion** (anti-transplant); 96-bit random nonce per row; HKDF for purpose-separated subkeys; **per-subject/per-tenant DEKs** make crypto-shred O(1) (destroy the DEK row). **First action regardless of profile:** replace the current `EncryptionService` (AES-CBC, no MAC, key=SHA256(rawKey), no AAD — live cross-tenant transplant defect) with the AesGcm+AAD service and re-encrypt stored tokens. KEK custody: **lite** = file keystore (0600, cross-platform default) + optional Windows DPAPI; **SaaS** = the one place we accept a vendor SDK — **Azure Key Vault `Azure.Security.KeyVault.Keys` 4.10.0** (MIT, EU Managed-HSM), loaded only in the `kms_envelope` DI branch so the lite binary carries zero crypto 3rd-parties.

### Sandbox execution — user-authored TS/JS
**Decision:** the design's split survives the mid-2026 truth check. **SaaS = Wasmtime 44.0.0** (run **only x86_64-Cranelift** — it dodged both April-2026 *critical* sandbox escapes; never enable Winch or aarch64-Cranelift). **But x86_64-Cranelift is not advisory-free** — the same April-2026 batch still hit it with lower-severity issues (CVE-2026-24116 f64.copysign, CVE-2026-34944 f64x2.splat), so the fast-patch SLA in tension #6 is **mandatory, not optional hedging**. **lite = Jint 4.9.2** (resource-safety threat model = single trusted operator). Both behind one `IScriptExecutor`. Reject ClearScript/V8 (not an isolation boundary). Compensating controls: host-call budget + wall-clock watchdog (epoch interruption doesn't preempt host calls — CVE-2026-27195 / issue #9188), WIT value-in/value-out contract, fail-closed unknown-action/condition, SSRF egress allowlist. **Blocker:** the capability-broker model is void until the 3 live defects are fixed — cross-tenant IDOR in `TenantResolutionMiddleware`, missing tenant query filter/RLS, transplantable token crypto.

### Twitch integration — Helix + EventSub
**Decision:** **hand-rolled** core (the textbook case where a 3rd-party doesn't earn its place). `HttpClient` via `IHttpClientFactory`; DTOs codegen'd from the Helix OpenAPI spec at dev-time (NSwag, committed, not Roslyn); `System.Text.Json`; **`Microsoft.Extensions.Http.Resilience` 10.7.0** for retry/breaker + a **custom `DelegatingHandler`** for Twitch's header-driven adaptive rate limiting (the generic pipeline can't do it). EventSub: **lite = `ClientWebSocket`** (in-box); **SaaS = conduits + webhooks** with in-box `HMACSHA256` signature verification + challenge-response + replay rejection. Retire `TwitchIrcService` (IRC join cap = 100 channels) → read via EventSub `channel.chat.message`, send via Helix Send Chat Message API. Net new deps: **one** (MS.Http.Resilience, 2nd-party).

### Logging / observability
**Decision:** **drop Serilog**, standardize on `ILogger` + `[LoggerMessage]` source-gen + **OpenTelemetry** (OTLP for logs+traces+metrics; the CNCF/.NET-10/Aspire default = acceptable 2nd-party). Set `IncludeFormattedMessage`/`ParseStateValues`/`IncludeScopes` so structured fields survive (closes the old reason to keep Serilog). DB spans via stable **Npgsql.OpenTelemetry** (avoid the beta EFCore instrumentation). One loss: no file sink — for lite keep `AddConsole()` and document "pipe to file / local OTel Collector". PII discipline: never log chat/usernames/tokens; add low-cardinality `tenant_id` as a scope/span attribute.

### Background jobs / scheduling / run-once
**Decision:** keep MS-native `BackgroundService` + `PeriodicTimer` + `Channels` (already shipped, zero-dep). Add the smallest thing only at real gaps: **SaaS run-once / leader-election** = **`DistributedLock.Postgres` 1.3.1 (MIT)** behind `IRunOnceGuard` (no-op on lite) — its async RAII/renewal over `pg_try_advisory_lock` is the picked mechanism, not a hand-roll; **cron** = `Cronos` (MIT) parser only, driven by our own job table. Reject Quartz/Hangfire (force shared-RDBMS store + 2nd self-host path; Hangfire LGPL). **Latent bug:** current `BackgroundService`s will double-fire on multi-instance SaaS until `IRunOnceGuard` lands.

### Validation / serialization
**Decision:** MS-only. `System.Text.Json` (already native); **consolidate the 6 duplicated `JsonSerializerOptions`** into one shared extension; enable **`.Strict` preset on untrusted inbound JSON** (EventSub, user pipelines, GDPR import). Source-gen `JsonSerializerContext` for hot/AOT/event-store types. Event-journal polymorphism: hand-roll a `JsonConverter<DomainEvent>` (discriminator + fail-closed + upcaster switch) over attribute-only `[JsonDerivedType]`. Request validation: in-box **`.NET 10 AddValidation()` source generator** (reached MVC parity — nested objects, collections, records, `IValidatableObject`); push async/uniqueness/allowlist rules into service-layer validators returning `Result<T>`. **Drop Newtonsoft.Json and the unused FluentValidation reference** (net −2 deps).

### Testing / quality
**Decision:** **remove FluentAssertions 8.10.0 immediately** (live license violation) → **AwesomeAssertions 9.4.0**. Migrate xunit v2 → **xunit.v3 3.2.2** + native MTP (drop Test.Sdk + VSTest runner). **Replace coverlet.collector** (VSTest-only, broken on MTP) with MS MTP coverage. Keep **NSubstitute**. Add **Mvc.Testing** (`WebApplicationFactory`) for end-to-end security tests. Integration DB: default to **SQLite + in-memory adapters** (zero deps, real code paths); add **Testcontainers** Postgres/Redis only for the SaaS subset that must prove **RLS tenant isolation** and real pub-sub. Highest-value tests: cross-tenant IDOR, tenant query-filter/RLS, AES-GCM AAD non-transplantability, fail-closed pipeline semantics.

---

## 4. Unavoidable-third-party justification

**Npgsql.EntityFrameworkCore.PostgreSQL (+ Npgsql).** Microsoft ships no first-party PostgreSQL EF provider, and Postgres is the SaaS database of record (RLS, scale, `jsonb` where provider-specific tuning is wanted). There is no credible alternative provider. It is maintained by Shay Rojansky, the Microsoft EF Core team's PostgreSQL lead, under the permissive OSI PostgreSQL License, with year-round activity (10.0.2 / Npgsql 10.0.3, 2026-05-27) and no open 10.x CVEs — making it the rare 3rd-party that is effectively 2nd-party-adjacent and clears the bar by default.

**StackExchange.Redis.** Any Redis path on .NET — HybridCache's L2, the SignalR backplane, the run-once lock — sits on this client; there is no in-box .NET Redis client and no serious competitor. It is MIT, maintained by Stack Overflow/Marc Gravell with ~1.1B downloads, and is pulled in *transitively* by the Microsoft caching/SignalR packages, so it is not a new owner-chosen dependency. The frightening 2025-26 CVSS-10 advisories (RediShell, DarkReplica) are against the Redis **server**, not this client — an ops patching concern, not a code dependency. We pin 2.13.17 and let the 3.0 RESP3-default line bake.

**Wasmtime (wasmtime-dotnet).** Multi-tenant execution of untrusted user JS requires a real isolation boundary. .NET 10 removed CAS/AppDomain isolation, so a hand-rolled "interpreter + reflection allowlist" is not a security boundary — exactly the rule against hand-rolling sandboxes. Wasmtime is the only memory-safe, deny-by-default, in-process boundary available, the WIT host-import surface *is* the capability allowlist, and it is actively maintained by the Bytecode Alliance under Apache-2.0-WITH-LLVM-exception. The April-2026 *critical* sandbox escapes did **not** affect the production-default x86_64-Cranelift backend (only Winch and aarch64-Cranelift), which we mandate. That backend is **not advisory-free**, however — the same (largest-ever, LLM-discovered) April-2026 batch still landed lower-severity x86_64 issues (CVE-2026-24116 f64.copysign, CVE-2026-34944 f64x2.splat). Mandating x86_64-Cranelift removes the Critical-escape exposure but **not** the need to patch promptly, which is why the fast-patch SLA (tension #6) is a hard requirement rather than an optional hedge.

**OpenIddict** *(conditional — only if we run an OIDC/OAuth2 issuer).* ASP.NET Core ships only the OIDC *client* handler and the *resource-server* validator — there is no in-box authorization server, and none on the .NET 10 roadmap. Standing up `/authorize` + `/token` + JWKS + PKCE + grant validation ourselves is hand-rolling the auth protocol, which the rule forbids. OpenIddict is the de-facto open-source ASP.NET Core OIDC server with a native EF Core store fitting our dual-provider model, under permissive Apache-2.0. The only other mature option, Duende, is license-encumbered precisely for an AGPL self-host + multi-tenant SaaS product: although a pre-revenue NomNomzBot would clear Duende Community Edition's **< $1M USD annual gross revenue** eligibility gate today, the **redistribution and customer-facing-SaaS exclusions** disqualify us regardless of revenue, and the revenue ceiling would bite once monetized (re-read the Duende license — fresh effective date **2026-06-02** — before quoting terms). OpenIddict's bus-factor is moderate, not severe: it is led by Kévin Chalet but **commercially sponsored by Rock Solid Knowledge**, and the Apache-2.0 license keeps it forkable.

**AwesomeAssertions** *(the rich-assertions library — decided).* FluentAssertions ≥ 8 moved to the proprietary Xceed license ($130/dev/yr commercial) — a live violation for a monetized SaaS. The testing standard ("assert data shape/invariants, not surface") wants object-graph/collection assertions. AwesomeAssertions is the community **Apache-2.0 fork of FluentAssertions** (7) with a namespace-compatible API (near-one-line migration), and is the binding pick for rich assertions across the test suite. Its single-org-fork bus-factor is the residual risk; Shouldly (MIT, non-fork) stands as the contingency replacement should that fork stall, at the cost of a syntax rewrite.

**Testcontainers** *(SaaS test subset only).* SQLite-as-Postgres-substitute is a known integration-test trap — it will not catch RLS gaps, jsonb/Npgsql-converter bugs, or migration drift, which are the security-critical SaaS isolation invariants. Real Postgres/Redis cannot be faked for those tests, and hand-rolling container lifecycle is wasted effort. Testcontainers is MIT, actively maintained, and confined to one SaaS test assembly so its Docker requirement never bleeds into the lite profile or unit suites.

---

## 5. Resolved decisions (final — see `2026-06-16-decisions-pending-confirmation.md` #1–#10)

Every item below is a **binding decision**; the rationale is retained for context. Items phrased as a prerequisite are plan **dependencies** (ordering is the task board's job), not deferrals.

1. **Distributed rate limiting (SaaS).** Multi-node per-node limits would be N× — a correctness gap, not just perf. *Decided:* the `IRateLimiter` adapter — Redis-backed token buckets (SaaS) / in-process `System.Threading.RateLimiter` (self-host), with a global Helix bucket + per-channel fair sub-budgets + per-tenant inbound caps (`spec/scaling-qos.md` §4).

2. **Jint is not a security boundary.** *Decided:* the lite profile is **single-trusted-operator only** (hard commit) — out of the threat model is any multi-user/shared lite deployment; that path requires Wasmtime + per-tenant process isolation.

3. **OIDC issuer vs. resource-server-only.** *Decided:* adopt **OpenIddict, feature-gated** — the authorize/token/JWKS surface is stood up only when `federation`/`multi_user_sso` is enabled; otherwise JwtBearer-only resource-server with signed-event federation. The issuer surface is hardened per `spec/federation-oidc.md`.

4. **HS256 → asymmetric signing.** *Decided:* RS256/ES256 issuance + published JWKS is a **hard dependency** of federation — federation does not start before it lands (`spec/federation-oidc.md`).

5. **StackExchange.Redis 3.0.** *Decided:* **hold at 2.13.17**; a 3.x bump requires a RESP2→RESP3 reply-shape audit first.

6. **Wasmtime/Cranelift advisory exposure.** *Decided:* **x86_64-Cranelift only**, with a mandatory advisory-subscription + fast-patch SLA on the SaaS edge (required, not an optional hedge).

7. **File logging.** *Decided:* **console-only for lite** (OTel + console output); no custom file `ILoggerProvider` (YAGNI — operators tail container/console).

8. **Multi-instance double-fire.** *Decided:* `IRunOnceGuard` (pg advisory lock) is a **hard dependency** of any multi-instance SaaS deploy — single-fire BackgroundServices ship before multi-node (`spec/scaling-qos.md` §1).

9. **DataProtection CVE-2026-40372.** *Decided:* pin DataProtection **≥ 10.0.7** on all profiles AND **rotate the key ring on every Linux/non-Windows (SaaS) deployment** — rotation (not just a version bump) invalidates forged-eligible tokens; SaaS Linux containers are exposed, a Windows lite self-host is not.

10. **Crypto-shred completeness.** *Decided:* **O(1) ciphertext shred (`[PII-shred]`) is the launch posture**; row-level plaintext scrub (`[PII-scrub]` usernames/messages) + one-subject-per-event enforcement is **committed dependency work** for the plaintext/multi-subject paths (`spec/gdpr-crypto.md`), not a deferral of the ciphertext path.

---

## Sources (verified 2026-06-16)

- DataProtection CVE-2026-40372: https://github.com/dotnet/announcements/issues/395 · https://www.bleepingcomputer.com/news/microsoft/microsoft-releases-emergency-security-updates-for-critical-aspnet-flaw/
- FluentAssertions license / AwesomeAssertions: https://www.infoq.com/news/2025/01/fluent-assertions-v8-license/ · https://www.nuget.org/packages/AwesomeAssertions
- OpenIddict 7.5.0 (Apache-2.0): https://www.nuget.org/packages/OpenIddict · https://github.com/openiddict/openiddict-core
- StackExchange.Redis 2.13.17: https://www.nuget.org/packages/stackexchange.redis/ · https://stackexchange.github.io/StackExchange.Redis/ReleaseNotes
- Wasmtime April-2026 advisories (x86_64-Cranelift escaped the *Critical* sandbox escapes but was still hit by lower-severity issues — not advisory-free): https://bytecodealliance.org/articles/wasmtime-security-advisories · aarch64 Critical CVE-2026-34971 https://advisories.gitlab.com/pkg/cargo/wasmtime/CVE-2026-34971/ · x86_64 lower-sev CVE-2026-24116 (f64.copysign) https://github.com/advisories/GHSA-vc8c-j3xm-xj73 · x86_64 lower-sev CVE-2026-34944 (f64x2.splat) https://advisories.gitlab.com/pkg/cargo/wasmtime/CVE-2026-34944/
- Npgsql 10.0.2 / 10.0.3: https://www.nuget.org/packages/npgsql.entityframeworkcore.postgresql/ · https://www.nuget.org/packages/npgsql/
- Jint (latest stable 4.9.2): https://www.nuget.org/packages/Jint · https://github.com/sebastienros/jint
- SQLite CVE-2025-6965 / SQLitePCLRaw.bundle_e_sqlite3 ≥ 3.0.3 (package version; bundles engine 3.50.4.5): https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/ · https://github.com/ericsink/SQLitePCL.raw/issues/636 · https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- HybridCache GA: https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/
- EF Core 10 named query filters: https://www.milanjovanovic.tech/blog/named-query-filters-in-ef-10-multiple-query-filters-per-entity
- .NET 10 AddValidation source generator: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation · https://medium.com/@adrianbailador/minimal-api-validation-in-net-10-8997a48b8a66
- OpenTelemetry .NET: https://opentelemetry.io/docs/languages/dotnet/logs/ · https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
- Twitch conduits / IRC migration: https://dev.twitch.tv/docs/eventsub/handling-conduit-events/ · https://dev.twitch.tv/docs/chat/irc-migration/
- AesGcm .NET 10 / cross-platform crypto: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.-ctor?view=net-10.0 · https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography
- Scalar: https://github.com/scalar/scalar/tree/main/integrations/dotnet/aspnetcore
- xUnit v3 / MTP: https://xunit.net/releases/v3/3.2.2 · https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage
- Testcontainers: https://www.nuget.org/packages/Testcontainers/
- Duende Community Edition eligibility (< $1M USD gross revenue; redistribution + customer-facing exclusions; license effective 2026-06-02): https://duendesoftware.com/products/communityedition · https://duendesoftware.com/license/SoftwareLicense.pdf
- OpenIddict commercial sponsorship (Rock Solid Knowledge): https://github.com/openiddict/openiddict-core · https://kevinchalet.com/2025/07/07/openiddict-7-0-is-out/
- Asp.Versioning.OpenApi RC ships final API + `WithDocumentPerVersion()` (re-check for GA): https://devblogs.microsoft.com/dotnet/api-versioning-in-dotnet-10-applications/ · https://github.com/dotnet/aspnet-api-versioning/wiki/OpenAPI-Options

---

## Corrections applied (adversarial fact-check, verified 2026-06-16)

- **SQLitePCLRaw pin corrected** — `bundle_e_sqlite3 ≥ 3.50.2` was an impossible/incorrect pin (`3.50.x` is the SQLite *engine* line, not the package version). Changed every occurrence (master table, Persistence decision, Sources) to **`≥ 3.0.3`** (package version, published 2026-05-07, bundles engine 3.50.4.5).
- **CVE-2026-40372 (DataProtection) re-scoped as platform-conditional** — removed the "highest-severity item in the whole stack / everyone exposed" framing; the affected config is .NET 10 on Linux/macOS/non-Windows OR .NET Framework/netstandard2.0 on any OS. Noted SaaS-on-Linux IS exposed (pin + rotate mandatory); a Windows lite self-host likely is not. Updated master table, Auth decision, and tension #9. Kept the ≥10.0.7 pin + key-ring-rotation guidance.
- **OpenIddict bus-factor softened** — replaced "single maintainer (key-person risk)" with "led by Kévin Chalet, commercially sponsored by Rock Solid Knowledge" across the master table, Auth decision, justification, and tension #3 (retitled). Kept the Apache-2.0 forkability mitigation; conclusion unchanged.
- **Duende rejection reasoning expanded** — added that Community Edition's actual gate is **< $1M USD annual gross revenue** (a pre-revenue NomNomzBot would qualify today), but the **redistribution + customer-facing-SaaS exclusions** disqualify us regardless, with the revenue ceiling biting once monetized. Flagged the license's fresh effective date (**2026-06-02**) to re-read before quoting. Updated AVOIDED table, Auth decision, and justification. Conclusion (reject Duende → OpenIddict) unchanged.
- **Asp.Versioning.OpenApi GA re-check note added** — the RC (`10.0.0-rc.1`) already ships the final API + `WithDocumentPerVersion()` (the exact per-version-doc feature being hand-rolled); added a note to re-check NuGet for a GA tag before committing the hand-roll and to drop the custom `IOpenApiDocumentTransformer` if GA has landed (Web API host decision).
- **Wasmtime strengthened — x86_64-Cranelift is not advisory-free** — clarified that x86_64-Cranelift escaped only the April-2026 *Critical* sandbox escapes; the same batch still hit it with lower-severity issues (CVE-2026-24116 f64.copysign, CVE-2026-34944 f64x2.splat). Updated the master table, Sandbox decision, justification, and tension #6 to make the fast-patch SLA a hard requirement rather than optional hedging. Added the two lower-severity CVE references to Sources.
