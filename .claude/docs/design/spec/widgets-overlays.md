# Interface Specification — Widgets & Overlays Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (§P.6–P.9, §A.2 `Channels.OverlayToken`); design `2026-06-16-widgets.md`; stack `2026-06-16-stack-and-dependencies.md`; defaults `2026-06-16-decisions-resolved.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PK `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **As-built file map** (paths relative to `server/src/`). The subsystem is built; this is where it lives.
> - **Domain** `NomNomzBot.Domain/Widgets/` — `Entities/` (`Widget`, `WidgetVersion`, `WidgetGalleryItem`, `WidgetGallerySubmissionEvent`); `Events/` (`WidgetConnectedEvent`, `WidgetDisconnectedEvent`, `WidgetLifecycleEvents` = build / settings / gallery-status events, `OverlayContentRetractedEvent`).
> - **Application** `NomNomzBot.Application/Widgets/` — `Services/` (`IWidgetService`, `IWidgetBuildService`, `IWidgetGalleryService`, `IWidgetEventNotifier`, `IOverlayRetractionNotifier`, `IOverlayPresenceRegistry`, `IRenderedAlertReplayer`, `IVueSfcCompiler`, `IWidgetDependencyAllowlist`, `IWidgetSettingsSchemaProvider`); `Dtos/` (`WidgetDtos`, `WidgetGalleryDtos`, `WidgetSettingsSchemaDtos`). `IWidgetService` takes `string` broadcaster/widget ids and parses them to `Guid` inside; the §3.1 listing keeps the `Guid` contract shape.
> - **Infrastructure** `NomNomzBot.Infrastructure/Widgets/` — `WidgetService`, `WidgetGalleryService`, `WidgetTemplateCatalogue`; `Persistence/` (EF configurations + `WidgetRepository`); `Bundling/` (`EsbuildWidgetBuildService`, `JintVueSfcCompiler`, `WidgetDependencyAllowlist`, `ProcessRunner`); `EventHandlers/` (`OverlayModerationRetractionHandler`, goal / supporter / voice-trigger widget handlers, `SystemWidgetSeedOnOnboardingHandler`); `PipelineActions/WidgetEventAction`; `Pipeline/WidgetOptionProvider`. Also `Content/Widgets/` (`FirstPartyWidgetCatalogue`, `FirstPartyWidgetCatalogueSeeder`, `WidgetSettingsSchemaProvider`) and `Overlays/` (`OverlayEventFeedHook`, `OverlayEventFilter`).
> - **Api** `NomNomzBot.Api/` — `Controllers/V1/` (`WidgetsController`, `WidgetGalleryController`, `WidgetTestEventController`, `OverlayController`); `Controllers/` (`OverlayHostController` = the `GET /overlay` browser-source page, `OverlaySdkController` = `/overlay/sdk.js`, `OverlayVueRuntimeController` = `/overlay/vue.js`, `OverlayTicketController` = `POST /overlay/ticket`); `Hubs/` (`OverlayHub`, `WidgetNotifier` + the `*NotifierAdapter` classes, `Clients/IOverlayClient`, `Dtos/HubResponseDtos`, `Overlay/` = ticket service, presence registry, connection throttle, `Broadcasters/` = widget alert and build-lifecycle handlers).
> - EF: `[VC:JSON]` Newtonsoft converters, no `HasColumnType("jsonb")` (re-verified against `WidgetConfiguration.cs` 2026-08-22); UUIDv7 ids.

---

## 1. Entities

All owned by this subsystem; **defined in the locked schema — referenced here, not redefined.** Conventions (PK `Guid`/UUIDv7, `BaseEntity` timestamps, soft-delete filter, `[VC:JSON]`/`[VC:enum]` converters, `BroadcasterId Guid` tenant scope) per schema §1.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`Widget`** | §P.6 `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Name string(255)`; `Description string(500)?`; `Framework string(20)` [VC:enum] (`vue`\|`react`\|`svelte`\|`vanilla`); `Source string(20)` [VC:enum] (`first_party`\|`verified_gallery`\|`custom`); `GalleryItemId Guid?` FK→`WidgetGalleryItem.Id` Index; `ActiveVersionId Guid?` FK→`WidgetVersion.Id` Index; `EventSubscriptions text?` **[VC:JSON]** `List<string>`; `Settings text?` **[VC:JSON]** `Dictionary<string,object?>`; `IsEnabled bool`; `LastRuntimeError text?` (audit B5); `LastRanAt timestamp?` (audit B5); `ConfigSchemaVersion int` (default 1); `InstalledSourceRevision int?` (the gallery item's `SourceRevision` this widget was last built from); `CatalogueVersionNumber int?` (the last `WidgetVersion` that carried the catalogue's own source verbatim; null for `custom`); `PlatformSourceDefinitionId Guid?` / `PlatformSourceVersion int?` / `PlatformSourceHash string(64)?` / `PlatformSourceSyncedAt timestamp?` (platform-content spine provenance); `OverlayToken string(64)` (this widget's own 48-hex browser-source credential); `PreviousOverlayToken string(64)?` + `PreviousOverlayTokenExpiresAt timestamp?` (rotation grace window); `CreatedAt/UpdatedAt/DeletedAt`. |
| **`WidgetVersion`** | §P.7 `[APPEND-ONLY]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `WidgetId Guid` FK→`Widget.Id` Index; `VersionNumber int`; `SourceCode text?`; `FilesJson text?` (multi-file project: raw JSON `path → content`); `ManifestJson text?` (raw JSON `{ entry, kind, framework, dependencies[] }`); `CompiledBundle text?`; `BuildStatus string(20)` [VC:enum] (`pending`\|`success`\|`error`); `BuildError text?`; `BuildLog text?`; `ContentHash string(64)` Index; `CompiledAt timestamp?`; `CreatedAt`. **Unique** `(WidgetId, VersionNumber)`. Append-only: corrections are new versions, never edits. |
| **`WidgetGalleryItem`** | §P.8 `[GLOBAL, soft-delete]` (no `BroadcasterId`) | global | `Id Guid` PK; `SubmitterUserId Guid?` FK→`Users.Id` Index (null for the platform-owned catalogue); `SubmitterTwitchUserId string(50)` Index [PII-hash]; `SubmitterDisplayNameSnapshot string(255)?` [PII-scrub]; `Name string(255)`; `Description text?`; `Framework string(20)`; `TrustTier string(20)` [VC:enum] Index (`first_party`\|`verified_community`\|`unverified`); `SourceKind string(20)` (`in_repo`\|`github`); `NaturalKey string?` (stable seed key, null for submissions); `GitHubRepoUrl string(2048)?`; `PinnedCommitSha string(40)?`; `PinnedTag string(100)?`; `SourceCode text?` (the curated source install/clone copy from); `SourceRevision int` (default 1; bumped only when `SourceCode` actually changes); `DefaultSettings` **[VC:JSON]**; `DefaultEventSubscriptions` **[VC:JSON]**; `ReviewStatus string(20)` [VC:enum] Index (`submitted`\|`in_review`\|`verified`\|`rejected`); `ReviewedByUserId Guid?` FK→`Users.Id`; `ReviewNotes text?`; `ReviewedAt timestamp?`; `AvailableInSaaS bool`; `InstallCount int`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(GitHubRepoUrl, PinnedCommitSha)`. |
| **`WidgetGallerySubmissionEvent`** | §P.9 `[GLOBAL, APPEND-ONLY]` (no `BroadcasterId`) | global | `Id Guid` PK; `GalleryItemId Guid` FK→`WidgetGalleryItem.Id` Index; `FromStatus string(20)?`; `ToStatus string(20)`; `ChangedByUserId Guid?` FK→`Users.Id`; `NewPinnedCommitSha string(40)?`; `Note text?`; `OccurredAt timestamp` Index; `CreatedAt`. Immutable review/pin-change history. |

**Adjacent (read-only here, owned elsewhere):** `Channels.OverlayToken string(36)` Unique (§A.2) — the opaque per-channel overlay token, kept as a **legacy channel-wide** credential that still resolves. AS-BUILT each widget and system surface has its **own independent** token, `Widget.OverlayToken` (48 hex, random, never derived from the channel token), rotated per widget with a grace window (`PreviousOverlayToken`); rotating one widget's token never touches another's. `IWidgetService.ResolveOverlayScopeAsync` resolves a presented token in this order: the widget's own token, a still-live previous token, then the legacy channel token — and returns an `OverlayTokenScope(BroadcasterId, WidgetId?)`; a widget token confines the connection to that widget. Verified at the ticket exchange (§7); not PII; **never** the user JWT (stack §Realtime). `WidgetGalleryItem.TrustTier` drives the SaaS rendering CSP tier (§ below).

**TrustTier source mapping (binding — security-load-bearing).** `OverlayWidgetEntry.TrustTier` (non-null, the CSP-tier input) is derived per widget from `Widget.Source`, **not** stored on `Widget`. A gallery-installed widget (`Source ∈ {verified_gallery, first_party}`, `GalleryItemId` set) inherits `WidgetGalleryItem.TrustTier` (`first_party`\|`verified_community`). A `Source=custom` widget (`GalleryItemId=null` — self-authored, the only output of `CreateAsync`+`CompileAsync`) has **no** `WidgetGalleryItem` and maps to **`unverified`** — fail-closed, never silently guessed. Mapping (exhaustive): `first_party` source → gallery `first_party`; `verified_gallery` source → gallery `verified_community`; `custom` source → `unverified`. A gallery-sourced widget whose `WidgetGalleryItem` is unexpectedly missing also falls back to `unverified` (fail-closed).

**EF mapping notes (binding):**
- All `[VC:JSON]` columns use the hand-rolled `JsonValueConverter<T>` + `JsonValueComparer<T>` convention (Newtonsoft.Json) — **never** `HasColumnType("jsonb")`/`HasDefaultValueSql("…::jsonb")`. **Done:** the live `Infrastructure/Widgets/Persistence/WidgetConfiguration.cs` uses the converters and no `jsonb` (re-verified 2026-08-22).
- `Widget` carries the soft-delete global filter (`DeletedAt == null`). `WidgetVersion`/`WidgetGallerySubmissionEvent` are append-only (no filter, no `UpdatedAt`/`DeletedAt`).
- `WidgetGalleryItem`/`WidgetGallerySubmissionEvent` are GLOBAL — **no** `BroadcasterId`, **no** tenant query filter; gallery reads are unscoped, writes are platform-IAM gated.

### 1.1 First-party catalogue (seeded)

**Nineteen** user-installable `WidgetGalleryItem` rows ship with the bot (the table below is the **only** source of the count — `dev-platform.md` and every other doc cite it, never a separate number), seeded idempotently by `FirstPartyWidgetCatalogueSeeder` (`ISeeder`, GLOBAL reference data, upsert by a stable natural key so a re-run adds nothing) from `Infrastructure/Content/Widgets/FirstPartyWidgetCatalogue.cs`. All rows: `TrustTier=first_party`, `ReviewStatus=verified`, `AvailableInSaaS=true`, `SubmitterUserId=null` (platform-owned), `InstallCount=0`. The two audio/alert surfaces the catalogue file still lists under the keys `alerts` and `tts_caption` are **not gallery items** — they are the channel-owned system surfaces of §1.2 and leave the installable catalogue (the file is trimmed to the nineteen below; the seeder provisions the §1.2 surfaces per channel instead).

**First-party provenance (schema delta).** First-party widgets ship their source IN-REPO (compiled from the in-repo widget source tree at build time — there is no static `web/` folder), not from GitHub. So for `TrustTier=first_party` the `GitHubRepoUrl` and `PinnedCommitSha` columns are NULL, and a new `SourceKind string(20) [VC:enum] = in_repo | github` discriminator on `WidgetGalleryItem` distinguishes them (community submissions = `github`, the seeded catalogue = `in_repo`). The seeder loads each item's `SourceCode` + default settings schema from its in-repo asset on seed; install and clone copy from there. Note this `SourceKind` column + the now-nullable `GitHubRepoUrl`/`PinnedCommitSha` are a delta to the locked schema's `WidgetGalleryItem` table (DOMAIN for widgets, §P.8) — added there too.

Each item declares a default settings schema (the config keys used to render its config form and validate overrides).

Event bindings are the `domain.action` names of `widget-sdk.md` §2.1 (one name per domain event across every platform connection).

| # | Name | key | Purpose | Binds | Config keys |
|---|---|---|---|---|---|
| 1 | Chat box | `chat_box` | live chat rendered from the DECORATED fragment tree (consumes `chat-decoration.md`: FFZ/7TV/BTTV emotes + badges) | `chat.message` | `theme`, `maxMessages`, `fadeAfterMs`, `showBadges`, `showEmotes`, `hideCommands`, `hideBots`, `fontFamily`, `background` |
| 2 | Now Playing | `now_playing` | current track | `song.changed` | `layout`, `showArt`, `showProgressBar`, `provider`, `youtubeMode` |
| 3 | SR Queue | `sr_queue` | upcoming song-request queue | `song.changed` | `count`, `showRequester`, `showDuration` |
| 4 | Goal bar | `goal_bar` | follower/sub/bits goal progress | `viewer.followed`, `viewer.subscribed`, `viewer.gifted`, `bits.cheered`, `goal.changed` | `metric`, `target`, `start`, `resetCadence`, `colors`, `labels` |
| 5 | Event ticker | `event_ticker` | scrolling recent events | `viewer.*`, `bits.cheered`, `channel.raided`, `supporter.any` | `events[]`, `speed`, `count` |
| 6 | Labels | `labels` | single-stat text (latest follower/sub, top cheerer, counts) | `viewer.followed`, `viewer.subscribed`, `bits.cheered` | `label`, `formatString` |
| 7 | Poll / Prediction | `poll_prediction` | live poll/prediction bars | `poll.updated`, `prediction.updated` | `position`, `colors` |
| 8 | Redemption alert | `redemption_alert` | channel-point redemption popup | `reward.redeemed` | `rewards[]` (per-reward enable), `textTemplate`, `sound` |
| 9 | Countdown / Timer | `countdown_timer` | countdown to a time or duration (BRB/soon), dashboard-controllable | — (settings push) | `target`, `durationMs`, `label`, `onCompleteText` |
| 10 | Emote wall | `emote_wall` | emotes from chat float across screen, incl. FFZ/7TV/BTTV emote fragments from the decorator | `chat.message` | `density`, `size`, `animation`, `providers[]` |
| 11 | Custom Data | `custom_data` | live value of a custom data source (`custom-events.md`); a heart-rate gauge is this bound to `heartrate.bpm` | `custom.<name>` | `source` (custom-data source `name`), `field` (optional), `render` (`number`\|`gauge`\|`text`), `label`, `min`, `max` |
| 12 | Drop Game | `drop_game` | live drop-game round: target zone, each chatter's landing marker, payout scoreboard | `game.lobby`, `game.running`, `game.resolved` | `accentColor`, `hideAfterMs` |
| 13 | Raffle | `raffle` | live raffle round: entrant roster, climbing pot, winner reveal | `game.lobby`, `game.running`, `game.resolved` | `accentColor`, `hideAfterMs` |
| 14 | Heist | `heist` | live heist round: crew roster, escape odds, per-member outcome | `game.lobby`, `game.running`, `game.resolved` | `accentColor`, `hideAfterMs` |
| 15 | Crash | `crash` | live crash round: rising multiplier, cash-out ticker, bust reveal | `game.lobby`, `game.running`, `game.resolved` | `accentColor`, `hideAfterMs` |
| 16 | Recent Followers | `recent_followers` | always-on panel of the most recent followers | `viewer.followed` | `count`, `title`, `accentColor` |
| 17 | Sub Train | `sub_train` | rolling-window sub/gift hype counter | `viewer.subscribed`, `viewer.gifted` | `windowMs`, `accentColor` |
| 18 | Socials | `socials` | rotating social-handles bar (config only, no event feed) | — | `handles[]`, `rotateMs`, `accentColor` |
| 19 | Top Cheerers | `top_cheerers` | ranked board of the session's biggest cheerers | `bits.cheered` | `count`, `title`, `accentColor` |

**Dependency:** items `chat_box` (#1) and `emote_wall` (#10) consume the third-party-emote fragment tree from `chat-decoration.md` (decorated DTO). **Test:** each of the nineteen seeds as `TrustTier=first_party` + `AvailableInSaaS=true`, installs into a channel, and carries its declared config keys in the default settings schema; the seeder's key set equals this table's key set exactly (a test diff-asserts it).

### 1.2 System surfaces (channel-owned, auto-provisioned — not gallery items)

A **system surface** is a channel-owned page that is never installed from the gallery: it is provisioned for every channel at channel creation (and on first use if missing), served like a widget (own SPA, own per-widget `Widget.OverlayToken`, see §1 Adjacent), configured from the page that owns it, and cannot be uninstalled — only disabled. Three ship:

| Surface | Owner page | Behavior | Config |
|---|---|---|---|
| **Alert surface** | Alerts & Events (event responses) | **The one alert queue across every platform connection.** Renders every on-air alert an event response produces — `viewer.followed` / `viewer.subscribed` / `viewer.gifted` / `bits.cheered` / `channel.raided` / `supporter.*` (branched on `Kind`, `supporter-events.md`) — from Twitch, Kick, YouTube and X alike, strictly in order, one at a time. It consumes `IOverlayClient.WidgetEvent` pushes from the event-response engine; there is no per-platform alert page. | `events[]` (per-event enable), `sound`, `image`, `textTemplate`, `durationMs`, `minBits`, `minGiftCount`, `minAmount` |
| **TTS surface** | TTS page (`tts.md` §6.2) | Holds the `<audio>` element for TTS. Consumes `IOverlayClient.TtsSpeak`; plays one utterance at a time from an **ordered audio queue** (AS-BUILT one `TtsSpeak` push = one utterance = one voice; multi-segment utterances are the S054 target, §7). Server-synthesized audio arrives as a `PlaySound` on the shared audio bus; `client_edge` utterances use the browser's `speechSynthesis` with `utter.voice`/`lang` resolved from `voiceId`/`locale`; an optional caption (speaking indicator + text) renders when `showText` is on. The former `tts_caption` gallery item is this surface. | `showText`, `voiceLabel`, `position`, `volume` |
| **Sound surface** | Sound clips page (`sound-system.md`) | Sound plays on the **shared overlay audio bus**: the overlay SDK (`/overlay/sdk.js`) that every widget page loads holds the `<audio>` elements and handles `PlaySound` / `StopSound`, so a clip plays on whichever browser source is connected. **Single-clip rule (S-OBS-06, `75dd21483`):** a clip started with no `Handle` is the one "current" clip — starting another stops it first, so clips never stack. A clip with a `Handle` is its own independent slot, stopped only by that handle or by `StopSound(All)`. `POST /sound-clips/stop` (`sounds:write`) pushes `StopSound(All)`; with no overlay connected it reports `NOT_ATTACHED` instead of a silent no-op. | `volume` |

The Alert, TTS and Sound surfaces are each added to OBS once (one browser source per surface); every other on-air element is a gallery widget.

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

// NEW — moderation retraction: pull content off-screen that must no longer be shown
public sealed record OverlayContentRetractedEvent : DomainEventBase
{
    public string? SourceMessageId { get; init; }   // platform message id, when the trigger was one message
    public Guid? AuthorUserId { get; init; }        // set for timeout/ban — retract everything from this author
    public required string Reason { get; init; }    // message_deleted | user_timeout | user_ban | mod_retract
    public Guid? RetractedByUserId { get; init; }   // null when automated
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

### 2a. Moderation retraction — deleted content leaves the screen

A message deleted in chat is still on stream if an overlay is showing it or the TTS surface is
reading it. The deletion is invisible to the audience and the payload lands anyway. **Every
moderation removal must reach the overlays, immediately.**

`OverlayModerationRetractionHandler` (`Infrastructure/Widgets/EventHandlers/`) subscribes to the
moderation events — message deleted, user timed out, user banned, network-nuke batch — publishes
`OverlayContentRetractedEvent`, and pushes `IOverlayClient.Retract` to the channel's overlay group.

```csharp
public sealed record RetractPayload(
    Guid BroadcasterId,
    string? SourceMessageId,   // retract content from this one message
    string? AuthorUserId,      // retract everything from this author (timeout/ban)
    string Reason);            // message_deleted | user_timeout | user_ban | mod_retract
```

**Status.** Server push is built (S-RETRACT-a: `OverlayContentRetractedEvent`, `OverlayModerationRetractionHandler`, `IOverlayClient.Retract`, `RetractPayload`). Client honouring is **not built**: the overlay SDK and the system surfaces do not yet act on `Retract` — S-RETRACT-b (Chat + Alert), S-RETRACT-c (TTS, `tts.md` §3.4a) and S-RETRACT-d (Sound + custom-widget SDK). The table below is the obligation each surface must meet, not shipped behavior.

Every surface must honour it, and each surface's obligation is explicit:

| Surface | On retract |
|---|---|
| **TTS** | Stop playback **mid-sentence**, drop matching queued utterances (`tts.md` §3.4a) |
| **Chat** | Remove the message node; remove all of the author's on removal by author |
| **Alert** | Cancel the alert if it is on-screen or queued |
| **Sound** | Stop a clip that this message triggered |
| **Custom widgets** | Receive the same event through the widget SDK (`widget-sdk.md`) and are expected to handle it; the SDK's chat/alert helpers do it automatically |

**Correlation is by `SourceMessageId`**, which every surface must retain on anything it renders
from a chat message — a widget that discards it cannot retract, so the SDK's helpers keep it.

Retraction is pushed on the **same connection** as the content it cancels, so it can never
overtake it. Retracting something already gone is a no-op, never an error.

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
- `GetOverlayManifestAsync` — resolves channel by `OverlayToken`, returns the channel's enabled widgets + their served bundle URLs + CSP nonce + trust tier; a public (token-auth) read path (the other token-resolved reads are in §5b); XSS-safe (no raw user HTML, see §rendering). Each entry's non-null `TrustTier` is derived from `Widget.Source` per the **TrustTier source mapping** (§1): gallery widgets inherit `WidgetGalleryItem.TrustTier`; `Source=custom` (`GalleryItemId=null`) maps to `unverified` (fail-closed) — never silently defaulted to a higher tier.
- `CompileAsync` — **compile-on-save core**: creates the next `WidgetVersion` (`VersionNumber = max+1`, `BuildStatus=pending`), invokes `IWidgetBuildService.BuildAsync`, persists `success`+`CompiledBundle`+`ContentHash` or `error`+`BuildError`+`BuildLog`; on success sets `Widget.ActiveVersionId`, publishes `WidgetBuildSucceededEvent` → OverlayHub `WidgetReload`; on failure publishes `WidgetBuildFailedEvent` → editor `WidgetCompileFailed` (never silent). Append-only — a failed build is a persisted version, not a discard.
- `ListVersionsAsync`/`GetVersionAsync` — version history (rollback/debug); `GetVersionAsync` includes `BuildLog`.
- `RollbackAsync` — re-points `Widget.ActiveVersionId` to an earlier **successful** version (fails if target build status ≠ `success`), publishes `WidgetBuildSucceededEvent` (cache-bust reload) without recompiling.
- `RecordRuntimeErrorAsync` — writes `Widget.LastRuntimeError`/`LastRanAt` from an overlay-reported runtime fault (OverlayHub `ReportRuntimeError`); no event.
- `InstallFromGalleryAsync` — fails unless the `WidgetGalleryItem` is `ReviewStatus=verified` **and** (SaaS profile) `AvailableInSaaS=true`; creates a `Widget` (`Source=verified_gallery`/`first_party`, `GalleryItemId` set), increments `InstallCount`, compiles the pinned-commit source into the first `WidgetVersion`. Unverified items are self-host-only (rejected on SaaS profile).
- `CloneToEditAsync` — forks a verified-gallery item OR an installed widget into a NEW, fully-owned `Source=custom` widget (⇒ `TrustTier=unverified`, fail-closed), `GalleryItemId=null`, `ActiveVersionId=null`; copies the source `SourceCode` into a fresh `WidgetVersion` (`VersionNumber=1`, `BuildStatus=pending`) but **not** the `CompiledBundle` — the clone recompiles on the owner's first save (`IWidgetBuildService.BuildAsync` sets `ActiveVersionId` then); new UUIDv7 `Id`, caller's `BroadcasterId`, `Name`/`Description`/`Framework` copied from the source; the clone is fully detached (no link back to the gallery item, independently editable). **Test:** the cloned widget has `Source=custom`, `GalleryItemId=null`, `ActiveVersionId=null`, a new `WidgetVersion` with the copied `SourceCode` + `BuildStatus=pending` and NO copied `CompiledBundle`, a fresh `Id`/`BroadcasterId`, and is unlinked from + independently editable of the gallery item.

**AS-BUILT — `CloneToEditAsync`.** The copy is compiled immediately (the clone is live on creation), not left at `ActiveVersionId=null` until the owner's first save; it stays `Source=custom`, `GalleryItemId=null`, fully detached.

**As-built additions to `IWidgetService`** (ids are `string` on the interface, see the file map):
- `GetDeleteBlastRadiusAsync` — the counted blast radius of a delete: stored versions plus the pipeline steps that name the widget in `PipelineStep.ConfigJson` (a MINIMUM when some references only resolve at run time). The dashboard renders it before the delete confirm.
- `GetByTokenAsync` — a widget by its public overlay token.
- `GetSettingsSchemaAsync` — the typed settings schema behind the generic settings form; for a `custom` widget it is read from the `settings.json` in the active version's project files (`widget-sdk.md` §7), and `WIDGET_NO_SETTINGS_SCHEMA` (`NOT_FOUND`) when the project has none.
- `GetProjectAsync` / `SaveProjectAsync` — the multi-file project (`ProjectDto` = `Files` + `Manifest`). Save re-builds through `IWidgetBuildService` (the trust boundary — a client bundle is never trusted); a clean build appends a new successful `WidgetVersion` (files + manifest + bundle + hash) and activates it; a failed build returns the reason and persists NO version.
- `ClearRuntimeErrorAsync` — the success-side twin of `RecordRuntimeErrorAsync`: clears a stamped `LastRuntimeError` and stamps `LastRanAt` when the browser source reconnects cleanly; a no-op when no error is stamped.
- Token-resolved public reads (token-auth only, never the user JWT): `GetOverlayBundleAsync`, `GetSpotifyPlaybackTokenAsync` (short-lived scoped access token, never the refresh token), `GetNowPlayingSnapshotAsync`, `GetScriptStorageValueAsync`, `GetQueueSnapshotAsync`.
- `GetTemplates` — the static starter templates for a new custom widget.
- `EnsureSystemWidgetAsync` — get-or-create a channel-owned system surface (§1.2) by the gallery item's natural key; returns the existing widget unchanged, otherwise installs it like `InstallFromGalleryAsync`.
- `UpdateFromGalleryAsync` — see *Catalogue update and reset-to-default* below.
- `RotateOverlayTokenAsync` — mints a new `Widget.OverlayToken` for exactly one widget; the retired token stays live for a grace window (`WidgetTokenRotationResult.GraceExpiresAt`).
- `ResolveOverlayScopeAsync` / `ResolveBroadcasterIdByOverlayTokenAsync` — token → `OverlayTokenScope` / channel (resolution order in §1 Adjacent).

#### Catalogue update and reset-to-default

An installed widget is a tracked copy of a gallery item; the platform never rebuilds it on its own.
- **Update available.** `WidgetDetail.GalleryUpdateAvailable` is true when the linked item's `SourceRevision` is greater than the widget's `InstalledSourceRevision`. The item's revision moves only when its `SourceCode` really changes (a first-party reseed with new in-repo source, or a community re-pin).
- **Customized.** `Widget.IsSourceCustomized(latestVersionNumber)` is true when the widget's newest `WidgetVersion` came after `CatalogueVersionNumber` (the channel saved its own source over the catalogue's). `WidgetDetail.IsCustomized` carries it. A catalogue update must never overwrite a customized widget on its own.
- **`POST /widgets/{widgetId}/update-from-gallery`** (`UpdateFromGalleryAsync`, `widget:write`) is BOTH the explicit "take the update" action and the "Reset to system default" action. It compiles the linked item's current source as a NEW `WidgetVersion` (compile-on-save; the channel's edited versions stay in history and rollback still reaches them), sets `CatalogueVersionNumber` to that version and `InstalledSourceRevision` to the item's revision. Settings and subscriptions the streamer changed are left untouched — only the source moves. Failures: `WIDGET_NOT_GALLERY_LINKED` (no `GalleryItemId`), `WIDGET_NO_SOURCE` (item has no source), `WIDGET_BUILD_FAILED` (the failed version is kept, the overlay keeps its previous code, and the call reports the failure instead of a reset that did not reach the stream).
- System surfaces (§1.2) use the same path: a per-channel edit is a new version, and reset-to-default re-pulls the catalogue source.

### 3.2 `IWidgetBuildService` (esbuild compile boundary; multi-file project input)

Namespace `NomNomzBot.Application.Widgets.Services`. Pure compile boundary; no DB. Impl `EsbuildWidgetBuildService` (`Infrastructure/Widgets/Bundling/`); failure is a `Result` failure, never a throw.

```csharp
public interface IWidgetBuildService
{
    Task<Result<WidgetBuildOutput>> BuildAsync(WidgetBuildInput input, CancellationToken ct = default);
}

// A project, not one string: manifest (entry, kind, framework, dependencies[]) + path -> content files.
public sealed record WidgetBuildInput(ProjectManifest Manifest, IReadOnlyDictionary<string, string> Files)
{
    public static WidgetBuildInput SingleFile(string framework, string source);   // wraps one source into a one-file project
}
public sealed record WidgetBuildOutput(string CompiledBundle, string ContentHash, string BuildLog);  // ContentHash = sha256(CompiledBundle), 64 lower-case hex
```

Behavior (`dev-platform.md` §4.2):
- **Checks first.** The manifest `Entry` must be in `Files` (`WIDGET_PROJECT_ENTRY_MISSING`); every path is checked against traversal before anything touches disk; every declared dependency must be on `IWidgetDependencyAllowlist` (`WIDGET_DEPENDENCY_NOT_ALLOWED`) — vetted, bot-provided libraries only, there is no npm.
- **Materialize and bundle.** The files are written to a temp directory (always deleted, `try/finally`) and the standalone `esbuild` binary (`Widgets:EsbuildPath`, default `esbuild` on PATH) bundles from `Entry` with `--bundle`, so cross-file relative imports resolve into ONE bundle. Failure carries esbuild's stderr as `ErrorMessage` (-> `WidgetVersion.BuildError`).
- **Per framework.** `vanilla`: no build step, the entry file IS the bundle. `react`: esbuild with `--jsx=automatic`. `vue`: a **two-stage build** — stage 1 compiles every `.vue` file to an ES module with `IVueSfcCompiler` (`JintVueSfcCompiler`: the vendored `@vue/compiler-sfc` run in a pooled set of pre-warmed Jint engines, a singleton; a compile problem is a coded failure); stage 2 bundles a synthetic mount module (`__nnz_mount__.ts`) with `vue` kept external, mapped to the host-injected `window.Vue` (`/overlay/vue.js`) by a `require` shim, so each bundle is its own closure and several coexist on one page. `svelte` needs the plugin-based build and is not on the standalone path: `WIDGET_FRAMEWORK_UNSUPPORTED`.
- Deterministic: same input -> same `ContentHash` (cache-bust correctness).

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
- `UpdatePinAsync` — re-pins `PinnedCommitSha`/`PinnedTag`; **forces `ReviewStatus` back to `in_review`** (re-verify on update — never auto-pull HEAD), appends an event with `NewPinnedCommitSha`. AS-BUILT: the re-pin also drops `TrustTier` to `unverified` and clears `AvailableInSaaS` (fail-closed — new code carries no earned trust), and first-party `in_repo` rows are immutable to review/pin (`FIRST_PARTY_IMMUTABLE`) — the seeder owns them. The pinned commit must be a FULL 40-hex sha; the repo URL canonicalizes to `https://github.com/{owner}/{repo}`. `gallery:review` seeds sensitive, granted to `platform-super-admin` + `platform-trust-safety`.

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
    bool IsEnabled, string? OverlayUrl, Guid? ActiveVersionId, Guid? GalleryItemId,
    Dictionary<string, object?> Settings, List<string> EventSubscriptions,
    string? LastRuntimeError, DateTime? LastRanAt, DateTime CreatedAt, DateTime UpdatedAt,
    bool GalleryUpdateAvailable, bool IsAttached, bool IsCustomized);   // AS-BUILT: staleness, live-presence, and customized flags

// AS-BUILT additions used by §3.1 / §5a / §5b:
public sealed record WidgetTokenRotationResult(Guid WidgetId, string PreviousUrl, string NewUrl, DateTimeOffset GraceExpiresAt);
public sealed record OverlayBundle(string Content, string Framework, string ContentHash);
public sealed record OverlayTokenScope(Guid BroadcasterId, Guid? WidgetId);   // WidgetId set = confined to that widget
public sealed record WidgetTemplate(string Key, string Name, string Description, string Framework, string Source);

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
    Guid Id, Guid WidgetId, int VersionNumber, string BuildStatus, string? SourceCode,
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
    public string? ReviewStatus { get; init; }   // AS-BUILT: honored only for a gallery:review principal (the reviewer's queue), not audit:read
}

public sealed record GalleryItemSummary(
    Guid Id, string Name, string Framework, string TrustTier,
    string ReviewStatus, int InstallCount, bool AvailableInSaaS);

// AS-BUILT: the detail is the SUPERSET of the install/clone shape (SourceKind, DefaultSettings,
// DefaultEventSubscriptions, SourceCode — what the preview/install UI reads) and the review/pin fields
// below; GitHub provenance is nullable (null for the in-repo first-party catalogue).
public sealed record GalleryItemDetail(
    Guid Id, string Name, string? Description, string Framework, string TrustTier,
    int InstallCount, bool AvailableInSaaS, string SourceKind,
    Dictionary<string, object> DefaultSettings, List<string> DefaultEventSubscriptions, string? SourceCode,
    string? GitHubRepoUrl, string? PinnedCommitSha, string? PinnedTag,
    string ReviewStatus, string? ReviewNotes, DateTime? ReviewedAt, DateTime CreatedAt);

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

**Role gate.** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the gate column's action key before the service call (403 `FORBIDDEN` when below). Rows marked `platform` are **Plane-C** (platform IAM) = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, …)`; the ASP.NET `[Authorize(Policy="<key>")]` policy-name **is** the permission key verbatim. The keys are seeded global `ActionDefinitions` (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Widget authoring is a **Moderator-default** management action (`609ae0fc2`: bot-internal tooling is reversible and auditable, so a Twitch mod manages it out of the box); a broadcaster may raise a floor via `ChannelActionOverride`. Reads (`widget:read`, `widget:version:read`) default to Moderator and may be lowered to VIP; the write keys (`widget:write`, `widget:compile`, `widget:rollback`, `widget:install`) default to Moderator and cannot be lowered. Gallery review is a **platform** action.

### 5a. Tenant widget CRUD, project, versions — `WidgetsController` (as-built)
`[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]` `[Authorize]` — every route below carries `[RequireAction("<key>")]`.

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `PageRequestDto` (query) | `PaginatedResponse<WidgetDetail>` | management / Moderator · `widget:read` |
| GET | `/templates` | — | `StatusResponseDto<IReadOnlyList<WidgetTemplate>>` | management / Moderator · `widget:read` |
| GET | `/{widgetId}` | — | `StatusResponseDto<WidgetDetail>` | management / Moderator · `widget:read` |
| GET | `/{widgetId}/settings-schema` | — | `StatusResponseDto<WidgetSettingsSchema>` | management / Moderator · `widget:read` |
| POST | `/` | `CreateWidgetRequest` | `StatusResponseDto<WidgetDetail>` (201) | management / Moderator · `widget:write` |
| POST | `/clone` | `CloneWidgetRequest` | `StatusResponseDto<WidgetDetail>` (201) | management / Moderator · `widget:write` |
| POST | `/install/{galleryItemId}` | — | `StatusResponseDto<WidgetDetail>` (201) | management / Moderator · `widget:install` |
| POST | `/{widgetId}/update-from-gallery` | — | `StatusResponseDto<WidgetDetail>` | management / Moderator · `widget:write` |
| PUT | `/{widgetId}` | `UpdateWidgetRequest` | `StatusResponseDto<WidgetDetail>` | management / Moderator · `widget:write` |
| GET | `/{widgetId}/blast-radius` | — | `StatusResponseDto<BlastRadiusDto>` | management / Moderator · `widget:write` |
| DELETE | `/{widgetId}` | — | 204 | management / Moderator · `widget:write` |
| POST | `/{widgetId}/compile` | `CompileWidgetRequest` | `StatusResponseDto<WidgetVersionDetail>` | management / Moderator · `widget:compile` |
| GET | `/{widgetId}/project` | — | `StatusResponseDto<ProjectDto>` | management / Moderator · `widget:read` |
| PUT | `/{widgetId}/project` | `ProjectDto` | `StatusResponseDto<WidgetVersionDetail>` | management / Moderator · `widget:write` |
| GET | `/{widgetId}/versions` | `PageRequestDto` (query) | `PaginatedResponse<WidgetVersionSummary>` | management / Moderator · `widget:version:read` |
| GET | `/{widgetId}/versions/{versionId}` | — | `StatusResponseDto<WidgetVersionDetail>` | management / Moderator · `widget:version:read` |
| POST | `/{widgetId}/rollback/{versionId}` | — | `StatusResponseDto<WidgetDetail>` | management / Moderator · `widget:rollback` |
| POST | `/{widgetId}/overlay-token/rotate` | — | `StatusResponseDto<WidgetTokenRotationResult>` | management / Moderator · `widget:write` |
| POST | `/test-event` (`WidgetTestEventController`) | `WidgetTestEventRequest` (`EventType`, optional `Data`) | `StatusResponseDto<string>` (who could have received it) | management / Moderator · `widget:write` |

### 5b. Public overlay routes — `OverlayController`, `OverlayTicketController`, host page
**OverlayToken auth only** (never the user JWT), all `[AllowAnonymous]`. A token is a widget's own `OverlayToken`, its still-live previous token, or the legacy channel token (§1 Adjacent); a missing token is 400.

`OverlayController` — `[Route("api/v{version:apiVersion}/overlay")]`, token in the `?token=` query:

| Verb | Route | Response | Purpose |
|---|---|---|---|
| GET | `/manifest` | `StatusResponseDto<OverlayManifest>` | the channel's enabled, built widgets + bundle URLs, hashes, trust tiers, settings; `access_token` scrubbed from logs |
| GET | `/bundle/{widgetId}` | `text/html` (vanilla) or `application/javascript`, `Cache-Control: public, max-age=31536000, immutable`, header `X-Widget-Framework` | one widget's active compiled bundle; the URL carries the content hash as `?v=` |
| GET | `/now-playing` | `StatusResponseDto<OverlayNowPlayingSnapshot>` (`Data` null when idle) | initial playback state for a `now_playing` widget |
| GET | `/queue` | `StatusResponseDto<IReadOnlyList<MusicQueueItem>>` | the playback queue for a "next up" widget |
| GET | `/spotify-token` | `StatusResponseDto<string>` | short-lived scoped Spotify token for the in-browser player |
| GET | `/storage/{key}` | `StatusResponseDto<string>` | one script-storage value to bootstrap durable widget state |

`OverlayTicketController` — `[Route("overlay")]` (unversioned, hidden from OpenAPI, anonymous rate-limit policy):

| Verb | Route | Request | Response |
|---|---|---|---|
| POST | `/overlay/ticket` | header `X-Overlay-Token: <token>` | `{ "ticket": "<opaque>" }`; 401 when the header is missing or the token matches nothing live; 429 when the per-token throttle trips |

Host and asset routes (unversioned, anonymous, rate-limited): `GET /overlay?widgetId=&token=` (`OverlayHostController`, the per-widget browser-source page), `GET /overlay/sdk.js` (`OverlaySdkController`), `GET /overlay/vue.js` (`OverlayVueRuntimeController`, vendored Vue global build).

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
| `widget_event` | params (no typed config DTO): `widget_id` (owned id, ULID or GUID, decoded by `OwnedIdCodec`; picker field kind `Widget`), `event_type` (text, required), `data` (optional JSON object) | Pushes one `WidgetEventDto(widgetId, eventType, data)` to the target widget's group via `IWidgetEventNotifier.SendWidgetEventAsync`. Values are template-resolved by the engine before the action runs. `data` is materialized to a plain CLR graph (dictionaries, lists, primitives) so it round-trips over the MessagePack hub protocol. **Fail-closed, typed `ActionResult.Failure`, no push, no throw:** `widget_id` missing or undecodable, `event_type` blank, or the widget not found or disabled in the executing tenant (`GetAsync` is tenant-scoped, so another channel's widget reads as not found). **Not built:** config-time validation of `event_type` against `IAutomationEventRegistry` (an unknown name is a run-time no-op the widget ignores) and server-side XSS sanitization of `data` — text safety rests on the Vue runtime escaping interpolations. |

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

**OverlayHub wire surface (`IOverlayClient`, as-built):**
```csharp
public interface IOverlayClient
{
    Task WidgetEvent(WidgetEventDto evt);                    // to the widget group: WidgetEventDto(string WidgetId, string EventType, object? Data)
    Task WidgetReload();                                     // widget group: compile success / rollback
    Task WidgetSettingsChanged(WidgetSettingsDto settings);  // widget group
    Task WidgetCompileFailed(WidgetCompileFailedDto error);  // widget group: editor surfaces the build error
    Task Event(OverlayEventDto evt);                         // overlay group: the generic channel-wide event feed, OverlayEventDto(string Type, string Payload = raw JSON)
    Task PlaySound(PlaySoundPayload payload);                // overlay group: start a clip on the shared audio bus (§1.2 Sound)
    Task StopSound(StopSoundPayload payload);                // overlay group: stop one handle, or everything when All
    Task TtsSpeak(TtsSpeakPayload payload);                  // overlay group: one client-edge utterance for the TTS surface (§1.2; tts.md §6.2)
    Task Retract(RetractPayload payload);                    // overlay group: moderation retraction (§2a)
}
```
Groups: every connection joins `overlay-{broadcasterId}` on connect; `JoinWidget` adds `widget-{broadcasterId}-{widgetId}`.

Hub server methods (`OverlayHub`): `JoinWidget(string widgetId)` -> `JoinWidgetResponse(Success, Error, InitialState = the widget's saved settings)`; `LeaveWidget(string widgetId)`; `WidgetReady(string widgetId)`; `ReportRuntimeError(string widgetId, string error)` -> `IWidgetService.RecordRuntimeErrorAsync` (message truncated to 2000 chars). A widget-scoped connection (its ticket carries a `WidgetId`) may `JoinWidget` only its own widget; a successful `JoinWidget` also clears a stale `LastRuntimeError`.

**Connect-time auth: single-use ticket, never the token on the WebSocket URL.** OBS browser sources cannot set WebSocket headers, and a long-lived token in the `/hubs/overlay` query string would leak into proxy logs and browser history. So: (1) the overlay SDK sends the overlay token in the `X-Overlay-Token` header of `POST /overlay/ticket` (§5b); (2) the server resolves it (`ResolveOverlayScopeAsync`), applies the per-token throttle, and mints an opaque ticket bound to the resulting `OverlayTokenScope`; (3) the SDK connects to `/hubs/overlay?ticket=<ticket>`; (4) `OnConnectedAsync` redeems the ticket (`IOverlayTicketService.RedeemTicket`). A ticket lives **30 seconds** and burns on first use; a missing, unknown, expired or reused ticket aborts the connection. **Never** the user JWT. `WidgetCompileFailedDto(string WidgetId, int VersionNumber, string BuildError)` lives in `Hubs/Dtos/HubResponseDtos.cs`.

**TTS utterance payload (`Hubs/Dtos/HubResponseDtos.cs`, as-built):** the `TtsSpeak` push DTO is **owned here** (the `IOverlayClient` contract lives in this subsystem) and **consumed by the system TTS surface** (§1.2; dispatch rules in `tts.md` §3.4/§6.2). AS-BUILT one `TtsSpeak` push is ONE utterance with ONE voice — a single `Text`, not a segment array.
```csharp
public sealed record TtsSpeakPayload(
    Guid BroadcasterId,        // tenant key (Guid) — overlay group scope
    string Text,               // the utterance text
    string VoiceId,            // resolved voice (tts.md §6.2 precedence) — the surface sets utter.voice/lang from it on client_edge
    string Provider,           // edge|elevenlabs|azure
    string? CueId,             // optional client-side dedupe / cancellation handle
    TtsSpeakOptions? Options,  // optional prosody overrides
    string? Locale = null);    // BCP-47 hint; steers utter.lang when no browser voice matches VoiceId

public sealed record TtsSpeakOptions(double? Rate, double? Pitch, double? Volume);
```

#### S054 target — multi-segment utterances (NOT built)

S054 (`SHORTCOMINGS-EXECUTION-PLAN.md`) replaces the single `Text`/`VoiceId`/`Provider` with an ordered segment array, so ONE push carries an utterance whose parts use different voices; the surface enqueues the utterance and plays its segments back-to-back (`client_edge` segments via `speechSynthesis` with `utter.voice`/`lang` from `VoiceId`; `byok`/`self_host` segments by `AudioUrl`). The `tts.md` request side (`TtsSegment` list, per-segment voice mode) is already written against this target shape.
```csharp
// TARGET shape — not the shipped record above
public sealed record TtsSpeakPayload(
    Guid BroadcasterId,
    IReadOnlyList<TtsSpeakSegment> Segments,  // ordered; played back-to-back as one utterance
    string? CueId,
    TtsSpeakOptions? Options);

public sealed record TtsSpeakSegment(string Text, string VoiceId, string Provider, string? AudioUrl);
```

**Sound-clip payloads (`Hubs/Dtos/HubResponseDtos.cs`):** `PlaySound` / `StopSound` are **owned here** and **consumed by `sound-system.md` `play_sound`/`stop_sound`**. The overlay SDK holds the `<audio>` elements on the shared audio bus (§1.2 Sound, single-clip rule S-OBS-06).
```csharp
public sealed record PlaySoundPayload(
    string PlaybackUrl,             // tokened, overlay-fetchable clip URL (ISoundClipStore)
    int Volume,                     // effective output volume (0–100)
    string? Handle);                // optional name: an independent slot a targeted stop_sound can stop

public sealed record StopSoundPayload(
    string? Handle,                 // stop this named clip
    bool All);                      // true = stop the current clip and every handled clip
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

1. **Widget authoring role — `Moderator` default (management plane).** Create/update/delete/compile/rollback/install (`widget:write`, `widget:compile`, `widget:rollback`, `widget:install`) default to `Moderator` (`609ae0fc2`: bot-internal tooling is reversible and auditable); reads (`widget:read`, `widget:version:read`) default to `Moderator` and may be lowered to `Vip`. A broadcaster may RAISE any of them via `ChannelActionOverride` (no signature change); the write keys cannot be lowered below `Moderator`.
2. **esbuild execution form — out-of-process CLI binary.** `EsbuildWidgetBuildService` shells out to a bundled `esbuild` CLI binary behind `IWidgetBuildService` (matches design §"server-side build (esbuild)"). The interface is stable regardless of runner, so the binary's path/runner is supplied from config (lite + SaaS) per §7.
