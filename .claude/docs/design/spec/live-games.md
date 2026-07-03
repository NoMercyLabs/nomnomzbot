# Interface Specification — Live Games Subsystem (interactive overlay games)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` (Domain K — `GameConfig` K.7, `GamePlay` K.9, the currency ledger K.2/K.3); economy `economy.md` (`ICurrencyAccountService`, `IGameService`); widgets `widgets-overlays.md` (`IWidgetNotifier`, `widget_event`); pipeline `commands-pipelines.md` (`ICommandAction`/`ActionContext` §3.13); platform `platform-conventions.md` (`IRunOnceGuard`, `IEventBus`, auto-discovery); roles `roles-permissions.md` (Gate-2 keys, planes).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PK `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter; AGPL header on every source file.

> **Why this subsystem exists.** `economy.md` `IGameService.PlayAsync` covers **instant-resolve** chat games
> (slots / coinflip — one message, atomic bet→roll→payout). It does **not** cover **stateful, multi-participant,
> live-on-the-overlay** games — the "dropgame" class where a host opens a round, many viewers join over a join
> window via chat, the overlay animates live, and the round resolves into multiple winners. Live Games is that
> session layer. **The hard requirement (owner): adding a new game must be a drop-in — a new class plus its
> overlay widget plus a default config, with zero edits to the engine or any core code.** This spec delivers
> exactly that by composing primitives the platform already standardized on; it invents one contract
> (`ILiveGame`) and one mutable entity (`GameSession`).

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **A game is a drop-in `ILiveGame` class** (§4), discovered by assembly scan (auto-discovery, like `IBuiltinCommandCatalog` / `ICommandAction`). The engine never names a concrete game; adding one touches no engine code. |
| D2 | **One generic engine** (`ILiveGameEngine` + the `LiveGameRunner` hosted service) runs every game — lobby window, ticks, resolution, crash-recovery. |
| D3 | **Config reuses `GameConfig` (K.7)**, keyed by `GameType == ILiveGame.GameKey`. A `GameConfig` whose `GameType` matches a discovered `ILiveGame` is a *live* game; one that does not is an *instant* economy game. No new config table. Config CRUD stays economy's (`IGameService.UpsertGameAsync`, key `economy:games:write`). |
| D4 | **Currency stays economy's.** Live Games never posts a ledger entry directly; it calls three new `IGameService` methods (§3.3, an economy delta) that wrap debits/credits + `GamePlay` appends in one `IUnitOfWork` tx. Entry-fee debits and payout credits are tagged `SourceType=live_game`, `SourceId=sessionId` on `CurrencyLedgerEntry` — the durable link used for crash-refunds. |
| D5 | **Overlay output reuses the existing push.** The engine sends frames via `IWidgetNotifier.SendWidgetEventAsync(broadcasterId, overlayWidgetId, WidgetEventDto{EventType="game.<phase>", Data})` (widgets-overlays §6) — **no new hub, no per-game socket**. One generic game-overlay protocol; each game declares an `OverlayWidgetKey` and emits frames. |
| D6 | **Start via the action seam; input via one generic subscription.** A round is **started** by the `start_live_game` `ICommandAction` (so a `!dropgame` command, a redemption, or a timer can launch it) or the dashboard. In-session **input** (`!drop`, a number, …) is routed by the engine's **single** chat-event subscription matching the active session's manifest keyword(s) — *one* dispatcher, never a listener per game. (Refines the earlier "input via a pipeline action" sketch: starting uses the action seam; high-frequency in-round input uses the engine's generic subscription — both keep games drop-in.) |
| D7 | **At most one non-terminal session per channel.** The overlay shows one game at a time; `StartAsync` fails `SESSION_ALREADY_ACTIVE` if a `lobby`/`running`/`resolving` session exists for the channel. |
| D8 | **Fun-money only.** Stakes/payouts are channel currency; the zero-value-out and optional-18+ rules of `economy.md` §3.5 apply unchanged (a live game with `Category=gambling` inherits the same `IAgeConsentService` gate). |
| D9 | **Crash-safe.** Engine session state is held in memory and snapshotted to `GameSession.StateJson` on each transition; on startup an `IRunOnceGuard`-guarded sweep cancels non-terminal sessions and **refunds** their entry-fee debits (reversing ledger entries matched by `SourceType=live_game`+`SourceId`). |
| D10 | **Schema additions (Domain K):** **K.9a `GameSession`** (new, soft-delete) and **`GamePlay` (K.9) += `GameSessionId Guid?`** (nullable — instant plays leave it null). No other schema change. |

---

## 1. Entities

Domain K. PK `Guid`/UUIDv7, `BaseEntity` timestamps, soft-delete filter, `[VC:JSON]`/`[VC:enum]` converters (Newtonsoft), `BroadcasterId Guid` tenant scope — per schema §1.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`GameSession`** | **K.9a (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `GameConfigId Guid` FK→`GameConfig.Id` Index; `GameType string(30)` Index; `Status string(20)` **[VC:enum]** (`lobby`\|`running`\|`resolving`\|`settled`\|`cancelled`); `StartedByUserId Guid?`; `StartedAt timestamp`; `JoinClosesAt timestamp?`; `ResolvedAt timestamp?`; `ParticipantCount int`; `StateJson text?` **[VC:JSON]** (engine/game state snapshot — overlay frame + recovery); `OutcomeJson text?` **[VC:JSON]** (resolved summary); `CancelReason string(60)?`; `CreatedAt/UpdatedAt/DeletedAt`. **Index** `(BroadcasterId, Status, CreatedAt)`. At most one row per `BroadcasterId` with non-terminal `Status` (service-enforced, D7 — not a DB unique since terminal rows accumulate). |
| **`GamePlay`** | **K.9 (DELTA)** `[APPEND-ONLY]` | tenant | EXISTING fields (`economy.md` §1) **+ `GameSessionId Guid?`** FK→`GameSession.Id` Index — null for instant `PlayAsync` games; set for every live-game award row. |

**Adjacent (read/used, owned elsewhere):** `GameConfig` K.7 (economy — the per-game config, keyed by `GameType`); `CurrencyAccount` K.2 + `CurrencyLedgerEntry` K.3 (economy — the wallet + ledger; live-game debits/credits carry `SourceType=live_game`, `SourceId=sessionId`); `Widget` P.6 (widgets — the overlay widget resolved from `ILiveGame.Manifest.OverlayWidgetKey`).

**EF mapping notes:** `GameSession` carries the soft-delete global filter; `StateJson`/`OutcomeJson` use the hand-rolled `JsonValueConverter<T>`+`JsonValueComparer<T>` (never `HasColumnType("jsonb")`). `GamePlay` stays append-only (no `UpdatedAt`/`DeletedAt`, no filter); the new FK is indexed for session-history reads.

---

## 2. Domain events

All inherit `DomainEventBase` (platform-conventions §2.0 — `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; do not redeclare). Published via `IEventBus`. Per-participant currency outcomes are **already** covered by economy's `GamePlayedEvent` (one per appended `GamePlay` row) — not re-emitted here.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record LiveGameStartedEvent : DomainEventBase
{
    public required Guid SessionId { get; init; }
    public required string GameType { get; init; }
    public Guid? StartedByUserId { get; init; }
}

public sealed record LiveGameResolvedEvent : DomainEventBase
{
    public required Guid SessionId { get; init; }
    public required string GameType { get; init; }
    public required int ParticipantCount { get; init; }
    public required int WinnerCount { get; init; }
    public required long TotalPaidOut { get; init; }
}

public sealed record LiveGameCancelledEvent : DomainEventBase
{
    public required Guid SessionId { get; init; }
    public required string Reason { get; init; }     // host_cancel | startup_sweep | min_players_unmet | timeout
}
```

---

## 3. Service & runtime contracts

Namespace `NomNomzBot.Application.Games`. All ids `Guid`. Fallible methods return `Task<Result<T>>` / `Task<Result>`. Implementations in `NomNomzBot.Infrastructure/Games/`.

### 3.1 `ILiveGameEngine` (NEW — the generic orchestrator)

```csharp
public interface ILiveGameEngine
{
    // Opens a session for GameType (must map to a discovered ILiveGame with an enabled GameConfig).
    // D7 guard (SESSION_ALREADY_ACTIVE); persists GameSession(Status=lobby); invokes ILiveGame.OnStartAsync;
    // publishes LiveGameStartedEvent; pushes the first overlay frame. Returns the session.
    Task<Result<GameSessionDto>> StartAsync(Guid broadcasterId, StartLiveGameCommand command, CancellationToken ct = default);

    // Host/operator cancels a non-terminal session: refunds entry-fee debits via IGameService.RefundLiveGameAsync,
    // sets Status=cancelled, publishes LiveGameCancelledEvent(host_cancel), pushes a final overlay frame.
    Task<Result> CancelAsync(Guid broadcasterId, Guid sessionId, CancellationToken ct = default);

    // The current non-terminal session for the channel (404-style Result if none).
    Task<Result<GameSessionDto>> GetActiveAsync(Guid broadcasterId, CancellationToken ct = default);

    // Session history (settled/cancelled), tenant-filtered, paged.
    Task<Result<PagedList<GameSessionDto>>> ListAsync(Guid broadcasterId, GameSessionFilter filter, PaginationParams pagination, CancellationToken ct = default);
}
```

`LiveGameRunner` (internal `IHostedService`, singleton) owns the wall-clock: it ticks active sessions at each game's `Manifest.TickInterval`, closes the lobby at `JoinClosesAt`, drives `OnResolveAsync`, and holds the **single** chat-event subscription (D6) that feeds `OnInputAsync`. Multi-instance safety: a session's tick/resolve loop is held under `IRunOnceGuard` keyed by `sessionId`, so on SaaS only one node advances a given session.

### 3.2 `ILiveGameCatalog` (NEW — the discovered registry)

```csharp
public interface ILiveGameCatalog
{
    IReadOnlyCollection<LiveGameManifest> All { get; }           // every discovered ILiveGame's manifest
    bool TryGet(string gameKey, out ILiveGame game);             // resolve a game by key
}
```

Populated by auto-discovery (§7). Startup validation: duplicate `GameKey` → fail fast; each `Manifest.OverlayWidgetKey` must resolve to a seeded first-party widget (fail-closed).

### 3.3 Economy delta — `IGameService` gains three methods (owned by `economy.md`)

Live Games does no currency I/O itself; these wrap it atomically (economy owns the ledger + `GamePlay`). Listed here as the binding contract Live Games consumes; `economy.md` §3.5 absorbs them.

```csharp
// On join with an entry fee: debits the stake (PostLedgerEntryAsync EntryType=spend_game,
// SourceType=live_game, SourceId=sessionId). Guards INSUFFICIENT_FUNDS / frozen account.
// Returns the account + the bet ledger-entry id (stashed in session state for settlement/refund).
Task<Result<LiveGameStakeResult>> StakeLiveGameEntryAsync(Guid broadcasterId, LiveGameStakeCommand command, CancellationToken ct = default);

// On resolve: in ONE IUnitOfWork tx, for each award credits Payout (>0) (EntryType=earn_game,
// SourceType=live_game, SourceId=sessionId) and appends a GamePlay (GameSessionId set, BetAmount=Stake,
// Outcome, PayoutAmount, NetResult, Bet/PayoutLedgerEntryId linked); publishes GamePlayedEvent per row.
Task<Result<LiveGameSettlementResult>> SettleLiveGameAsync(Guid broadcasterId, LiveGameSettlement settlement, CancellationToken ct = default);

// On cancel/startup-sweep: posts reversing credits (EntryType=refund_game, RelatedEntryId=original)
// for every spend_game/SourceId=sessionId debit not already settled. Idempotent per session.
Task<Result> RefundLiveGameAsync(Guid broadcasterId, Guid sessionId, CancellationToken ct = default);
```

---

## 4. The game contract — `ILiveGame` (the drop-in seam)

Namespace `NomNomzBot.Application.Games`. A game is **pure logic**: it reads the engine-supplied `LiveGameState`, mutates its own state bag, and returns a transition. It never touches the DB, currency, chat, tokens, or SignalR — the engine brokers all of that.

```csharp
public interface ILiveGame
{
    string GameKey { get; }                 // "drop_game" — unique; equals the GameConfig.GameType
    LiveGameManifest Manifest { get; }

    Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct);
    Task<LiveGameTransition> OnInputAsync(LiveGameState state, LiveGameInput input, CancellationToken ct);
    Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct);    // no-op allowed for non-timed games
    Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct);
}

public sealed record LiveGameManifest(
    string DisplayName,
    IReadOnlyList<string> InputKeywords,    // matched (case-insensitive) against the first chat token while active
    string OverlayWidgetKey,                // first-party widget that renders this game (D5)
    int MinPlayers,
    int MaxPlayers,                         // 0 = unbounded
    TimeSpan LobbyWindow,                   // join window before auto-resolve
    TimeSpan? TickInterval,                 // null = no ticks (event-driven only)
    bool RequiresEntryFee);                 // true → engine stakes each joiner via StakeLiveGameEntryAsync

// Engine-owned, passed in; the game reads it and mutates only Data.
public sealed class LiveGameState
{
    public required Guid SessionId { get; init; }
    public required Guid BroadcasterId { get; init; }
    public required GameConfigView Config { get; init; }            // bet/odds/ConfigJson view of GameConfig
    public required IReadOnlyList<LiveGameParticipant> Participants { get; init; }
    public required LiveGamePhase Phase { get; init; }              // Lobby | Running | Resolving
    public required IDictionary<string, object?> Data { get; init; } // the game's own state bag → StateJson
    public required IGameRandom Random { get; init; }                // engine CSPRNG — fair odds, seedable in tests
}

public interface IGameRandom { int Next(int maxExclusive); double NextDouble(); bool Roll(double percent); }  // engine-provided; CSPRNG in prod, seeded fake in tests

public sealed record LiveGameParticipant(Guid UserId, Guid AccountId, string DisplayName, long Stake);
public sealed record LiveGameInput(LiveGameParticipant Player, string Keyword, IReadOnlyList<string> Args, string RawMessage);

// A hook's outcome: optionally push an overlay frame, optionally trigger resolution.
public sealed record LiveGameTransition(bool PushOverlay, object? OverlayPayload = null, bool Resolve = false)
{
    public static LiveGameTransition Continue() => new(false);
    public static LiveGameTransition Push(object payload) => new(true, payload);
    public static LiveGameTransition GoResolve(object? payload = null) => new(payload is not null, payload, true);
    public static LiveGameTransition Ignore() => new(false);        // input rejected (not a valid play)
}

// Resolution: who won what. Engine credits Payout, appends GamePlay rows, pushes the final frame.
public sealed record LiveGameResolution(IReadOnlyList<LiveGameAward> Awards, object? FinalOverlayPayload);
public sealed record LiveGameAward(Guid UserId, Guid AccountId, long Stake, string Outcome, long Payout);
//   Outcome ∈ win | lose | push | jackpot  (matches GamePlay.Outcome [VC:enum])
```

**Engine ↔ game loop (what the engine does, so the game stays pure):**
1. `StartAsync` → persist `GameSession(lobby)`, call `OnStartAsync`, snapshot `Data`→`StateJson`, push the frame.
2. A chat input matching `Manifest.InputKeywords` while `lobby`/`running` → if `RequiresEntryFee`, stake the **first numeric arg clamped to `GameConfig`'s `[MinBet, MaxBet]`** (absent → `MinBet`) via `StakeLiveGameEntryAsync` first (skip the player on `INSUFFICIENT_FUNDS`); add the participant, call `OnInputAsync`, apply the transition (snapshot + optional push), resolve early if it returned `Resolve`.
3. Each `TickInterval` → `OnTickAsync` → apply transition.
4. Lobby/`MaxPlayers`/explicit-resolve reached → set `resolving`, call `OnResolveAsync`; if `Participants.Count < MinPlayers`, **cancel + refund** (`LiveGameCancelledEvent(min_players_unmet)`) instead; else `SettleLiveGameAsync(awards)`, set `settled`, write `OutcomeJson`, publish `LiveGameResolvedEvent`, push the final frame.

### 4.1 Adding a game = three artifacts, zero core edits (the reference: DropGame)

To ship dropgame (and every future game), drop in:
1. **`DropGame : ILiveGame`** in `Infrastructure/Games/Catalog/` — `GameKey="drop_game"`; on `!drop` it records a randomized landing position in `Data`; `OnResolveAsync` scores proximity to the target and awards the closest players a `PayoutMultiplier × Stake`. Pure logic.
2. **A `drop_game` first-party overlay widget** (widgets-overlays catalogue) keyed `OverlayWidgetKey="drop_game"` — renders the `game.<phase>` frames (parachutes, target, scoreboard).
3. **A default `GameConfig` seed** (`GameType="drop_game"`, `Category=minigame`, bet/cooldown/`MaxPlaysPerStream`, `ConfigJson` target/precision).

Auto-discovery registers (1); the engine, REST, the `start_live_game` action, the dashboard Games page, ledger, and overlay push all work **unchanged**. Plinko, heist-live, battle-royale follow the identical three-artifact shape.

### 4.2 Overlay delivery tiers (the art problem — decided)

The `ILiveGame` class needs no art; only its `OverlayWidgetKey` does. Games therefore split by how much their overlay demands, and **that split is the build order**:

- **Tier 1 — data overlays (ship first, no illustration).** The overlay is styled data + motion the design system already provides: a name list, a climbing counter, a countdown, a struck-through roster, a card grid. **Heist, Raffle, Crash, Battle Royale, Trivia, Bingo.** Authored first-party from `frontend-design-system` primitives — no designer dependency.
- **Tier 2 — art/physics overlays (engine-ready, art-gated).** The overlay needs illustration or physics: parachutes, marbles, a Plinko board, a prize wheel. **Drop, Marble Race, Plinko, Wheel, Horse Race.** The engine runs them today; each ships when its overlay widget exists — authored by the designer or arriving via the verified-community gallery + clone-to-edit (`widgets-overlays.md`). **No engine change when the art lands** — the game class and config are already valid; only the `OverlayWidgetKey` target appears.

---

## 5. REST surface

Controller `GameSessionsController`, `[Route("api/v{version:apiVersion}/games/sessions")]`. `[Authorize]`; Gate-2 keys via the roles middleware. Cells: `<plane> / <Role> · action:key` (per `roles-permissions.md`).

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/active` | — | `StatusResponseDto<GameSessionDto>` | management / Moderator · `games:session:read` |
| GET | `/` | `GameSessionFilter`+`PageRequestDto` | `PaginatedResponse<GameSessionDto>` | management / Moderator · `games:session:read` |
| POST | `/` | `StartLiveGameRequest(GameType)` | `StatusResponseDto<GameSessionDto>` | management / Moderator · `games:session:start` |
| DELETE | `/{sessionId}` | — | `StatusResponseDto<GameSessionDto>` | management / Moderator · `games:session:cancel` |
| GET | `/catalog` | — | `StatusResponseDto<IReadOnlyList<LiveGameManifest>>` | management / Moderator · `games:session:read` |

DTOs: `GameSessionDto(Guid Id, string GameType, string Status, int ParticipantCount, DateTime StartedAt, DateTime? JoinClosesAt, DateTime? ResolvedAt, IReadOnlyDictionary<string,object?>? State, IReadOnlyDictionary<string,object?>? Outcome)`; `GameSessionFilter(string? GameType, string? Status)`. Per-game **config** CRUD is **not** here — it is economy's `GET/PUT /economy/games` (`economy:games:read`/`write`); the dashboard Games page composes both surfaces.

Seed `games:session:read/start/cancel` permission rows (Gate-2) in `roles-permissions.md`'s seed at the `management` plane, `Moderator` floor.

---

## 6. Pipeline actions

`ICommandAction` (canonical contract, commands-pipelines §3.13: `Type`/`Category`/`Description`/`Task<ActionResult> ExecuteAsync(ActionContext, CancellationToken)`; params from `ctx.Parameters`, tenant `ctx.BroadcasterId`). Registered `Transient` with the action set.

| Type | Config | Behavior |
|---|---|---|
| `start_live_game` | `game_type:string` | Calls `ILiveGameEngine.StartAsync`. Writes `session_id`/`status` into `ctx.Variables`. Fails closed (`ActionResult.Fail`) if the game is unknown/disabled or a session is already active (D7). Lets `!dropgame`, a redemption, or a timer launch a round. |
| `cancel_live_game` | — | Calls `ILiveGameEngine.CancelAsync` for the channel's active session (refund + cancel). No-op success if none active. |

In-round joins (`!drop`, etc.) are **not** pipeline actions — they are routed by the engine's single chat subscription (D6).

---

## 7. DI & auto-discovery

`NomNomzBot.Infrastructure/Games/DependencyInjection.cs` (`AddLiveGames()`), called from the root `DependencyInjection.cs`.

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `ILiveGameEngine` | `LiveGameEngine` | Scoped | orchestration; depends on `IGameService` (economy), `IWidgetNotifier` (widgets), `ILiveGameCatalog`, repos, `IEventBus` |
| `ILiveGameCatalog` | `LiveGameCatalog` | Singleton | built from all discovered `ILiveGame` |
| `ILiveGame` (each game) | `DropGame`, … | Singleton | **auto-discovered** by assembly scan — `AddLiveGames()` registers every `ILiveGame` in the games assembly (the platform auto-discovery convention; no manual edit to add one). Pure/stateless ⇒ singleton-safe. |
| `LiveGameRunner` | `LiveGameRunner` | Singleton `IHostedService` | wall-clock + the single chat-input subscription; per-session loops under `IRunOnceGuard` |
| `ICommandAction` (`start_live_game`, `cancel_live_game`) | `StartLiveGameAction`, `CancelLiveGameAction` | Transient | registered with the pipeline action set |
| `GameSessionRepository` | `GameSessionRepository` | Scoped | tenant-filtered |

**Auto-discovery (the no-hack guarantee):** `AddLiveGames()` scans the games assembly for `ILiveGame` implementors and registers each — adding a game class is the *only* edit. A startup `ILiveGameCatalog` build fails fast on duplicate `GameKey` or an `OverlayWidgetKey` with no seeded widget. **Crash-recovery sweep:** on startup, under `IRunOnceGuard`, non-terminal `GameSession` rows are cancelled and refunded (D9).

---

## 8. Testing — prove behavior

Per the project standard: assert state changes, emitted events, and ledger side-effects — never "didn't throw".

- **Engine lifecycle** — drive a fake `ILiveGame` through start→join×N→resolve and assert: `GameSession.Status` transitions `lobby→running→resolving→settled`; `ParticipantCount`; one `GamePlay` row **per awarded participant** with `GameSessionId` set and `Outcome`/`PayoutAmount`/`NetResult` matching the resolution; `LiveGameStartedEvent` + `LiveGameResolvedEvent` (with the right `WinnerCount`/`TotalPaidOut`) on the bus.
- **Entry-fee + settlement atomicity** — with `RequiresEntryFee`, assert each joiner's wallet is debited `spend_game`/`SourceType=live_game`/`SourceId=sessionId` on join and credited `earn_game` on win; a forced fault mid-settlement rolls back **all** credits + `GamePlay` appends (one tx).
- **Min-players + crash refund** — a session that resolves under `MinPlayers` cancels and **fully refunds** every stake (reversing entries); a non-terminal session present at startup is swept, cancelled, and refunded exactly once (idempotent under `IRunOnceGuard`).
- **D7 single-session** — `StartAsync` while a non-terminal session exists fails `SESSION_ALREADY_ACTIVE`.
- **Drop-in proof** — a second fake `ILiveGame` with a distinct `GameKey` is discovered and runnable **without** any engine/registration edit; a duplicate `GameKey` fails the catalog build at startup.
- **Overlay push** — assert the engine emits `IWidgetNotifier.SendWidgetEventAsync` frames with `EventType="game.lobby"/"game.running"/"game.resolved"` and the game's payload on each transition.

---

## 9. Decisions (resolved)

All settled and binding: `ILiveGame` drop-in + one generic engine (D1–D2); config on `GameConfig` keyed by `GameType` (D3); currency via the three economy methods, tagged `live_game`/`sessionId` (D4); overlay via the existing `widget_event` push (D5); start via action, input via one engine subscription (D6); one non-terminal session per channel (D7); fun-money + inherited 18+ gate (D8); crash-safe refunds (D9); schema delta K.9a `GameSession` + `GamePlay.GameSessionId` (D10). Adding a game is three artifacts, zero core edits (§4.1).
