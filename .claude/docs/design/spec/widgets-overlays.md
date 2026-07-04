# Interface Specification — Widgets & Overlays Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (§P.6–P.9, §A.2 `Channels.OverlayToken`); design `2026-06-16-widgets.md`; stack `2026-06-16-stack-and-dependencies.md`; defaults `2026-06-16-decisions-pending-confirmation.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PK `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **Relationship to existing code (EXTEND, do not duplicate).** A thin v0 already exists and is **aligned, not replaced**, by this spec:
> - `NomNomzBot.Domain/Entities/Widget.cs` — widen `Id`/`BroadcasterId` `string`→`Guid`, add the locked-schema columns (`Source`, `GalleryItemId`, `ActiveVersionId`, `LastRuntimeError`, `LastRanAt`, `ConfigSchemaVersion`), drop the ad-hoc `Version`/`TemplateId`/`CustomCode` fields (source/version now live on `WidgetVersion`).
> - `NomNomzBot.Application/Services/IWidgetService.cs` — keep CRUD shape, widen ids to `Guid`, add compile/version/gallery/trust methods below.
> - `NomNomzBot.Application/DTOs/Widgets/WidgetDtos.cs` — extend records below.
> - `NomNomzBot.Api/Hubs/OverlayHub.cs`, `Hubs/Clients/IOverlayClient.cs`, `Hubs/WidgetNotifier.cs`, `Hubs/Dtos/HubResponseDtos.cs` — extend (add `WidgetCompileFailed`, `WidgetSettingsChanged` already present, CSP-nonce delivery; XSS-safe payloads).
> - `NomNomzBot.Api/Controllers/V1/WidgetsController.cs` — extend with version/compile/gallery routes.
> - `NomNomzBot.Infrastructure/Services/Application/WidgetService.cs`, `Persistence/Configurations/WidgetConfiguration.cs`, `Persistence/Repositories/WidgetRepository.cs` — align (`[VC:JSON]` converters replace the banned `HasColumnType("jsonb")`; UUIDv7 ids).
> - `NomNomzBot.Domain/Events/WidgetConnectedEvent.cs` / `WidgetDisconnectedEvent.cs` — keep; widen ids to `Guid`; add the new events below.

---

## 1. Entities

All owned by this subsystem; **defined in the locked schema — referenced here, not redefined.** Conventions (PK `Guid`/UUIDv7, `BaseEntity` timestamps, soft-delete filter, `[VC:JSON]`/`[VC:enum]` converters, `BroadcasterId Guid` tenant scope) per schema §1.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`Widget`** | §P.6 `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(255)`; `Description string(500)?`; `Framework string(20)` [VC:enum] (`vue`\|`react`\|`svelte`\|`vanilla`); `Source string(20)` [VC:enum] (`first_party`\|`verified_gallery`\|`custom`); `GalleryItemId Guid?` FK→`WidgetGalleryItem.Id` Index; `ActiveVersionId Guid?` FK→`WidgetVersion.Id` Index; `EventSubscriptions text?` **[VC:JSON]** `List<string>`; `Settings text?` **[VC:JSON]** `Dictionary<string,object?>`; `IsEnabled bool`; `LastRuntimeError text?` (audit B5); `LastRanAt timestamp?` (audit B5); `ConfigSchemaVersion int` (default 1); `CreatedAt/UpdatedAt/DeletedAt`. |
| **`WidgetVersion`** | §P.7 `[APPEND-ONLY]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `WidgetId Guid` FK→`Widget.Id` Index; `VersionNumber int`; `SourceCode text?`; `CompiledBundle text?`; `BuildStatus string(20)` [VC:enum] (`pending`\|`success`\|`error`); `BuildError text?`; `BuildLog text?`; `ContentHash string(64)` Index; `CompiledAt timestamp?`; `CreatedAt`. **Unique** `(WidgetId, VersionNumber)`. Append-only: corrections are new versions, never edits. |
| **`WidgetGalleryItem`** | §P.8 `[GLOBAL, soft-delete]` (no `BroadcasterId`) | global | `Id Guid` PK; `SubmitterUserId Guid` FK→`Users.Id` Index; `SubmitterTwitchUserId string(50)` Index [PII-hash]; `SubmitterDisplayNameSnapshot string(255)?` [PII-scrub]; `Name string(255)`; `Description text?`; `Framework string(20)`; `TrustTier string(20)` [VC:enum] Index (`first_party`\|`verified_community`\|`unverified`); `GitHubRepoUrl string(2048)`; `PinnedCommitSha string(40)`; `PinnedTag string(100)?`; `ReviewStatus string(20)` [VC:enum] Index (`submitted`\|`in_review`\|`verified`\|`rejected`); `ReviewedByUserId Guid?` FK→`Users.Id`; `ReviewNotes text?`; `ReviewedAt timestamp?`; `AvailableInSaaS bool`; `InstallCount int`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(GitHubRepoUrl, PinnedCommitSha)`. |
| **`WidgetGallerySubmissionEvent`** | §P.9 `[GLOBAL, APPEND-ONLY]` (no `BroadcasterId`) | global | `Id Guid` PK; `GalleryItemId Guid` FK→`WidgetGalleryItem.Id` Index; `FromStatus string(20)?`; `ToStatus string(20)`; `ChangedByUserId Guid?` FK→`Users.Id`; `NewPinnedCommitSha string(40)?`; `Note text?`; `OccurredAt timestamp` Index; `CreatedAt`. Immutable review/pin-change history. |

**Adjacent (read-only here, owned elsewhere):** `Channels.OverlayToken string(36)` Unique (§A.2) — the opaque per-channel browser-source token this subsystem validates at OverlayHub connect; not PII; **never** the user JWT (stack §Realtime). `WidgetGalleryItem.TrustTier` drives the SaaS rendering CSP tier (§ below).

**TrustTier source mapping (binding — security-load-bearing).** `OverlayWidgetEntry.TrustTier` (non-null, the CSP-tier input) is derived per widget from `Widget.Source`, **not** stored on `Widget`. A gallery-installed widget (`Source ∈ {verified_gallery, first_party}`, `GalleryItemId` set) inherits `WidgetGalleryItem.TrustTier` (`first_party`\|`verified_community`). A `Source=custom` widget (`GalleryItemId=null` — self-authored, the only output of `CreateAsync`+`CompileAsync`) has **no** `WidgetGalleryItem` and maps to **`unverified`** — fail-closed, never silently guessed. Mapping (exhaustive): `first_party` source → gallery `first_party`; `verified_gallery` source → gallery `verified_community`; `custom` source → `unverified`. A gallery-sourced widget whose `WidgetGalleryItem` is unexpectedly missing also falls back to `unverified` (fail-closed).

**EF mapping notes (binding):**
- All `[VC:JSON]` columns use the hand-rolled `JsonValueConverter<T>` + `JsonValueComparer<T>` convention (Newtonsoft.Json) — **never** `HasColumnType("jsonb")`/`HasDefaultValueSql("…::jsonb")` (banned; the live `WidgetConfiguration.cs` uses the banned form and MUST be corrected to converters).
- `Widget` carries the soft-delete global filter (`DeletedAt == null`). `WidgetVersion`/`WidgetGallerySubmissionEvent` are append-only (no filter, no `UpdatedAt`/`DeletedAt`).
- `WidgetGalleryItem`/`WidgetGallerySubmissionEvent` are GLOBAL — **no** `BroadcasterId`, **no** tenant query filter; gallery reads are unscoped, writes are platform-IAM gated.

### 1.1 First-party catalogue (seeded)

Thirteen `WidgetGalleryItem` rows ship with the bot, seeded idempotently by a new `FirstPartyWidgetCatalogueSeeder` (`ISeeder`, GLOBAL reference data, upsert by a stable natural key so a re-run adds nothing). All rows: `TrustTier=first_party`, `ReviewStatus=verified`, `AvailableInSaaS=true`, `SubmitterUserId=null` (platform-owned), `InstallCount=0`.

**First-party provenance (schema delta).** First-party widgets ship their source IN-REPO (compiled from the in-repo widget source tree at build time — there is no static `web/` folder), not from GitHub. So for `TrustTier=first_party` the `GitHubRepoUrl` and `PinnedCommitSha` columns are NULL, and a new `SourceKind string(20) [VC:enum] = in_repo | github` discriminator on `WidgetGalleryItem` distinguishes them (community submissions = `github`, the seeded catalogue = `in_repo`). The seeder loads each item's `SourceCode` + default settings schema from its in-repo asset on seed; install and clone copy from there. Note this `SourceKind` column + the now-nullable `GitHubRepoUrl`/`PinnedCommitSha` are a delta to the locked schema's `WidgetGalleryItem` table (DOMAIN for widgets, §P.8) — added there too.

Each item declares a default settings schema (the config keys used to render its config form and validate overrides).

| # | Name | key | Purpose | Config keys |
|---|---|---|---|---|
| 1 | Alerts | `alerts` | follow/sub/resub/gift/raid/cheer + `supporter.*` (tip/membership/merch/charity, branched on `Kind` — `supporter-events.md`) popups | `events[]` (per-event enable), `sound`, `image`, `textTemplate`, `durationMs`, `minBits`, `minGiftCount`, `minAmount` |
| 2 | Chat box | `chat_box` | live chat rendered from the DECORATED fragment tree (consumes `chat-decoration.md`: FFZ/7TV/BTTV emotes + badges) | `theme`, `maxMessages`, `fadeAfterMs`, `showBadges`, `showEmotes`, `hideCommands`, `hideBots` |
| 3 | Now Playing | `now_playing` | current track | `layout`, `showArt`, `showProgressBar`, `provider` |
| 4 | SR Queue | `sr_queue` | upcoming song-request queue | `count`, `showRequester`, `showDuration` |
| 5 | TTS caption | `tts_caption` | speaking indicator + caption; binds `OverlayHub.TtsSpeak` (§7) | `showText`, `voiceLabel`, `position` |
| 6 | Goal bar | `goal_bar` | follower/sub/bits goal progress | `metric`, `target`, `start`, `resetCadence`, `colors`, `labels` |
| 7 | Event ticker | `event_ticker` | scrolling recent events | `events[]`, `speed`, `count` |
| 8 | Labels | `labels` | single-stat text (latest follower/sub, top cheerer, counts) | `label`, `formatString` |
| 9 | Poll / Prediction | `poll_prediction` | live poll/prediction bars; binds `channel.poll.*` / `channel.prediction.*` | `position`, `colors` |
| 10 | Redemption alert | `redemption_alert` | channel-point redemption popup | `rewards[]` (per-reward enable), `textTemplate`, `sound` |
| 11 | Countdown / Timer | `countdown_timer` | countdown to a time or duration (BRB/soon), dashboard-controllable | `target`, `durationMs`, `label`, `onCompleteText` |
| 12 | Emote wall | `emote_wall` | emotes from chat float across screen, incl. FFZ/7TV/BTTV emote fragments from the decorator | `density`, `size`, `animation`, `providers[]` |
| 13 | Custom Data | `custom_data` | live value of a custom data source (`custom-events.md`); a heart-rate gauge is this bound to `heartrate.bpm` | `source` (custom-data source `name`), `field` (optional), `render` (`number`\|`gauge`\|`text`), `label`, `min`, `max` |

**Dependency:** items `chat_box` (#2) and `emote_wall` (#12) consume the third-party-emote fragment tree from `chat-decoration.md` — they render real BTTV/FFZ/7TV emotes only once that subsystem's decorated DTO ships. **Test:** each of the thirteen seeds as `TrustTier=first_party` + `AvailableInSaaS=true`, installs into a channel, and carries its declared config keys in the default settings schema.

---

## 2. Domain events

All inherit `DomainEventBase` (the `abstract record` defined in platform-conventions §2.0, providing `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; events must NOT redeclare these). Published via `IEventBus`. Records use `required` init properties matching the existing `WidgetConnectedEvent` style. **Existing two events widened** (`WidgetId`/`ConnectionId` ids stay `string` on the wire for SignalR connection ids; widget ids become `Guid`).

```csharp
namespace NomNomzBot.Domain.Events;

// EXISTING — widen WidgetId string→Guid
public sealed record WidgetConnectedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required string ConnectionId { get; init; }   // SignalR connection id
}

public sealed record WidgetDisconnectedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required string ConnectionId { get; init; }
}

// NEW — build lifecycle (compile-on-save)
public sealed record WidgetBuildSucceededEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required Guid VersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required string ContentHash { get; init; }   // 64-char sha256, cache-bust key
}

public sealed record WidgetBuildFailedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required Guid VersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required string BuildError { get; init; }     // surfaced to editor, never silent
}

// NEW — settings live-push
public sealed record WidgetSettingsChangedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
}

// NEW — gallery review lifecycle (platform plane, BroadcasterId null = global)
public sealed record WidgetGalleryItemStatusChangedEvent : DomainEventBase
{
    public required Guid GalleryItemId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public string? NewPinnedCommitSha { get; init; }
    public required Guid ChangedByUserId { get; init; }
}
```

---

## 3. Service interface(s)

Namespace `NomNomzBot.Application.Services`. All ids `Guid`. All returns `Task<Result<T>>` / `Task<Result>`. Implementations in `NomNomzBot.Infrastructure/Services/Application/`. Tenant-scoped queries go through the repository under the EF global filter; **no raw `DbContext` in controllers**.

### 3.1 `IWidgetService` (EXTEND existing)

```csharp
public interface IWidgetService
{
    // ── CRUD (EXISTING — ids widened to Guid) ─────────────────────────────────
    Task<Result<WidgetDetail>> CreateAsync(Guid broadcasterId, CreateWidgetRequest request, CancellationToken ct = default);
    Task<Result<WidgetDetail>> UpdateAsync(Guid broadcasterId, Guid widgetId, UpdateWidgetRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid broadcasterId, Guid widgetId, CancellationToken ct = default);
    Task<Result<PagedList<WidgetDetail>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<WidgetDetail>> GetAsync(Guid broadcasterId, Guid widgetId, CancellationToken ct = default);

    // ── Overlay serving (EXISTING — token-resolved, public) ───────────────────
    Task<Result<OverlayManifest>> GetOverlayManifestAsync(string overlayToken, CancellationToken ct = default);

    // ── Versions / compile-on-save (NEW) ──────────────────────────────────────
    Task<Result<WidgetVersionDetail>> CompileAsync(Guid broadcasterId, Guid widgetId, CompileWidgetRequest request, CancellationToken ct = default);
    Task<Result<PagedList<WidgetVersionSummary>>> ListVersionsAsync(Guid broadcasterId, Guid widgetId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<WidgetVersionDetail>> GetVersionAsync(Guid broadcasterId, Guid widgetId, Guid versionId, CancellationToken ct = default);
    Task<Result<WidgetDetail>> RollbackAsync(Guid broadcasterId, Guid widgetId, Guid versionId, CancellationToken ct = default);

    // ── Runtime health (NEW — audit B5) ───────────────────────────────────────
    Task<Result> RecordRuntimeErrorAsync(Guid broadcasterId, Guid widgetId, string error, CancellationToken ct = default);

    // ── Install from gallery (NEW) ────────────────────────────────────────────
    Task<Result<WidgetDetail>> InstallFromGalleryAsync(Guid broadcasterId, Guid galleryItemId, CancellationToken ct = default);

    // ── Clone-to-edit fork (NEW) ──────────────────────────────────────────────
    // Fork a verified-gallery OR installed widget into a NEW custom widget the caller fully owns and may edit.
    // Produces Source=custom (⇒ TrustTier=unverified, fail-closed), GalleryItemId=null, ActiveVersionId=null, with the
    // source widget's SourceCode copied into a NEW WidgetVersion (VersionNumber=1, BuildStatus=pending). The source's
    // CompiledBundle is NOT copied — the clone recompiles on the owner's first save (IWidgetBuildService.BuildAsync sets
    // ActiveVersionId then). New UUIDv7 Id, the caller's BroadcasterId, Name/Description/Framework copied from the source.
    // The clone is fully detached: no link back to the gallery item, independently editable.
    Task<Result<WidgetDetail>> CloneToEditAsync(Guid broadcasterId, CloneWidgetRequest request, CancellationToken ct = default);
    // CloneWidgetRequest: exactly one of { Guid? GalleryItemId, Guid? InstalledWidgetId } is set (the source to fork).
}
```

Behavior (one line each):
- `CreateAsync` — inserts a `Widget` (UUIDv7 id, `Source=custom`, no active version yet); returns detail with overlay URL. No build occurs until first `CompileAsync`.
- `UpdateAsync` — patches name/settings/subscriptions/enabled; if `Settings` changed, publishes `WidgetSettingsChangedEvent` → OverlayHub `WidgetSettingsChanged` push; does **not** rebuild.
- `DeleteAsync` — soft-deletes (`DeletedAt` set); pushes nothing (overlay drops on next reconnect).
- `ListAsync`/`GetAsync` — tenant-filtered reads; `GetAsync` returns 404-style `Result` failure if not owned.
- `GetOverlayManifestAsync` — resolves channel by `OverlayToken`, returns the channel's enabled widgets + their served bundle URLs + CSP nonce + trust tier; the **only** public (token-auth) read path; XSS-safe (no raw user HTML, see §rendering). Each entry's non-null `TrustTier` is derived from `Widget.Source` per the **TrustTier source mapping** (§1): gallery widgets inherit `WidgetGalleryItem.TrustTier`; `Source=custom` (`GalleryItemId=null`) maps to `unverified` (fail-closed) — never silently defaulted to a higher tier.
- `CompileAsync` — **compile-on-save core**: creates the next `WidgetVersion` (`VersionNumber = max+1`, `BuildStatus=pending`), invokes `IWidgetBuildService.BuildAsync`, persists `success`+`CompiledBundle`+`ContentHash` or `error`+`BuildError`+`BuildLog`; on success sets `Widget.ActiveVersionId`, publishes `WidgetBuildSucceededEvent` → OverlayHub `WidgetReload`; on failure publishes `WidgetBuildFailedEvent` → editor `WidgetCompileFailed` (never silent). Append-only — a failed build is a persisted version, not a discard.
- `ListVersionsAsync`/`GetVersionAsync` — version history (rollback/debug); `GetVersionAsync` includes `BuildLog`.
- `RollbackAsync` — re-points `Widget.ActiveVersionId` to an earlier **successful** version (fails if target build status ≠ `success`), publishes `WidgetBuildSucceededEvent` (cache-bust reload) without recompiling.
- `RecordRuntimeErrorAsync` — writes `Widget.LastRuntimeError`/`LastRanAt` from an overlay-reported runtime fault (OverlayHub `ReportRuntimeError`); no event.
- `InstallFromGalleryAsync` — fails unless the `WidgetGalleryItem` is `ReviewStatus=verified` **and** (SaaS profile) `AvailableInSaaS=true`; creates a `Widget` (`Source=verified_gallery`/`first_party`, `GalleryItemId` set), increments `InstallCount`, compiles the pinned-commit source into the first `WidgetVersion`. Unverified items are self-host-only (rejected on SaaS profile).
- `CloneToEditAsync` — forks a verified-gallery item OR an installed widget into a NEW, fully-owned `Source=custom` widget (⇒ `TrustTier=unverified`, fail-closed), `GalleryItemId=null`, `ActiveVersionId=null`; copies the source `SourceCode` into a fresh `WidgetVersion` (`VersionNumber=1`, `BuildStatus=pending`) but **not** the `CompiledBundle` — the clone recompiles on the owner's first save (`IWidgetBuildService.BuildAsync` sets `ActiveVersionId` then); new UUIDv7 `Id`, caller's `BroadcasterId`, `Name`/`Description`/`Framework` copied from the source; the clone is fully detached (no link back to the gallery item, independently editable). **Test:** the cloned widget has `Source=custom`, `GalleryItemId=null`, `ActiveVersionId=null`, a new `WidgetVersion` with the copied `SourceCode` + `BuildStatus=pending` and NO copied `CompiledBundle`, a fresh `Id`/`BroadcasterId`, and is unlinked from + independently editable of the gallery item.

### 3.2 `IWidgetBuildService` (NEW — esbuild compile boundary; profile adapter)

Namespace `NomNomzBot.Application.Services`. Pure compile boundary; no DB. Impl shells out to a bundled `esbuild` (stdin source → stdout bundle); failure is a `Result` failure, never a throw.

```csharp
public interface IWidgetBuildService
{
    Task<Result<WidgetBuildOutput>> BuildAsync(WidgetBuildInput input, CancellationToken ct = default);
}

public sealed record WidgetBuildInput(string Framework, string SourceCode);   // Framework ∈ vue|react|svelte|vanilla
public sealed record WidgetBuildOutput(string CompiledBundle, string ContentHash, string BuildLog);  // ContentHash = sha256(CompiledBundle), 64 hex
```

Behavior: `BuildAsync` — runs esbuild for the framework, computes `ContentHash`, returns bundle+log on success or a `Result` failure carrying the esbuild stderr as `ErrorMessage` (→ `WidgetVersion.BuildError`). Deterministic: same input → same `ContentHash` (cache-bust correctness).

### 3.3 `IWidgetGalleryService` (NEW — global, curated/verified GitHub-sourced)

Namespace `NomNomzBot.Application.Services`. GLOBAL (no tenant scope); list is public-read, mutations are platform-IAM gated.

```csharp
public interface IWidgetGalleryService
{
    Task<Result<PagedList<GalleryItemSummary>>> ListAsync(GalleryListRequest request, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<GalleryItemDetail>> GetAsync(Guid galleryItemId, CancellationToken ct = default);
    Task<Result<GalleryItemDetail>> SubmitAsync(Guid submitterUserId, SubmitGalleryItemRequest request, CancellationToken ct = default);
    Task<Result<GalleryItemDetail>> ReviewAsync(Guid reviewerUserId, Guid galleryItemId, ReviewGalleryItemRequest request, CancellationToken ct = default);
    Task<Result<GalleryItemDetail>> UpdatePinAsync(Guid reviewerUserId, Guid galleryItemId, UpdatePinRequest request, CancellationToken ct = default);
}
```

Behavior:
- `ListAsync` — filters by `TrustTier`/`Framework`/`ReviewStatus`; on SaaS profile, callers see only `AvailableInSaaS && ReviewStatus=verified` unless platform-IAM `audit:read`.
- `GetAsync` — single item incl. pinned commit/tag + review notes.
- `SubmitAsync` — inserts a `WidgetGalleryItem` (`ReviewStatus=submitted`, `TrustTier=unverified`, snapshot submitter display name), appends a `WidgetGallerySubmissionEvent` (`null→submitted`). GitHub URL is validated/normalized; never auto-pulls HEAD.
- `ReviewAsync` — transitions `ReviewStatus` (`in_review`/`verified`/`rejected`), sets `TrustTier=verified_community` on verify, writes `ReviewedBy*`/`ReviewedAt`/`ReviewNotes`, appends a `WidgetGallerySubmissionEvent`, publishes `WidgetGalleryItemStatusChangedEvent`. Platform-IAM gated.
- `UpdatePinAsync` — re-pins `PinnedCommitSha`/`PinnedTag`; **forces `ReviewStatus` back to `in_review`** (re-verify on update — never auto-pull HEAD), appends an event with `NewPinnedCommitSha`.

### 3.4 `ILinkPreviewService` (NEW — OG-card + YouTube trust score for widget-rendered links)

Namespace `NomNomzBot.Application.Services`. Owns OG-card metadata fetch and the per-source trust score used to decide whether a widget may auto-embed a link/card (defense-in-depth: untrusted links are gated, not blindly embedded). SSRF-egress-allowlisted (stack §Sandbox compensating controls).

```csharp
public interface ILinkPreviewService
{
    Task<Result<LinkPreview>> GetPreviewAsync(Guid broadcasterId, string url, CancellationToken ct = default);
}

public sealed record LinkPreview(
    string Url,
    string? Title,
    string? Description,
    string? ImageUrl,
    string SiteName,
    string Provider,          // "youtube" | "og" | "none"
    decimal TrustScore,       // 0–1 decimal(8,4); REAL-affinity on SQLite (schema §1.4)
    bool AutoEmbedAllowed     // TrustScore ≥ configured min-trust threshold AND host on egress allowlist
);
```

**Auto-embed threshold — config home (binding).** The minimum `TrustScore` a link must reach to auto-embed is **not** a magic constant: it lives in `AppSetting` (P.11) under `Category="widgets"`, `Key="link_autoembed_min_trust"`, `ValueType="string"` (decimal `0–1`), **global default `0.75`** seeded as a global (null-`BroadcasterId`) row. It is per-channel-overridable — a streamer may tighten it for their overlays (operators tighten, never silently loosen below the seeded baseline, mirroring the `custom_code` AppSetting precedent in `code-execution-sandbox.md` §Profile defaults). `ILinkPreviewService` reads it via `IAppSettingsService.GetForTenantAsync<decimal>("widgets", "link_autoembed_min_trust", ct)` (platform-conventions §3.5) — the tenant row when present, otherwise the global default; on `NOT_FOUND` it falls back to the seeded `0.75` (fail-closed, never auto-embed on a missing setting).

Behavior: `GetPreviewAsync` — fetches sanitized OpenGraph/oEmbed metadata via the SSRF-allowlisted `HttpClient` (resilience pipeline), computes `TrustScore` (YouTube/first-party hosts score high; unknown hosts low), then sets `AutoEmbedAllowed = TrustScore ≥ link_autoembed_min_trust (resolved per above) AND host on egress allowlist`. All returned strings are XSS-token-pipeline-cleaned (no raw remote HTML reaches the overlay). Result failure on disallowed host / fetch error — fail-closed (`AutoEmbedAllowed=false`).

---

## 4. DTOs / contracts

Namespace `NomNomzBot.Application.DTOs.Widgets`. Records `sealed`; requests use `init`; ids `Guid` (serialized as string). `WidgetEventDto`/`WidgetSettingsDto` live in `NomNomzBot.Api.Hubs.Dtos` (§5/§wire) and are kept.

```csharp
// ── Widget detail / list (EXTEND existing) ───────────────────────────────────
public sealed record WidgetListItem(Guid Id, string Name, string Framework, string Source, bool IsEnabled, DateTime CreatedAt);

public sealed record WidgetDetail(
    Guid Id, string Name, string? Description, string Framework, string Source,
    bool IsEnabled, string OverlayUrl, Guid? ActiveVersionId, Guid? GalleryItemId,
    Dictionary<string, object?> Settings, List<string> EventSubscriptions,
    string? LastRuntimeError, DateTime? LastRanAt, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateWidgetRequest
{
    public required string Name { get; init; }
    public required string Framework { get; init; }       // vue|react|svelte|vanilla
    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
}

public sealed record UpdateWidgetRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
    public bool? IsEnabled { get; init; }
}

// Fork source — exactly one of { GalleryItemId, InstalledWidgetId } is set (validated server-side; both-set / neither-set => Result.Failure).
public sealed record CloneWidgetRequest
{
    public Guid? GalleryItemId { get; init; }     // fork a verified-gallery item
    public Guid? InstalledWidgetId { get; init; } // fork an installed widget
}

// ── Compile / version (NEW) ──────────────────────────────────────────────────
public sealed record CompileWidgetRequest { public required string SourceCode { get; init; } }

public sealed record WidgetVersionSummary(
    Guid Id, int VersionNumber, string BuildStatus, string? ContentHash,
    DateTime? CompiledAt, DateTime CreatedAt);

public sealed record WidgetVersionDetail(
    Guid Id, Guid WidgetId, int VersionNumber, string BuildStatus,
    string? BuildError, string? BuildLog, string? ContentHash,
    DateTime? CompiledAt, DateTime CreatedAt);

// ── Overlay manifest (NEW — public, token-resolved) ──────────────────────────
public sealed record OverlayManifest(
    Guid ChannelId, string CspNonce, List<OverlayWidgetEntry> Widgets);

public sealed record OverlayWidgetEntry(
    Guid WidgetId, string Name, string Framework, string TrustTier, // first_party|verified_community|unverified — derived from Widget.Source per §1 TrustTier source mapping (custom => unverified, fail-closed)
    string BundleUrl, string ContentHash, List<string> EventSubscriptions,
    Dictionary<string, object?> Settings);

// ── Gallery (NEW) ────────────────────────────────────────────────────────────
public sealed record GalleryListRequest
{
    public string? TrustTier { get; init; }
    public string? Framework { get; init; }
    public string? ReviewStatus { get; init; }   // honored only with audit:read
}

public sealed record GalleryItemSummary(
    Guid Id, string Name, string Framework, string TrustTier,
    string ReviewStatus, int InstallCount, bool AvailableInSaaS);

public sealed record GalleryItemDetail(
    Guid Id, string Name, string? Description, string Framework, string TrustTier,
    string GitHubRepoUrl, string PinnedCommitSha, string? PinnedTag,
    string ReviewStatus, string? ReviewNotes, DateTime? ReviewedAt,
    bool AvailableInSaaS, int InstallCount, DateTime CreatedAt);

public sealed record SubmitGalleryItemRequest
{
    public required string Name { get; init; }
    public required string Framework { get; init; }
    public required string GitHubRepoUrl { get; init; }
    public required string PinnedCommitSha { get; init; }
    public string? PinnedTag { get; init; }
    public string? Description { get; init; }
}

public sealed record ReviewGalleryItemRequest
{
    public required string ReviewStatus { get; init; }   // in_review|verified|rejected
    public string? ReviewNotes { get; init; }
    public bool AvailableInSaaS { get; init; }
}

public sealed record UpdatePinRequest
{
    public required string PinnedCommitSha { get; init; }
    public string? PinnedTag { get; init; }
    public string? Note { get; init; }
}
```

---

## 5. Controller endpoints

All under `[ApiVersion("1.0")]`, return `StatusResponseDto<T>` / `PaginatedResponse<T>`, inherit `BaseController`.

**Role gate.** Gate 1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the gate column's action key before the service call (403 `FORBIDDEN` when below). Plane-C rows = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, …)`; the ASP.NET `[Authorize(Policy="<key>")]` policy-name **is** the permission key verbatim. The keys are seeded global `ActionDefinitions` (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Widget authoring is an **Editor-floor** management action (overlays touch what's on stream); gallery review is a **platform** action.

### 5a. Tenant widget CRUD + versions — `WidgetsController` (EXTEND)
`[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]` `[Authorize]`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `PageRequestDto` (query) | `PaginatedResponse<WidgetDetail>` | management / Moderator · `widget:read` |
| GET | `/{widgetId}` | — | `StatusResponseDto<WidgetDetail>` | management / Moderator · `widget:read` |
| POST | `/` | `CreateWidgetRequest` | `StatusResponseDto<WidgetDetail>` (201) | management / Editor · `widget:create` |
| PUT | `/{widgetId}` | `UpdateWidgetRequest` | `StatusResponseDto<WidgetDetail>` | management / Editor · `widget:update` |
| DELETE | `/{widgetId}` | — | 204 | management / Editor · `widget:delete` |
| POST | `/{widgetId}/compile` | `CompileWidgetRequest` | `StatusResponseDto<WidgetVersionDetail>` | management / Editor · `widget:compile` |
| GET | `/{widgetId}/versions` | `PageRequestDto` (query) | `PaginatedResponse<WidgetVersionSummary>` | management / Moderator · `widget:version:read` |
| GET | `/{widgetId}/versions/{versionId}` | — | `StatusResponseDto<WidgetVersionDetail>` | management / Moderator · `widget:version:read` |
| POST | `/{widgetId}/rollback/{versionId}` | — | `StatusResponseDto<WidgetDetail>` | management / Editor · `widget:rollback` |
| POST | `/{widgetId}/install/{galleryItemId}` | — | `StatusResponseDto<WidgetDetail>` (201) | management / Editor · `widget:install` |
| POST | `clone` | `CloneWidgetRequest` | `StatusResponseDto<WidgetDetail>` (201) | management / Editor · `widget:create` |

### 5b. Public overlay manifest — `OverlayController` (NEW)
`[Route("api/v{version:apiVersion}/overlay")]` `[AllowAnonymous]` — **OverlayToken auth only** (never user JWT).

| Verb | Route | Request | Response | Auth |
|---|---|---|---|---|
| GET | `/manifest` | `?token={overlayToken}` (query) | `StatusResponseDto<OverlayManifest>` | OverlayToken (validated against `Channels.OverlayToken`); rate-limited; `access_token` scrubbed from logs |

### 5c. Global gallery — `WidgetGalleryController` (NEW)
`[Route("api/v{version:apiVersion}/widget-gallery")]`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `GalleryListRequest` (query) + `PageRequestDto` | `PaginatedResponse<GalleryItemSummary>` | anonymous (verified+SaaS-available only) |
| GET | `/{galleryItemId}` | — | `StatusResponseDto<GalleryItemDetail>` | anonymous |
| POST | `/` | `SubmitGalleryItemRequest` | `StatusResponseDto<GalleryItemDetail>` (201) | authenticated (any authenticated user submits) |
| POST | `/{galleryItemId}/review` | `ReviewGalleryItemRequest` | `StatusResponseDto<GalleryItemDetail>` | platform · `gallery:review` (seeds as an `IamPermission`, category `Iam`) |
| POST | `/{galleryItemId}/pin` | `UpdatePinRequest` | `StatusResponseDto<GalleryItemDetail>` | platform · `gallery:review` (seeds as an `IamPermission`, category `Iam`) |

> **Tenant resolution:** `channelId` route segment resolves `BroadcasterId Guid` via the existing tenant middleware; service calls receive the resolved `Guid`, never the raw route string. Cross-tenant access is denied by the EF global filter + RLS (SaaS).

---

## 6. Pipeline actions

One action — overlays are pushed from pipelines (alerts/now-playing). Folder `NomNomzBot.Infrastructure/Pipeline/Actions/`, implementing the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13 (`string Type` + `Category`/`Description`; `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`); config DTO in `NomNomzBot.Application/Contracts/Pipeline/`.

| Type string | Config DTO | Behavior |
|---|---|---|
| `widget_event` | `WidgetEventActionConfig(Guid WidgetId, string EventType, Dictionary<string,object?>? Data)` | Pushes one `WidgetEventDto` to the target widget's Overlay group via `IWidgetNotifier.SendWidgetEventAsync(broadcasterId, widgetId, dto)`. Payload is XSS-token-pipeline-sanitized before send. Fail-closed if widget not found/disabled in tenant. |

(Reload/settings pushes are **not** pipeline actions — they are service-internal side effects of compile/update.)

---

## 7. DI registration

`NomNomzBot.Infrastructure/DependencyInjection.cs` (services/repos) and `NomNomzBot.Api/Program.cs` (hub-facing notifier, hub mapping). All app services **Scoped**; notifier **Scoped** (wraps `IHubContext`); the `widget_event` pipeline action **Transient** (per the commands-pipelines `ICommandAction` registration convention); build/link-preview adapters chosen by `DeploymentProfile`.

| Interface | Implementation | Lifetime | Where | Profile adapter |
|---|---|---|---|---|
| `IWidgetService` | `WidgetService` | Scoped | Infrastructure DI | — |
| `IWidgetBuildService` | `EsbuildWidgetBuildService` | Scoped | Infrastructure DI | single impl; esbuild path/runner from config (lite + SaaS) |
| `IWidgetGalleryService` | `WidgetGalleryService` | Scoped | Infrastructure DI | — (GLOBAL tables; SaaS-visibility filtered in service) |
| `ILinkPreviewService` | `LinkPreviewService` | Scoped | Infrastructure DI | uses SSRF-allowlisted `HttpClient` (resilience pipeline); reads the auto-embed threshold via `IAppSettingsService` (§3.4) |
| `WidgetRepository` | `WidgetRepository` (+ `WidgetVersionRepository`, `WidgetGalleryRepository`) | Scoped | Infrastructure DI | — |
| `IWidgetNotifier` | `WidgetNotifier` | Scoped | `Program.cs` | wraps `IHubContext<OverlayHub, IOverlayClient>` |
| `OverlayHub` | (SignalR) | — | `Program.cs` `MapHub<OverlayHub>("/hubs/overlay")` | SaaS adds `SignalR.StackExchangeRedis` backplane; lite in-memory |
| `ICommandAction` (`widget_event`) | `WidgetEventAction` | Transient | Infrastructure DI — `AddTransient<ICommandAction, WidgetEventAction>()` (registered with the pipeline action set per commands-pipelines §3.13) | — |

**OverlayHub wire surface (extend `IOverlayClient`):**
```csharp
public interface IOverlayClient
{
    Task WidgetEvent(WidgetEventDto evt);                 // EXISTING
    Task WidgetReload();                                  // EXISTING (compile success / rollback)
    Task WidgetSettingsChanged(WidgetSettingsDto settings); // EXISTING
    Task WidgetCompileFailed(WidgetCompileFailedDto error);  // NEW — editor surfaces build error
    Task TtsSpeak(TtsSpeakPayload payload);                  // NEW — server-sent utterance; consumed by tts.md client_edge dispatch (browser-source renders audio client-side)
    Task PlaySound(PlaySoundPayload payload);                // NEW — server-sent sound-clip play; consumed by sound-system.md play_sound (browser-source plays the clip client-side)
}
```
Hub server methods (extend `OverlayHub`): keep `JoinWidget`/`LeaveWidget`/`WidgetReady`; add `Task ReportRuntimeError(string widgetId, string error)` → `IWidgetService.RecordRuntimeErrorAsync`. Connect-time auth stays OverlayToken-only (validate `Channels.OverlayToken`; abort on mismatch) — **never** the user JWT. Add `WidgetCompileFailedDto(string WidgetId, int VersionNumber, string BuildError)` to `Hubs/Dtos/HubResponseDtos.cs`.

**TTS utterance payload (extend `Hubs/Dtos/HubResponseDtos.cs`):** the `TtsSpeak` push DTO is **owned here** (the `IOverlayClient` contract lives in this subsystem) and **consumed by `tts.md` `client_edge` dispatch** — the server sends the utterance event and the browser-source widget renders audio client-side (no server-side audio synthesis on the `edge` path).
```csharp
public sealed record TtsSpeakPayload(
    Guid BroadcasterId,             // tenant key (Guid) — overlay group scope
    string Text,                    // utterance text (XSS-token-pipeline-cleaned before send)
    string VoiceId,                 // provider voice identifier
    string Provider,                // edge|elevenlabs|azure
    string? CueId,                  // optional client-side dedupe / cancellation handle
    TtsSpeakOptions? Options);      // optional prosody overrides

public sealed record TtsSpeakOptions(
    double? Rate,                   // playback rate multiplier
    double? Pitch,                  // pitch adjustment
    double? Volume);                // output volume (0–1)
```

**Sound-clip play payload (extend `Hubs/Dtos/HubResponseDtos.cs`):** the `PlaySound` push DTO is **owned here** (the `IOverlayClient` contract lives in this subsystem) and **consumed by `sound-system.md` `play_sound`/`stop_sound`** — parallel to `TtsSpeak`, the always-loaded overlay holds the `<audio>` element and plays (or stops) the clip on this push; plays overlap by default (each independent), and a non-null `Handle` lets `stop_sound` target one playback.
```csharp
public sealed record PlaySoundPayload(
    string PlaybackUrl,             // tokened, overlay-fetchable clip URL (ISoundClipStore)
    int Volume,                     // effective output volume (0–100)
    string? Handle);                // optional name for a targeted stop_sound
```

---

## 8. Dependencies (stack-doc libs)

| Lib | Party | Use |
|---|---|---|
| `Microsoft.AspNetCore.SignalR` (+ `.Protocols.MessagePack`) | 2nd | OverlayHub real-time push (event/reload/settings/compile-failed) |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | 2nd | **SaaS-only** backplane for multi-node overlay fan-out (lite = none) |
| `Microsoft.EntityFrameworkCore` (+ Sqlite / Npgsql provider) | 2nd / 3rd | `Widget`/`WidgetVersion`/gallery persistence; EF10 named filters (soft-delete + tenant); profile-selected provider |
| `Newtonsoft.Json` | app JSON | `[VC:JSON]` `ValueConverter`/`ValueComparer` for `Settings`/`EventSubscriptions`; overlay manifest serialization |
| `System.Security.Cryptography` (SHA-256) | 1st (in-box) | `ContentHash` of compiled bundle (cache-bust) — no 3rd-party |
| `Microsoft.Extensions.Http.Resilience` (+ `IHttpClientFactory`) | 2nd | `ILinkPreviewService` OG-card/oEmbed fetch with retry/breaker over SSRF-allowlisted `HttpClient` |
| `Microsoft.Extensions.Caching.Hybrid` (`ICacheService`) | 2nd | cache compiled bundles by `ContentHash` + OG-card previews (L1 lite, L1+Redis SaaS) |
| `Asp.Versioning.Mvc` | 2nd | versioned controllers |
| **esbuild** (external binary, not a NuGet runtime dep) | tool | server-side widget bundling behind `IWidgetBuildService` (shelled out, per design §Build pipeline) |

No new 3rd-party NuGet packages are introduced by this subsystem (esbuild is an external CLI binary, invoked out-of-process — not linked).

---

## 9. Decisions (resolved)

1. **Widget authoring role floor — `Editor` (management plane).** The floor for create/update/compile/rollback/install is `Editor`, since overlays change on-stream content; reads are at `Moderator`. A broadcaster may raise the floor via `ChannelActionOverride` (no signature change), but the seeded `FloorLevel` is `Editor`.
2. **esbuild execution form — out-of-process CLI binary.** `EsbuildWidgetBuildService` shells out to a bundled `esbuild` CLI binary behind `IWidgetBuildService` (matches design §"server-side build (esbuild)"). The interface is stable regardless of runner, so the binary's path/runner is supplied from config (lite + SaaS) per §7.
