# Interface Specification — Per-Viewer Data Store

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** legacy parity — `C:\Projects\StoneyEagle\nomercy-bot` (`Record` per-user activity/JSON store + the `Stats` command that aggregates per-viewer totals). Corpus (the read-models this spec **composes, never duplicates**): `analytics.md` (`IViewerAnalyticsService.GetProfileAsync`→`ViewerProfileDto` over M.1 `ViewerProfiles`: `TotalWatchSeconds/TotalMessages/TotalCommandsUsed/TotalRedemptions/TotalSongRequests/First+LastSeenAt`; `GetStreakAsync` over M.3); `community-dashboard.md` (`IViewerProfileService.GetProfileAsync`→`ViewerProfileSummaryDto` = identity/standing + moderation summary + activity + streak + economy + permits + overrides + command usage + free-form viewer data + recent quotes, served by `CommunityController` at `GET /channels/{channelId}/community/{userId:guid}/profile`); `economy.md` (`ICurrencyAccountService.GetBalanceAsync`/`GetOrCreateAccountAsync`→`CurrencyAccountDto`); `event-store.md` (`IEventJournal.QueryAsync` by `ActorUserId`); `commands-pipelines.md` (`NamedCounter` G.4 + `set_counter`/`adjust_counter` + `{{count.<name>}}` dispatch-seeded resolution, §6.3 helpers, §3.13 `ICommandAction`, built-in commands `ChannelBuiltinCommand` G.2a); `gdpr-crypto.md` (per-subject erasure). Locked schema `2026-06-16-database-schema.md` (Domain G — beside `NamedCounter`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json.

> **Why.** The legacy bot "tracks everything a viewer has done" and exposes a `Stats` command. In the new architecture, the **inputs** exist — analytics projections (`ViewerProfiles`, `WatchStreaks`, engagement dailies), the economy wallet, and the event journal record messages, watch-time, follows/subs/bits/redemptions/song-requests/raids — and the aggregate `IViewerProfileService.GetProfileAsync`→`ViewerProfileSummaryDto` that composes them is **built** and served by `CommunityController` (`GET /channels/{channelId}/community/{userId:guid}/profile`); this spec depends on it, never re-derives it. The **other missing piece** is a **writable per-viewer key/value store** — the analog of the legacy `Record` (arbitrary per-viewer custom data a pipeline sets and reads: a per-viewer death counter, "favorite game", a quest flag). `NamedCounter` (G.4) is **per-channel**; there is no **per-viewer** mutable store. This spec adds it (a per-viewer sibling of `NamedCounter`), surfaces it + the existing stats in the template/command layer, and ships `!stats` / `!profile` built-ins for parity — without re-deriving a single projection.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Writable per-viewer KV: `ViewerDatum` (G.14)** — the per-viewer analog of `NamedCounter` (G.4): unique `(BroadcasterId, ViewerUserId, Key)`, a string `Value`. Three actions: `set_viewer_data` (string set/upsert), `adjust_viewer_data` (atomic numeric increment — parse current as `long`, add `Delta`, store) and `clear_viewer_data` (bulk-clear one key for **every** viewer in the channel, e.g. reset a lurk/AFK flag when the stream goes live). One table covers per-viewer flags/strings **and** per-viewer counters. |
| D2 | **Compose, never duplicate, the aggregate profile.** "Everything a viewer has done" is `IViewerProfileService.GetProfileAsync`→`ViewerProfileSummaryDto` (served by `CommunityController.GetProfile`; its `FreeFormData` list **is** the `ViewerDatum` map for that viewer) and `IViewerAnalyticsService.GetProfileAsync`. This spec adds **no** new profile projection or read endpoint — it references those, surfacing the `ViewerDatum` map alongside them. |
| D3 | **Surface in the pipeline layer.** Template `{{viewer.data.<key>}}` / `{{target.data.<key>}}` (the `{{count.*}}` pattern — the template resolver's async pre-pass `ResolveViewerDataAsync` bulk-loads only the referenced keys, one read per side, into the variable bag before rendering, so rendering itself does no I/O; unset keys render empty). Plus per-viewer **stat** helpers sourced from M.1: `{{viewer.messages}}`, `{{viewer.watchtime}}`, `{{viewer.firstseen}}`, `{{viewer.redemptions}}`, `{{viewer.songrequests}}` (and `{{target.*}}` mirrors). Existing helpers (`{{target.balance}}`, `{{target.followage}}`, economy `{{economy.rank:user}}`) are **not** redefined. |
| D4 | **`!stats` and `!profile` built-in commands** — two `IBuiltinCommand`s (`BuiltinKey` `stats` and `profile`, one shared `StatsBuiltinBase`; not an alias of each other) surface a viewer's headline stats (messages, watch-time, points + rank, streak, first-seen) for the caller or a `@target`, composing the existing read services. Everyone, 5 s default cooldown. Parity with the legacy `Stats` command; each is a `ChannelBuiltinCommand` (G.2a). The reply is a `stats` response-slot group whose text is customizable via the `{stats.*}` helpers (§4). |
| D5 | **Privacy-proportionate.** `ViewerDatum` is ordinary per-viewer personal data (gameplay/custom flags — **not** special-category). On erasure, a subject's `ViewerDatum` rows are hard-deleted across every channel, soft-deleted rows included (`ErasureService`). Writes are tenant-scoped + viewer-scoped and bounded by **fixed** safety-baseline caps (not tier-scaled): key = lowercase slug ≤ 50 chars, value ≤ 500 chars, ≤ 100 live keys per viewer per channel — over-cap is rejected, never truncated. |
| D6 | **Schema delta: G.14 `ViewerDatum`** only. Three pipeline actions + the template helpers + the `!stats` / `!profile` built-ins are added to `commands-pipelines.md`. |

---

## 1. Entities

Domain G. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`ViewerDatum`** | **G.14 (NEW)** `[soft-delete]` `ITenantScoped` | tenant+viewer | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `ViewerUserId Guid` FK→`Users.Id` Index; `Key string(50)` (slug); `Value string(500)` (numeric ops parse/format as `long`); `UpdatedAt DateTime`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, ViewerUserId, Key)`. |

No new profile/aggregate table (D2). Reads of "everything a viewer has done" use the existing M.* projections + economy + event journal via their existing services.

---

## 2. Domain events

None. Per-viewer data writes are pipeline side effects (already journaled via the pipeline-execution record). Reads are read-only.

---

## 3. Service interface

Namespace `NomNomzBot.Application.ViewerData.Services` (request DTO `SetViewerDatumRequest(string Value)` in `NomNomzBot.Application.ViewerData.Dtos`). `Task<Result<T>>` / `Task<Result>`. Impl `ViewerDataService` in `NomNomzBot.Infrastructure/ViewerData/` (over `IApplicationDbContext`; `AdjustAsync` is a read-modify-write under an optimistic concurrency token on `Value` with 5 bounded retries, so concurrent increments sum).

```csharp
public interface IViewerDataService
{
    Task<Result<string?>> GetAsync(Guid broadcasterId, Guid viewerUserId, string key, CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<string, string>>> ListForViewerAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    Task<Result> SetAsync(Guid broadcasterId, Guid viewerUserId, string key, string value, CancellationToken ct = default);
    Task<Result<long>> AdjustAsync(Guid broadcasterId, Guid viewerUserId, string key, long delta, CancellationToken ct = default); // atomic upsert; returns new value
    Task<Result> DeleteAsync(Guid broadcasterId, Guid viewerUserId, string key, CancellationToken ct = default);

    // Bulk-clears the key for EVERY viewer in the channel (soft delete). Returns the number of rows cleared.
    Task<Result<int>> ClearKeyForAllAsync(Guid broadcasterId, string key, CancellationToken ct = default);

    // Bulk pre-load for the template resolver: the keys a template references, for the triggering viewer (+ target). One round-trip; missing keys are simply absent.
    Task<Result<IReadOnlyDictionary<string, string>>> LoadKeysAsync(Guid broadcasterId, Guid viewerUserId, IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
```

The **aggregate profile read is not redefined here** — callers use `IViewerProfileService.GetProfileAsync` (via `CommunityController`) / `IViewerAnalyticsService.GetProfileAsync` / `ICurrencyAccountService` (the `!stats` / `!profile` built-ins and the dashboard viewer card compose those). `ListForViewerAsync` lets the dashboard viewer card show the custom-data map beside them.

---

## 4. Pipeline actions, template helpers, built-in

**Actions** (`ICommandAction`, §3.13; in `NomNomzBot.Infrastructure/ViewerData/PipelineActions/`, category `pipeline.category.viewer_data`):

| Action `Type` | Parameters | Behavior |
|---|---|---|
| **`set_viewer_data`** | `{ string Key, string Value, string? Target }` | upsert `ViewerDatum` for the target viewer (default = triggering viewer). `Value` is a templated field (the action resolves its own templates). Writes `viewer.data.<key>` into the run variables and returns the stored value as `Output`. |
| **`adjust_viewer_data`** | `{ string Key, long Delta = 1, string? Target }` | atomic numeric increment (a non-numeric `Delta` fails); returns the new value as `Output` and into `Variables["viewer.data.<key>"]`. |
| **`clear_viewer_data`** | `{ string Key }` | bulk-clears the key for every viewer in the channel (`ClearKeyForAllAsync`); `Output` = the number of rows cleared. |

`Target` (both viewer-scoped actions) accepts a `{variable}` reference (e.g. `{target.id}`), an `@login`/login, a platform id or a user Guid; an unseen login fails with `NOT_FOUND` (typed failure, no throw).

**Template helpers** (added to `commands-pipelines.md` §6.3):
- `{{viewer.data.<key>}}` / `{{target.data.<key>}}` — the stored value (empty if unset). The resolver's pre-pass loads the referenced keys for the triggering viewer (+ target) via `LoadKeysAsync` before rendering (the `{{count.*}}` pattern).
- `{{viewer.messages}}`, `{{viewer.watchtime}}`, `{{viewer.firstseen}}`, `{{viewer.redemptions}}`, `{{viewer.songrequests}}` (+ `{{target.*}}` mirrors) — sourced from `ViewerProfileDto` (M.1) in the same pre-pass; a never-seen viewer renders honest zeros.

**Built-in commands** (`ChannelBuiltinCommand` G.2a; `StatsBuiltin` `BuiltinKey="stats"` and `ProfileBuiltin` `BuiltinKey="profile"`, `NomNomzBot.Infrastructure/ViewerData/Builtins/StatsBuiltins.cs`): `!stats` / `!profile` render the caller's (or `@target`'s) headline stats by composing `IViewerAnalyticsService.GetProfileAsync` + `GetStreakAsync` + `ICurrencyAccountService.GetBalanceAsync`; the points rank is computed in the command as the dense rank `1 + richer wallets` over `CurrencyAccounts` (not via `IEconomyLeaderboardService`). No argument = the caller (get-or-create); `@name` = a **known local viewer only** — stats about someone never seen here are all-zero by definition, so there is no remote lookup (a stranger gets the `NotSeen` reply). A viewer with neither profile nor wallet also gets `NotSeen`.

The reply is the `stats` response-slot group (`BuiltinResponseSlots.Stats`: `Profile`, `NotSeen`, `AccountUnresolved`), customizable per channel with the `{stats.*}` helpers:

| Helper | Value |
|---|---|
| `{stats.user}` | the viewer's display name |
| `{stats.messages}` | total chat messages |
| `{stats.watchtime}` | `{h}h {m}m` (or `{m}m` under an hour) |
| `{stats.points}` | wallet balance |
| `{stats.rank}` / `{stats.rankpart}` | dense rank, or `unranked`; `rankpart` = ` (rank #N)` or empty |
| `{stats.streak}` / `{stats.streakpart}` | current watch streak; `streakpart` = ` · N-stream streak` or empty |
| `{stats.firstseen}` | `yyyy-MM-dd`, or `unknown` |

The shipped line: `{stats.user} · {stats.messages} messages · {stats.watchtime} watched · {stats.points} points{stats.rankpart}{stats.streakpart} · first seen {stats.firstseen}`.

---

## 5. REST surface

Controller `ViewerDataController`, `[Route("api/v{version:apiVersion}/viewers/{viewerId}/data")]`; the tenant comes from `ICurrentTenantService`. `{viewerId}` is a string: an internal `User` Guid **or** a Twitch user id (resolved to the owning `User`); an unknown viewer returns 404. `[Authorize]`; Gate-2 keys. (The aggregate viewer profile is served by the existing `CommunityController.GetProfile` — not duplicated here.)

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<IReadOnlyDictionary<string,string>>` | management / Moderator · `viewerdata:read` |
| PUT | `/{key}` | `SetViewerDatumRequest { string Value }` | `StatusResponseDto<bool>` | management / Moderator · `viewerdata:write` |
| DELETE | `/{key}` | — | `StatusResponseDto<bool>` | management / Moderator · `viewerdata:write` |

Seeded in `ActionDefinitionSeeder` (S-MOD-PERMS): **`viewerdata:read`** and **`viewerdata:write`** both default to **Moderator** (`management`, `Low`) — browsing and hand-editing what pipelines wrote is reversible bot-internal tooling; a channel may raise either floor.

---

## 6. DI & testing

Registered in `NomNomzBot.Infrastructure/DependencyInjection.cs` (there is no `ViewerData/DependencyInjection.cs` and no `AddViewerData()`): `IViewerDataService`→`ViewerDataService` (Scoped, `I<X>Service` convention scan); `StatsBuiltin` + `ProfileBuiltin` (`IBuiltinCommand`, Scoped, explicit); `set_viewer_data` + `adjust_viewer_data` + `clear_viewer_data` auto-discovered into the action registry. No repository class: `ViewerDataService` works over `IApplicationDbContext` (`ViewerDatumConfiguration` maps the table). The template resolver's pre-pass calls `LoadKeysAsync` for `{{viewer.data.*}}`/`{{target.data.*}}` keys it parsed (alongside the existing `{{count.*}}` pre-load). `ErasureService` hard-deletes a subject's `ViewerDatum` rows.

**Tests (prove behavior):** `set_viewer_data` upserts the right `(BroadcasterId, ViewerUserId, Key)` row and a second set overwrites (no duplicate); `adjust_viewer_data` from unset starts at `Delta` and is atomic under concurrent increments (final value = sum); `{{viewer.data.deaths}}` resolves the triggering viewer's value and `{{target.data.deaths}}` the target's, both pre-seeded (resolver does no I/O); a per-viewer key-count (100) or value-length (500) over the fixed cap is rejected with no write; `clear_viewer_data` soft-deletes the key for every viewer in the channel and reports the row count; `!stats @user` composes messages + watch-time + points + rank + streak from the **existing** services (no new projection), `!stats` with no arg uses the caller, and `!stats @stranger` replies `NotSeen`; `!profile` behaves identically; deleting a key removes the row; erasing a subject removes all their `ViewerDatum` in every channel; targeting a non-existent viewer yields a typed failure, not a throw.

---

## 7. Decisions (resolved)

Writable per-viewer KV `ViewerDatum` (G.14), the per-viewer `NamedCounter` (D1); aggregate profile composed from existing read-models, never duplicated (D2); `{{viewer.data.*}}` + M.1 stat helpers, loaded by the resolver pre-pass (D3); `!stats` and `!profile` built-ins with `{stats.*}` reply helpers for legacy parity (D4); privacy-proportionate, erasure-scrubbed, bounded by fixed caps (D5); schema delta **G.14 `ViewerDatum`** + `set_viewer_data` / `adjust_viewer_data` / `clear_viewer_data` + helpers + built-ins in `commands-pipelines.md` (D6).
