# Interface Specification — Quotes Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (Domain G — channel content; `Commands` G.2, `NamedCounters` G.4); commands `commands-pipelines.md` (`IBuiltinCommand`/`IBuiltinCommandCatalog` §3.10, `ICommandAction` §3.13, `ITemplateResolver`); platform `platform-conventions.md` (`ITenantSequenceAllocator`, `IEventBus`).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PK `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter; AGPL header on every source file.

> **Why.** `!quote` is a baseline engagement feature on every major bot (StreamElements, Nightbot, Fossabot) and the corpus had none. Quotes are channel content (Domain G) — a numbered, searchable library of memorable lines, addable from chat by mods and surfaced via a built-in command, a pipeline action (quote-of-the-day on a timer), and the dashboard.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Quotes are per-channel numbered** (`#1, #2, …`). The number is **monotonic and stable** — allocated via `ITenantSequenceAllocator` (gap-free per channel), and a deleted quote's number is **never reused** (the soft-deleted row keeps it). |
| D2 | **`!quote` is a built-in command** (`IBuiltinCommand`, toggleable per channel like every built-in): no arg → random; `<n>` → that quote; `add <text>` / `edit <n> <text>` / `del <n>` → mod-gated mutations. |
| D3 | **Quote-of-the-day rides a pipeline action** (`post_quote`), so a timer or command can post a random/specific quote — no dedicated scheduler. |
| D4 | **Schema addition:** **G.5 `Quote`** (soft-delete). No other schema change. |

---

## 1. Entities

Domain G. PK `Guid`/UUIDv7, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`Quote`** | **G.5 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `Number int` (per-channel monotonic, D1); `Text string(500)`; `QuotedDisplayName string(100)?` (who said it); `ContextGame string(100)?` (game/category at the time); `QuotedAt DateTime?` (when said — defaults to creation); `UserId Guid?` FK→`Users.Id` (the resolved local user the quote is attributed to; null when `QuotedDisplayName` never resolved to a known chatter, including every quote that predates the column); `CreatedByUserId Guid?` FK→`Users.Id`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Number)`. **Index** `(BroadcasterId, Number)`. |

A free-text search over `Text`/`QuotedDisplayName` uses a `(BroadcasterId)` filtered `ILIKE`/`to_tsvector` read (provider-appropriate; no new column).

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record QuoteAddedEvent : DomainEventBase
{
    public required Guid QuoteId { get; init; }
    public required int Number { get; init; }
    public Guid? CreatedByUserId { get; init; }
}
```

---

## 3. Service interface

Namespace `NomNomzBot.Application.Quotes`. All returns `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Quotes/`.

```csharp
public interface IQuoteService
{
    // Allocates the next per-channel Number via ITenantSequenceAllocator under a tx, inserts the Quote,
    // publishes QuoteAddedEvent. Returns the created quote (with its assigned Number).
    Task<Result<QuoteDto>> AddAsync(Guid broadcasterId, AddQuoteRequest request, CancellationToken ct = default);

    Task<Result<QuoteDto>> GetAsync(Guid broadcasterId, int number, CancellationToken ct = default);          // 404-style Result if missing
    Task<Result<QuoteDto>> GetRandomAsync(Guid broadcasterId, CancellationToken ct = default);                // uniform over non-deleted; Result failure (QUOTES_EMPTY) if none
    Task<Result<PagedList<QuoteDto>>> ListAsync(Guid broadcasterId, QuoteSearch search, PaginationParams pagination, CancellationToken ct = default);
    // Quotes resolved-attributed to one person (Quote.UserId), newest first — the Community Profile page's "quotes by this person".
    // A quote whose QuotedDisplayName never resolved to this user never appears, even if the display name matches.
    Task<Result<PagedList<QuoteDto>>> ListByUserAsync(Guid broadcasterId, Guid userId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<QuoteDto>> EditAsync(Guid broadcasterId, int number, EditQuoteRequest request, CancellationToken ct = default);  // Number is immutable
    Task<Result> DeleteAsync(Guid broadcasterId, int number, CancellationToken ct = default);                 // soft-delete; Number not reused (D1)
}

public sealed record AddQuoteRequest(string Text, string? QuotedDisplayName, string? ContextGame, DateTime? QuotedAt, Guid? CreatedByUserId);
public sealed record EditQuoteRequest(string Text, string? QuotedDisplayName, string? ContextGame);
public sealed record QuoteSearch(string? Term);
public sealed record QuoteDto(Guid Id, int Number, string Text, string? QuotedDisplayName, string? ContextGame, DateTime? QuotedAt, DateTime CreatedAt, Guid? UserId = null);
```

---

## 4. Built-in command + pipeline action

**Built-in `!quote`** (`IBuiltinCommand`, `BuiltinKey="quote"`, toggle row `ChannelBuiltinCommands` G.2a):
- `!quote` → `GetRandomAsync`, renders `#{number}: "{text}" — {quotedDisplayName} ({contextGame})`.
- `!quote <n>` → `GetAsync(n)`.
- `!quote add <text>` → `AddAsync` (floor **Moderator**); replies with the new `#number`.
- `!quote edit <n> <text>` / `!quote del <n>` → `EditAsync`/`DeleteAsync` (floor **Moderator**).
- Default min-permission: read sub-commands `Everyone`, mutating sub-commands `Moderator` (per-channel overridable via the built-in toggle).

**Pipeline action `post_quote`** (`ICommandAction`, canonical contract): config `{ number:int? }` — null → random, else that quote; writes the rendered line to `ctx.Variables["quote"]` and (when used as a message step) posts it. Lets a timer run quote-of-the-day. Fails closed (`ActionResult.Fail`) if the channel has no quotes.

---

## 5. REST surface

Controller `QuotesController`, `[Route("api/v{version:apiVersion}/quotes")]`. `[Authorize]`; Gate-2 keys. Cells `<plane> / <Role> · action:key`.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/` | `QuoteSearch`+`PageRequestDto` | `PaginatedResponse<QuoteDto>` | management / Moderator · `quotes:read` |
| GET | `/random` | — | `StatusResponseDto<QuoteDto>` | management / Moderator · `quotes:read` |
| GET | `/{number}` | — | `StatusResponseDto<QuoteDto>` | management / Moderator · `quotes:read` |
| POST | `/` | `AddQuoteRequest` | `StatusResponseDto<QuoteDto>` | management / Moderator · `quotes:write` |
| PUT | `/{number}` | `EditQuoteRequest` | `StatusResponseDto<QuoteDto>` | management / Moderator · `quotes:write` |
| DELETE | `/{number}` | — | `StatusResponseDto<QuoteDto>` | management / Moderator · `quotes:delete` |

Seed in `roles-permissions.md` (management): **`quotes:read`** and **`quotes:write`** default to Moderator with a **Vip floor** (the broadcaster may lower them so a trusted VIP can read and curate — add/edit); **`quotes:delete`** is Moderator with a **Moderator floor** (deleting is data loss, so it can never be lowered to VIP). The DELETE route carries `quotes:delete`; POST/PUT carry `quotes:write`.

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Quotes/DependencyInjection.cs` (`AddQuotes()`): `IQuoteService` → `QuoteService` (Scoped); `QuoteRepository` (Scoped); the `!quote` `IBuiltinCommand` and the `post_quote` `ICommandAction` registered with their catalogs (auto-discovery / transient per the commands-pipelines convention).

**Tests (prove behavior):** `AddAsync` assigns `Number = previousMax + 1` and persists the full shape (Text/QuotedDisplayName/ContextGame), publishing `QuoteAddedEvent`; deleting `#2` then adding a new quote yields `#(max+1)`, **never** reusing `2`; `GetRandomAsync` returns only non-deleted quotes and fails `QUOTES_EMPTY` on an empty channel; the `!quote add` built-in is **Moderator-gated** (a viewer is rejected) while `!quote`/`!quote <n>` are open; `post_quote` renders and posts a quote and fails closed when none exist.

---

## 7. Decisions (resolved)

Per-channel monotonic numbering via `ITenantSequenceAllocator`, numbers never reused (D1); `!quote` is a toggleable built-in with mod-gated mutations (D2); quote-of-the-day via the `post_quote` action on a timer (D3); schema delta G.5 `Quote` (D4).
