# Interface Specification — Platform Admin: Content Authoring & Propagation

**Status:** Implementable. Code from this directly.
**Sources (authoritative):** `roles-permissions.md` (Plane C — platform IAM, `IamAuditLog` O.9, `IPlatformIamService.AuthorizePlatformAsync`, §5 cell format); `PRODUCT-ALIGNMENT.md` (D-series decisions, `saas` restricted-option marker); the live tree — `AdminController`, `PlatformAdminController` (9 routes), `PlatformIamController`, `AdminBillingController`, `FeatureFlagAdminController`, `PlatformAnalyticsController`, `AdminSpamDefenseController` (10 admin controllers, 11 admin-plane tabs, 2,732 lines, measured 2026-09-04); the 17 platform-content seeders (`DefaultCommandsSeeder`, `FirstPartyWidgetCatalogueSeeder`, `RaidFlowSeeder`, `RaidStartFlowSeeder`, `RaidCommitFlowSeeder`, `EventResponseDefaultsSeeder`, `TtsVoiceSeeder`, `BillingTierSeeder`, `IamCatalogSeeder`, `ActionDefinitionSeeder`, `PronounSeeder`, `ConfigSeeder`, + 5 more) in `NomNomzBot.Infrastructure/Content/*`; the existing tenant-scoped entities `Command` (`Domain/Commands/Entities/Command.cs`), `Widget` + `WidgetVersion` (`Domain/Widgets/Entities/Widget.cs` — already carries the `GalleryItemId`/`InstalledSourceRevision` staleness pattern this spec generalizes), `CodeScript` + `CodeScriptVersion` (`Domain/CustomCode/Entities/*`, append-only version rows), `Pipeline` (`Domain/Commands/Entities/Pipeline.cs`).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/...")]`; surrogate PK `guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter; explicit types (never `var`); AGPL header on every source file.

**`saas` mode marker:** this whole subsystem is a **platform-employee surface** — it exists only where a Plane-C IAM principal exists at all, which is the `saas` deployment profile. `saas` mode is a **RESTRICTED option** reserved to NoMercy Labs under the AGPL-3.0 licence; a self-hosted operator never sees this plane (their own channel's content editors — `CommandsController`, `WidgetsController`, `PipelinesController`, `CodeScriptsController` — are the entire authoring surface they get, and platform content on `self_host_*` is simply what the seeders wrote once, unmanaged, exactly as today). Every screen and route in this spec carries this marker; none of it is built or exposed on a self-host profile.

---

## 1. Problem

Every piece of platform-shipped content — system commands, first-party widgets, system pipelines (raid flow), default event responses, TTS voices, billing tiers — is written **once**, by a C# seeder, into every tenant's own rows at onboarding time. There is no controller, no service, no dashboard screen that lets a platform operator **change** that content after the fact. The seeder principle that keeps this safe today (`seeders-adopt-empty-stubs`: adopt an empty stub, never overwrite built content, never match on name alone) is exactly why nothing can reach the 400 tenants who already have a copy — the seeder only ever runs once per tenant, at creation.

Two questions have to be settled before any of that surface is built. §2 settles them.

---

## 2. Decisions

### 2.1 Propagation — what happens to a tenant who renamed / edited / deleted their copy

**Decision: per-edit publish mode, chosen by the owner, blast radius shown before commit — never automatic, never silent.**

An owner editing a platform content definition creates a new **draft version** (§3.2) first — nothing tenant-facing changes yet. Publishing that version to tenants is a separate, explicit step that names one of three modes:

| Mode | What it touches | When to use it |
|---|---|---|
| `publish_as_new` | Nothing. Creates a new installable definition; existing tenant rows are completely untouched. | The new content is different enough it shouldn't retroactively change what tenants already have (a new built-in, a redesigned widget). Zero blast radius by construction. |
| `update_in_place_where_untouched` | Only tenant rows whose current content hash still equals the tenant's `PlatformSourceHash` (§3.3) — i.e. the tenant never edited their copy. | The common "I fixed a bug in the shipped default" case. A tenant who customized their copy keeps their customization; a tenant who never touched it gets the fix. This is the seeder principle's "adopt an empty stub" rule applied to an update instead of a create. |
| `force` | Every installed tenant row, regardless of local edits — local edits are overwritten. | Security fix, ToS-driven content change, or a broken default causing active harm. Gated by its own Critical-tier action key (§4), separate from `content:publish`, because it is the one mode capable of destroying tenant work. |

Before any of the three modes commits, `POST …/publish-preview` (§4) runs the **exact same selection query** the publish will use and returns the affected tenant count, a sample of affected tenant names, and — for `update_in_place_where_untouched` and `force` — the count of tenants who **would be skipped or overwritten** because they edited their copy. The UI renders this number before the confirm button is enabled (the standing rule: consequences must be visible before a save commits — the same pattern `pipeline-tree-and-editor.md` "Save-time blast radius" already uses for trigger-changing pipeline saves). No mode ever runs from a blind "Publish" button.

**Why the alternatives lose:**
- **Seeder-principle-only, no propagation path at all** (the status quo) loses because it makes "the owner fixed a broken default and wants it to reach people" structurally impossible — the only way to reach an existing tenant today is a one-by-one manual dashboard edit, which does not scale to hundreds of tenants and is the exact gap this slice exists to close.
- **Always overwrite (force-only)** loses because it silently destroys every tenant customization on every publish — it violates `consequences-must-be-visible` (nothing is shown before the blast lands) and the opt-in/default-deny house rule (a tenant never opted into having their edits clobbered).
- **Match tenant rows by name to decide "untouched"** loses because it is exactly the footgun `seeders-adopt-empty-stubs` already exists to prevent: a tenant who independently named their own custom command `!sr` would get silently overwritten by an update to the platform `!sr`. Untouched-ness is decided by **content hash**, never by name.

### 2.2 Seed vs template vs live — what a platform content row actually is

**Decision: platform content is an immutable, versioned template. A tenant installs a snapshot of a version; it is never a live pointer to a shared row.**

Concretely: a `PlatformContentDefinition` (one per shipped command/widget/pipeline/script, keyed by kind + natural key) owns an append-only sequence of `PlatformContentVersion` rows (§3.1–3.2) — the same append-only-version shape already used by `WidgetVersion` and `CodeScriptVersion`. A tenant's own `Command` / `Widget` / `Pipeline` / `CodeScript` row carries a nullable `PlatformSourceDefinitionId` + `PlatformSourceVersion` + `PlatformSourceHash` (§3.3), generalizing the `Widget.GalleryItemId` / `InstalledSourceRevision` pattern that already exists for gallery-installed widgets to all four content kinds. Installing (seeding, or a later `publish_as_new`) copies the version's payload into the tenant's row and stamps the provenance fields; it does not create a foreign-key dependency the tenant's row can't survive without.

An owner edit is therefore **a new immutable version plus a fan-out job** (§3.4) that walks tenant rows per the chosen publish mode and re-copies the payload — never a schema migration, never a live cross-tenant read.

**Why the other two lose:**
- **A row the seeder writes (current state)** loses because a plain seed has no version history, no revision number, and no way to tell "this tenant's copy is stale" from "this tenant edited it on purpose" — which is precisely why `WidgetGalleryItem` already had to grow `SourceRevision` on top of its seeder to represent this same problem for one content kind. Generalizing that field (as `PlatformSourceVersion`/`PlatformSourceHash`) rather than re-inventing a fourth ad hoc staleness marker is the smaller, already-validated change.
- **A live reference tenants point at** loses on three counts: (a) it removes per-tenant edit independence outright — a tenant could no longer rename or customize their copy while it's still "the platform command", because there would be nothing tenant-owned to edit; (b) a live pointer means a platform edit changes 400 tenants' *live* behavior the instant it saves, with no preview and no opt-out — a direct violation of `consequences-must-be-visible` and opt-in/default-deny; (c) every tenant-scoped read in this codebase runs through a global EF query filter scoped to one tenant (`ITenantScoped.BroadcasterId`) — a row genuinely shared and live-read across tenants breaks that isolation model everywhere else it's assumed (unique-index scoping, soft-delete filters, audit attribution), for a benefit (no copy step) that a versioned template gets for free via `publish_as_new`.

---

## 3. Entity shapes

All new entities live in `NomNomzBot.Domain/PlatformContent/Entities/` (new domain folder — platform-scoped content authoring is its own responsibility, distinct from the tenant-scoped `Commands`/`Widgets`/`CustomCode` folders it feeds). None are `ITenantScoped` — they are platform-global by design (§2.2).

### 3.1 `PlatformContentDefinition`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | `Guid.CreateVersion7()` |
| `Kind` | `string` | `command` \| `widget` \| `pipeline` \| `code_script` |
| `Key` | `string` | Natural key within `Kind` (e.g. `sr`, `raid-flow`, `now-playing-widget`); unique per `Kind` |
| `DisplayName` | `string` | |
| `Description` | `string?` | |
| `CurrentVersionId` | `Guid?` | Points at the latest **published** `PlatformContentVersion`; null until first publish |
| `LatestDraftVersionId` | `Guid?` | Points at the newest version regardless of publish state (may equal `CurrentVersionId`) |
| `CreatedAt` | `DateTime` | |
| `CreatedByPrincipalId` | `Guid` | FK `IamPrincipal` |
| `RetiredAt` | `DateTime?` | Soft-retire: stops future installs; never touches already-installed tenant copies (mirrors the "never overwrite built content" seeder principle applied to removal) |

### 3.2 `PlatformContentVersion` (append-only, immutable once published)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | `Guid.CreateVersion7()` |
| `DefinitionId` | `Guid` | FK `PlatformContentDefinition` |
| `Version` | `int` | Monotonic per definition, starting at 1 |
| `ContentHash` | `string` | SHA-256 of the canonicalized `PayloadJson` — the value compared against a tenant's `PlatformSourceHash` to decide "untouched" (§2.1) |
| `PayloadJson` | `string` | Kind-shaped: command → `{templateResponse, templateResponses, matchMode, tier, …}`; widget → `{framework, vueSource, manifest}` (the widget re-arch's Vue SPA source + config, `widget-rearchitecture-true-vue`); pipeline → the pipeline tree document (`pipeline-tree-and-editor.md` shape); code script → the multi-file `path → content` map (`CodeScriptVersion.FilesJson` shape) |
| `RenderGalleryRefs` | `List<string>?` | Widget kind only — asset ids of the captured render-gallery screenshots for this version (`widget-render-gallery-capture`: every render re-shot after a change) |
| `PublishNote` | `string?` | Free-text changelog entry, required when `Mode = force` |
| `DraftedAt` | `DateTime` | |
| `DraftedByPrincipalId` | `Guid` | FK `IamPrincipal` |
| `PublishedAt` | `DateTime?` | Null while still a draft |
| `PublishedByPrincipalId` | `Guid?` | |

### 3.3 Tenant-side provenance (extend `Command`, `Widget`, `Pipeline`, `CodeScript`)

Four nullable fields added to each of the four tenant-scoped entities — generalizing the existing `Widget.GalleryItemId`/`InstalledSourceRevision` pair:

| Field | Type | Notes |
|---|---|---|
| `PlatformSourceDefinitionId` | `Guid?` | Which `PlatformContentDefinition` this row was installed from; null for a fully tenant-authored row |
| `PlatformSourceVersion` | `int?` | The `PlatformContentVersion.Version` installed |
| `PlatformSourceHash` | `string?` | The `ContentHash` at install/last-sync time — compared against the tenant row's live content hash to decide "untouched" for `update_in_place_where_untouched` |
| `PlatformSourceSyncedAt` | `DateTime?` | When the row last received a platform-originated update |

### 3.4 `PlatformContentPublishJob` (append-only, one row per publish attempt)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | `Guid.CreateVersion7()` |
| `DefinitionId` | `Guid` | |
| `FromVersion` | `int?` | Null for a first publish |
| `ToVersion` | `int` | |
| `Mode` | `string` | `publish_as_new` \| `update_in_place_where_untouched` \| `force` |
| `RequestedByPrincipalId` | `Guid` | |
| `RequestedAt` | `DateTime` | |
| `PreviewAffectedCount` | `int` | From the `publish-preview` call this publish was confirmed against |
| `PreviewSkippedCount` | `int` | Tenants skipped because their copy was edited (modes 2–3 only) |
| `ConfirmedAffectedCount` | `int?` | Actual count once the fan-out completes — compared against `PreviewAffectedCount` as a drift check |
| `Status` | `string` | `running` \| `completed` \| `failed` |
| `CompletedAt` | `DateTime?` | |
| `FailureReason` | `string?` | |

---

## 4. §5 — Controller endpoints

One new controller. `[ApiVersion("1.0")]`, inherits `BaseController`, `[Authorize]`, returns `StatusResponseDto<T>`/`PaginatedResponse<T>` via `ResultResponse(...)`. Not a channel route — no tenant resolution, matching the existing `PlatformIamController`/`PlatformAnalyticsController` convention (`platform/…` prefix reserved for Plane-C surfaces).

### `PlatformContentController` — `[Route("api/v{version:apiVersion}/platform/content")]`

`[Authorize]` + Plane-C policy per action (policy name = action key verbatim, `PlatformIamAuthorizationHandler`, always audited — §5). `saas`-only (§0 marker).

| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/definitions` | `?kind=` | `StatusResponseDto<PaginatedResponse<PlatformContentDefinitionDto>>` | platform · `content:read` |
| GET | `/definitions/{id:guid}` | — | `StatusResponseDto<PlatformContentDefinitionDetailDto>` (incl. version history) | platform · `content:read` |
| POST | `/definitions` | `CreateContentDefinitionRequest(Kind, Key, DisplayName, Description?, PayloadJson)` | `StatusResponseDto<PlatformContentDefinitionDto>` (version 1, unpublished draft) | platform · `content:author` |
| POST | `/definitions/{id:guid}/versions` | `DraftContentVersionRequest(PayloadJson, RenderGalleryRefs?)` | `StatusResponseDto<PlatformContentVersionDto>` (new draft, not yet published) | platform · `content:author` |
| GET | `/definitions/{id:guid}/versions/{versionId:guid}` | — | `StatusResponseDto<PlatformContentVersionDto>` | platform · `content:read` |
| POST | `/definitions/{id:guid}/versions/{versionId:guid}/publish-preview` | `PublishPreviewRequest(Mode)` | `StatusResponseDto<PublishPreviewDto>` (`AffectedCount`, `SkippedCount`, `SampleTenantNames`) | platform · `content:author` |
| POST | `/definitions/{id:guid}/versions/{versionId:guid}/publish` | `PublishContentRequest(Mode, PublishNote?, ConfirmedPreviewAffectedCount)` | `StatusResponseDto<PlatformContentPublishJobDto>` | platform · `content:publish` (Critical: reaches every installed tenant; `force` additionally requires `content:publish:force`) |
| GET | `/publish-jobs/{id:guid}` | — | `StatusResponseDto<PlatformContentPublishJobDto>` | platform · `content:read` |
| DELETE | `/definitions/{id:guid}` | — | `StatusResponseDto<object>` (sets `RetiredAt`; never touches installed tenant copies) | platform · `content:author` |

`PublishContentRequest.ConfirmedPreviewAffectedCount` must byte-match the count the immediately-prior `publish-preview` call returned — a changed count (a tenant onboarded or edited their copy in between) fails closed with `PREVIEW_STALE`, forcing a fresh preview. This is the mechanical enforcement of "blast radius shown before commit," not just a UI convention.

New Gate-2 action keys seeded in `ActionDefinitionSeeder`/`roles-permissions.md` §7.1 in the same slice that builds this controller: `content:read` (Support-tier), `content:author` (Elevated-tier), `content:publish` (Critical-tier), `content:publish:force` (Critical-tier, distinct from `content:publish` per the guardrail in §2.1).

---

## 5. Audit contract

Every `PlatformContentController` write action, and every publish job's execution, appends a row to `IamAuditLog` (roles-permissions.md schema O.9) — the same append-only Plane-C audit table every other cross-tenant platform action already writes to. Two fields are **added** to `IamAuditLog` in this slice (schema is not locked; extend in place, `no-backwards-compat-build-right`):

| New field | Type | Notes |
|---|---|---|
| `AffectedTenantCount` | `int?` | Set on `content:publish` rows to the job's `ConfirmedAffectedCount`; null for non-fan-out actions |
| `PublishJobId` | `Guid?` | FK `PlatformContentPublishJob`; null for non-publish actions |

Populated per existing `IamAuditLog` columns: `Permission` = the action key exercised (`content:author`, `content:publish`, `content:publish:force`); `TargetResource` = `{Kind}:{Key}@v{Version}` (e.g. `command:sr@v3`); `TargetBroadcasterId` stays null for a fan-out (it targets many tenants, not one — `AffectedTenantCount` carries that instead); `Justification` is **required** (not merely accepted) on every `content:publish` call with `Mode = force`, mirroring the existing `BreakGlass` justification requirement; `Outcome` records success/failure/partial (a `force` publish that fails mid-fan-out is `Outcome = Partial` with `PlatformContentPublishJob.Status = failed` carrying the detail). No content-authoring or publish action is exempt from this table — the same "universal Gate-2 coverage is a hard invariant" rule that governs every other tenant-scoped controller in `roles-permissions.md` §0 applies here to Plane-C coverage.

---

## 6. Scope pointers — S-ADMIN-2..9

One line each; each is its own slice, spec'd in its own pass before being built:

- **S-ADMIN-2 — Content authoring UI.** Dashboard screens driving §4 above: definition list, draft/version editor per `Kind` (command template editor, widget Vue-source + render-gallery preview, pipeline tree editor, code-script multi-file editor — reusing the existing tenant-facing editor components, not forking them), and the publish-preview → confirm flow surfacing §2.1's blast radius.
- **S-ADMIN-3 — Tenant ops.** Act-as (impersonation) already exists in `AdminController` (`POST tenants/{id}/access`, `POST users/{id}/impersonate`) — gate it explicitly and confirm every impersonated session is audited; suspension must be **enforced** at the auth/middleware layer (not merely flagged in the DB, per `truthful-data-not-fake-enforcement`); add per-tenant quota overrides, forced re-migration, and GDPR export/erase.
- **S-ADMIN-4 — Plans/billing/entitlements.** Extend `AdminBillingController` (tier grants, founder grants, invoice refunds) with entitlement-override visibility and audit parity with §5.
- **S-ADMIN-5 — Flags/rollout/kill switches.** Extend `FeatureFlagAdminController` with staged percentage rollout and an emergency kill-switch action distinct from a normal flag flip (own action key, own audit note).
- **S-ADMIN-6 — Operate + diagnose.** `AdminController` (`system`, `health`, `events`) + `PlatformAnalyticsController` (`stats`) become the incident-diagnosis surface — correlate an alert to the tenant(s)/content version(s) responsible.
- **S-ADMIN-7 — Support desk.** A single support-session view combining tenant lookup + act-as (§S-ADMIN-3) + this content's install/version history (§4) + the tenant's own audit trail, so a support answer doesn't require five separate tabs.
- **S-ADMIN-8 — Platform-wide trust & safety.** Extend `AdminSpamDefenseController` defaults; cross-tenant ban/nuke propagation; abuse-pattern detection across tenants (distinct from the per-channel `moderation:nuke` in `chat-client.md` §3.5).
- **S-ADMIN-9 — Navigability.** Regroup the 11 admin tabs by **job** (author content / operate tenants / bill / gate features / diagnose / support / police) instead of by controller; one primary action per surface and concentric-radius/scarce-accent per the Sleak skill, same bar as the tenant dashboard; breakpoints honoured identically.

---

## 7. Open dependencies (not blockers — named so the building slices don't re-derive them)

- `content:*` action keys must land in `ActionDefinitionSeeder` + `roles-permissions.md` §7.1 in the **same** slice that ships `PlatformContentController` (per that spec's own hard rule: a §5 cell whose key is absent from the seed catalogue is a seed bug).
- `IamAuditLog`'s two new columns (§5) require a migration in **both** migration assemblies (SQLite + Postgres) per `two-migration-assemblies-always-both`.
- The four tenant-entity provenance fields (§3.3) require the same dual migration, plus a one-time backfill pass that stamps existing tenant rows created by the current seeders with `PlatformSourceDefinitionId`/`Version`/`Hash` computed retroactively from the seeder's own known payload — otherwise every pre-existing tenant row reads as tenant-authored (`PlatformSourceDefinitionId = null`) and is silently excluded from every future `update_in_place_where_untouched` publish. This backfill is itself S-ADMIN-1's exit condition for "existing tenants are reachable," not a later slice's problem.
