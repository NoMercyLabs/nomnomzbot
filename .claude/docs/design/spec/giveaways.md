# Interface Specification — Giveaways Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (Domain G — channel content; `Quotes` G.5, `NamedCounters` G.4); economy `economy.md` (`ICurrencyAccountService.PostLedgerEntryAsync`); pipeline `commands-pipelines.md` (`ICommandAction`/`ActionContext` §3.13, trigger kinds §4.1, `IPipelineExecutor`); chat `ChatController`; whisper send `ITwitchWhispersApi.SendWhisperAsync` (twitch-helix.md §3.2); crypto `gdpr-crypto.md` (`IFieldCipher` AEAD); community `community-dashboard.md` (active-viewer resolution); widgets `widgets-overlays.md` (`IWidgetNotifier`); roles `roles-permissions.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`/`PaginatedResponse<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; UUIDv7 `Guid` PKs; `BroadcasterId` `Guid`; soft-delete filter; AGPL header on every source file.

> **Why.** A purpose-built giveaway tool — keyword/active-viewer entry, eligibility filters, sub-luck weighting, multi-winner weighted draw, re-roll, winner history, and four prize-fulfillment modes (announce / currency / pipeline / **code-key pool**) — is a baseline on StreamElements/Streamlabs and was a corpus gap. Distinct from the economy's instant games and the live-games engine: this is a *campaign* (open → collect entries → draw winners → fulfill), not a per-play game.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Two entry modes** (`EntryMode`): **`keyword`** — viewers type the giveaway's keyword while it's open (one `GiveawayEntry` each); **`active_viewers`** — no entry needed, the draw pulls from recently-active chatters (resolved via `community-dashboard.md`). |
| D2 | **One active (`open`/`closed`-not-yet-drawn) giveaway per channel** — clear keyword, no contention. `OpenAsync` fails `GIVEAWAY_ALREADY_ACTIVE` otherwise. |
| D3 | **Eligibility is opt-in / default-everyone** (`EligibilityJson`): require-follower, require-sub, min-watch-minutes, min-account-age-days, min community standing. Empty = everyone (per the opt-in/default-deny rule). |
| D4 | **Weighting (sub-luck) default OFF** (`WeightingJson` null = 1 ticket each). When set, tickets scale by sub tier / role (e.g. `{sub_t1:2, sub_t3:4, vip:2}`). The broadcaster is **always** excluded from winning; mods excluded iff `ExcludeModerators`. |
| D5 | **Four prize modes** (`PrizeMode`, owner-selected all four): **`announce`** (draw + name only); **`currency`** (fixed `PrizeCurrencyAmount` or `PrizeFromPot` = the summed entry costs, credited via economy); **`pipeline`** (run `PrizePipelineId` once per winner — grant/whisper/TTS/webhook, anything the engine does); **`code_pool`** (each winner gets a unique unused code from a `GiveawayCodePool`, delivered by **whisper**). |
| D6 | **Codes are secrets.** `GiveawayCode.CodeCipher` is **AEAD-encrypted via `IFieldCipher`** ([PII-shred]); the plaintext is **never** returned by a list/read API (masked), only delivered to the winner by whisper. Whisper failure (Twitch restriction) leaves the code assigned and **reveals it to the broadcaster in winner history** for manual relay — never lost, never leaked to others. |
| D7 | **Optional claim window** (`ClaimWindowMinutes`, null = none): a drawn winner must respond within the window or auto-`forfeited` → re-roll. |
| D8 | **Pipeline + redemption driven.** `open_giveaway` / `draw_giveaway` / `enter_giveaway` actions let a command, timer, or channel-point redemption open a giveaway or enter the triggering viewer. `GiveawayDrawnEvent` fires per draw. |
| D9 | **Schema additions (Domain G):** **G.6 `Giveaway`**, **G.7 `GiveawayEntry`**, **G.8 `GiveawayWinner`**, **G.9 `GiveawayCodePool`**, **G.10 `GiveawayCode`**. Economy delta: `EntryType` enum gains `spend_giveaway` / `earn_giveaway`. |

---

## 1. Entities

Domain G. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter (except append-only winners), `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`Giveaway`** | **G.6 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Title string(140)`; `EntryMode string(20)` **[VC:enum]** (`keyword`\|`active_viewers`); `Keyword string(50)?`; `EntryCost long?` (loyalty points; null/0 = free); `MaxEntriesPerUser int` (default 1); `EligibilityJson text?` **[VC:JSON]**; `WeightingJson text?` **[VC:JSON]**; `WinnerCount int` (default 1); `ExcludeModerators bool`; `ClaimWindowMinutes int?`; `PrizeMode string(20)` **[VC:enum]** (`announce`\|`currency`\|`pipeline`\|`code_pool`); `PrizeCurrencyAmount long?`; `PrizeFromPot bool`; `PrizePipelineId Guid?` FK→`Pipelines.Id`; `PrizeCodePoolId Guid?` FK→`GiveawayCodePool.Id`; `Status string(20)` **[VC:enum]** (`draft`\|`open`\|`closed`\|`drawn`\|`archived`); `OpenedAt/ClosesAt/DrawnAt DateTime?`; `ConfigSchemaVersion int`; `CreatedAt/UpdatedAt/DeletedAt`. **Index** `(BroadcasterId, Status)`. |
| **`GiveawayEntry`** | **G.7 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `GiveawayId Guid` FK→`Giveaway.Id` Index; `ViewerUserId Guid` FK→`Users.Id`; `ViewerTwitchUserId string(50)` **[PII-hash]**; `TicketCount int` (weighted, D4); `EntryCostLedgerEntryId long?` (if paid); `EnteredAt DateTime`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(GiveawayId, ViewerUserId)`. |
| **`GiveawayWinner`** | **G.8 (NEW)** `[APPEND-ONLY]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `GiveawayId Guid` FK Index; `ViewerUserId Guid` FK→`Users.Id`; `ViewerTwitchUserId string(50)` **[PII-hash]**; `DrawnAt DateTime`; `Status string(20)` **[VC:enum]** (`drawn`\|`claimed`\|`forfeited`\|`redrawn`); `IsRedraw bool`; `AssignedCodeId Guid?` FK→`GiveawayCode.Id`; `FulfillmentLedgerEntryId long?` (currency payout); `WhisperDelivered bool?` (code mode). **Index** `(GiveawayId)`, `(BroadcasterId, DrawnAt)`. |
| **`GiveawayCodePool`** | **G.9 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `Name string(100)`; `Description string(300)?`; `CreatedAt/UpdatedAt/DeletedAt`. |
| **`GiveawayCode`** | **G.10 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK Index; `CodePoolId Guid` FK→`GiveawayCodePool.Id` Index; `CodeCipher text` **[PII-shred]** (AEAD via `IFieldCipher`, D6 — never plaintext at rest or in reads); `Label string(100)?`; `Status string(20)` **[VC:enum]** (`available`\|`assigned`\|`delivered`\|`revoked`); `AssignedWinnerId Guid?` FK→`GiveawayWinner.Id`; `AssignedAt DateTime?`; `CreatedAt/UpdatedAt/DeletedAt`. **Index** `(CodePoolId, Status)`. |

**Adjacent (used, owned elsewhere):** `CurrencyAccount`/`CurrencyLedgerEntry` (economy — entry-cost debit `spend_giveaway`, currency-prize credit `earn_giveaway`); `Pipelines` (pipeline prize); `Users` (every entrant is a get-or-create User — no fabricated entries).

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record GiveawayOpenedEvent : DomainEventBase
{
    public required Guid GiveawayId { get; init; }
    public required string EntryMode { get; init; }
    public string? Keyword { get; init; }
}

public sealed record GiveawayDrawnEvent : DomainEventBase
{
    public required Guid GiveawayId { get; init; }
    public required IReadOnlyList<Guid> WinnerUserIds { get; init; }
    public required int EntryCount { get; init; }
    public required string PrizeMode { get; init; }
}
```

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Giveaways`. All returns `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Giveaways/`.

### 3.1 `IGiveawayService`

```csharp
public interface IGiveawayService
{
    Task<Result<GiveawayDto>> CreateAsync(Guid broadcasterId, UpsertGiveawayRequest request, CancellationToken ct = default);
    Task<Result<GiveawayDto>> UpdateAsync(Guid broadcasterId, Guid giveawayId, UpsertGiveawayRequest request, CancellationToken ct = default);  // draft/closed only
    Task<Result> DeleteAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);
    Task<Result<PagedList<GiveawayDto>>> ListAsync(Guid broadcasterId, GiveawayFilter filter, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<GiveawayDto>> GetAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);

    // Opens for entries (D2 single-active guard); publishes GiveawayOpenedEvent; starts keyword matching (keyword mode).
    Task<Result<GiveawayDto>> OpenAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);
    Task<Result<GiveawayDto>> CloseAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);   // stop accepting entries

    // A viewer joins (keyword listener or enter_giveaway action): eligibility (D3) + dedup (Unique) + MaxEntriesPerUser;
    // debits EntryCost via PostLedgerEntryAsync (spend_giveaway) when set → INSUFFICIENT_FUNDS; computes TicketCount (D4).
    Task<Result<GiveawayEntryDto>> EnterAsync(Guid broadcasterId, Guid giveawayId, Guid viewerUserId, CancellationToken ct = default);

    // Draws WinnerCount distinct winners with a CSPRNG weighted by TicketCount, from GiveawayEntries (keyword) or the
    // eligible active-viewer set (active_viewers); excludes broadcaster (+ mods if set). Fulfills per PrizeMode (§4),
    // appends GiveawayWinner rows, publishes GiveawayDrawnEvent, sets Status=drawn. One IUnitOfWork tx.
    Task<Result<IReadOnlyList<GiveawayWinnerDto>>> DrawAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct = default);

    // Replaces one winner (forfeit/absent): marks it redrawn, draws a replacement excluding all prior winners,
    // re-runs fulfillment for the replacement (reassign code / re-credit / re-run pipeline).
    Task<Result<GiveawayWinnerDto>> RedrawAsync(Guid broadcasterId, Guid giveawayId, Guid winnerId, CancellationToken ct = default);

    Task<Result<PagedList<GiveawayWinnerDto>>> GetWinnersAsync(Guid broadcasterId, Guid giveawayId, PaginationParams pagination, CancellationToken ct = default);
}
```

### 3.2 `IGiveawayCodePoolService` (secret-safe code pools — D6)

```csharp
public interface IGiveawayCodePoolService
{
    Task<Result<CodePoolDto>> CreatePoolAsync(Guid broadcasterId, CreateCodePoolRequest request, CancellationToken ct = default);
    Task<Result<CodePoolDto>> AddCodesAsync(Guid broadcasterId, Guid poolId, AddCodesRequest request, CancellationToken ct = default);  // bulk; AEAD-encrypts each (D6)
    Task<Result<PagedList<CodePoolDto>>> ListPoolsAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<CodePoolDetailDto>> GetPoolAsync(Guid broadcasterId, Guid poolId, CancellationToken ct = default);   // codes MASKED (label + last4 + status), never plaintext
    Task<Result> DeletePoolAsync(Guid broadcasterId, Guid poolId, CancellationToken ct = default);
    Task<Result<string>> RevealAssignedCodeAsync(Guid broadcasterId, Guid winnerId, CancellationToken ct = default); // broadcaster-only fallback when whisper failed (D6); decrypts for manual relay
}
```

`CodePoolDto(Id, Name, Description, Total, Available, Assigned)`; `AddCodesRequest(IReadOnlyList<CodeInput> Codes)` where `CodeInput(string Code, string? Label)`; reads never echo `Code`.

---

## 4. Draw & fulfillment flow

`DrawAsync` (one `IUnitOfWork` tx):
1. Build the candidate set — `keyword`: the giveaway's `GiveawayEntry` rows; `active_viewers`: the eligible recently-active chatters (community service). Re-apply eligibility; expand by `TicketCount` into a weighted pool; remove the broadcaster (+ mods if `ExcludeModerators`) and any already-drawn winner.
2. Pick `WinnerCount` distinct winners with a CSPRNG over the weighted pool.
3. **Fulfill per `PrizeMode`:**
   - `announce` — record only; the winner name surfaces via `GiveawayDrawnEvent` (a pipeline/built-in announces it).
   - `currency` — credit each winner `PrizeCurrencyAmount`, or (if `PrizeFromPot`) the summed `EntryCost` pot split per the rules, via `PostLedgerEntryAsync` (`earn_giveaway`); store `FulfillmentLedgerEntryId`.
   - `pipeline` — enqueue `PrizePipelineId` once per winner (`IPipelineExecutor`, fire-and-forget, winner bound as the triggering user).
   - `code_pool` — atomically claim one `available` code from `PrizeCodePoolId` per winner (`→ assigned`, `AssignedWinnerId` set), **whisper** the decrypted code to the winner via `ITwitchWhispersApi.SendWhisperAsync`; on success `→ delivered`, `WhisperDelivered=true`; on failure leave `assigned`, `WhisperDelivered=false` (broadcaster reveals it via §3.2, D6). Fails `CODE_POOL_EXHAUSTED` if fewer codes than winners (drawn winners without a code are flagged, not silently dropped).
4. Append `GiveawayWinner` rows, `Status=drawn` (or `claimed` if no claim window), publish `GiveawayDrawnEvent`, set the giveaway `Status=drawn`.

A `ClaimWindowMinutes` giveaway arms a per-winner timer (`IRunOnceGuard`-guarded sweep); unclaimed → `forfeited`, eligible for `RedrawAsync`.

**Economy delta (owner `economy.md`):** `EntryType` enum gains `spend_giveaway` (entry cost) and `earn_giveaway` (currency prize / pot payout) — the ledger primitive is unchanged.

---

## 5. Pipeline actions

`ICommandAction` (canonical contract). Registered `Transient`. Floors per §6.

| Type | Config | Behavior |
|---|---|---|
| `open_giveaway` | `giveaway_id` | `OpenAsync`. Lets a command/timer open a prepared giveaway. Fails closed if one is already active (D2). |
| `draw_giveaway` | `giveaway_id?` (null = the active one) | `DrawAsync`. |
| `enter_giveaway` | `giveaway_id?` (null = active) | `EnterAsync` for the triggering viewer — so a channel-point redemption can be "redeem to enter." Fails closed if no active giveaway / ineligible. |

In-chat keyword entry (the common path) is matched by the engine's keyword listener while a `keyword`-mode giveaway is `open` — not a per-giveaway command.

---

## 6. REST surface

Controllers `GiveawaysController` `[Route("api/v{version:apiVersion}/giveaways")]` and `GiveawayCodePoolsController` `[Route("api/v{version:apiVersion}/giveaways/code-pools")]`. `[Authorize]`; Gate-2 keys. Cells `<plane> / <Role> · action:key`.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | `GiveawayFilter`+`PageRequestDto` | `PaginatedResponse<GiveawayDto>` | management / Moderator · `giveaways:read` |
| POST | `/` | `UpsertGiveawayRequest` | `StatusResponseDto<GiveawayDto>` | management / Moderator · `giveaways:write` |
| GET | `/{id}` | — | `StatusResponseDto<GiveawayDto>` | management / Moderator · `giveaways:read` |
| PUT | `/{id}` | `UpsertGiveawayRequest` | `StatusResponseDto<GiveawayDto>` | management / Moderator · `giveaways:write` |
| DELETE | `/{id}` | — | `StatusResponseDto` | management / Moderator · `giveaways:write` |
| POST | `/{id}/open` | — | `StatusResponseDto<GiveawayDto>` | management / Moderator · `giveaways:write` |
| POST | `/{id}/close` | — | `StatusResponseDto<GiveawayDto>` | management / Moderator · `giveaways:write` |
| POST | `/{id}/draw` | — | `StatusResponseDto<IReadOnlyList<GiveawayWinnerDto>>` | management / Moderator · `giveaways:write` |
| POST | `/{id}/winners/{winnerId}/redraw` | — | `StatusResponseDto<GiveawayWinnerDto>` | management / Moderator · `giveaways:write` |
| GET | `/{id}/winners` | `PageRequestDto` | `PaginatedResponse<GiveawayWinnerDto>` | management / Moderator · `giveaways:read` |
| GET | `/{id}/winners/{winnerId}/code` | — | `StatusResponseDto<string>` | management / Broadcaster · `giveaways:codes:write` |
| GET | `/code-pools` | `PageRequestDto` | `PaginatedResponse<CodePoolDto>` | management / Broadcaster · `giveaways:codes:write` |
| POST | `/code-pools` | `CreateCodePoolRequest` | `StatusResponseDto<CodePoolDto>` | management / Broadcaster · `giveaways:codes:write` |
| GET | `/code-pools/{poolId}` | — | `StatusResponseDto<CodePoolDetailDto>` (masked) | management / Broadcaster · `giveaways:codes:write` |
| POST | `/code-pools/{poolId}/codes` | `AddCodesRequest` | `StatusResponseDto<CodePoolDto>` | management / Broadcaster · `giveaways:codes:write` |
| DELETE | `/code-pools/{poolId}` | — | `StatusResponseDto` | management / Broadcaster · `giveaways:codes:write` |

Seed `giveaways:read` (Moderator), `giveaways:write` (Moderator), `giveaways:codes:write` (Broadcaster — code pools hold valuable secrets) in `roles-permissions.md`.

**Overlay:** a first-party **`giveaway`** widget (entrant count, draw spin, winner reveal) is added to the widgets OOTB catalogue and driven by `IWidgetNotifier.SendWidgetEventAsync` (`EventType="giveaway.opened|entered|drawn"`).

---

## 7. DI registration

`NomNomzBot.Infrastructure/Giveaways/DependencyInjection.cs` (`AddGiveaways()`): `IGiveawayService` → `GiveawayService` (Scoped); `IGiveawayCodePoolService` → `GiveawayCodePoolService` (Scoped, depends on `IFieldCipher`); repositories (Scoped); a `GiveawayKeywordListener` (Singleton, subscribes to chat while a `keyword`-mode giveaway is open — one generic listener, like the live-games input dispatcher); the three pipeline actions (Transient). Code whisper via the existing `ITwitchWhispersApi.SendWhisperAsync` (Helix Send Whisper `user:manage:whispers` on the bot account; rate-limited — failures fall back to broadcaster reveal, D6).

---

## 8. Testing — prove behavior

- **Entry** — `EnterAsync` enforces eligibility (an ineligible viewer is rejected with the reason), dedups via the unique key, caps at `MaxEntriesPerUser`, debits `EntryCost` (`spend_giveaway`, `INSUFFICIENT_FUNDS` when broke), and assigns the weighted `TicketCount` from `WeightingJson` (a T3 sub gets the configured multiple; unweighted = 1).
- **Draw** — picks exactly `WinnerCount` **distinct** winners, never the broadcaster, weighted by tickets (statistical over many runs), and writes one `GiveawayWinner` per winner; `GiveawayDrawnEvent` carries the right ids + count.
- **Code pool** — each winner gets a **unique, previously-`available`** code (`→ delivered` on whisper success, `assigned`+`WhisperDelivered=false` on failure); a code is **never** reused across winners/giveaways; `GetPoolAsync` returns codes **masked** (assert no plaintext); a pool with fewer codes than winners returns `CODE_POOL_EXHAUSTED` and flags the un-coded winners (not silent).
- **Currency pot** — `PrizeFromPot` credits the winner the **summed** entry costs (assert the ledger entry amount = Σ `EntryCost`).
- **Re-roll** — `RedrawAsync` forfeits the target, draws a replacement excluding **all** prior winners, and re-fulfills (new code assigned / new credit / pipeline re-run).
- **Claim window** — an unclaimed winner past `ClaimWindowMinutes` is `forfeited` and re-drawable; the sweep is idempotent under `IRunOnceGuard`.
- **Secret custody** — `CodeCipher` is AEAD ciphertext at rest; no API path returns plaintext except the broadcaster-gated `RevealAssignedCodeAsync` / `/code` endpoint.

---

## 9. Decisions (resolved)

Two entry modes — keyword + active-viewers (D1); one active giveaway per channel (D2); opt-in eligibility (D3); default-off sub-luck weighting, broadcaster always excluded (D4); four prize modes incl. secret-safe code pools (D5/D6); optional claim window (D7); pipeline/redemption driven via three actions (D8); schema deltas G.6–G.10 + economy `EntryType` `spend_giveaway`/`earn_giveaway` (D9). Codes AEAD-encrypted, whisper-delivered, broadcaster-reveal fallback; winner history append-only.
