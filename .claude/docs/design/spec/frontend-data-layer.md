# Frontend Data Layer — the query engine

**Status:** Implementable. This is the multi-file, ground-up rebuild of the custom TanStack-Query
client (the single 1,900-line `QueryClient.kt` from the nomercy KMP app), rebuilt **clean,
`commonMain`/`wasmJs`-safe, dependency-injected, and small-file**. It is the server-state layer for
the dashboard — in the no-ViewModel model (`frontend.md` §4), **the QueryClient is the server-state
container** ("its own ViewModel"); screens read it through query hooks.

**Area:** `core/query/` — a **generic, feature-agnostic** cache engine. It knows nothing about
commands, rewards, or any domain (the nomercy version had media-server logic fused in — that is
stripped out; domain logic lives in per-feature hooks, `frontend-structure.md` §3).

**Conventions:** `commonMain`-first, full `wasmJs` parity; explicit types; one public type per file;
package == folder path; AGPL header; coroutines (never block); `ApiResult<T>` (`frontend.md` §3) is
the fetcher contract.

---

## 0. Decisions (binding)

- **Q1 — One injected `QueryClient` per active connection.** A Koin **singleton behind an
  `interface`**, not a global `object` + service-locator (the nomercy anti-pattern). Constructor-
  injected, testable, swappable. On a connection switch (`core/connection`) the cache is cleared and
  re-pointed (different backend = different data).
- **Q2 — TanStack-v5 status model, not boolean soup.** `status ∈ {Pending, Success, Error}` +
  `fetchStatus ∈ {Idle, Fetching}`. Derived booleans (`isLoading = status==Pending && fetchStatus==Fetching`)
  are computed, not stored — kills the nomercy `isLoading/isFetching/isStale/isMutating` drift.
- **Q3 — Hierarchical keys, prefix-matchable, feature-owned.** `QueryKey = List<String>`, canonical
  `join("|")`, exact + prefix match. Key factories live per feature (`<x>Keys`, `frontend-structure.md`
  §2) and **mirror the backend route space** so an invalidation maps to the same key the screen reads.
- **Q4 — Stale-while-revalidate + in-flight dedup (kept) + real GC (new).** Cache returns instantly and
  revalidates in the background; concurrent fetches of one key dedupe to one request; **entries with
  zero observers are evicted `gcTime` after going idle** (observer ref-count) — fixes the nomercy cache
  that grew forever.
- **Q5 — Retry with backoff, only when retryable.** `RetryPolicy.Default = 3 retries`, exponential
  backoff `1s · 2ⁿ` with full jitter, **cap 30 s**; retries network errors, `5xx`, and `429` (honoring
  `Retry-After`); **never** other `4xx`. One policy object (`RetryPolicy.kt`), mirrors the REST client.
- **Q6 — Config-gated refetch triggers via platform seams.** On-mount-if-stale, on-app/tab-focus,
  on-reconnect, on-`refetchInterval`. Focus/online are `expect`/`actual` seams (desktop window focus /
  web `visibilitychange` + `online`) — **replacing the Android `LifecycleOwner` coupling**, which does
  not exist on `wasmJs`.
- **Q7 — Mutations follow the TanStack lifecycle.** `onMutate` (optimistic snapshot) → on error
  **rollback** → `onSuccess`/`onSettled` (invalidate keys). The optimistic helpers (`setData`,
  `updateData`, list add/remove/update) are kept.
- **Q8 — Realtime invalidation shares the key vocabulary.** The backend pushes an invalidation message
  carrying a `QueryKey` **in the same vocabulary the client uses**; the bridge calls
  `invalidate(key)` — **no client-side key remapping** (deletes nomercy's brittle `invalidateFromServer`
  translation). The shared contract is documented in §8.
- **Q9 — Platform-clean.** No `System.currentTimeMillis()`, no `android.util.Log`, no `LifecycleOwner`,
  no `okhttp`. Time, logging, focus, and online are seams (§7); everything else is `commonMain`.
- **Q10 — Performance is a first-class engine concern.** `select`/transform to derive without
  refetch; structural equality to suppress no-op recompositions; `placeholderData`/`keepPreviousData`
  for paging — the "fast & responsive" requirement, by construction.

---

## 1. File decomposition (`core/query/`)

The whole point of the rebuild — one tiny single-purpose file each, replacing the monolith:

| File | Responsibility |
|---|---|
| `QueryKey.kt` | key type, canonicalization, exact/prefix matching |
| `QueryStatus.kt` | `QueryStatus`, `FetchStatus` enums |
| `QueryState.kt` | immutable `QueryState<T>` snapshot + derived booleans |
| `QueryOptions.kt` | per-query options + `QueryDefaults` presets (realtime/standard/static) |
| `RetryPolicy.kt` | retry count, backoff+jitter, retryable classifier |
| `QueryEntry.kt` | **internal** — one cache cell: `StateFlow`, mutexed fetch, dedup, stale check, retry, observer ref-count |
| `QueryCache.kt` | **internal** — entry registry + `gcTime` eviction scheduler |
| `QueryClient.kt` | public **interface** (the API surface) |
| `DefaultQueryClient.kt` | impl: observe/get/set/update, invalidate, prefetch, cancel, remove, clear |
| `Query.kt` | the read **handle** the UI gets (`state` + `refetch()`) |
| `Mutation.kt` | `MutationState`, `Mutation<TData,TVars>` handle + options |
| `time/QueryClock.kt` | clock seam (`interface` + default via `kotlinx.datetime`) |
| `log/QueryLog.kt` | logging seam (`interface` + no-op default) |
| `platform/AppActivity.kt` | `expect` focus + online `Flow`s; `actual` per target |
| `realtime/QueryInvalidationBridge.kt` | SignalR invalidation messages → `queryClient.invalidate` |
| `compose/LocalQueryClient.kt` | composition access to the injected client |
| `compose/UseQuery.kt` | `useQuery` / `useQueries` |
| `compose/UseInfiniteQuery.kt` | pagination over `Page<T>` |
| `compose/UseMutation.kt` | `useMutation` |
| `di/QueryModule.kt` | Koin module: client singleton, bridge, seams |

---

## 2. Core types

```kotlin
@JvmInline value class QueryKey(val segments: List<String>) {
    fun canonical(): String = segments.joinToString("|")
    fun matches(prefix: QueryKey, exact: Boolean): Boolean =
        if (exact) canonical() == prefix.canonical() else canonical().startsWith(prefix.canonical())
    companion object { fun of(vararg s: String): QueryKey = QueryKey(s.toList()) }  // <x>Keys factories return QueryKey
}

interface Subscription { fun dispose() }   // idempotent; releases the gcTime observer ref-count (§3)

enum class QueryStatus { Pending, Success, Error }
enum class FetchStatus { Idle, Fetching }

data class QueryState<out T>(
    val status: QueryStatus = QueryStatus.Pending,
    val fetchStatus: FetchStatus = FetchStatus.Idle,
    val data: T? = null,
    val error: ApiError? = null,
    val isStale: Boolean = true,
    val updatedAt: Long = 0L,
    val isPlaceholder: Boolean = false,
) {
    val isLoading: Boolean get() = status == QueryStatus.Pending && fetchStatus == FetchStatus.Fetching
    val isError: Boolean get() = status == QueryStatus.Error
    val isSuccess: Boolean get() = status == QueryStatus.Success
}

data class QueryOptions<T>(
    val staleTime: Duration = 30.seconds,
    val gcTime: Duration = 5.minutes,
    val refetchOnFocus: Boolean = true,
    val refetchOnReconnect: Boolean = true,
    val refetchInterval: Duration? = null,
    val enabled: Boolean = true,
    val retry: RetryPolicy = RetryPolicy.Default,
    val placeholder: Placeholder<T> = Placeholder.None,   // None | KeepPrevious | Value(T)
    val select: ((T) -> T)? = null,                        // derive/slice without refetch
)

sealed interface Placeholder<out T> {
    data object None : Placeholder<Nothing>
    data object KeepPrevious : Placeholder<Nothing>        // show the prior key's last Success.data on key change
    data class Value<T>(val value: T) : Placeholder<T>
}

data class RetryPolicy(
    val maxRetries: Int = 3,
    val baseDelay: Duration = 1.seconds,                   // backoff = baseDelay · 2^attempt, full jitter
    val maxDelay: Duration = 30.seconds,                   // cap
) {
    fun isRetryable(error: ApiError): Boolean =            // network (status 0) / 5xx / 429 — never other 4xx
        error.status == 0 || error.status in 500..599 || error.status == 429
    companion object { val Default = RetryPolicy() }
}
```

`QueryDefaults` holds the named presets carried over from nomercy (`realtime` 30s/1m, `standard`
5m/10m, `static` 30m/60m + no auto-refetch), **exposed as functions** returning `QueryOptions<T>`
(`QueryDefaults.realtime()`, `.standard()`, `.static()`).

---

## 3. The entry lifecycle (`QueryEntry` + `QueryCache`)

- **One `QueryEntry<T>` per canonical key**, holding a `MutableStateFlow<QueryState<T>>`. The cache is a
  concurrent map keyed by canonical string.
- **`fetch(force)`** is mutex-guarded: if data is fresh (`now - updatedAt < staleTime`) and not forced →
  no-op; if a fetch is in flight → join it (**dedup**); else run the fetcher and fold the result with
  the sibling's exact variants — `ApiResult.Ok(value)` → `QueryState(status=Success, data=value)`,
  `ApiResult.Failure(error)` → `QueryState(status=Error, error=error)` — applying `retry` on retryable
  failures. `now`/`updatedAt` are read from `QueryClock` (§7), never `System.currentTimeMillis()`.
- **Stale-while-revalidate:** a revalidation keeps the old `data` visible (`fetchStatus=Fetching`,
  `status=Success`) so the UI never blanks.
- **Observer ref-count + GC:** `useQuery` mounting calls `entry.subscribe()`, unmount `dispose()`.
  When count hits 0 the cache starts a `gcTime` timer; on expiry with still-0 observers the entry is
  **evicted**. **Races (pinned):** (a) the timer is scheduled on the client scope and measured against
  `QueryClock`; (b) if observers hit 0 while a fetch is in flight, the fetch is **allowed to complete**
  but its result is discarded if the entry was evicted meanwhile; (c) `invalidate(refetch=true)` on a
  **zero-observer** entry only marks it stale — it does **not** start a fetch or reset the GC timer;
  (d) `subscribe()` always cancels a pending GC timer. (This is the nomercy memory leak, fixed.)
- **Cancellation:** a forced refetch cancels the prior in-flight fetch for that key; `CancellationException`
  never lands in `error`.

---

## 4. The client API (`QueryClient`)

```kotlin
interface QueryClient {
    fun <T> observe(key: QueryKey, options: QueryOptions<T>, fetcher: suspend () -> ApiResult<T>): StateFlow<QueryState<T>>
    fun subscribe(key: QueryKey): Subscription          // observer ref-count for gcTime (§3); dispose() on unmount
    fun <T> getData(key: QueryKey): T?
    fun <T> setData(key: QueryKey, data: T)
    fun <T> updateData(key: QueryKey, update: (T?) -> T?)
    suspend fun <T> prefetch(key: QueryKey, options: QueryOptions<T>, fetcher: suspend () -> ApiResult<T>)
    fun invalidate(key: QueryKey? = null, exact: Boolean = false, refetch: Boolean = true)
    fun cancel(key: QueryKey)
    fun remove(key: QueryKey)
    fun clear()                                   // connection switch / logout
    fun <TData, TVars> mutation(options: MutationOptions<TData, TVars>): Mutation<TData, TVars>
}
```

`invalidate` keeps nomercy's exact/prefix + refetch semantics. **`setData`/`updateData` create a
detached entry if the key is absent** (with `QueryDefaults.standard()` gcTime) so optimistic writes
survive; `updateData` returning `null` leaves `data=null` but does **not** evict. The **list helpers are
extension functions** on `QueryClient` in `ListHelpers.kt`, matching by predicate —
`fun <T> QueryClient.updateInList(key, match: (T)->Boolean, update: (T)->T)`,
`addToList(key, item, index = -1)`, `removeFromList(key, match)`. The client holds its own
`CoroutineScope`; **`clear()` cancels it and creates a fresh child scope** so the *same* singleton
instance is reusable after a connection switch / logout-then-login (§9).

## 5. The UI surface (`compose/`)

```kotlin
@Composable
fun <T> useQuery(
    key: QueryKey,
    options: QueryOptions<T> = QueryDefaults.standard(),
    fetcher: suspend () -> ApiResult<T>,
): Query<T> {
    val client: QueryClient = LocalQueryClient.current
    val flow: StateFlow<QueryState<T>> = remember(key.canonical()) { client.observe(key, options, fetcher) }
    DisposableEffect(key.canonical()) {                    // ref-count for GC (Q4) + trigger-if-stale
        val handle = client.subscribe(key); onDispose { handle.dispose() }
    }
    val state: QueryState<T> by flow.collectAsStateWithLifecycle()
    return Query(state) { client.invalidate(key, exact = true) }
}
```

- `Query<T>` = `{ val state; fun refetch() }` — the handle screens render (`state.isLoading/data/error`).
  The composable is **thin**: it binds the injected client to composition (the client is the state
  owner, not the composable). No reflection, explicit Koin access via `LocalQueryClient` — `wasmJs`-safe.
- **On-mount fetch:** `observe()` triggers a force-if-stale fetch on the entry's **first** subscription
  (the `DisposableEffect` ref-counts via `subscribe()`), so a hook always refetches a *stale* entry on
  mount, never a fresh one.
- `useInfiniteQuery` wraps `Page<T>` (from `core/network`; the backend DTO `PaginatedResponse<T>` is
  mapped to `Page<T>` by the facade). It accumulates pages in **one** cache entry, derives the next page
  as `page + 1` while `page * pageSize < total`, exposes `fetchNextPage()` / `hasNextPage`, and uses
  `Placeholder.KeepPrevious` so paging never blanks (Q10).
- `useQueries(specs: List<QuerySpec<*>>): List<Query<*>>` runs many queries (e.g. a dashboard's parallel
  widgets) in one call, where `data class QuerySpec<T>(val key: QueryKey, val options: QueryOptions<T>,
  val fetcher: suspend () -> ApiResult<T>)`.

## 6. Mutations (`Mutation.kt`, `UseMutation.kt`)

```kotlin
data class MutationOptions<TData, TVars>(
    val mutate: suspend (TVars) -> ApiResult<TData>,
    val onMutate: (suspend (TVars) -> Any?)? = null,         // snapshot for optimistic rollback
    val onSuccess: (suspend (TData, TVars) -> Unit)? = null,
    val onError: (suspend (ApiError, TVars, context: Any?) -> Unit)? = null,  // rollback here
    val onSettled: (suspend () -> Unit)? = null,
    val invalidateKeys: List<QueryKey> = emptyList(),
)
```

Flow: `onMutate` snapshots (its return value is the `context`) + optimistically writes via
`setData/updateData` → run `mutate` → on `ApiResult.Failure` call `onError(error, vars, context)` (which
restores the snapshot) → on `ApiResult.Ok` call `onSuccess` → always `onSettled`, then
`invalidate(invalidateKeys)`.

```kotlin
data class MutationState<out TData>(
    val status: QueryStatus = QueryStatus.Pending,   // Pending = idle/in-flight; Success; Error
    val data: TData? = null,
    val error: ApiError? = null,
) { val isPending: Boolean get() = status == QueryStatus.Pending }

interface Mutation<TData, TVars> {
    val state: StateFlow<MutationState<TData>>
    fun mutate(vars: TVars)                              // fire-and-forget
    suspend fun mutateAsync(vars: TVars): ApiResult<TData>
    fun reset()
}

@Composable fun <TData, TVars> useMutation(options: MutationOptions<TData, TVars>): Mutation<TData, TVars>

// sugar over a single-key optimistic mutation:
fun <TData, TVars, T> optimisticUpdate(
    key: QueryKey, update: (T?) -> T?, options: MutationOptions<TData, TVars>,
): MutationOptions<TData, TVars>
```

## 7. Platform seams (the wasm-parity boundary)

**Five seams; only two are `expect`/`actual`.** `QueryClock` and `QueryLog` are plain `commonMain`
interfaces with platform-supplied defaults (not `expect`/`actual`); `AppActivity.focus`/`online` are the
genuine `expect`/`actual` pair; reconnect observes the realtime `HubState`. Everything else is shared:

| Seam | `commonMain` | desktop `actual` | web `actual` |
|---|---|---|---|
| `QueryClock` | `interface { fun nowMs(): Long }` | `kotlinx.datetime` | `kotlinx.datetime` (shared default; interface stays for fake-clock tests) |
| `QueryLog` | `interface` + no-op | bridge to Serilog-style sink | `console` |
| `AppActivity.focus` | `expect fun focusEvents(): Flow<Unit>` | window focus-gained | `visibilitychange` → visible |
| `AppActivity.online` | `expect fun onlineEvents(): Flow<Boolean>` | network reachability | `navigator.onLine` + `online`/`offline` |
| (reconnect) | observes the realtime `HubState` from `core/realtime` | — | — |

`refetchOnFocus`/`refetchOnReconnect` subscribe to these and force-refetch stale entries — the
multiplatform replacement for nomercy's `ON_RESUME` + `invalidateAllStale` sweep.

## 8. Realtime invalidation (`QueryInvalidationBridge`)

The backend already pushes over SignalR (`DashboardHub`, etc.). One server→client message —
`InvalidateMessage{ key: List<String>, exact: Boolean }` (declared in `frontend.md` §3.2, surfaced on
`DashboardHubClient.invalidations`) — carries a `key` in the **same `QueryKey` vocabulary** the client
uses (we own both ends, per `frontend.md` §3 / backend route space). The bridge:

```kotlin
class QueryInvalidationBridge(private val client: QueryClient, private val hub: DashboardHubClient) {
    fun start(scope: CoroutineScope) = scope.launch {
        hub.invalidations.collect { msg -> client.invalidate(QueryKey(msg.key), exact = msg.exact, refetch = true) }
    }
}
```

No translation table (nomercy's `invalidateFromServer` mapping is deleted). The shared key contract is
asserted by a cross-cutting test that the backend's emitted keys and the client's `<x>Keys` factories
agree.

**Backend emit (both ends closed).** The matching emit is the **`CacheInvalidationBroadcaster`**
(`Api/Hubs/Broadcasters/`, specified in `backend-structure.md` §2): it subscribes to mutation /
projection-completed events and pushes `Invalidate{ key, exact }` to the channel's `DashboardHub` group
(e.g. command created → `Invalidate(["commands","list",channelId])`), in this exact `QueryKey`
vocabulary. The message shape above is the binding contract for both ends, and the cross-cutting test
asserts the backend's emitted keys and the client's `<x>Keys` agree.

## 9. Connection-switch behavior

There is **one** `QueryClient` singleton (Q1) for the app's lifetime — its **instance identity does not
change** across switches, so every injected site and `LocalQueryClient` stays valid.
`ConnectionStore.switchTo` (`frontend.md` §6) calls `queryClient.clear()` as a step: `clear()` empties
the cache, cancels the current internal `CoroutineScope`, **and creates a fresh child scope**, so the
same instance works cold against the new base (the injected REST facade already re-points). This
replaces nomercy's `currentServer.collect { clear()/invalidate() }` block — now explicit and testable.

## 10. Testing — prove behavior (project testing standard)

- **Entry lifecycle:** assert stale-while-revalidate (old data stays visible during refetch), in-flight
  dedup (two concurrent `observe`s → one fetcher call), and **GC eviction** (0 observers + `gcTime`
  elapsed via the fake `QueryClock` → entry gone; re-subscribe before expiry → retained).
- **Retry:** a 5xx retries with backoff and succeeds; a 4xx does **not** retry; assert attempt counts.
- **Mutation optimistic path:** `onMutate` writes, `mutate` fails → state rolled back to the snapshot;
  success → `invalidateKeys` refetched.
- **Realtime:** an `Invalidate{key}` message marks the matching entry stale and refetches; a
  non-matching key is untouched.
- **Refetch triggers:** a focus event force-refetches a stale entry but not a fresh one (fake clock).
- **Connection switch:** `clear()` drops all entries and cancels in-flight fetches; the new client
  fetches cold.

## 11. Ported from nomercy — keep / fix ledger

| nomercy behavior | here |
|---|---|
| `QueryKey` list + canonical join + prefix invalidate | **kept** (Q3) |
| stale-while-revalidate + in-flight dedup | **kept** (Q4) |
| optimistic `setData`/`updateData`/list helpers | **kept** (Q6/Q7) |
| named config presets (`realtime`/`standard`/`static`) | **kept** as `QueryDefaults` |
| SignalR push → invalidation | **kept**, generalized (Q8) |
| 1,900-line single file | **fixed** → 20 single-purpose files (§1) |
| Android-only (`Log`, `LifecycleOwner`, `currentTimeMillis`, okhttp) | **fixed** → `commonMain` + 5 seams (§7) |
| global `object QueryClient` + `QueryClientContext.get()` | **fixed** → injected `interface` singleton (Q1) |
| `cacheTime` never evicts (unbounded growth) | **fixed** → `gcTime` + observer ref-count (Q4/§3) |
| boolean state soup (`isLoading/isFetching/isStale/isMutating`) | **fixed** → `status`+`fetchStatus`, derived booleans (Q2) |
| `invalidateFromServer` hardcoded server→client key map | **fixed** → shared key vocabulary, no remap (Q8) |
| `while(true){delay}` polling in hooks | **fixed** → first-class `refetchInterval` (Q6) |
| media-server domain logic inside the engine | **fixed** → engine is generic; domain in feature hooks |
| `@Suppress("UNCHECKED_CAST")` throughout | **reduced** → typed entries; casts isolated to the cache map boundary |

## 12. Decisions (resolved)

All settled and binding: one injected `QueryClient` per connection behind an interface (Q1);
v5 status model (Q2); feature-owned hierarchical keys mirroring backend routes (Q3); SWR + dedup +
`gcTime` eviction (Q4); backoff retry, retryable-only (Q5); seam-driven refetch triggers (Q6);
TanStack mutation lifecycle + optimistic rollback (Q7); shared-vocabulary realtime invalidation, no
remap (Q8); platform-clean with five seams (Q9); performance via `select`/structural-equality/
`keepPreviousData` (Q10). Twenty single-purpose files (§1) replace the monolith.
