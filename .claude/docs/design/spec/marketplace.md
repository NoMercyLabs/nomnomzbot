# Interface Specification — Import/Export & Marketplace

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Streamer.bot's import/export ecosystem (portable shareable bundles — the ecosystem reference; we use **ZIP files with a standard per-type schema**, not opaque strings). Corpus: `commands-pipelines.md` (`Pipeline`/`Command` definitions — the primary shareable; `ICommandAction` types incl. `run_code`); `code-execution-sandbox.md` / `custom-code.md` (imported `run_code` scripts stay sandboxed + disabled-until-enabled); `widgets-overlays.md` (`WidgetConfig` + widget assets); `sound-system.md` (`SoundClip`); `custom-events.md` (`CustomDataSource`, minus secrets); `gdpr-crypto.md` (strip secrets/PII on export); `platform-conventions.md` (`IDeploymentProfileService` for the marketplace URL, `IFieldCipher`); `scaling-qos.md` (`IRateLimiter`); locked schema `2026-06-16-database-schema.md` (Domain H — automation-content management). The **marketplace service** (hosted catalog + vetting + storage) is a separate NoMercy-hosted component; this spec defines the **bot's** local import/export + its marketplace **client**, and the marketplace **API contract** the client depends on.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json; ZIP via `System.IO.Compression` (no new dep).

> **Why.** Sharing setups is Streamer.bot's biggest network effect. The owner's decision: a **NoMercy-hosted marketplace** where creators publish features the community installs, **security-vetted** before listing — and the portable artifact is a **ZIP file following a standard schema per export type**. Crucially, ZIP import/export works **standalone** (paste/share a ZIP anywhere; self-host installs it with **zero NoMercy infra**), so the hosted marketplace is the curated, vetted discovery layer on top — consistent with the "direct-connect, no central broker" principle. The bot is always a *client* of the marketplace (browse/install/publish); the marketplace service runs separately on NoMercy infra.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Portable artifact = a ZIP with a standard schema per type.** A bundle ZIP = `/manifest.json` (`BundleManifest`: schemaVersion, metadata, author, license, `items[]`, `dependencies[]`) + per-type entries (`/pipelines/*.json`, `/commands/*.json`, `/widgets/<key>/*`, `/sounds/*` + meta, `/custom-data-sources/*.json`). Each exportable type has a **versioned export contract** (`PipelineExport`, `CommandExport`, `WidgetExport`, `SoundExport`, `CustomDataSourceExport`); validation = deserialize into the typed contract + validate (no JSON-Schema engine dep). A bundle may hold **many** items + their dependency graph (a "pack"). |
| D2 | **Secrets/PII stripped on export.** Exports carry **definitions/config + assets only** — never tokens, AEAD secrets, API keys, per-viewer data, or overlay/automation tokens. Secret-requiring fields (e.g. a `CustomDataSource` auth) export **empty**; the importer fills them. Enforced in the export mappers (allowlist of exportable fields), not by scrubbing afterward. |
| D3 | **Local import/export needs zero infra.** `Export` produces a ZIP download; `Import` validates + installs a ZIP upload. Both work fully offline (share ZIPs over Discord/gist/anywhere). The marketplace is **optional** on top. |
| D4 | **Imported code is sandboxed + disabled-until-enabled.** Any `run_code` action in an imported pipeline lands in the `code-execution-sandbox.md` runtime AND **disabled**, requiring an explicit owner enable before it can run. Imported pipelines with destructive actions (ban/timeout/etc.) are bound by the **importer's own runtime roles** (no privilege is imported). Import surfaces a **capability summary** (what the bundle's pipelines can do) before install. |
| D5 | **Hosted marketplace, security-vetted.** Publishing pushes a bundle to the marketplace service, which enters a **security-review queue** (automated scan — flags `run_code`, `http_request`/`send_webhook` egress, destructive actions, widget code, and verifies no secrets — plus human approval for risky content); only **approved** bundles are listed. The bot client sees submission status (`pending`/`approved`/`rejected` + reason). Vetting + catalog + storage run on NoMercy infra (separate service); the bot only calls its API. |
| D6 | **Installs are tracked for update/uninstall.** `InstalledBundle` (H.11) records what was installed (source, version, the created entity ids), so a bundle can be **updated** (re-import a newer version) or **uninstalled** (remove its entities). Conflict policy on import: `rename` (default) \| `overwrite` \| `skip`. |
| D7 | **Marketplace URL is configurable, default = the NoMercy public marketplace.** Self-host points at it opt-in (direct-connect, like any backend URL); browse/install is free, publishing requires a signed-in account on the marketplace. Schema delta **H.11 `InstalledBundle`** only (the catalog lives in the separate marketplace service). |

---

## 1. Entities

Domain H. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`InstalledBundle`** | **H.11 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(150)`; `Source string(20)` **[VC:enum]** (`local`\|`marketplace`); `MarketplaceItemId string(64)?` (null for local ZIPs); `Version string(40)`; `Author string(100)?`; `License string(40)?`; `ManifestJson text` **[VC:JSON]** (the `BundleManifest`); `InstalledEntityIdsJson text` **[VC:JSON]** (`{ type → Guid[] }` — for update/uninstall); `InstalledByUserId Guid` FK→`Users.Id`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Source, MarketplaceItemId)` (one install per marketplace item; local installs use `Id`). |

The marketplace **catalog/listings/reviews** are **not** in the bot schema — they live in the separate marketplace service.

---

## 2. Domain events

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record BundleInstalledEvent : DomainEventBase
{
    public required Guid InstalledBundleId { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }   // local | marketplace
    public required IReadOnlyList<string> Capabilities { get; init; } // capability summary (D4)
}
```

(Uninstall is ordinary CRUD; publish status is polled from the marketplace, not journaled here.)

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Marketplace`. `Task<Result<T>>`. Impl in `NomNomzBot.Infrastructure/Marketplace/`.

```csharp
public interface IBundleExportService
{
    // Export the requested entities → a ZIP (manifest + per-type entries + assets), secrets stripped (D2).
    Task<Result<Stream>> ExportAsync(Guid broadcasterId, ExportRequest request, CancellationToken ct = default);
}

public interface IBundleImportService
{
    // Parse + validate a ZIP without installing: returns the manifest, the capability summary, and any issues.
    Task<Result<BundleInspection>> InspectAsync(Guid broadcasterId, Stream zip, CancellationToken ct = default);
    // Install: creates the entities (run_code disabled per D4), records an InstalledBundle, emits BundleInstalledEvent.
    Task<Result<InstalledBundleDto>> ImportAsync(Guid broadcasterId, Guid actorUserId, Stream zip, ImportConflictPolicy policy, CancellationToken ct = default);

    Task<Result<IReadOnlyList<InstalledBundleDto>>> ListInstalledAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> UninstallAsync(Guid broadcasterId, Guid installedBundleId, Guid actorUserId, CancellationToken ct = default);
}

// Client of the separate, NoMercy-hosted marketplace service (browse/install/publish).
public interface IMarketplaceClient
{
    Task<Result<PagedList<MarketplaceItemDto>>> SearchAsync(MarketplaceQuery query, CancellationToken ct = default);
    Task<Result<MarketplaceItemDto>> GetItemAsync(string itemId, CancellationToken ct = default);
    Task<Result<Stream>> DownloadAsync(string itemId, CancellationToken ct = default);          // → the bundle ZIP (then ImportAsync)
    Task<Result<PublishSubmissionDto>> PublishAsync(Guid broadcasterId, Stream zip, PublishMetadata metadata, CancellationToken ct = default); // enters vetting
    Task<Result<PublishSubmissionDto>> GetSubmissionAsync(string submissionId, CancellationToken ct = default); // pending|approved|rejected
}

public sealed record ExportRequest(IReadOnlyList<ExportItemRef> Items, BundleMetadata Metadata);
public sealed record ExportItemRef(string Type, Guid Id);   // Type: pipeline|command|widget|sound|custom_data_source
public sealed record BundleInspection(BundleManifest Manifest, IReadOnlyList<string> Capabilities, IReadOnlyList<string> Issues);
public enum ImportConflictPolicy { Rename, Overwrite, Skip }
public sealed record InstalledBundleDto(Guid Id, string Name, string Source, string? MarketplaceItemId, string Version, DateTime InstalledAt);
public sealed record MarketplaceItemDto(string ItemId, string Name, string Author, string Version, string Summary, IReadOnlyList<string> Capabilities, double Rating, long Installs);
public sealed record PublishSubmissionDto(string SubmissionId, string Status, string? ReviewNote);
```

The **standard export contracts** (`PipelineExport`, `CommandExport`, `WidgetExport`, `SoundExport`, `CustomDataSourceExport`, `BundleManifest`) live in `NomNomzBot.Application/Contracts/Marketplace/`, each `SchemaVersion`-stamped; export maps entity→contract through an allowlist (D2), import maps contract→entity, resolving `dependencies[]` topologically.

---

## 4. The marketplace API contract (what `IMarketplaceClient` depends on)

The NoMercy-hosted marketplace service exposes (the bot is a client; full service build is separate scope): `GET /v1/items?q&type&page` (search approved listings) · `GET /v1/items/{id}` (detail) · `GET /v1/items/{id}/download` (the ZIP) · `POST /v1/publish` (multipart ZIP + metadata → a submission entering **vetting**) · `GET /v1/submissions/{id}` (status). Auth: the publisher's marketplace account token (the bot stores it vaulted per `gdpr-crypto.md`); browse/download are public. Vetting (automated scan + human approval, D5) is internal to the service.

---

## 5. REST surface

Controller `BundlesController`, `[Route("api/v{version:apiVersion}/bundles")]`, and `MarketplaceController`, `[Route("api/v{version:apiVersion}/marketplace")]`. `[Authorize]`; Gate-2 keys.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| POST | `/bundles/export` | `ExportRequest` | ZIP (`application/zip`) | management / Editor · `bundles:export` |
| POST | `/bundles/inspect` | multipart ZIP | `StatusResponseDto<BundleInspection>` | management / Editor · `bundles:import` |
| POST | `/bundles/import` | multipart ZIP + `policy` | `StatusResponseDto<InstalledBundleDto>` | management / Editor · `bundles:import` |
| GET | `/bundles/installed` | — | `StatusResponseDto<IReadOnlyList<InstalledBundleDto>>` | management / Moderator · `bundles:read` |
| DELETE | `/bundles/installed/{id}` | — | `StatusResponseDto<bool>` | management / Editor · `bundles:import` |
| GET | `/marketplace/items` | query | `PaginatedResponse<MarketplaceItemDto>` | management / Moderator · `bundles:read` |
| POST | `/marketplace/items/{id}/install` | `{ policy }` | `StatusResponseDto<InstalledBundleDto>` | management / Editor · `bundles:import` |
| POST | `/marketplace/publish` | multipart ZIP + `PublishMetadata` | `StatusResponseDto<PublishSubmissionDto>` | management / Broadcaster · `bundles:publish` |

Seed in `roles-permissions.md`: **`bundles:read`** (Moderator 10, `Low`), **`bundles:export`** + **`bundles:import`** (Editor 30, `Low` — imported code is sandboxed+disabled, destructive actions bound by the importer's runtime roles, D4), **`bundles:publish`** (Broadcaster 40, `Low`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Marketplace/DependencyInjection.cs` (`AddMarketplace()`): `IBundleExportService`→`BundleExportService` (Scoped); `IBundleImportService`→`BundleImportService` (Scoped); `IMarketplaceClient`→`HttpMarketplaceClient` (Scoped, base URL from `IDeploymentProfileService.Current`/config, default = the NoMercy public marketplace); `InstalledBundleRepository` (Scoped). Import resolves dependencies topologically, creates entities via the owning services (so each entity's own validation runs), and registers `run_code` scripts disabled (D4). Rate-limited via `IRateLimiter` (import/publish buckets).

**Tests (prove behavior):** export of a pipeline that references a sub-pipeline + a widget produces a ZIP whose `manifest.json` lists all three with the dependency edges, and the exported JSON contains **no** secret/token field (a connection's auth exports empty); round-trip (export→import into a clean channel) recreates the entities with equivalent config and a fresh id set; importing a bundle whose pipeline has a `run_code` action installs it **disabled** and the `BundleInspection.Capabilities` lists "executes custom code"; conflict policy `skip` leaves an existing same-named command untouched while `overwrite` replaces it; uninstall removes exactly the `InstalledEntityIds` and the `InstalledBundle` row; a malformed/oversized ZIP or an unknown `schemaVersion` is rejected by `InspectAsync` with issues and **nothing is created**; `IMarketplaceClient.PublishAsync` returns a `pending` submission and `GetSubmissionAsync` reflects approval/rejection; install-from-marketplace downloads the ZIP and runs the same import path (dedup via the `(BroadcasterId, Source, MarketplaceItemId)` unique key on re-install → update, not duplicate).

---

## 7. Decisions (resolved)

ZIP bundles with a versioned per-type export contract (D1); secrets/PII stripped on export via field allowlist (D2); local import/export works zero-infra (D3); imported code sandboxed + disabled-until-enabled, no privilege imported, capability summary shown (D4); hosted marketplace with security vetting, bot is a client (D5); installs tracked for update/uninstall + conflict policy (D6); configurable marketplace URL defaulting to the NoMercy public marketplace, schema delta **H.11 `InstalledBundle`** only — catalog lives in the separate marketplace service (D7).

---

## 8. As-built notes (2026-07-17 — local bundle half shipped)

- **Routes are channel-routed:** `api/v1/channels/{channelId}/bundles/...` (the explicit-target tenant
  convention every management controller follows), not the bare `/bundles` shown in §5.
- **Command export allowlist = the `CreateCommandDto` surface** (name/tier/minPermissionLevel/
  templateResponse(s)/cooldowns/description/aliases + isEnabled + the `PipelineName` link).
  PrefixMode/MatchMode/CustomPrefix/UserCooldownSeconds are not portable yet — the module create API
  does not accept them.
- **Pipelines export the builder `GraphJsonCache` document** (the persisted authoring truth); the
  normalized `PipelineStep` rows are not separately exported.
- **A command's pipeline is auto-pulled into the bundle** with a `pipeline:<name>` dependency edge in
  the manifest.
- **Sound clips export their audio bytes** (sibling ZIP entry) and re-upload through
  `ISoundClipService.UploadAsync` on import so probing/validation re-run; bounded by the 20 MB
  bundle cap enforced at inspect AND import.
- **Rename policy semantics:** slug-typed names (command/sound/custom-data-source) suffix
  `-bundle`/`-bundle-N`; free-text names (pipeline/widget) suffix ` (bundle)`/` (bundle N)`; the base
  is trimmed to fit the column. Import is all-or-nothing — a mid-import failure rolls back everything
  created.
- **Capability summary** = a quoted-token scan of the graph JSON against a fixed action→capability
  catalog (`BundleConventions.ActionCapabilities`) + per-type flags (widgets, sounds, data sources).
- **DI is convention-bound** (`I<X>Service` scan); the spec's `AddMarketplace()` module,
  `InstalledBundleRepository`, and the import/publish rate-limit buckets are deferred to the
  marketplace-client slice. `bundles:publish` is already seeded (Broadcaster); its route ships with
  that slice.
- **`BundleInstalledEvent` is a sealed class** (the house domain-event style), not a record.
