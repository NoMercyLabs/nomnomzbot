# Custom Command Authoring & Execution — Architecture Decision

Source: execution-model research + red-team workflow (`wf_2a82dee7`), 2026-06-16.
Status: **directionally decided**. Red-team verdict: **NEEDS-WORK** (must-fix list below).
Authoring language: **TypeScript** — DECIDED 2026-06-16 (DX preserved via tsserver/Monaco; safely sandboxable in both modes; keeps the No-Roslyn rule).

## Decisions

1. **Capability ladder (3 tiers).** Each tier absorbs a slice of demand so the tier above never grows unbounded.
   - **T1 Template commands** — string templates over the 90+ template vars (`VariableResolver`), zero logic. Default "new command" UX. ~80% of commands.
   - **T2 Visual pipeline** — ordered typed action blocks + conditions/branch, rendered from the action registry. The heart of the product.
   - **T3 Code escape hatch** — a single sandboxed `RunCode` block droppable inside a T2 pipeline. The pressure valve that caps block sprawl. **T3 is a block *within* T2, not a separate authoring surface.**

2. **Execution runtime — split by deployment mode, behind one `IScriptExecutor`.**
   - **SaaS (multi-tenant):** JS-in-WASM via **Wasmtime** (`wasmtime-dotnet`). The only memory-safe, deny-by-default, in-process boundary on .NET 10 (CAS/AppDomain sandboxing is gone). WIT host API = the capability allowlist; tokens injected host-side, never in the sandbox.
   - **Self-hosted (single-user):** **Jint** (managed JS interpreter). Threat model inverts — optimise DX + resource-safety, not isolation.
   - **Language: TypeScript/JavaScript, NOT C#.** Untrusted C# cannot be sandboxed on .NET 10 (full host trust); this also keeps the **"No Roslyn" rule intact**. Author in TypeScript for full type-safety + autocomplete; executes as JS in both runtimes. *(DECIDED: TypeScript — confirmed 2026-06-16. C# was rejected: unsandboxable multi-tenant, and self-host-only C# would split into two languages.)*

3. **Visual blocks — EXTEND the existing pipeline engine, do NOT build node-RED.** `CommandActionRegistry` already *is* the node registry; `ICommandAction` is self-describing (`Type`/`Category`/`Description`/`ExecuteAsync`). ~18 blocks for 80% coverage; `RunCode` + `HttpRequest` absorb the long tail. Stay linear-with-branch, not free-form DAG (YAGNI).

4. **Trust boundary = capability broker (not the process edge).** User logic names an *intent* bound to a resource owner it already controls — never a credential, URL, or another tenant. Per-tenant pre-authorized client injection (`SpotifyClient` bound to `ctx.BroadcasterId`, token injected host-side). `SongRequestAction` already does this — make it an enforced invariant (save-time schema validator + architecture test forbidding tenant/credential/url params on any `ICommandAction`).

5. **Token storage — one `IIntegrationTokenVault`, two impls, DI-selected by mode.** SaaS = envelope encryption (per-tenant DEK in AES-256-GCM wrapped by KMS/HSM KEK; AAD = tenant+provider+keyVersion; crypto-shred = GDPR erasure in O(1)). Self-host = local AEAD key (AES-256-GCM + HKDF). The vault is the only decryptor; tokens never leave the client factory.

6. **DX (Streamer.bot-grade) preserved:** one typed `bot` facade (`bot.chat.send`, `bot.music.queue`, `bot.vars`, `bot.args`); editor types generated from the same source the engine validates against (zero drift); validate-on-save (fail at save, never mid-stream); per-unit cache swap keyed by `tenant+version` for no-restart hot reload.

## Red-Team verdict: NEEDS-WORK

### Three LIVE pre-existing defects in current code (fix regardless of the code tier)
1. **Cross-tenant IDOR (live breach).** `TenantResolutionMiddleware` sets tenant from route / `X-Channel-Id` header / `channelId` query with NO check the JWT subject owns that channel → any tenant acts as another via `?channelId=<victim>`, using the victim's injected Spotify/Discord client. The entire capability-broker model is void until `ctx.BroadcasterId` is unforgeable. Fix: derive + verify channel from the authenticated principal; route/header/query may only select among provably-owned channels; mismatch = 403.
2. **No tenant query filter / no Postgres RLS.** Only a `DeletedAt` soft-delete filter exists. Cross-tenant reads rely on every service remembering `.Where(BroadcasterId == ...)`. Fix: `ITenantScoped` global query filter bound to `CurrentTenantService` + Postgres RLS (`SET app.tenant_id` per connection).
3. **Transplantable token crypto.** AES-CBC, no MAC, key = `SHA256(rawKey)`, no AAD → a token ciphertext copies from one tenant's row into another and decrypts under the shared key. Fix: AES-256-GCM with AAD = tenant+provider+keyVersion, key via HKDF.

### Code-tier must-fixes (before T3 `RunCode`/`HttpRequest` ships)
4. **Fail-closed engine semantics.** Currently unknown action = skip, unknown condition = treat-as-true, action error = continue → security gates are structurally bypassable. Make unknown type/condition a hard fail; reject unknown types at save-time validation.
5. **Lock the WIT host-import contract.** Value-in/value-out only (copied/owned/value types); no host handles, shared memory, or re-entrant callbacks; no PII fields (viewer email/IP) exposed to the `bot` facade; fuzz every import as a release gate.
6. **No general `HttpRequest` egress.** Per-channel, owner-approved destination allowlist; FQDN-pinned resolution (DNS-rebind + DNS-exfil defense); no redirects; response-size cap; SSRF-hardened (block loopback/RFC-1918/169.254.169.254).
7. **Aggregate + host-call DoS controls.** Per-execution host-call budget + wall-clock-incl-host watchdog (covers Wasmtime epoch gap #9188 on tight host-call loops); per-tenant rate limits on side-effecting imports; **global** (not just per-channel) concurrency + admission control; bounded `StepLogs`; cap step count at save-time; seconds-not-minutes timeout; cumulative `Wait` cap.
8. **Reject "OS-confined worker + Jint" interim as a multi-tenant boundary** unless it is one confined process *per tenant per execution* — Jint multiplexed inside a shared worker is Jint-as-sole-boundary between co-tenants.
9. **GDPR erasure beyond tokens.** Hard-delete/anonymization for viewer/chat PII (soft-delete ≠ erasure); include execution logs/telemetry in the erasure path with TTL + PII exclusion.

Items 1–3 are live defects in the current codebase, independent of whether the code tier ever ships.

### Key files
`Api/Middleware/TenantResolutionMiddleware.cs` (#1), `Infrastructure/Services/Identity/CurrentTenantService.cs` (#1), `Infrastructure/Persistence/Extensions/ModelBuilderExtensions.cs` + `*Configuration.cs` (#2), `Infrastructure/Services/Security/EncryptionService.cs` (#3), `Infrastructure/Pipeline/PipelineEngine.cs` (#4/#7), `Infrastructure/Pipeline/Actions/MusicActions.cs` (`SongRequestAction` — correct broker pattern, only as safe as `ctx.BroadcasterId`).
