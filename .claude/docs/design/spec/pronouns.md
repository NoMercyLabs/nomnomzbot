# Interface Specification — Pronoun Provider Integration

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth (verified live 2026-06-23):** alejo.io Twitch pronouns **v1 API** — base `https://api.pronouns.alejo.io/v1`, open/no-auth, CORS `*`; catalog `GET /pronouns` → object keyed by id, each `{ name, subject, object, singular }` (13 ids: `hehim`, `sheher`, `theythem`, `any`, `other`, …); per-user `GET /v1/users/{login}` → `{ channel_id, channel_login, pronoun_id, alt_pronoun_id|null }`, **`404 not_found`** when unset/unknown; `Cache-Control: max-age=3600` (per-user cache ~1h, catalog near-static). Display combine: `alt=null` → (`singular`? `subject` : `subject/object`); `alt` set → `subject(primary)/subject(alt)` (e.g. `sheher`+`theythem` → "She/They"). Corpus: locked schema R.1 `Pronouns` (curated grammar lookup) + `Users.PronounId`/`PronounManualOverride` (A.1); `commands-pipelines.md` (§6.3.1 pronoun-helper grammar engine — ported from the legacy bot's `TemplateHelper`); `gdpr-crypto.md` (special-category consent `pronoun_special_category`, erasure null-out); `platform-conventions.md` (`ICacheService`, `IRunOnceGuard`, `IDeploymentProfileService`); `scaling-qos.md` (`IRateLimiter`, `HttpEgressAllowlist`). Legacy parity: `C:\Projects\StoneyEagle\nomercy-bot` `PronounService` (`LoadPronouns` + `GetUserPronoun`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; secrets/PII per `gdpr-crypto.md`.

> **Why.** Pronouns are already first-party where it's hard: the `Pronouns` lookup (R.1, richer grammar than any provider — possessive determiner/pronoun, gendered-term, smart-alternation), `Users.PronounId`, and the grammar `TemplateHelper` (`{{user.subject}}`, `{{user.possessive}}`, …) are all specced. The **only** missing piece is the thin provider that **auto-populates** a viewer's `PronounId` from alejo.io (the legacy `PronounService`). This spec adds that — as a pluggable `IPronounProvider` (alejo shipped, seam for PronounDB later), with v1's primary+alt support and a `{{user.pronouns}}` display badge. R.1 stays the curated source of grammar; the provider only fills in *which* pronoun each viewer chose.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Pluggable `IPronounProvider`; ship `AlejoPronounProvider`.** alejo.io v1, open/no-auth, called through the tenant egress path. The provider is auto-discovered — adding PronounDB later = drop an impl, no engine edit (the project's extensibility line). |
| D2 | **R.1 stays the curated source of grammar; the catalog only validates coverage.** On startup (and daily, `IRunOnceGuard`-guarded) the provider's `GetCatalogAsync` is reconciled against R.1 by `Key`; a provider id with **no** R.1 grammar row is logged (so the seed can be extended) — the catalog **never overwrites** R.1's hand-curated grammar. The alejo id (`sheher`, `theythem`, …) **is** the R.1 `Key`. |
| D3 | **Per-viewer resolve is lazy, cached, and respects manual override.** On a viewer's chat activity (the existing chat path, debounced), if there's no fresh cached pronoun **and** `Users.PronounManualOverride = false`, the provider resolves `GET /users/{login}` → maps `pronoun_id`→`Users.PronounId`, `alt_pronoun_id`→`Users.AltPronounId`. Honors `Cache-Control` via `ICacheService`: positive ~1h, **negative (404) cached briefly** so unset chatters aren't re-queried. 404 ⇒ pronoun stays null ⇒ the TemplateHelper's neutral smart-alternation default (already specced) — no behavior change. Rate-limited (`IRateLimiter`, per-channel provider bucket). |
| D4 | **Primary drives grammar; primary+alt drive the display badge.** Sentence rendering (`{{user.subject}}` etc.) uses the **primary** `PronounId` exactly as today (you cannot grammatically alternate two pronoun sets mid-sentence — the legacy bot stored one, this preserves that). A new helper **`{{user.pronouns}}`** renders the badge per alejo's combine rule (`subject(primary)/subject(alt)`, or `subject`/`subject/object` when alt is null). `{{target.pronouns}}` likewise. |
| D5 | **Self-service + special-category.** A user may set their own pronoun in our dashboard (sets `PronounId`/`AltPronounId` + `PronounManualOverride=true`, which suppresses provider overwrite). `PronounId` **and** the new `AltPronounId` are `[PII-S9]` (GDPR Art. 9) — explicit-consent gated and nulled on erasure exactly as `PronounId` already is (`gdpr-crypto.md`); this spec changes no consent model, it only fills the consent-governed field. |
| D6 | **Schema delta: `Users.AltPronounId` (A.1) only** — FK→`Pronouns`, nullable, `[PII-S9]`. No new table (per-viewer value lives on `Users`; lookups cache in `ICacheService`). One new template helper in `commands-pipelines.md`. |

---

## 1. Entities

No new table. One column added to **`Users` (A.1)**:

| Table | Schema ref | Change | Field (type) |
|---|---|---|---|
| **`Users`** | **A.1 (column add)** | add | `AltPronounId Guid?` — FK→`Pronouns.Id`, Null, Index. **[PII-S9]** secondary pronoun (alejo `alt_pronoun_id`); drives the display badge's second half only. Nulled on erasure (with `PronounId`). |

`Pronouns` (R.1) and `Users.PronounId`/`PronounManualOverride` are **referenced, not owned** here (existing). Per-viewer lookup results cache in `ICacheService` key `pronoun:{providerKey}:{login}` (positive ~1h, negative short); no DB timestamp.

---

## 2. Domain events

None. Pronoun resolution mutates `Users` in place (idempotent, cache-gated); it is not an activity worth an event. Self-service edits are ordinary CRUD.

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Pronouns`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Pronouns/`.

```csharp
// Pluggable provider (alejo shipped; seam for PronounDB etc.). Auto-discovered.
public interface IPronounProvider
{
    string ProviderKey { get; }   // "alejo"
    Task<Result<IReadOnlyDictionary<string, PronounCatalogEntry>>> GetCatalogAsync(CancellationToken ct = default);
    Task<Result<ResolvedPronounRef?>> LookupAsync(string twitchLogin, CancellationToken ct = default); // null = 404 / unset
}

// Resolve (cache-first) and persist Users.PronounId/AltPronounId for a viewer; idempotent; skips manual-override.
public interface IPronounResolutionService
{
    Task<Result> ResolveAndApplyAsync(Guid viewerUserId, string twitchLogin, CancellationToken ct = default);

    // The catalog↔R.1 coverage check (startup + daily, IRunOnceGuard); logs provider ids missing an R.1 grammar row.
    Task<Result<PronounCatalogReport>> ValidateCatalogCoverageAsync(CancellationToken ct = default);
}

// Self-service over the caller's own User identity (global, not tenant-scoped).
public interface IPronounSelfService
{
    Task<Result<IReadOnlyList<PronounDto>>> GetCatalogAsync(CancellationToken ct = default);             // R.1 entries
    Task<Result<UserPronounDto>> GetMineAsync(Guid userId, CancellationToken ct = default);
    Task<Result<UserPronounDto>> SetMineAsync(Guid userId, SetPronounRequest request, CancellationToken ct = default); // sets PronounManualOverride
}

public sealed record PronounCatalogEntry(string Name, string Subject, string Object, bool Singular);
public sealed record ResolvedPronounRef(string PronounId, string? AltPronounId);
public sealed record PronounDto(string Key, string Subject, string Object, bool Singular, string DisplayBadge);
public sealed record UserPronounDto(string? PronounKey, string? AltPronounKey, string DisplayBadge, bool ManualOverride);
public sealed record SetPronounRequest(string? PronounKey, string? AltPronounKey); // null clears (back to provider-managed)
public sealed record PronounCatalogReport(int ProviderCount, IReadOnlyList<string> MissingFromSeed);
```

`AlejoPronounProvider` maps alejo's `subject`/`object`/`singular` only for the coverage check; the resolved `pronoun_id`/`alt_pronoun_id` map to R.1 `Key`s. The chat path calls `ResolveAndApplyAsync` fire-and-forget (cache-gated, so a fully-resolved channel adds ~one cache read per chatter). Egress to alejo goes through the tenant `HttpEgressAllowlist` (alejo's host seeded by default).

---

## 4. Template helper

Add to the `commands-pipelines.md` §6.3.1 pronoun-helper block:

- **`{{user.pronouns}}`** → the display badge from `PronounId` (+ `AltPronounId`): `subject(primary)/subject(alt)`, or `subject`/`subject/object` when alt is null, or the neutral default ("they/them") when unset. `{{target.pronouns}}` mirrors it for the command target.

Grammar helpers (`{{user.subject}}`, `{{user.object}}`, `{{user.possessive}}`, smart-alternation) are **unchanged** — they key off the primary `PronounId` exactly as today (D4).

---

## 5. REST surface

Controller `PronounsController`, `[Route("api/v{version:apiVersion}/pronouns")]`. `[Authorize]`. The self-service endpoints act on the **caller's own global `Users` row** (not tenant-scoped).

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/catalog` | — | `StatusResponseDto<IReadOnlyList<PronounDto>>` | community / Everyone · `pronouns:read` |
| GET | `/me` | — | `StatusResponseDto<UserPronounDto>` | community / Everyone · `pronouns:read` |
| PUT | `/me` | `SetPronounRequest` | `StatusResponseDto<UserPronounDto>` | community / Everyone · `pronouns:self:write` |

Seed in `roles-permissions.md`: **`pronouns:read`** (`community`, Everyone 0, `Low`) and **`pronouns:self:write`** (`community`, Everyone 0, `Low` — a user editing their own pronoun/override; the special-category consent gate is enforced in the service, not the role floor).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Pronouns/DependencyInjection.cs` (`AddPronouns()`): `IPronounProvider`→`AlejoPronounProvider` (Scoped, auto-discovered — multiple providers allowed); `IPronounResolutionService`→`PronounResolutionService` (Scoped); `IPronounSelfService`→`PronounSelfService` (Scoped); a startup/daily `PronounCatalogSyncHostedService` (`IRunOnceGuard`-guarded) calling `ValidateCatalogCoverageAsync`. The chat-activity path invokes `ResolveAndApplyAsync` (cache-gated). Alejo's host seeded into the default `HttpEgressAllowlist`.

**Tests (prove behavior):** a `200` lookup for a viewer maps `pronoun_id`/`alt_pronoun_id` to the right R.1 `Key`s and persists `Users.PronounId`/`AltPronounId`, and a second chat within the cache window performs **no** HTTP call (cache hit); a `404` leaves both null, is negative-cached, and the TemplateHelper renders the neutral default; a viewer with `PronounManualOverride=true` is **never** overwritten by the provider; `{{user.pronouns}}` renders "She/They" for `sheher`+`theythem`, "He/Him" for `hehim` alone, and "they/them" when unset, while `{{user.subject}}` still resolves from the **primary** only; `ValidateCatalogCoverageAsync` reports any provider id absent from the R.1 seed without mutating R.1; `SetMineAsync` sets the pronoun + `PronounManualOverride=true` and clears back to provider-managed when keys are null; resolution egress is blocked if alejo's host is removed from the allowlist; rate-limit denial skips the lookup and leaves state unchanged.

---

## 7. Decisions (resolved)

Pluggable `IPronounProvider`, alejo v1 shipped (D1); R.1 stays curated, catalog only validates coverage (D2); lazy cached per-viewer resolve honoring `Cache-Control` + manual override + 404-as-unset (D3); primary drives grammar, primary+alt drive the `{{user.pronouns}}` badge (D4); self-service set + special-category consent unchanged (D5); schema delta `Users.AltPronounId` only + one template helper, no new table (D6).
