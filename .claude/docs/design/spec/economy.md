# Economy — Interface Specification

Implementable spec for the **economy** subsystem: per-channel currency, viewer balances, append-only
ledger, earning rules, store/catalog redemptions, savings jars, mini-games/gambling, and economy
leaderboards. Code from this directly.

Source of truth: locked schema `2026-06-16-database-schema.md` Domain **K** (Economy), plus the K-owned
slices of Domain **L** (LeaderboardConfigs/OptOuts/Snapshots), Domain **O** (EventJournal, ConsentRecords),
and Domain **Q** (TenantSequences). Library choices: `2026-06-16-stack-and-dependencies.md`. This spec
conforms to the resolved cross-cutting decisions in `2026-06-16-decisions-pending-confirmation.md`.

## Binding conventions (every signature below obeys these)

- Namespace root `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait`).
- Fallible operations return `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`). Never null,
  never throw for expected failure. Error codes reuse `BaseController.ResultResponse`'s known set
  (`NOT_FOUND`, `VALIDATION_FAILED`, `FORBIDDEN`, `ALREADY_EXISTS`, `RATE_LIMITED`, `FEATURE_DISABLED`, …)
  plus the economy-specific codes named in §3.
- **Tenant key `BroadcasterId` is `Guid`** (locked schema §1.1 — `ITenantScoped.BroadcasterId` is widened
  `string`→`Guid` as part of this rebuild). All economy entities are `Guid`-tenanted. Subject/account/user
  ids are `Guid`. Amounts are `long` (schema `bigint`). Twitch ids are indexed `string` attribute columns,
  never keys.
- Surrogate PKs are `Guid` via `Guid.CreateVersion7()`; **append-only journals/logs use `long` identity**
  (`CurrencyLedgerEntries.Id`, `JarContributions.Id`, `GamePlays.Id`, `CatalogPurchases.Id`,
  `LeaderboardSnapshots.Id`).
- Repository + `IUnitOfWork`; no raw `DbContext` in controllers. Per-tenant monotonic positions
  (`CurrencyLedgerEntries.TenantPosition`) are app-assigned via `TenantSequences` (Q.3) under a per-tenant
  row lock **in the same transaction** as the ledger insert — never DB auto-increment.
- `[VC:JSON]` columns (`EarningRules.BonusConfigJson`, `GameConfigs.ConfigJson`, `GamePlays.ResultJson`)
  serialize with **Newtonsoft.Json** via an EF `ValueConverter`+`ValueComparer`. `[VC:enum]` columns store
  the short string token (e.g. `EntryType`), not the int.
- Responses are `StatusResponseDto<T>` (`NomNomzBot.Api.Models`) or `PaginatedResponse<T>`; list endpoints
  page via `PageRequestDto` → `PaginationParams` and return `PagedList<T>` from services.
- Controllers: `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/...")]`, `[Authorize]`, inherit
  `BaseController`, return through `ResultResponse` / `GetPaginatedResponse`.
- DI via typed interfaces (NO MediatR, no Roslyn). Soft-delete via `SoftDeletableEntity` + global filter;
  append-only entities carry `CreatedAt` only.

---

## 1. Entities

Defined and **owned** by this subsystem (locked schema — referenced, not redefined here). All implement
`ITenantScoped` (`BroadcasterId : Guid`) and the EF global tenant + soft-delete filters except where the
schema marks `[APPEND-ONLY]` (no `DeletedAt`/`UpdatedAt`) or `[CROSS-TENANT]` (membership-predicate RLS).

| Entity | Schema | Kind | Key fields (abridged — schema is authoritative) |
|---|---|---|---|
| `CurrencyConfig` | K.1 | soft-delete, `Unique(BroadcasterId)` | `Id:Guid`, `BroadcasterId:Guid`, `CurrencyName:string(50)`, `CurrencyNamePlural:string?`, `IconUrl:string?`, `IsEnabled:bool`, `StartingBalance:long`, `MaxBalance:long?`, `DecimalPlaces:int` |
| `EarningRule` | K.1a | soft-delete, `Unique(BroadcasterId,Source)` | `Id:Guid`, `BroadcasterId:Guid`, `Source:string(30)[VC:enum]`, `IsEnabled:bool`, `Rate:long`, `UnitWindowSeconds:int?`, `PerWindowCap:long?`, `PerStreamCap:long?`, `MinRoleLevel:int?`, `ConfigSchemaVersion:int`, `BonusConfigJson:string?[VC:JSON]` |
| `CurrencyAccount` | K.2 | soft-delete, `Unique(BroadcasterId,ViewerUserId)` | `Id:Guid`, `BroadcasterId:Guid`, `ViewerUserId:Guid`, `ViewerTwitchUserId:string(50)[PII-hash]`, `Balance:long`, `LifetimeEarned:long`, `LifetimeSpent:long`, `IsFrozen:bool`, `LastActivityAt:DateTime?` |
| `CurrencyLedgerEntry` | K.3 | **APPEND-ONLY**, `Unique(BroadcasterId,TenantPosition)` | `Id:long`, `BroadcasterId:Guid`, `TenantPosition:long`, `AccountId:Guid`, `ViewerUserId:Guid`, `ViewerTwitchUserId:string[PII-hash]`, `Amount:long` (signed), `BalanceAfter:long`, `EntryType:string(30)[VC:enum]`, `SourceType:string?[VC:enum]`, `SourceId:Guid?`, `RelatedEntryId:long?`, `EventId:Guid?`(FK→EventJournal.EventId), `Reason:string?[PII-scrub]`, `ActorUserId:Guid?` |
| `SavingsJar` | K.4 | soft-delete, **CROSS-TENANT** | `Id:Guid`, `OwnerBroadcasterId:Guid`, `Name:string(100)`, `Description:string?`, `GoalAmount:long?`, `Balance:long`, `IconUrl:string?`, `IsOpen:bool`, `MaxWithdrawalPerChannel:long?` |
| `SavingsJarMembership` | K.5 | soft-delete, `Unique(JarId,MemberBroadcasterId)` | `Id:Guid`, `JarId:Guid`, `MemberBroadcasterId:Guid`, `Role:string(20)[VC:enum]`(owner/partner/viewer), `Status:string(20)[VC:enum]`(pending/accepted/revoked), `ContributionCapPerStream:long?`, `WithdrawalCap:long?`, `InvitedByBroadcasterId:Guid?`, `AcceptedAt:DateTime?` |
| `JarContribution` | K.6 | **APPEND-ONLY** | `Id:long`, `JarId:Guid`, `SourceBroadcasterId:Guid`, `ContributorAccountId:Guid?`, `ContributorUserId:Guid?`, `Amount:long` (signed), `MovementType:string(20)[VC:enum]`(contribute/withdraw), `LedgerEntryId:long?`, `ActorUserId:Guid?` |
| `GameConfig` | K.7 | soft-delete, `Unique(BroadcasterId,GameType)` | `Id:Guid`, `BroadcasterId:Guid`, `GameType:string(30)`, `Category:string(20)`(minigame/gambling), `IsEnabled:bool`, `Requires18Plus:bool`, `MinBet:long?`, `MaxBet:long?`, `HouseEdgePercent:decimal(5,2)?`, `WinChancePercent:decimal(5,2)?`, `PayoutMultiplier:decimal(8,2)?`, `CooldownSeconds:int`, `MaxPlaysPerStream:int?`, `ConfigJson:string?[VC:JSON]`, `Permission:string(20)[VC:enum]` |
| `ViewerAgeConsent` | K.8 | soft-delete, `Unique(BroadcasterId,ViewerUserId)` | `Id:Guid`, `BroadcasterId:Guid`, `ViewerUserId:Guid`, `ViewerTwitchUserId:string[PII-hash]`, `ConsentRecordId:Guid`(FK→ConsentRecords O.5), `Granted:bool`, `ConfirmedAt:DateTime`, `RevokedAt:DateTime?`, `ConfirmationMethod:string(30)` — thin 1:1 cache over `ConsentRecords` (`ConsentType=age_18_gambling`) |
| `GamePlay` | K.9 | **APPEND-ONLY** | `Id:long`, `BroadcasterId:Guid`, `GameConfigId:Guid`, `GameSessionId:Guid?` (null for instant plays; set by live-games session settlement), `PlayerAccountId:Guid`, `PlayerUserId:Guid`, `BetAmount:long`, `Outcome:string(20)[VC:enum]`(win/lose/push/jackpot), `PayoutAmount:long`, `NetResult:long`, `ResultJson:string?[VC:JSON]`, `BetLedgerEntryId:long?`, `PayoutLedgerEntryId:long?` |
| `CatalogItem` | K.10 | soft-delete, `Unique(BroadcasterId,NameNormalized)` | `Id:Guid`, `BroadcasterId:Guid`, `Name:string(100)`, `NameNormalized:string(100)`, `Description:string?`, `SinkType:string(30)`, `Cost:long`, `IconUrl:string?`, `IsEnabled:bool`, `Permission:string(20)[VC:enum]`, `PipelineId:Guid?`, `CooldownSeconds:int`, `CooldownPerUser:bool`, `StockLimit:int?`, `StockRemaining:int?`, `MaxPerViewerPerStream:int?`, `SortOrder:int` |
| `CatalogPurchase` | K.11 | **APPEND-ONLY** | `Id:long`, `BroadcasterId:Guid`, `CatalogItemId:Guid`, `BuyerAccountId:Guid`, `BuyerUserId:Guid`, `CostPaid:long`, `ItemNameSnapshot:string(100)`, `Status:string(20)[VC:enum]`(completed/pending/refunded/failed), `LedgerEntryId:long?`, `InputArgs:string?[PII-scrub]` |
| `LeaderboardConfig` | L.1 | soft-delete | `Id:Guid`, `BroadcasterId:Guid?`, `JarId:Guid?`, `Metric:string(30)`, `Scope:string(20)`(channel/jar), `Period:string(20)`, `IsPublic:bool`, `TopN:int` |
| `LeaderboardOptOut` | L.2 | `Unique(BroadcasterId,ViewerUserId)` | `Id:Guid`, `BroadcasterId:Guid`, `ViewerUserId:Guid`, `ViewerTwitchUserId:string[PII-hash]`, `OptedOutAt:DateTime` |
| `LeaderboardSnapshot` | L.3 | **APPEND-ONLY** | `Id:long`, `LeaderboardConfigId:Guid`, `BroadcasterId:Guid?`, `PeriodKey:string(20)`, `Rank:int`, `SubjectAccountId:Guid?`, `SubjectUserId:Guid?`, `SubjectTwitchUserId:string[PII-hash]`, `DisplayNameSnapshot:string(255)[PII-scrub]`, `Value:long`, `CapturedAt:DateTime` |

Referenced (NOT owned — read/consumed by economy): `EventJournal` (O.1) is the ledger's `EventId` FK and the
projection source; `ConsentRecords` (O.5, owned by GDPR/compliance) is the authoritative store for the
optional 18+ self-confirmation that `ViewerAgeConsent` caches; `TenantSequences` (Q.3) supplies `currency_ledger_position`;
`ChannelCommunityStandings` (B.2) supplies the `LevelValue` for `MinRoleLevel`/`Permission` gates;
`ActionDefinitions` (B.3) supplies the economy `ActionKey` floors. Song-request paid priority is recorded
via `SongRequestItems.CatalogPurchaseId` (L.5, owned by the engagement/music subsystem — economy only
produces the `CatalogPurchase`).

---

## 2. Domain events

All inherit `DomainEventBase` (`NomNomzBot.Domain.Events`: `EventId:string`, `Timestamp:DateTimeOffset`,
`BroadcasterId:string?`). Published via `IEventBus.PublishAsync` / `PublishFireAndForget`. `EventType`
strings align with the schema's `EventJournal.EventType` namespace (`economy.*`). New file:
`NomNomzBot.Domain/Events/EconomyEvents.cs`.

| Event record | EventType | Payload (in addition to base) | Emitted when |
|---|---|---|---|
| `CurrencyCreditedEvent` | `economy.balance.credited` | `Guid AccountId, Guid ViewerUserId, long Amount, long BalanceAfter, string EntryType, string? SourceType, Guid? SourceId, long LedgerEntryId` | A positive ledger entry is committed (earn/jar payout/admin credit) |
| `CurrencyDebitedEvent` | `economy.balance.debited` | `Guid AccountId, Guid ViewerUserId, long Amount, long BalanceAfter, string EntryType, string? SourceType, Guid? SourceId, long LedgerEntryId` | A negative ledger entry is committed (spend/jar contribute/admin debit) |
| `CurrencyEarnedEvent` | `economy.currency.earned` | `Guid AccountId, Guid ViewerUserId, string Source, long Amount, bool Capped` | An earning rule accrues currency; `Capped=true` if a window/stream cap clamped it |
| `LedgerEntryRecordedEvent` | `economy.ledger.recorded` | `long LedgerEntryId, long TenantPosition, Guid AccountId, long Amount, string EntryType` | Any ledger entry committed (audit/projection cursor) |
| `CatalogItemPurchasedEvent` | `economy.catalog.purchased` | `long PurchaseId, Guid CatalogItemId, Guid BuyerUserId, Guid BuyerAccountId, long CostPaid, string SinkType, Guid? PipelineId, string Status` | A catalog purchase completes (after debit) |
| `CatalogPurchaseRefundedEvent` | `economy.catalog.refunded` | `long PurchaseId, Guid CatalogItemId, Guid BuyerUserId, long AmountRefunded, long ReversalLedgerEntryId` | A purchase is refunded (reversing ledger entry) |
| `GamePlayedEvent` | `economy.game.played` | `long GamePlayId, Guid GameConfigId, string GameType, Guid PlayerUserId, long BetAmount, string Outcome, long PayoutAmount, long NetResult` | A mini-game/gamble resolves |
| `JarContributedEvent` | `economy.jar.contributed` | `Guid JarId, Guid SourceBroadcasterId, Guid? ContributorUserId, long Amount, long JarBalanceAfter, long ContributionId` | A jar contribution is committed |
| `JarWithdrawnEvent` | `economy.jar.withdrawn` | `Guid JarId, Guid SourceBroadcasterId, Guid ActorUserId, long Amount, long JarBalanceAfter, long ContributionId` | A jar withdrawal is committed |
| `JarGoalReachedEvent` | `economy.jar.goal_reached` | `Guid JarId, long GoalAmount, long Balance` | A contribution brings `Balance >= GoalAmount` (once per crossing) |
| `SavingsJarInviteSentEvent` | `economy.jar.invite_sent` | `Guid JarId, Guid OwnerBroadcasterId, Guid InvitedBroadcasterId, string Role` | A membership invite is created (status `pending`) |
| `SavingsJarMembershipChangedEvent` | `economy.jar.membership_changed` | `Guid JarId, Guid MemberBroadcasterId, string Status` | Membership accepted/revoked |
| `AgeConsentGrantedEvent` | `economy.consent.age18_granted` | `Guid ViewerUserId, Guid ConsentRecordId, string ConfirmationMethod` | A viewer passes the 18+ gambling gate |
| `AgeConsentRevokedEvent` | `economy.consent.age18_revoked` | `Guid ViewerUserId, Guid ConsentRecordId` | A viewer revokes 18+ consent |

`BroadcasterId` on the base is the `Guid.ToString()` of the tenant (matches existing `DomainEventBase`
string contract). The economy never invents the `EventJournal` row — it sets `CurrencyLedgerEntry.EventId`
to the triggering journal entry where one exists (EventSub-sourced earns), else null (admin/game/spend).

---

## 3. Service interfaces

New files under `NomNomzBot.Application/Services/`. Implementations under
`NomNomzBot.Infrastructure/Services/Economy/`. Every method takes `Guid broadcasterId` and a trailing
`CancellationToken ct = default`. Behavior note = the state change + events + side effects.

Economy-specific error codes (added to the `ResultResponse` map): `INSUFFICIENT_FUNDS`, `ACCOUNT_FROZEN`,
`CURRENCY_DISABLED`, `MAX_BALANCE_EXCEEDED`, `OUT_OF_STOCK`, `ON_COOLDOWN`, `AGE_CONSENT_REQUIRED`,
`GAMBLING_DISABLED`, `BET_OUT_OF_RANGE`, `JAR_NOT_OPEN`, `JAR_CAP_EXCEEDED`, `JAR_MEMBERSHIP_REQUIRED`,
`LEADERBOARD_OPTED_OUT`.

### 3.1 `ICurrencyConfigService` — currency definition + earning rules

```csharp
public interface ICurrencyConfigService
{
    // Returns the channel's currency definition (null-data Result if not yet configured → caller seeds defaults).
    Task<Result<CurrencyConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken ct = default);

    // Upserts CurrencyConfig (Unique(BroadcasterId)); validates name non-empty, StartingBalance>=0,
    // MaxBalance>=StartingBalance when set. Persists; no ledger effect. Returns the saved config.
    Task<Result<CurrencyConfigDto>> UpsertConfigAsync(Guid broadcasterId, UpsertCurrencyConfigRequest request, CancellationToken ct = default);

    // Lists all EarningRules for the channel (one per Source). Read-only.
    Task<Result<IReadOnlyList<EarningRuleDto>>> ListEarningRulesAsync(Guid broadcasterId, CancellationToken ct = default);

    // Upserts one EarningRule by (BroadcasterId, Source); validates Source ∈ allowed set, Rate>=0, caps>=0.
    // Persists the rule (opt-in: IsEnabled defaults false). No ledger effect. Returns saved rule.
    Task<Result<EarningRuleDto>> UpsertEarningRuleAsync(Guid broadcasterId, UpsertEarningRuleRequest request, CancellationToken ct = default);

    // Soft-deletes an EarningRule by id (sets DeletedAt). Idempotent; NOT_FOUND if absent.
    Task<Result> DeleteEarningRuleAsync(Guid broadcasterId, Guid ruleId, CancellationToken ct = default);
}
```

### 3.2 `ICurrencyAccountService` — wallets, balance, and the ledger (the core mutation surface)

```csharp
public interface ICurrencyAccountService
{
    // Gets the viewer's wallet for this channel, creating it lazily (Balance=StartingBalance, one seed
    // ledger entry EntryType=admin_adjust SourceType=account_open) under a tx if absent. Returns the account.
    Task<Result<CurrencyAccountDto>> GetOrCreateAccountAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Reads current balance by folding the ledger via the (BroadcasterId,AccountId,Id) index when the
    // projection is stale, else the CurrencyAccounts.Balance projection. Read-only.
    Task<Result<long>> GetBalanceAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Paginated wallet list for the channel (dashboard "balances" table), ordered by Balance desc by default.
    Task<Result<PagedList<CurrencyAccountDto>>> ListAccountsAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    // THE atomic mutation primitive. Within one tx: locks the account; rejects if IsFrozen
    // (ACCOUNT_FROZEN) or currency disabled (CURRENCY_DISABLED); for credits enforces MaxBalance
    // (MAX_BALANCE_EXCEEDED), for debits enforces Balance+Amount>=0 (INSUFFICIENT_FUNDS); assigns
    // TenantPosition from TenantSequences('currency_ledger_position') under the per-tenant lock; appends
    // ONE CurrencyLedgerEntry with BalanceAfter; updates the account projection (Balance, Lifetime*,
    // LastActivityAt); commits; publishes Currency(Credited|Debited)Event + LedgerEntryRecordedEvent.
    // Amount sign in the command decides credit vs debit. Returns the committed entry.
    // EntryType extension: giveaways.md contributes spend_giveaway (entry-cost debit) and
    // earn_giveaway (currency/pot prize credit) — same ledger primitive, no new behavior.
    // EntryType extension: media-share.md contributes spend_media (clip-submission entry cost) and
    // refund_media (rejected/skipped refund) — same ledger primitive, no new behavior.
    Task<Result<CurrencyLedgerEntryDto>> PostLedgerEntryAsync(Guid broadcasterId, PostLedgerEntryCommand command, CancellationToken ct = default);

    // Moves Amount (>0) between two accounts in the same channel as a linked debit+credit pair
    // (EntryType=transfer, each row's RelatedEntryId pointing at the other) inside one tx. Same guards as
    // PostLedgerEntry on both sides. Returns both entries.
    Task<Result<TransferResultDto>> TransferAsync(Guid broadcasterId, TransferCommand command, CancellationToken ct = default);

    // Broadcaster/admin manual credit or debit (EntryType=admin_adjust). Records ActorUserId + Reason.
    // Same guards. Returns the entry.
    Task<Result<CurrencyLedgerEntryDto>> AdminAdjustAsync(Guid broadcasterId, AdminAdjustCommand command, CancellationToken ct = default);

    // Freezes/unfreezes an account (anti-abuse). Sets IsFrozen; no ledger effect. Returns the account.
    Task<Result<CurrencyAccountDto>> SetFrozenAsync(Guid broadcasterId, Guid viewerUserId, bool frozen, CancellationToken ct = default);

    // Paginated ledger history for one viewer, newest first (TenantPosition desc). Read-only audit view.
    Task<Result<PagedList<CurrencyLedgerEntryDto>>> GetLedgerAsync(Guid broadcasterId, Guid viewerUserId, PaginationParams pagination, CancellationToken ct = default);
}
```

### 3.3 `ICurrencyEarningService` — applies earning rules to engagement/Twitch events

```csharp
public interface ICurrencyEarningService
{
    // Resolves the EarningRule for `source`; no-op success if rule missing/disabled or feature gated off.
    // Computes amount = Rate * units, clamps against PerWindowCap (per UnitWindowSeconds window) and
    // PerStreamCap (cached per stream), applies BonusConfigJson multipliers (sub tier / raid size), enforces
    // MinRoleLevel via ChannelCommunityStandings, runs anti-AFK/anti-bot presence checks for watch_time.
    // On a positive net, calls PostLedgerEntryAsync (EntryType=earn_<source>) and publishes CurrencyEarnedEvent.
    // Returns the amount actually credited (0 when capped/gated). Idempotent per (source, EventId).
    Task<Result<long>> ApplyEarningAsync(Guid broadcasterId, EarnRequest request, CancellationToken ct = default);

    // Batch variant for the watch-time tick (presence sweep): one PostLedgerEntry per eligible viewer in a
    // single tx-batched pass; returns per-viewer credited amounts. Caps applied per viewer.
    Task<Result<IReadOnlyList<EarnResultDto>>> ApplyWatchTimeBatchAsync(Guid broadcasterId, WatchTimeBatchRequest request, CancellationToken ct = default);
}
```

### 3.4 `ICatalogService` — store catalog + redemptions (the spend surface)

```csharp
public interface ICatalogService
{
    Task<Result<PagedList<CatalogItemDto>>> ListItemsAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);
    Task<Result<CatalogItemDto>> GetItemAsync(Guid broadcasterId, Guid itemId, CancellationToken ct = default);

    // Creates a catalog item; sets NameNormalized (lowercased), rejects duplicate name (ALREADY_EXISTS via
    // Unique(BroadcasterId,NameNormalized)); validates Cost>=0, SinkType ∈ allowed set, PipelineId exists when set.
    Task<Result<CatalogItemDto>> CreateItemAsync(Guid broadcasterId, CreateCatalogItemRequest request, CancellationToken ct = default);

    // Partial update (PATCH semantics); re-normalizes name on change; same validation. Returns saved item.
    Task<Result<CatalogItemDto>> UpdateItemAsync(Guid broadcasterId, Guid itemId, UpdateCatalogItemRequest request, CancellationToken ct = default);

    // Soft-deletes a catalog item (DeletedAt). Existing purchases retain their ItemNameSnapshot.
    Task<Result> DeleteItemAsync(Guid broadcasterId, Guid itemId, CancellationToken ct = default);

    // THE redemption flow. In one tx: loads item (must IsEnabled, currency enabled); checks the action's
    // role/permission floor (Permission) via the access service (FORBIDDEN); enforces cooldown
    // (ON_COOLDOWN, per-user when CooldownPerUser), per-viewer-per-stream cap, and stock
    // (OUT_OF_STOCK → decrements StockRemaining); debits Cost via PostLedgerEntryAsync
    // (EntryType=spend_catalog, SourceType=catalog_item, SourceId=itemId) → INSUFFICIENT_FUNDS bubbles up;
    // writes a CatalogPurchase (status=completed, LedgerEntryId set, ItemNameSnapshot, InputArgs);
    // if PipelineId set, enqueues that pipeline for execution (fire-and-forget, not awaited);
    // publishes CatalogItemPurchasedEvent. Returns the purchase. Idempotent per IdempotencyKey when supplied.
    Task<Result<CatalogPurchaseDto>> PurchaseAsync(Guid broadcasterId, PurchaseRequest request, CancellationToken ct = default);

    // Reverses a completed purchase: posts a reversing credit (EntryType=refund_catalog,
    // RelatedEntryId=original), restores stock, sets CatalogPurchase row's Status via a NEW append-only
    // purchase row (status=refunded) — original row stays immutable; publishes CatalogPurchaseRefundedEvent.
    Task<Result<CatalogPurchaseDto>> RefundPurchaseAsync(Guid broadcasterId, long purchaseId, RefundRequest request, CancellationToken ct = default);

    Task<Result<PagedList<CatalogPurchaseDto>>> ListPurchasesAsync(Guid broadcasterId, PurchaseFilter filter, PaginationParams pagination, CancellationToken ct = default);
}
```

### 3.5 `IGameService` — mini-games + gambling (optional 18+ toggle)

> **The "gambling" here is FUN-MONEY only — it is NOT regulated gambling, and there is NO mandatory
> 18+ requirement.** The bet currency is per-channel loyalty/channel currency that **cannot be
> purchased** and **cannot be cashed out**; payouts are non-monetary in-stream "chaos perks" (more
> fun-money, a shoutout, a sound, a role-perk). No real money changes hands → it falls outside
> legally regulated gambling → no KYC, no compliance age-verification, no mandatory gate.
> The `Requires18Plus` flag is therefore an **OPTIONAL, off-by-default, per-game streamer toggle**,
> provided for streamers who *want* one (community vibe, advertiser-friendliness, or personal
> preference) — **not** a legal/compliance requirement. When a streamer leaves it off (the default),
> `PlayAsync` runs the game for anyone with **no** age check and **no** prompt. The 18+ inference +
> consent path (§3.6) engages **only** when a streamer flips `Requires18Plus=true` on that game.

```csharp
public interface IGameService
{
    Task<Result<IReadOnlyList<GameConfigDto>>> ListGamesAsync(Guid broadcasterId, CancellationToken ct = default);

    // Upserts a GameConfig by (BroadcasterId, GameType); validates odds ranges, MinBet<=MaxBet,
    // Category ∈ {minigame,gambling}; gambling games default IsEnabled=false (TOS-sensitive, opt-in).
    // Requires18Plus defaults false (optional streamer toggle — not a compliance requirement).
    Task<Result<GameConfigDto>> UpsertGameAsync(Guid broadcasterId, UpsertGameConfigRequest request, CancellationToken ct = default);

    // Resolves and settles one play in a single tx: loads config (IsEnabled else GAMBLING_DISABLED/
    // FEATURE_DISABLED). The 18+ gate is OFF by default (Requires18Plus=false): when off, NO age check
    // and NO prompt — the game runs for anyone. ONLY if the streamer set Requires18Plus=true does it
    // verify the gate via IAgeConsentService.HasGrantedAsync — passes on affirmative confirmation OR an
    // account-age/Twitch-personnel inference (§3.6); else AGE_CONSENT_REQUIRED (prompt for self-confirm).
    // Then validates bet ∈ [MinBet,MaxBet] (BET_OUT_OF_RANGE); enforces cooldown
    // (ON_COOLDOWN) and MaxPlaysPerStream; debits the bet (EntryType=spend_game) → INSUFFICIENT_FUNDS;
    // rolls the outcome using HouseEdge/WinChance/PayoutMultiplier with a CSPRNG; credits any payout
    // (EntryType=earn_game) in the same tx; appends a GamePlay row linking BetLedgerEntryId/
    // PayoutLedgerEntryId; publishes GamePlayedEvent. Returns the play outcome.
    Task<Result<GamePlayResultDto>> PlayAsync(Guid broadcasterId, PlayGameRequest request, CancellationToken ct = default);

    Task<Result<PagedList<GamePlayDto>>> GetGameHistoryAsync(Guid broadcasterId, GameHistoryFilter filter, PaginationParams pagination, CancellationToken ct = default);

    // Live-games entry stake: debits the entry fee for one joiner via PostLedgerEntryAsync
    // (EntryType=spend_game, SourceType=live_game, SourceId=sessionId). Guards INSUFFICIENT_FUNDS /
    // frozen account. Returns the account + the bet ledger-entry id (the engine stashes it for
    // settlement/refund). Consumed by live-games.md (ILiveGameEngine).
    Task<Result<LiveGameStakeResult>> StakeLiveGameEntryAsync(Guid broadcasterId, LiveGameStakeCommand command, CancellationToken ct = default);

    // Live-games settlement: in ONE IUnitOfWork tx, for each line credits Payout (>0) via
    // PostLedgerEntryAsync (EntryType=earn_game, SourceType=live_game, SourceId=sessionId) and appends a
    // GamePlay (GameSessionId set, BetAmount=Stake, Outcome, PayoutAmount, NetResult,
    // BetLedgerEntryId/PayoutLedgerEntryId linked); publishes GamePlayedEvent per row. Atomic.
    Task<Result<LiveGameSettlementResult>> SettleLiveGameAsync(Guid broadcasterId, LiveGameSettlement settlement, CancellationToken ct = default);

    // Live-games refund (cancel / startup sweep): posts reversing credits (EntryType=refund_game,
    // RelatedEntryId=original) for every spend_game entry with SourceType=live_game + SourceId=sessionId
    // not already settled. Idempotent per session.
    Task<Result> RefundLiveGameAsync(Guid broadcasterId, Guid sessionId, CancellationToken ct = default);
}
```

### 3.6 `IAgeConsentService` — lightweight 18+ toggle (auto-pass + one-tap self-confirm)

This is the **opt-in** path: it runs only when a streamer turned `Requires18Plus=true` on a game (off by
default — §3.5). It is a lightweight **"remember this viewer is 18+ in this channel"** mechanism — an
account-age auto-pass plus a one-tap self-confirm — **not** a GDPR special-category consent ritual.
Age / 18+ status is **regular personal data**, not Art. 9 special-category data (that heavyweight
treatment is reserved for pronouns, handled separately). The `LawfulBasis` / provenance snapshot columns
are kept because they stay useful and honest (they make each auto-pass auditable), but no
"special-category / extra-care / explicit-consent-gated" treatment applies to the age gate.

When the gate is on, it passes an adult through **without** any prompt when their adulthood is provable
from immutable Twitch facts, and keeps every inference visibly distinct from a self-confirmation in the
data model and audit trail (never forged into a `ConsentRecords(age_18_gambling, granted, consent)`
row). The three allowed methods, in precedence order:

```
allowed18Plus =
       HasActiveConsent(age_18_gambling)                       // affirmative consent (LawfulBasis=consent)
    OR account_age ≥ Age18AccountYears                         // inferred_account_age   (MONOTONIC, no TTL)
    OR twitch_type ∈ {staff, admin, global_mod}               // inferred_twitch_personnel (revocable, re-checked)
// else → AGE_CONSENT_REQUIRED (prompt for explicit consent)
```

- **Account-age inference (PRIMARY).** Twitch's ToS sets a hard minimum signup age of **13** (no
  exception), so an account `Age18AccountYears` ≥ the **5-year mathematical floor** (13 + 5) **provably**
  belongs to an adult. The threshold is the configurable constant **`Age18AccountYears`, default `7`** — a
  2-year hedge over the 5y floor against self-attested signup age. Basis: account creation date =
  Helix `Get Users` `created_at` → `Users.CreatedAt` (already surfaced as `{{user.accountage}}`). This
  inference is **MONOTONIC**: `created_at` is immutable, so once `account_age ≥ threshold` it is
  permanently satisfied — there is **no re-verification TTL** (unlike the revocable personnel method).
  Config home: code default `Age18AccountYears = 7`, overridable via `AppSetting` (P.11,
  `Category="economy"`, `Key="age18_account_years"`, `ValueType=int`) — global row (null `BroadcasterId`)
  with optional per-tenant override; never below the 5y floor.
- **Twitch personnel (SECONDARY).** `type ∈ {staff, admin, global_mod}` (Helix `Get Users` `type`) are
  provably 18+ but rare. Status is **revocable**, so unlike account-age it carries a `StatusVerifiedAt`
  re-check timestamp and the snapshotted `InferredFromStatus`; the gate re-reads live `type` and refreshes
  the snapshot rather than trusting a stale value.
- **Affiliate, Partner, and plain broadcaster are EXCLUDED** as adulthood signals — Twitch permits 13–17
  minors to hold and monetize those statuses (with guardian involvement), so none of them implies 18+.
  `Users.BroadcasterType` (`""`\|`affiliate`\|`partner`) is therefore never an inference basis.
- **Fail-closed.** Missing/unknown `created_at` or `type`, or any uncertainty, → no auto-pass; the gate
  returns "not granted" and the caller surfaces `AGE_CONSENT_REQUIRED` (prompt). An inference never
  manufactures certainty it does not have.
- **Lightweight, not KYC.** The 13-floor is itself self-attested at signup, and this gate guards
  fun-money (no purchase in, no cash-out) — so it is a deliberately *lightweight* check, never real-money
  KYC age verification. Inferences stay materialized in the **K.8 cache only** (`ConfirmationMethod` ∈
  {`inferred_account_age`, `inferred_twitch_personnel`}, `LawfulBasis=legitimate_interest`), never written
  to the `ConsentRecords` ledger — so the consent ledger keeps meaning strictly "the human affirmatively
  self-confirmed".

```csharp
public interface IAgeConsentService
{
    // True iff the viewer is allowed through the 18+ gate by ANY of three methods (precedence order):
    //   (1) HasActiveConsent(age_18_gambling) — a granted, non-revoked ViewerAgeConsent whose linked
    //       ConsentRecords row is Status=granted (reads K.8 cache first, falls back to ConsentRecords);
    //   (2) inferred_account_age — Users.CreatedAt age ≥ Age18AccountYears (default 7y; ≥5y is the proven
    //       floor since Twitch min signup age is 13). MONOTONIC, no re-verification;
    //   (3) inferred_twitch_personnel — live Helix `type` ∈ {staff,admin,global_mod}.
    // Affiliate/Partner/broadcaster are NOT adulthood signals (Twitch allows 13-17 minors). FAIL-CLOSED:
    // unknown/missing CreatedAt or type, or any uncertainty → returns false (caller prompts). When (2)/(3)
    // first hold, materializes a distinct K.8 inference row (ConfirmationMethod=inferred_*,
    // LawfulBasis=legitimate_interest, snapshot basis: InferredAccountCreatedAt / InferredFromStatus +
    // StatusVerifiedAt) — NEVER a ConsentRecords(age_18_gambling,granted,consent) row. Read-only w.r.t.
    // ConsentRecords. Idempotent on the K.8 inference row.
    Task<Result<bool>> HasGrantedAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Records consent: creates/updates the authoritative ConsentRecords row (ConsentType=age_18_gambling,
    // LawfulBasis=consent, IP cipher proof) AND the ViewerAgeConsent cache in one tx; publishes
    // AgeConsentGrantedEvent. Idempotent per (BroadcasterId,ViewerUserId). Returns the consent view.
    Task<Result<AgeConsentDto>> GrantAsync(Guid broadcasterId, GrantAgeConsentRequest request, CancellationToken ct = default);

    // Withdraws consent: sets ConsentRecords.Status=withdrawn + cache RevokedAt; publishes
    // AgeConsentRevokedEvent. Idempotent. Returns the consent view.
    Task<Result<AgeConsentDto>> RevokeAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);
}
```

### 3.7 `ISavingsJarService` — pooled cross-channel accounts (federation-trust gated)

All jar mutations enforce the **membership predicate** (`JarId ∈ accepted SavingsJarMemberships for the
acting tenant`) — the cross-tenant RLS guard (schema §4) — before any balance change. A partner can never
withdraw beyond its `WithdrawalCap` / `MaxWithdrawalPerChannel`.

```csharp
public interface ISavingsJarService
{
    // Creates a jar owned by broadcasterId + the owner SavingsJarMembership (Role=owner, Status=accepted)
    // in one tx. Returns the jar.
    Task<Result<SavingsJarDto>> CreateJarAsync(Guid broadcasterId, CreateSavingsJarRequest request, CancellationToken ct = default);

    Task<Result<SavingsJarDto>> GetJarAsync(Guid broadcasterId, Guid jarId, CancellationToken ct = default);

    // Jars this tenant owns or has an accepted membership in. Read-only.
    Task<Result<IReadOnlyList<SavingsJarDto>>> ListJarsForChannelAsync(Guid broadcasterId, CancellationToken ct = default);

    // Owner invites a partner channel: creates a SavingsJarMembership (Status=pending) with caps;
    // publishes SavingsJarInviteSentEvent. JAR_MEMBERSHIP_REQUIRED if caller isn't owner. Returns membership.
    Task<Result<SavingsJarMembershipDto>> InviteChannelAsync(Guid broadcasterId, InviteChannelRequest request, CancellationToken ct = default);

    // Invited channel accepts its own pending membership (mutual-consent federation): Status=accepted,
    // AcceptedAt set; publishes SavingsJarMembershipChangedEvent. Returns membership.
    Task<Result<SavingsJarMembershipDto>> AcceptMembershipAsync(Guid broadcasterId, Guid membershipId, CancellationToken ct = default);

    // Owner or self revokes a membership: Status=revoked; publishes SavingsJarMembershipChangedEvent.
    Task<Result> RevokeMembershipAsync(Guid broadcasterId, Guid membershipId, CancellationToken ct = default);

    // Contributes Amount(>0) from a viewer's channel account into the jar in one tx: verifies membership +
    // jar IsOpen (JAR_NOT_OPEN) + ContributionCapPerStream (JAR_CAP_EXCEEDED); debits the viewer account
    // (EntryType=jar_contribute) → INSUFFICIENT_FUNDS; increments SavingsJar.Balance; appends an audited
    // JarContribution (MovementType=contribute, LedgerEntryId set); publishes JarContributedEvent and
    // JarGoalReachedEvent when GoalAmount is crossed. Returns the movement.
    Task<Result<JarMovementDto>> ContributeAsync(Guid broadcasterId, JarContributeRequest request, CancellationToken ct = default);

    // Withdraws Amount(>0) from the jar back to a channel account in one tx: verifies membership +
    // WithdrawalCap + MaxWithdrawalPerChannel (JAR_CAP_EXCEEDED) + jar Balance>=Amount; decrements
    // SavingsJar.Balance; credits the target account (EntryType=jar_withdraw); appends an audited
    // JarContribution (MovementType=withdraw, ActorUserId set); publishes JarWithdrawnEvent. Returns the movement.
    Task<Result<JarMovementDto>> WithdrawAsync(Guid broadcasterId, JarWithdrawRequest request, CancellationToken ct = default);

    // Paginated audited movement log for a jar (federation audit view). Read-only.
    Task<Result<PagedList<JarMovementDto>>> GetJarHistoryAsync(Guid broadcasterId, Guid jarId, PaginationParams pagination, CancellationToken ct = default);
}
```

### 3.8 `IEconomyLeaderboardService` — rankings (channel + jar), opt-out respected

```csharp
public interface IEconomyLeaderboardService
{
    Task<Result<IReadOnlyList<LeaderboardConfigDto>>> ListConfigsAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<LeaderboardConfigDto>> UpsertConfigAsync(Guid broadcasterId, UpsertLeaderboardConfigRequest request, CancellationToken ct = default);
    Task<Result> DeleteConfigAsync(Guid broadcasterId, Guid configId, CancellationToken ct = default);

    // Computes a live ranking for the config's Metric/Period/Scope, EXCLUDING viewers with a
    // LeaderboardOptOut row; returns the top TopN (or `top` override) entries. Read-only.
    Task<Result<IReadOnlyList<LeaderboardEntryDto>>> GetRankingAsync(Guid broadcasterId, Guid configId, int? top, CancellationToken ct = default);

    // Freezes the current standings for a closed period into append-only LeaderboardSnapshots (one row per
    // rank). Returns the captured rows. Called by the period-close background job.
    Task<Result<IReadOnlyList<LeaderboardEntryDto>>> CaptureSnapshotAsync(Guid broadcasterId, Guid configId, string periodKey, CancellationToken ct = default);

    // GDPR opt-out: upserts a LeaderboardOptOut for the viewer (excluded from all live rankings going
    // forward). Idempotent. Also writes the ConsentRecords leaderboard_opt_in=withdrawn marker via GDPR svc.
    Task<Result> OptOutAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);

    // Removes the opt-out (re-includes the viewer). Idempotent.
    Task<Result> OptInAsync(Guid broadcasterId, Guid viewerUserId, CancellationToken ct = default);
}
```

---

## 4. DTOs / contracts

New file `NomNomzBot.Application/DTOs/Economy/EconomyDtos.cs` (responses) and
`NomNomzBot.Application/DTOs/Economy/EconomyRequests.cs` (requests). Records, `Nullable` enabled.

### Responses

```csharp
public sealed record CurrencyConfigDto(Guid Id, Guid BroadcasterId, string CurrencyName, string? CurrencyNamePlural, string? IconUrl, bool IsEnabled, long StartingBalance, long? MaxBalance, int DecimalPlaces, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record EarningRuleDto(Guid Id, string Source, bool IsEnabled, long Rate, int? UnitWindowSeconds, long? PerWindowCap, long? PerStreamCap, int? MinRoleLevel, int ConfigSchemaVersion, IReadOnlyDictionary<string, object?>? BonusConfig);

public sealed record CurrencyAccountDto(Guid Id, Guid ViewerUserId, string ViewerTwitchUserId, long Balance, long LifetimeEarned, long LifetimeSpent, bool IsFrozen, DateTime? LastActivityAt);

public sealed record CurrencyLedgerEntryDto(long Id, long TenantPosition, Guid AccountId, Guid ViewerUserId, long Amount, long BalanceAfter, string EntryType, string? SourceType, Guid? SourceId, long? RelatedEntryId, Guid? EventId, string? Reason, Guid? ActorUserId, DateTime CreatedAt);

public sealed record TransferResultDto(CurrencyLedgerEntryDto Debit, CurrencyLedgerEntryDto Credit);

public sealed record CatalogItemDto(Guid Id, string Name, string? Description, string SinkType, long Cost, string? IconUrl, bool IsEnabled, string Permission, Guid? PipelineId, int CooldownSeconds, bool CooldownPerUser, int? StockLimit, int? StockRemaining, int? MaxPerViewerPerStream, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CatalogPurchaseDto(long Id, Guid CatalogItemId, Guid BuyerUserId, Guid BuyerAccountId, long CostPaid, string ItemNameSnapshot, string Status, long? LedgerEntryId, string? InputArgs, DateTime CreatedAt);

public sealed record GameConfigDto(Guid Id, string GameType, string Category, bool IsEnabled, bool Requires18Plus, long? MinBet, long? MaxBet, decimal? HouseEdgePercent, decimal? WinChancePercent, decimal? PayoutMultiplier, int CooldownSeconds, int? MaxPlaysPerStream, string Permission, IReadOnlyDictionary<string, object?>? Config);

public sealed record GamePlayDto(long Id, Guid GameConfigId, Guid PlayerUserId, long BetAmount, string Outcome, long PayoutAmount, long NetResult, DateTime CreatedAt);

public sealed record GamePlayResultDto(long Id, string GameType, string Outcome, long BetAmount, long PayoutAmount, long NetResult, long BalanceAfter, IReadOnlyDictionary<string, object?>? Result);

// Live-games ↔ economy currency-domain contracts (consumed by live-games.md §3.3).
public sealed record LiveGameStakeCommand(Guid SessionId, Guid ViewerUserId, long Amount);
public sealed record LiveGameStakeResult(CurrencyAccountDto Account, long BetLedgerEntryId);
public sealed record LiveGameSettlement(Guid SessionId, IReadOnlyList<LiveGameSettlementLine> Lines);
public sealed record LiveGameSettlementLine(Guid UserId, Guid AccountId, long Stake, long BetLedgerEntryId, string Outcome, long Payout);  // Outcome ∈ win|lose|push|jackpot
public sealed record LiveGameSettlementResult(int WinnerCount, long TotalPaidOut, IReadOnlyList<long> GamePlayIds);

public sealed record AgeConsentDto(Guid ViewerUserId, Guid ConsentRecordId, bool Granted, DateTime ConfirmedAt, DateTime? RevokedAt, string ConfirmationMethod);

public sealed record SavingsJarDto(Guid Id, Guid OwnerBroadcasterId, string Name, string? Description, long? GoalAmount, long Balance, string? IconUrl, bool IsOpen, long? MaxWithdrawalPerChannel, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SavingsJarMembershipDto(Guid Id, Guid JarId, Guid MemberBroadcasterId, string Role, string Status, long? ContributionCapPerStream, long? WithdrawalCap, Guid? InvitedByBroadcasterId, DateTime? AcceptedAt);

public sealed record JarMovementDto(long Id, Guid JarId, Guid SourceBroadcasterId, Guid? ContributorUserId, long Amount, string MovementType, long JarBalanceAfter, long? LedgerEntryId, Guid? ActorUserId, DateTime CreatedAt);

public sealed record EarnResultDto(Guid ViewerUserId, long AmountCredited, bool Capped);

public sealed record LeaderboardConfigDto(Guid Id, Guid? BroadcasterId, Guid? JarId, string Metric, string Scope, string Period, bool IsPublic, int TopN, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record LeaderboardEntryDto(int Rank, Guid? SubjectUserId, Guid? SubjectAccountId, string DisplayName, long Value);
```

### Requests / commands

```csharp
public sealed record UpsertCurrencyConfigRequest(string CurrencyName, string? CurrencyNamePlural, string? IconUrl, bool IsEnabled, long StartingBalance, long? MaxBalance, int DecimalPlaces);

public sealed record UpsertEarningRuleRequest(string Source, bool IsEnabled, long Rate, int? UnitWindowSeconds, long? PerWindowCap, long? PerStreamCap, int? MinRoleLevel, IReadOnlyDictionary<string, object?>? BonusConfig);

// Sign of Amount decides credit (+) vs debit (-). EntryType MUST be one of the [VC:enum] tokens.
public sealed record PostLedgerEntryCommand(Guid ViewerUserId, long Amount, string EntryType, string? SourceType, Guid? SourceId, Guid? EventId, string? Reason, Guid? ActorUserId, string? IdempotencyKey);

public sealed record TransferCommand(Guid FromViewerUserId, Guid ToViewerUserId, long Amount, string? Reason, Guid? ActorUserId);

public sealed record AdminAdjustCommand(Guid ViewerUserId, long Amount, string Reason, Guid ActorUserId);

public sealed record EarnRequest(Guid ViewerUserId, string Source, long Units, Guid? EventId, int? ViewerRoleLevel, IReadOnlyDictionary<string, object?>? Context);

public sealed record WatchTimeBatchRequest(IReadOnlyList<WatchTimeViewer> Viewers, int WindowSeconds, Guid? StreamId);
public sealed record WatchTimeViewer(Guid ViewerUserId, int PresentSeconds, bool PresenceVerified, int RoleLevel);

public sealed record CreateCatalogItemRequest(string Name, string? Description, string SinkType, long Cost, string? IconUrl, bool IsEnabled, string Permission, Guid? PipelineId, int CooldownSeconds, bool CooldownPerUser, int? StockLimit, int? MaxPerViewerPerStream, int SortOrder);
public sealed record UpdateCatalogItemRequest(string? Name, string? Description, long? Cost, string? IconUrl, bool? IsEnabled, string? Permission, Guid? PipelineId, int? CooldownSeconds, bool? CooldownPerUser, int? StockLimit, int? MaxPerViewerPerStream, int? SortOrder);

public sealed record PurchaseRequest(Guid ItemId, Guid BuyerUserId, string? InputArgs, int RoleLevel, string? IdempotencyKey);
public sealed record RefundRequest(string Reason, Guid ActorUserId);
public sealed record PurchaseFilter(Guid? CatalogItemId, Guid? BuyerUserId, string? Status);

public sealed record UpsertGameConfigRequest(string GameType, string Category, bool IsEnabled, bool Requires18Plus, long? MinBet, long? MaxBet, decimal? HouseEdgePercent, decimal? WinChancePercent, decimal? PayoutMultiplier, int CooldownSeconds, int? MaxPlaysPerStream, string Permission, IReadOnlyDictionary<string, object?>? Config);
public sealed record PlayGameRequest(Guid GameConfigId, Guid PlayerUserId, long BetAmount, int RoleLevel);
public sealed record GameHistoryFilter(Guid? GameConfigId, Guid? PlayerUserId, string? Outcome);

public sealed record GrantAgeConsentRequest(Guid ViewerUserId, string ConfirmationMethod, string? IpAddress, string? ConsentVersion);

public sealed record CreateSavingsJarRequest(string Name, string? Description, long? GoalAmount, string? IconUrl, bool IsOpen, long? MaxWithdrawalPerChannel);
public sealed record InviteChannelRequest(Guid JarId, Guid InvitedBroadcasterId, string Role, long? ContributionCapPerStream, long? WithdrawalCap);
public sealed record JarContributeRequest(Guid JarId, Guid ContributorUserId, long Amount);
public sealed record JarWithdrawRequest(Guid JarId, Guid TargetViewerUserId, long Amount, Guid ActorUserId);

public sealed record UpsertLeaderboardConfigRequest(Guid? Id, string Metric, string Scope, string Period, bool IsPublic, int TopN, Guid? JarId);
```

---

## 5. Controller endpoints

New controllers under `NomNomzBot.Api/Controllers/V1/`, all `[ApiVersion("1.0")]`, inherit `BaseController`,
`[Authorize]`, route through `ResultResponse`/`GetPaginatedResponse`. Tenant `{channelId}` is resolved to
`Guid broadcasterId` by tenant middleware + `IChannelAccessService` (caller must control the channel).

**Role gate** (schema B.3 `ActionDefinitions`: per-action `AuthPlane` ∈ {`Community`,`Management`} + seeded
`FloorLevel`). The keys below are seeded global `ActionDefinitions`; a broadcaster may raise a floor via
`ChannelActionOverride` but never below the seeded `FloorLevel`. The gate runs in two stages:
- **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's).
- **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the
  per-route floor named in the Action-key column before the service call (403 FORBIDDEN when below). The
  effective caller level is `MAX(community standing, management role, active !permit grant)` over the unified
  ladder spanning the community and management planes.
- **Plane C** (platform IAM, `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)`, where
  the `[Authorize(Policy="<key>")]` policy name IS the permission key verbatim) governs cross-tenant/privileged
  platform ops — none in this subsystem.

The per-action `AuthPlane` tag records which plane the floor sits in: **management** = dashboard config writes
(currency config, earning rules, catalog CRUD, game config, jar admin, leaderboard config, admin adjust, freeze,
refund) floored on a canonical PascalCase `ManagementRole` (`Moderator` for read/mod-tier writes, `Broadcaster`
for owner-tier writes); **community** = viewer-initiated actions (purchase, play, jar contribute, balance read,
age consent) floored on the per-item/per-game `Permission` (`CatalogItem.Permission` / `GameConfig.Permission`)
resolved against `ChannelCommunityStandings.LevelValue`, default `Everyone`.

### CurrencyController — `api/v{version}/channels/{channelId}/economy`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/config` | — | `StatusResponseDto<CurrencyConfigDto>` | management / Moderator · `economy:config:read` |
| PUT | `/config` | `UpsertCurrencyConfigRequest` | `StatusResponseDto<CurrencyConfigDto>` | management / Broadcaster · `economy:config:write` |
| GET | `/earning-rules` | — | `StatusResponseDto<IReadOnlyList<EarningRuleDto>>` | management / Moderator · `economy:earning-rules:read` |
| PUT | `/earning-rules` | `UpsertEarningRuleRequest` | `StatusResponseDto<EarningRuleDto>` | management / Broadcaster · `economy:earning-rules:write` |
| DELETE | `/earning-rules/{ruleId}` | — | `StatusResponseDto<object>` | management / Broadcaster · `economy:earning-rules:delete` |
| GET | `/accounts` | `PageRequestDto` | `PaginatedResponse<CurrencyAccountDto>` | management / Moderator · `economy:accounts:read` |
| GET | `/accounts/{viewerUserId}` | — | `StatusResponseDto<CurrencyAccountDto>` | community / Moderator · `economy:account:read` (self-or-Gate-2) |
| GET | `/accounts/{viewerUserId}/ledger` | `PageRequestDto` | `PaginatedResponse<CurrencyLedgerEntryDto>` | community / Moderator · `economy:ledger:read` (self-or-Gate-2) |
| POST | `/accounts/{viewerUserId}/adjust` | `AdminAdjustCommand` | `StatusResponseDto<CurrencyLedgerEntryDto>` | management / Broadcaster · `economy:account:adjust` |
| POST | `/accounts/{viewerUserId}/freeze` | `{ bool frozen }` | `StatusResponseDto<CurrencyAccountDto>` | management / Moderator · `economy:account:freeze` |
| POST | `/transfer` | `TransferCommand` | `StatusResponseDto<TransferResultDto>` | management / Broadcaster · `economy:transfer:write` |

### CatalogController — `.../economy/catalog`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | `PageRequestDto` | `PaginatedResponse<CatalogItemDto>` | community / Everyone · `economy:catalog:read` |
| GET | `/{itemId}` | — | `StatusResponseDto<CatalogItemDto>` | community / Everyone · `economy:catalog:read` |
| POST | `/` | `CreateCatalogItemRequest` | `StatusResponseDto<CatalogItemDto>` (201) | management / Moderator · `economy:catalog:create` |
| PATCH | `/{itemId}` | `UpdateCatalogItemRequest` | `StatusResponseDto<CatalogItemDto>` | management / Moderator · `economy:catalog:update` |
| DELETE | `/{itemId}` | — | `StatusResponseDto<object>` | management / Moderator · `economy:catalog:delete` |
| POST | `/{itemId}/purchase` | `PurchaseRequest` | `StatusResponseDto<CatalogPurchaseDto>` | community / Everyone · `economy:catalog:purchase` (`CatalogItem.Permission` CommunityStanding, default Everyone) |
| GET | `/purchases` | `PurchaseFilter`+`PageRequestDto` | `PaginatedResponse<CatalogPurchaseDto>` | management / Moderator · `economy:catalog:purchases:read` |
| POST | `/purchases/{purchaseId}/refund` | `RefundRequest` | `StatusResponseDto<CatalogPurchaseDto>` | management / Broadcaster · `economy:catalog:refund` |

> **Music bump redeems** (see `music-sr.md` §3.8) are ordinary `CatalogItem` rows — a `music_queue_jump` `SinkType` with a `PipelineId` that performs the bump. They are **opt-in, off by default**: the streamer creates them (e.g. a *"bump my song"* item). On purchase, the resulting `CatalogPurchase.Id` is what `music-sr` records as `SongRequestItem.CatalogPurchaseId` to place the request in the bump band. The song-request **raffle** (`music-sr.md` §3.11, a *chance* at a bump) is a **separate** sink — the entry fee spends points and records its `CatalogPurchaseId` on the raffle entry, not on a song. No always-on paid lane exists.

### GamesController — `.../economy/games`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<IReadOnlyList<GameConfigDto>>` | community / Everyone · `economy:games:read` |
| PUT | `/` | `UpsertGameConfigRequest` | `StatusResponseDto<GameConfigDto>` | management / Broadcaster · `economy:games:write` |
| POST | `/{gameConfigId}/play` | `PlayGameRequest` | `StatusResponseDto<GamePlayResultDto>` | community / Everyone · `economy:games:play` (`GameConfig.Permission` CommunityStanding, default Everyone; +18 gate via IAgeConsentService if `Requires18Plus`) |
| GET | `/history` | `GameHistoryFilter`+`PageRequestDto` | `PaginatedResponse<GamePlayDto>` | management / Moderator · `economy:games:history:read` |
| GET | `/consent/{viewerUserId}` | — | `StatusResponseDto<bool>` | community / Moderator · `economy:consent:read` (self-or-Gate-2) |
| POST | `/consent` | `GrantAgeConsentRequest` | `StatusResponseDto<AgeConsentDto>` | community / Everyone · `economy:consent:write` (self-or-Gate-2) |
| DELETE | `/consent/{viewerUserId}` | — | `StatusResponseDto<object>` | community / Moderator · `economy:consent:revoke` (self-or-Gate-2) |

### SavingsJarsController — `.../economy/jars`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<IReadOnlyList<SavingsJarDto>>` | management / Moderator · `economy:jars:read` |
| POST | `/` | `CreateSavingsJarRequest` | `StatusResponseDto<SavingsJarDto>` (201) | management / Broadcaster · `economy:jars:create` |
| GET | `/{jarId}` | — | `StatusResponseDto<SavingsJarDto>` | management / Moderator · `economy:jars:read` (jar-membership scope check) |
| POST | `/{jarId}/invite` | `InviteChannelRequest` | `StatusResponseDto<SavingsJarMembershipDto>` | management / Broadcaster · `economy:jars:invite` (jar-owner scope check) |
| POST | `/memberships/{membershipId}/accept` | — | `StatusResponseDto<SavingsJarMembershipDto>` | management / Broadcaster · `economy:jars:membership:accept` (invited-channel scope check) |
| DELETE | `/memberships/{membershipId}` | — | `StatusResponseDto<object>` | management / Broadcaster · `economy:jars:membership:revoke` (jar-owner-or-self scope check) |
| POST | `/{jarId}/contribute` | `JarContributeRequest` | `StatusResponseDto<JarMovementDto>` | community / Everyone · `economy:jars:contribute` (member-channel scope check) |
| POST | `/{jarId}/withdraw` | `JarWithdrawRequest` | `StatusResponseDto<JarMovementDto>` | management / Broadcaster · `economy:jars:withdraw` (owner/partner-within-cap scope check) |
| GET | `/{jarId}/history` | `PageRequestDto` | `PaginatedResponse<JarMovementDto>` | management / Moderator · `economy:jars:history:read` (jar-membership scope check) |

### EconomyLeaderboardsController — `.../economy/leaderboards`

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/configs` | — | `StatusResponseDto<IReadOnlyList<LeaderboardConfigDto>>` | management / Moderator · `economy:leaderboards:config:read` |
| PUT | `/configs` | `UpsertLeaderboardConfigRequest` | `StatusResponseDto<LeaderboardConfigDto>` | management / Broadcaster · `economy:leaderboards:config:write` |
| DELETE | `/configs/{configId}` | — | `StatusResponseDto<object>` | management / Broadcaster · `economy:leaderboards:config:delete` |
| GET | `/{configId}` | `?top=` | `StatusResponseDto<IReadOnlyList<LeaderboardEntryDto>>` | community / Everyone · `economy:leaderboards:read` (Everyone if `IsPublic`, else Moderator) |
| POST | `/opt-out/{viewerUserId}` | — | `StatusResponseDto<object>` | community / Everyone · `economy:leaderboards:opt-out` (self-or-Gate-2) |
| POST | `/opt-in/{viewerUserId}` | — | `StatusResponseDto<object>` | community / Everyone · `economy:leaderboards:opt-in` (self-or-Gate-2) |

---

## 6. Pipeline actions

New file `NomNomzBot.Infrastructure/Pipeline/Actions/EconomyActions.cs`, each implementing the **single
canonical `ICommandAction`** defined in `commands-pipelines.md` §3.13 (`Application/Pipeline`): `string Type`
(+ `Category`/`Description` for the editor); `Task<ActionResult> ExecuteAsync(ActionContext context,
CancellationToken ct)`. Params read from `context.Parameters` (the step's resolved `ConfigJson`). These let
economy ride the existing command/event pipeline (e.g. `!balance`, reward-driven payouts). All resolve the
viewer via `context.TriggeredByUserId` and the tenant via `context.BroadcasterId` (already a `Guid` — no parse).
(The pre-consolidation Infrastructure `ICommandAction` shape — `ActionType`/`ExecuteAsync(PipelineExecutionContext,
ActionDefinition)` — is collapsed away per commands-pipelines §0; do not target it.)

| Action | `ActionType` | Config params | Behavior |
|---|---|---|---|
| `GrantCurrencyAction` | `grant_currency` | `amount:int` (or template), `reason:string?`, `source_type:string?` | Credits the triggering viewer via `ICurrencyAccountService.PostLedgerEntryAsync` (EntryType=earn_pipeline). Output = new balance. Fails closed if currency disabled. |
| `DeductCurrencyAction` | `deduct_currency` | `amount:int`, `reason:string?` | Debits the viewer; `ActionResult.Failure` (stops pipeline) on `INSUFFICIENT_FUNDS`. Output = new balance. |
| `CheckBalanceAction` | `check_balance` | `min:int?`, `set_var:string?` | Reads balance; writes it into `ctx.Variables[set_var ?? "balance"]`; `Failure` when `min` set and balance<min (gates downstream steps). |
| `PlayGameAction` | `play_game` | `game_type:string`, `bet:int` (or template) | Calls `IGameService.PlayAsync`; writes `outcome`/`payout`/`net` into `ctx.Variables`; applies the optional 18+ gate only when `Requires18Plus=true` + config. Fails closed if game disabled/unknown. |
| `JarContributeAction` | `jar_contribute` | `jar_id:string(guid)`, `amount:int` | Contributes the viewer's currency to a jar via `ISavingsJarService.ContributeAsync` (membership + caps enforced). Output = jar balance after. |

Registered transient (stateless) alongside the existing `ICommandAction` block. Action keys are also surfaced
in the pipeline-builder UI catalog, which the frontend subsystem owns and renders from this action set.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, in the "Application services" block (scoped — all
consume `IApplicationDbContext`/repositories/`IUnitOfWork`). Implementations in
`NomNomzBot.Infrastructure/Services/Economy/`.

```csharp
// Economy — application services (scoped: use DbContext + UnitOfWork)
services.AddScoped<ICurrencyConfigService, CurrencyConfigService>();
services.AddScoped<ICurrencyAccountService, CurrencyAccountService>();
services.AddScoped<ICurrencyEarningService, CurrencyEarningService>();
services.AddScoped<ICatalogService, CatalogService>();
services.AddScoped<IGameService, GameService>();
services.AddScoped<IAgeConsentService, AgeConsentService>();
services.AddScoped<ISavingsJarService, SavingsJarService>();
services.AddScoped<IEconomyLeaderboardService, EconomyLeaderboardService>();

// Economy — per-tenant monotonic ledger position allocator (scoped: row-locks TenantSequences in-tx)
services.AddScoped<ITenantSequenceAllocator, TenantSequenceAllocator>();

// Economy — repositories (scoped) — extend GenericRepository<T>
services.AddScoped<CurrencyAccountRepository>();
services.AddScoped<CurrencyLedgerRepository>();
services.AddScoped<CatalogRepository>();
services.AddScoped<SavingsJarRepository>();

// Economy — pipeline actions (transient: stateless), in the existing ICommandAction block
services.AddTransient<ICommandAction, GrantCurrencyAction>();
services.AddTransient<ICommandAction, DeductCurrencyAction>();
services.AddTransient<ICommandAction, CheckBalanceAction>();
services.AddTransient<ICommandAction, PlayGameAction>();
services.AddTransient<ICommandAction, JarContributeAction>();
```

**Deployment-profile adapters** (one boot-time `App__DeploymentMode` switch — schema §4, stack §1):
- `ITenantSequenceAllocator` — **same interface, profile-specific impl**: `PostgresTenantSequenceAllocator`
  (`SELECT … FOR UPDATE` on `TenantSequences`) vs `SqliteTenantSequenceAllocator` (`BEGIN IMMEDIATE`
  write-lock). Selected by `DeploymentProfile.DbProvider` in DI — the **only** economy code that is
  provider-specific; everything else is one EF model across both providers.
- Cross-tenant `SavingsJar*` isolation: Postgres profile adds the membership-predicate RLS interceptor; lite
  (SQLite) relies on the EF membership-predicate query filter alone (same predicate code path,
  `RlsEnabled=false`). No separate economy impl — the persistence layer's profile adapter handles it.
- No economy service needs Redis/Wasmtime/Jint/KMS branches; it inherits `ICacheService` (HybridCache
  L1/L1+L2) and `IEventBus` (in-proc / Redis) transparently from the platform DI.

`[VC:JSON]`/`[VC:enum]` `ValueConverter`+`ValueComparer` registrations for `BonusConfigJson`, `ConfigJson`,
`ResultJson`, and every enum-token column go in the economy EF `IEntityTypeConfiguration<T>` classes under
`NomNomzBot.Infrastructure/Persistence/Configurations/` (one per entity), reusing the shared portable
Newtonsoft.Json converter convention (schema §1.4) — **not** `HasColumnType("jsonb")`.

---

## 8. Dependencies (from the stack doc)

Economy is **near-100% in-box / 2nd-party** — it earns no new third-party dependency.

| Need | Library (party) | Use |
|---|---|---|
| ORM, entities, two-provider model | `Microsoft.EntityFrameworkCore` 10.0.9 + named query filters (2nd) | All economy tables; soft-delete + tenant + membership-predicate filters |
| Postgres provider (SaaS) | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2 (3rd, accepted) | SaaS profile only; RLS on `SavingsJar*` |
| SQLite provider (lite) | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9 + `SQLitePCLRaw.bundle_e_sqlite3 ≥ 3.0.3` (2nd) | Self-host profile; `BEGIN IMMEDIATE` sequence lock |
| App JSON (`[VC:JSON]`) | **Newtonsoft.Json** (binding project rule for app JSON) | `BonusConfigJson`, `ConfigJson`, `ResultJson` converters |
| Caching (balance projection, cooldowns, per-stream caps) | `Microsoft.Extensions.Caching.Hybrid` 10.7.0 (2nd) via `ICacheService` | L1 (lite) / L1+Redis-L2 (SaaS); stampede-safe cap counters |
| Domain events | in-proc `IEventBus` (lite) / `RedisEventBus` (SaaS) — existing platform code (1st/2nd) | `economy.*` event publication |
| CSPRNG (game outcomes) | `System.Security.Cryptography.RandomNumberGenerator` (in-box, 1st) | Fair slot/duel/coinflip rolls — never `System.Random` for money |
| Validation | in-box `.NET 10 AddValidation()` source generator + service-layer `Result<T>` validators (2nd/1st) | Request DTO validation + async uniqueness/cap rules |
| Resilience | inherited; economy makes no outbound HTTP | — |
| Tests | `xunit.v3` 3.2.2 + `AwesomeAssertions` 9.4.0 + `NSubstitute` 5.3.0; Testcontainers Postgres for the RLS/jar-isolation + ledger-ordering subset (3rd, SaaS test subset) | Cross-tenant jar leak, ledger monotonicity/double-spend, AAD non-transplant, fail-closed gambling gate |

No MediatR, no Roslyn, no MassTransit, no Quartz/Hangfire (the period-close + watch-time accrual run on the
existing `BackgroundService` + `PeriodicTimer`, guarded by `IRunOnceGuard` on multi-instance SaaS).

---

## 9. Decisions (resolved)

Two cross-subsystem alignment decisions, both binding:

1. **`BroadcasterId` is `Guid` across the entire economy subsystem.** The locked schema (§1.1) mandates the
   `string`→`Guid` widening of `ITenantScoped.BroadcasterId` as part of this rebuild, so the economy
   interfaces are coded to the `Guid` target and stay that way. This depends on the tenant-key widening:
   while live `ITenantScoped.BroadcasterId` is still `string` and existing services (e.g. `IRewardService`)
   take `string broadcasterId`, a thin `Guid.Parse` adapter at the controller boundary bridges the gap. The
   economy interfaces are never weakened back to `string`.
2. **Economy ledger PII protection is row-level `[PII-hash]`/`[PII-scrub]`, not per-event crypto-shred.** No
   economy event is multi-subject (gift-sub/raid live in the engagement domain), so per-event crypto-shred
   does not apply here; ledger subject PII is protected as `[PII-hash]`/`[PII-scrub]` row data. Subject-level
   erasure of that data is delivered through the shared GDPR subsystem (decisions doc #10), which the economy
   ledger consumes rather than reimplements.
