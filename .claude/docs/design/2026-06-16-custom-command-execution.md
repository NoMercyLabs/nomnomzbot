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

## Red-Team findings — resolved and open

The original verdict was NEEDS-WORK. Status of each finding against current code:

### Resolved
1. **Cross-tenant IDOR.** `TenantResolutionMiddleware` now verifies the authenticated caller may act as the requested channel (`IChannelAccessService.CanResolveTenantAsync`). A mismatch fails closed with 403. Anonymous callers select a channel for public endpoints only, and suspended tenants are refused.
2. **Tenant query filter (app-level half).** `ITenantScoped` entities get a global query filter bound to `ICurrentTenantService` (`ModelBuilderExtensions`), next to the `DeletedAt` soft-delete filter.
3. **Transplantable token crypto.** The AES-CBC `IEncryptionService` is replaced by `IFieldCipher` (`AesGcmFieldCipher`, AES-256-GCM) with AAD = `CipherAad` (tenant + provider + key version).
4. **Fail-closed engine semantics.** `PipelineEngine` blocks the step on an unknown condition type, unknown action type and unknown block kind.
6. **Egress.** `HttpEgressAllowlist` (per-channel destinations), `EgressAddressGuard` (loopback, RFC-1918, link-local incl. 169.254.169.254, IPv4-mapped IPv6) and `EgressHttpClient` (redirects off).
9. **GDPR erasure.** `ErasureService` anonymizes the profile and hard-deletes chat messages, with an `ErasureRequest` pipeline and preview.

### Open
- **Postgres RLS (second half of #2).** `SET app.tenant_id` per connection plus RLS policies are not built. Tracked under the RLS owner question.
- **#5 WIT host-import contract.** A fuzz release gate for every host import does not exist yet.
- **#7 Aggregate DoS controls.** Per-execution host-call budget, fuel and epoch interruption exist (`JintScriptExecutor`, `WasmtimeScriptExecutor`). Global (cross-channel) concurrency and admission control, per-tenant rate limits on side-effecting imports, and a cumulative `Wait` cap need a re-check.
- **#8 Interim OS-confined worker + Jint.** Still rejected as a multi-tenant boundary unless it is one confined process per tenant per execution.

### Key files
`Api/Middleware/TenantResolutionMiddleware.cs` (#1), `Infrastructure/Platform/Auth/CurrentTenantService.cs` (#1), `Infrastructure/Platform/Persistence/Extensions/ModelBuilderExtensions.cs` (#2), `Infrastructure/Platform/Security/AesGcmFieldCipher.cs` (#3), `Infrastructure/Platform/Pipeline/PipelineEngine.cs` (#4/#7), `Infrastructure/CustomCode/` (`JintScriptExecutor`, `WasmtimeScriptExecutor`; #5/#7), `Infrastructure/Sandbox/` (`EgressAddressGuard`, `EgressHttpClient`; #6), `Infrastructure/Identity/ErasureService.cs` (#9), `Infrastructure/Music/PipelineActions/SongRequestAction.cs` (correct broker pattern, only as safe as `ctx.BroadcasterId`).
