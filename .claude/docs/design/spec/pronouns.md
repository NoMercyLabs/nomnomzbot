# Interface Specification — Pronoun Provider Integration

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth (verified live 2026-06-23):** alejo.io Twitch pronouns **v1 API** — base `https://api.pronouns.alejo.io/v1`, open/no-auth, CORS `*`; catalog `GET /pronouns` → object keyed by id, each `{ name, subject, object, singular }` (13 ids: `hehim`, `sheher`, `theythem`, `any`, `other`, …); per-user `GET /v1/users/{login}` → `{ channel_id, channel_login, pronoun_id, alt_pronoun_id|null }`, **`404 not_found`** when unset/unknown; `Cache-Control: max-age=3600` (per-user cache ~1h, catalog near-static). Display combine: `alt=null` → (`singular`? `subject` : `subject/object`); `alt` set → `subject(primary)/subject(alt)` (e.g. `sheher`+`theythem` → "She/They"). Corpus: locked schema R.1 `Pronouns` (curated grammar lookup) + `Users.PronounId`/`PronounManualOverride` (A.1); `commands-pipelines.md` (§6.3.1 pronoun-helper grammar engine — ported from the legacy bot's `TemplateHelper`); `gdpr-crypto.md` (special-category consent `pronoun_special_category`, erasure null-out); `platform-conventions.md` (`ICacheService`, `IRunOnceGuard`, `IDeploymentProfileService`); `scaling-qos.md` (`IRateLimiter`, `HttpEgressAllowlist`). Legacy parity: `C:\Projects\StoneyEagle\nomercy-bot` `PronounService` (`LoadPronouns` + `GetUserPronoun`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; secrets/PII per `gdpr-crypto.md`.

> **Why.** Pronouns are already first-party where it's hard: the `Pronouns` lookup (R.1, richer grammar than any provider — possessive determiner/pronoun, gendered-term, smart-alternation), `Users.PronounId`, and the grammar `TemplateHelper` (`{{user.subject}}`, `{{user.possessive}}`, …) are all specced. The **only** missing piece is the thin provider that **auto-populates** a viewer's `PronounId` from alejo.io (the legacy `PronounService`). This spec adds that — as a pluggable `IPronounProvider` (alejo shipped; another provider is a new impl bound at the DI registration), with v1's primary+alt support and a `{{user.pronouns}}` display badge. R.1 stays the curated source of grammar; the provider only fills in *which* pronoun each viewer chose.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Pluggable `IPronounProvider`; ship `AlejoPronounProvider`.** alejo.io v1, open/no-auth, called through the named `AlejoHttpClient` (Polly resilience handler: 10 s per-attempt timeout + retries). One provider is bound (`IPronounResolutionService` takes a single `IPronounProvider`); a second provider is a new impl swapped in at its DI registration, no engine edit. |
| D2 | **R.1 stays the source of grammar; `PronounSeeder` keeps it current.** At boot (`ISeeder`, Order 10) `PronounSeeder` fetches the alejo catalog through `IAlejoPronounClient.FetchAsync` and upserts R.1 by natural key `Name` (the slash form, e.g. `they/them`) — a re-run updates changed rows and adds new ones, never duplicates, never deletes. A failed or empty fetch upserts a bundled fallback so the table is never empty; the combination pronouns (`he/they`, `she/they`, `he/she`) always come from the bundle because alejo does not list them (their `Key` is null). The alejo id (`sheher`, `theythem`, …) **is** the R.1 `Key`. |
| D3 | **Per-viewer resolve is lazy, cached, and respects manual override.** On a viewer's chat activity (the existing chat path, debounced), if there's no fresh cached pronoun **and** `Users.PronounManualOverride = false`, the provider resolves `GET /users/{login}` → maps `pronoun_id`→`Users.PronounId`, `alt_pronoun_id`→`Users.AltPronounId`. Cooldown-gated per viewer through `IMemoryCache` (`pronoun:resolve:{userId}`, **24 h**): the key is set *before* the lookup, so a concurrent chat burst races into one call and a 404/failed lookup is cooldown-cached exactly like a hit (unset chatters are not re-queried for 24 h). 404 ⇒ pronoun stays null ⇒ the neutral they/them grammar fallback (see §4) — no behavior change. A provider key with no R.1 row is logged at debug and skipped. There is no per-channel rate limiter; the cooldown is the throttle. |
| D4 | **Primary drives grammar; primary+alt drive the display badge.** Sentence rendering (`{{user.subject}}` etc.) uses the **primary** `PronounId` exactly as today (you cannot grammatically alternate two pronoun sets mid-sentence — the legacy bot stored one, this preserves that). The helper **`{{user.pronouns}}`** renders the badge through `UserPronounDisplay.Format(primary, alt)`: `subject(primary)/subject(alt)` when alt is set, else the primary pronoun's own `Name`; see §4. |
| D5 | **Self-service + special-category.** A user may set their own pronoun in our dashboard (sets `PronounId`/`AltPronounId`; `ManualOverride=true` suppresses provider overwrite). `PronounId` **and** `AltPronounId` are `[PII-S9]` (GDPR Art. 9) — both are nulled on erasure (`ErasureService`). `ConsentService` carries the `pronoun_special_category` purpose, but `PronounSelfService` and `PronounResolutionService` do not consult it: a self-service write is the user's own explicit act, and this spec changes no consent model. |
| D6 | **Schema delta: `Users.AltPronounId` (A.1) only** — `int?` FK→`Pronouns.Id` (`Pronouns.Id` is `int`, as is `Users.PronounId`), nullable, `[PII-S9]`. No new table (per-viewer value lives on `Users`; the resolve cooldown lives in `IMemoryCache`). One new template helper in `commands-pipelines.md`. |

---

## 1. Entities

No new table. One column added to **`Users` (A.1)**:

| Table | Schema ref | Change | Field (type) |
|---|---|---|---|
| **`Users`** | **A.1 (column add)** | add | `AltPronounId int?` — FK→`Pronouns.Id` (`int`), Null, Index. **[PII-S9]** secondary pronoun (alejo `alt_pronoun_id`); drives the display badge's second half only. Nulled on erasure (with `PronounId`). |

`Pronouns` (R.1) and `Users.PronounId`/`PronounManualOverride` are **referenced, not owned** here (existing). The per-viewer resolve cooldown is an `IMemoryCache` entry `pronoun:resolve:{userId}` (24 h); no DB timestamp.

---

## 2. Domain events

None. Pronoun resolution mutates `Users` in place (idempotent, cache-gated); it is not an activity worth an event. Self-service edits are ordinary CRUD.

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Identity.Services` (interfaces, plus the `PronounCatalogEntry` / `ResolvedPronounRef` records); DTOs `UserPronounDto` / `SetPronounRequest` in `NomNomzBot.Application.Identity.Dtos` (`UserDtos.cs`). Impls: `AlejoPronounProvider` + `AlejoPronounClient` in `NomNomzBot.Infrastructure/Identity/Providers/`, `PronounResolutionService` + `PronounSelfService` in `NomNomzBot.Infrastructure/Identity/Services/`. The service methods return plain values / nullable DTOs, not `Result<T>`.

```csharp
// Thin alejo v1 HTTP client (best-effort: never throws, null on any transport/status/parse failure).
public interface IAlejoPronounClient
{
    Task<IReadOnlyList<PronounRecord>?> FetchAsync(CancellationToken ct = default);                 // GET /v1/pronouns -> id-keyed catalog
    Task<AlejoUserPronoun?> LookupUserAsync(string twitchLogin, CancellationToken ct = default);   // GET /v1/users/{login}; null on 404/failure
}

// Pluggable provider over the client (alejo shipped).
public interface IPronounProvider
{
    string Name { get; }   // "alejo"
    Task<IReadOnlyDictionary<string, PronounCatalogEntry>?> GetCatalogAsync(CancellationToken ct = default);  // null = provider unreachable
    Task<ResolvedPronounRef?> LookupAsync(string twitchLogin, CancellationToken ct = default);                 // null = 404 / unset / failure
}

// Resolve (cooldown-gated) and persist Users.PronounId/AltPronounId for a viewer; idempotent; skips manual-override.
public interface IPronounResolutionService
{
    Task ResolveAndApplyAsync(Guid userId, string twitchLogin, CancellationToken ct = default);
}

// Self-service over the caller's own User identity (global, not tenant-scoped). null = no such user.
public interface IPronounSelfService
{
    Task<UserPronounDto?> GetAsync(Guid userId, CancellationToken ct = default);
    Task<UserPronounDto?> SetAsync(Guid userId, SetPronounRequest request, CancellationToken ct = default);
}

public sealed record PronounRecord(string Subject, string Object, bool Singular, string Key);
public sealed record AlejoUserPronoun(string PronounId, string? AltPronounId);
public sealed record PronounCatalogEntry(string Key, string Subject, string Object, bool Singular, string Name);
public sealed record ResolvedPronounRef(string PronounKey, string? AltPronounKey);
public sealed record UserPronounDto(int? PronounId, string? PronounName, string? PronounBadge, int? AltPronounId, string? AltPronounName, bool ManualOverride);
public sealed record SetPronounRequest   // init-only properties
{
    public int? PronounId { get; init; }       // null = leave unchanged, 0 = clear
    public int? AltPronounId { get; init; }    // null = leave unchanged, 0 = clear
    public bool? ManualOverride { get; init; } // true pins the choice against provider overwrite
}
```

`AlejoPronounProvider` derives each catalog entry's `Name` from the alejo `subject`/`object` (`subject` alone when they match, else `subject/object`); the resolved `pronoun_id`/`alt_pronoun_id` map to R.1 `Key`s inside `PronounResolutionService`. `SetAsync` clears back to provider-managed by sending `0` ids with `ManualOverride=false`.

**Chat path — `PronounHydrationHandler`** (`NomNomzBot.Infrastructure/Chat/EventHandlers/`, `IEventHandler<ChatMessageReceivedEvent>`). On each chat message it returns early for an empty broadcaster/user id and for **any non-Twitch provider** (alejo is keyed by Twitch logins; other platforms have no pronoun source). Otherwise it `GetOrCreateAsync`s the chatter's `User` row, then calls `ResolveAndApplyAsync(viewerUserId, login)`. Failures are logged at debug and never break chat ingest.

**Profile backfill — `UserProfileHydrationService`** (`NomNomzBot.Infrastructure/Identity/Jobs/`, a `BackgroundService`). The bare `User` row a chatter gets on first message carries only id/login/display name; this worker (every 2 min, gated on `IPlatformBotReadinessGate`) re-reads never-hydrated or 12 h-stale profiles from Helix Get Users (100 ids per call, at most 300 users per tick) and writes avatar, rename, broadcaster type, description and account age. It does not touch pronouns; it fills the `User` rows the pronoun path creates so chat and the community page render real avatars.

**Pipeline action — `set_pronoun`** (`SetPronounAction`, `ICommandAction`, `NomNomzBot.Infrastructure/Identity/PipelineActions/`; category `pipeline.category.identity`): fields `username` (Twitch user; `@mention` accepted) and `pronoun` (an R.1 `Name` such as `they/them`, case-insensitive, or `clear`/`reset`). It resolves the target through Helix + `IUserService.GetOrCreateAsync`, then `IPronounSelfService.SetAsync` with `ManualOverride=true` (or a `0`/`0`/`false` clear). An unknown pronoun fails with the list of available names, an unknown user fails with a not-found message. Its usage string is `!setpronoun <username> <pronoun|clear>`.

---

## 4. Template helper

Registered in `TemplateHelperRegistry`, resolved by `TemplateResolver`, in the `commands-pipelines.md` §6.3.1 pronoun-helper block:

- **`{{user.pronouns}}`** → the display badge from `UserPronounDisplay.Format(user.Pronoun, user.AltPronoun)` (one function shared with the dashboard hub enrichment, so both surfaces render identically): with an alt set, `subject(primary)/subject(alt)` in lowercase (`she/they` for `sheher` + `theythem`); with no alt, the primary pronoun's own `Name` (`he/him`, `they/them`); when the viewer has **no** primary pronoun, the variable is left **unset** (no neutral badge is invented). There is no `{{target.pronouns}}` helper.
- **Grammar helpers** — `{{subject}}`, `{{object}}`, `{{possessive}}`, `{{presentTense}}`, `{{pastTense}}`, `{{genderedTerm}}` (bare: mirror the `@mention` target when one is present, else the triggering user), plus explicit `{{user.*}}` and `{{target.*}}` forms — key off the **primary** `PronounId` only (D4). A missing user/target or a viewer with no pronoun gets the universal fallback `they` / `them` / `their` / `are` / `were` / `person`, so a placeholder never renders raw.

---

## 5. REST surface

Controller `PronounsController`, `[Route("api/v{version:apiVersion}/pronouns")]`. The self-service endpoints act on the **caller's own global `Users` row** (not tenant-scoped). `GET /system/pronouns` (`SystemController`, anonymous) returns the same catalogue as `{Id, Name, Subject, Object}`.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/catalog` | — | `StatusResponseDto<IEnumerable<PronounCatalogDto>>` (`Id`, `Name`, `Subject`, `Object`, `Key`), ordered by `Name` | `[AllowAnonymous]` (the participant Me screen shows pronouns before login) |
| GET | `/me` | — | `StatusResponseDto<UserPronounDto>`; `404` when the user row is missing | `[Authorize]` (any signed-in user) |
| PUT | `/me` | `SetPronounRequest` (`PronounId?`, `AltPronounId?`, `ManualOverride?`) | `StatusResponseDto<UserPronounDto>`; `404` when the user row is missing | community / Everyone · `pronouns:self:write` |

Seeded in `ActionDefinitionSeeder`: **`pronouns:self:write`** (community plane, Everyone, `Low` — a user editing their own pronoun/override). It is the only pronoun action key; the catalog and `GET /me` need no key.

---

## 6. DI & testing

Registered inline in `NomNomzBot.Infrastructure/DependencyInjection.cs` (there is no `Pronouns/DependencyInjection.cs` and no `AddPronouns()`): `IAlejoPronounClient`→`AlejoPronounClient` (Singleton) with the named `AlejoHttpClient` (30 s ceiling + `AddAlejoResilienceHandler()`); `IPronounProvider`→`AlejoPronounProvider` (Singleton); `IPronounResolutionService`→`PronounResolutionService` (Scoped); `IPronounSelfService`→`PronounSelfService` (Scoped). `PronounSeeder` is an `ISeeder`; `PronounHydrationHandler` (`IEventHandler<ChatMessageReceivedEvent>`), `UserProfileHydrationService` (hosted worker) and `SetPronounAction` (`ICommandAction`) are auto-discovered by their registries. The chat path invokes `ResolveAndApplyAsync` (cooldown-gated).

**Tests (prove behavior):** a `200` lookup for a viewer maps `pronoun_id`/`alt_pronoun_id` to the right R.1 `Key`s and persists `Users.PronounId`/`AltPronounId`, and a second chat within the 24 h cooldown performs **no** HTTP call; a `404` leaves both null and the resolver still consumed the cooldown, so the grammar helpers render the they/them fallback; a viewer with `PronounManualOverride=true` is **never** overwritten by the provider; a provider key with no R.1 row is skipped with no write; `{{user.pronouns}}` renders `she/they` for `sheher`+`theythem`, `he/him` for `hehim` alone, and stays unset when no primary pronoun exists, while `{{user.subject}}` still resolves from the **primary** only; `PronounSeeder` upserts by `Name` without duplicating or deleting and falls back to the bundle when the fetch fails; `SetAsync` sets the pronoun and clears back to provider-managed on `0`/`0`/`false`; `PronounHydrationHandler` does nothing for a non-Twitch message; `set_pronoun` writes the target's pronoun with `ManualOverride=true` and rejects an unknown pronoun name.

---

## 7. Decisions (resolved)

Pluggable `IPronounProvider`, alejo v1 shipped (D1); R.1 seeded and kept current from the alejo catalog by `PronounSeeder` (D2); lazy per-viewer resolve with a 24 h `IMemoryCache` cooldown + manual override + 404-as-unset, Twitch chat only (D3); primary drives grammar, primary+alt drive the `{{user.pronouns}}` badge (D4); self-service set, both pronoun columns nulled on erasure (D5); schema delta `Users.AltPronounId` (`int?`) only + one template helper, no new table (D6); `set_pronoun` action for moderator-set pronouns.
