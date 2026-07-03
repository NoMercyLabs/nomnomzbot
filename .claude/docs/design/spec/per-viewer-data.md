# Interface Specification — Per-Viewer Data Store

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** legacy parity — `C:\Projects\StoneyEagle\nomercy-bot` (`Record` per-user activity/JSON store + the `Stats` command that aggregates per-viewer totals). Corpus (the read-models this spec **composes, never duplicates**): `analytics.md` (`IViewerAnalyticsService.GetProfileAsync`→`ViewerProfileDto` over M.1 `ViewerProfiles`: `TotalWatchSeconds/TotalMessages/TotalCommandsUsed/TotalRedemptions/TotalSongRequests/First+LastSeenAt`; `GetStreakAsync` over M.3); `community-dashboard.md` (`ICommunityService.GetViewerAsync`→`ViewerDetailDto` = profile + standing + role + recent `EventJournal` activity); `economy.md` (`ICurrencyAccountService.GetBalanceAsync`/`GetOrCreateAccountAsync`→`CurrencyAccountDto`); `event-store.md` (`IEventJournal.QueryAsync` by `ActorUserId`); `commands-pipelines.md` (`NamedCounter` G.4 + `set_counter`/`adjust_counter` + `{{count.<name>}}` dispatch-seeded resolution, §6.3 helpers, §3.13 `ICommandAction`, built-in commands `ChannelBuiltinCommand` G.2a); `gdpr-crypto.md` (per-subject erasure). Locked schema `2026-06-16-database-schema.md` (Domain G — beside `NamedCounter`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json.

> **Why.** The legacy bot "tracks everything a viewer has done" and exposes a `Stats` command. In the new architecture, **that aggregate already exists** — analytics projections (`ViewerProfiles`, `WatchStreaks`, engagement dailies), the economy wallet, and the event journal already record messages, watch-time, follows/subs/bits/redemptions/song-requests/raids, and `ICommunityService.GetViewerAsync` already composes them into one `ViewerDetailDto`. The **one missing piece** is a **writable per-viewer key/value store** — the analog of the legacy `Record` (arbitrary per-viewer custom data a pipeline sets and reads: a per-viewer death counter, "favorite game", a quest flag). `NamedCounter` (G.4) is **per-channel**; there is no **per-viewer** mutable store. This spec adds it (a per-viewer sibling of `NamedCounter`), surfaces it + the existing stats in the template/command layer, and ships a `!stats` built-in for parity — without re-deriving a single projection.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Writable per-viewer KV: `ViewerDatum` (G.14)** — the per-viewer analog of `NamedCounter` (G.4): unique `(BroadcasterId, ViewerUserId, Key)`, a string `Value`. Two actions: `set_viewer_data` (string set/upsert) and `adjust_viewer_data` (atomic numeric increment — parse current as `long`, add `Delta`, store). One table covers per-viewer flags/strings **and** per-viewer counters. |
| D2 | **Compose, never duplicate, the aggregate profile.** "Everything a viewer has done" is **already** `ICommunityService.GetViewerAsync`→`ViewerDetailDto` (profile + standing + role + recent journal activity) and `IViewerAnalyticsService.GetProfileAsync`. This spec adds **no** new profile projection or read endpoint — it references those, optionally surfacing the new `ViewerDatum` map alongside them. |
| D3 | **Surface in the pipeline layer.** Template `{{viewer.data.<key>}}` / `{{target.data.<key>}}` (the `{{count.*}}` pattern — the dispatcher **pre-seeds** referenced keys into the run bag, so the resolver stays I/O-free). Plus per-viewer **stat** helpers sourced from M.1 that don't yet exist: `{{viewer.messages}}`, `{{viewer.watchtime}}`, `{{viewer.firstseen}}`, `{{viewer.redemptions}}`, `{{viewer.songrequests}}` (and `{{target.*}}` mirrors). Existing helpers (`{{target.balance}}`, `{{target.followage}}`, economy `{{economy.rank:user}}`) are **not** redefined. |
| D4 | **`!stats` (alias `!profile`) built-in command** — surfaces a viewer's headline stats (messages, watch-time, points + rank, streak, first-seen) for the caller or a `@target`, composing the existing read services. Parity with the legacy `Stats` command; registered as a `ChannelBuiltinCommand` (G.2a). |
| D5 | **Privacy-proportionate.** `ViewerDatum` is ordinary per-viewer personal data (gameplay/custom flags — **not** special-category). On erasure, a subject's `ViewerDatum` rows are deleted (the existing per-subject scrub extends to G.14). Writes are tenant-scoped + viewer-scoped; bounded (per-viewer key count + value length tier-scaled). |
| D6 | **Schema delta: G.14 `ViewerDatum`** only. Two pipeline actions + the template helpers + the `!stats` built-in are added to `commands-pipelines.md`. |

---

## 1. Entities

Domain G. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`ViewerDatum`** | **G.14 (NEW)** `[soft-delete]` `ITenantScoped` | tenant+viewer | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `ViewerUserId Guid` FK→`Users.Id` Index; `Key string(50)` (slug); `Value text` (string; numeric ops parse/format as `long`); `UpdatedAt DateTime`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, ViewerUserId, Key)`. |

No new profile/aggregate table (D2). Reads of "everything a viewer has done" use the existing M.* projections + economy + event journal via their existing services.

---

## 2. Domain events

None. Per-viewer data writes are pipeline side effects (already journaled via the pipeline-execution record). Reads are read-only.

---

## 3. Service interface

Namespace `NomNomzBot.Application.ViewerData`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/ViewerData/`.

```csharp
public interface IViewerDataService
{
    Task<Result<string?>> GetAsync(Guid broadcasterId, Guid viewerUserId, string key, CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<string, string>>> ListForViewerAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    Task<Result> SetAsync(Guid broadcasterId, Guid viewerUserId, string key, string value, CancellationToken ct = default);
    Task<Result<long>> AdjustAsync(Guid broadcasterId, Guid viewerUserId, string key, long delta, CancellationToken ct = default); // atomic upsert; returns new value
    Task<Result> DeleteAsync(Guid broadcasterId, Guid viewerUserId, string key, CancellationToken ct = default);

    // Bulk pre-load for the dispatcher: the keys a pipeline references, for the triggering viewer (+ target). One round-trip.
    Task<Result<IReadOnlyDictionary<string, string>>> LoadKeysAsync(Guid broadcasterId, Guid viewerUserId, IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
```

The **aggregate profile read is not redefined here** — callers use `ICommunityService.GetViewerAsync` / `IViewerAnalyticsService.GetProfileAsync` / `ICurrencyAccountService` (the `!stats` built-in and the dashboard viewer card compose those). `ListForViewerAsync` lets the dashboard viewer card show the custom-data map beside them.

---

## 4. Pipeline actions, template helpers, built-in

**Actions** (`ICommandAction`, §3.13; in `NomNomzBot.Infrastructure/ViewerData/PipelineActions/`):

| Action `Type` | Parameters | Behavior |
|---|---|---|
| **`set_viewer_data`** | `{ string Key, string Value, string? Target }` | upsert `ViewerDatum` for the target viewer (default = triggering viewer; `Target` resolves a `@name`/id). |
| **`adjust_viewer_data`** | `{ string Key, long Delta, string? Target }` | atomic numeric increment; returns the new value as `Output` and into `Variables[Key]`. |

**Template helpers** (added to `commands-pipelines.md` §6.3):
- `{{viewer.data.<key>}}` / `{{target.data.<key>}}` — the stored value (empty if unset). **Dispatcher pre-seeds** referenced keys for the triggering viewer (+ target) via `LoadKeysAsync` — resolver stays I/O-free (the `{{count.*}}` rule).
- `{{viewer.messages}}`, `{{viewer.watchtime}}`, `{{viewer.firstseen}}`, `{{viewer.redemptions}}`, `{{viewer.songrequests}}` (+ `{{target.*}}` mirrors) — sourced from `ViewerProfileDto` (M.1), pre-seeded at dispatch.

**Built-in command** (`ChannelBuiltinCommand` G.2a): **`!stats`** (alias `!profile`) — renders the caller's (or `@target`'s) headline stats (messages, watch-time, points + rank, streak, first-seen) by composing `IViewerAnalyticsService.GetProfileAsync` + `ICurrencyAccountService` + `IEconomyLeaderboardService`. Output text is template-customizable per the built-in-command convention.

---

## 5. REST surface

Controller `ViewerDataController`, `[Route("api/v{version:apiVersion}/viewers/{viewerId:guid}/data")]`. `[Authorize]`; Gate-2 keys. (The aggregate viewer profile is served by the existing `CommunityController.GetViewerAsync` — not duplicated here.)

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<IReadOnlyDictionary<string,string>>` | management / Moderator · `viewerdata:read` |
| PUT | `/{key}` | `{ string Value }` | `StatusResponseDto<bool>` | management / Editor · `viewerdata:write` |
| DELETE | `/{key}` | — | `StatusResponseDto<bool>` | management / Editor · `viewerdata:write` |

Seed in `roles-permissions.md`: **`viewerdata:read`** (`management`, Moderator 10, `Low`), **`viewerdata:write`** (`management`, Editor 30, `Low`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/ViewerData/DependencyInjection.cs` (`AddViewerData()`): `IViewerDataService`→`ViewerDataService` (Scoped); `ViewerDatumRepository` (Scoped); `set_viewer_data` + `adjust_viewer_data` actions + the `!stats` built-in auto-discovered into their registries. The pipeline dispatcher calls `LoadKeysAsync` for `{{viewer.data.*}}`/`{{target.data.*}}` keys it parsed (alongside the existing `{{count.*}}` pre-load). `gdpr-crypto.md` erasure deletes a subject's `ViewerDatum` rows.

**Tests (prove behavior):** `set_viewer_data` upserts the right `(BroadcasterId, ViewerUserId, Key)` row and a second set overwrites (no duplicate); `adjust_viewer_data` from unset starts at `Delta` and is atomic under concurrent increments (final value = sum); `{{viewer.data.deaths}}` resolves the triggering viewer's value and `{{target.data.deaths}}` the target's, both pre-seeded (resolver does no I/O); a per-viewer key-count or value-length over the tier cap is rejected with no write; `!stats @user` composes messages + watch-time + points + rank + streak from the **existing** services (no new projection) and `!stats` with no arg uses the caller; deleting a key removes the row; erasing a subject removes all their `ViewerDatum`; targeting a non-existent viewer yields a typed failure, not a throw.

---

## 7. Decisions (resolved)

Writable per-viewer KV `ViewerDatum` (G.14), the per-viewer `NamedCounter` (D1); aggregate profile composed from existing read-models, never duplicated (D2); `{{viewer.data.*}}` + M.1 stat helpers, dispatch-seeded (D3); `!stats`/`!profile` built-in for legacy parity (D4); privacy-proportionate, erasure-scrubbed, bounded (D5); schema delta **G.14 `ViewerDatum`** + actions/helpers/built-in in `commands-pipelines.md` (D6).
