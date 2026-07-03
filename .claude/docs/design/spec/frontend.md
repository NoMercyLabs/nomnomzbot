# Frontend — Interface Specification

**Status:** Implementable. Build the dashboard from this directly.
**Subsystem:** The NomNomzBot dashboard — one **Kotlin Multiplatform (KMP) + Compose Multiplatform** codebase shipping the **identical** app to **JVM desktop** and **web (wasmJs)** (Android/iOS later). Profile-agnostic, direct-connect: REST (v1) + SignalR are reached through one typed shared client; there is no broker. Public viewer/OBS pages (song-request, overlays, OAuth landing) are **not** this app — they are the lightweight `web/` pages and are out of scope here.

## Grounding & locked decisions (binding)

- **One codebase, two first-class targets.** Desktop (`jvm`) and web (`wasmJs`) build from the **same** `commonMain` — the web build is a full dashboard, not a cut-down view. Every decision below is constrained by **wasmJs parity**: no JVM-only shortcut (reflection, `Locale.setDefault`, raw sockets) may leak into `commonMain`.
- **Profile-agnostic, direct-connect.** The app needs only a backend **base URL** and talks REST + SignalR straight to it — no central orchestrator, so a self-host bot needs zero NoMercy infrastructure. **Native is multi-origin** (a saved-connection switcher fed by mDNS LAN discovery + manual add; switching swaps the active backend + its keychain token and reconnects). **Web is single-origin** — it only talks to the origin that served it (`window.location.origin`); no host picker, mDNS is a no-op.
- **The typed shared client is the *only* integration point.** Screens fetch/mutate exclusively through it (REST + SignalR). No screen constructs an `HttpClient`, URL, or hub connection ad hoc.
- **i18n: `en` + `nl`, never hardcode user-facing strings.** Compose Multiplatform resources; runtime locale switch without restart.
- **shadcn/ui (new-york) is the design source of truth** — ported 1:1 to Compose; fully specified in `frontend-design-system.md`. The previous Figma file is discarded (it did not represent a viable dashboard); a fresh Figma, if ever minted, is derived *from* this spec, never the reverse. The OKLCH token contract, component catalogue, and the dynamic chat-color accent live in `frontend-design-system.md` (§8 below is a summary).
- **Codegen for external contracts** (matches the backend's NSwag-for-Helix rule): REST DTOs + endpoint stubs are **generated from the backend v1 OpenAPI document**, committed, and hand-wrapped. SignalR has no schema → hand-authored.
- **Kotlin/Compose house style.** Explicit types, `commonMain`-first, feature packages (never a `misc`/`utils` dump), one responsibility per file, UDF state. AGPL header on every source file (`//` line comments).

---

## 1. Module & source-set structure

The KMP project lives under `app/` (per repo layout). **One** Compose module, `:composeApp`, holds all targets — a separate `:shared` module is added only if a non-Compose consumer ever appears (YAGNI until then). Targets now: `jvm` (desktop), `wasmJs` (web). `androidTarget` / iOS frameworks are added later without touching `commonMain`.

```
app/
├── settings.gradle.kts                      # includes :composeApp
├── gradle/libs.versions.toml                # version catalog (the §10 coordinate set)
├── build.gradle.kts
└── composeApp/
    ├── build.gradle.kts                     # kotlin { jvm(); wasmJs { browser() } }, compose, serialization, openapi-gen task
    └── src/
        ├── commonMain/
        │   ├── kotlin/bot/nomnomz/dashboard/
        │   │   ├── App.kt                    # root composable: theme + connection gate + NavHost
        │   │   ├── core/
        │   │   │   ├── network/              # Ktor client config, auth, ApiResult mapping, facades
        │   │   │   │   └── generated/        # OpenAPI-generated DTOs + raw endpoint stubs (committed)
        │   │   │   ├── realtime/             # hand-rolled SignalR-over-WebSockets client + typed hub clients
        │   │   │   ├── connection/           # ConnectionProfile, store, mDNS (expect), token vault (expect)
        │   │   │   ├── di/                   # Koin modules
        │   │   │   ├── navigation/           # route graph (@Serializable routes), top-level shell
        │   │   │   └── designsystem/         # shadcn OKLCH tokens/theme + component/ + pattern/ + icon/  (core also holds query/ + i18n/ — see frontend-structure.md §1)
        │   │   └── feature/
        │   │       ├── setup/                # first-run wizard (connect Twitch, connect bot, basics)
        │   │       ├── dashboard/            # home widgets + live chat feed
        │   │       ├── commands/             # command CRUD + pipeline attach
        │   │       ├── pipeline/             # visual pipeline builder
        │   │       ├── community/  moderation/  rewards/  timers/
        │   │       ├── widgets/              # overlay/widget management
        │   │       ├── integrations/         # Spotify/Discord/YouTube/TTS
        │   │       └── settings/             # incl. connection switcher, language
        │   └── composeResources/
        │       ├── values/strings.xml        # en (default)
        │       ├── values-nl/strings.xml     # nl
        │       └── drawable/  font/
        ├── jvmMain/kotlin/.../               # main.kt (window), Ktor CIO engine, Swing dispatcher,
        │                                     #   OS-vault token store, NSD/JmDNS discovery, loopback OAuth
        └── wasmJsMain/kotlin/.../            # main.kt (canvas), Ktor JS engine, sessionStorage token store,
                                              #   no-op discovery, redirect OAuth; resources/index.html
```

**Platform source sets stay thin** — only `actual` implementations of the `expect` seams in §6 (token vault, discovery, OAuth launcher, Ktor engine, main dispatcher). All UI, state-holders, navigation, the query engine (`core/query`), i18n (`core/i18n`), and the client live in `commonMain` (authoritative tree: `frontend-structure.md` §1).

---

## 2. Stack — the locked library set

| Concern | Library | Coordinate (version) | wasmJs note |
|---|---|---|---|
| UI + targets | Compose Multiplatform | `org.jetbrains.compose` (CMP plugin) | first-class |
| Navigation | AndroidX Navigation Compose | `org.jetbrains.androidx.navigation:navigation-compose:2.9.2` | ✅ type-safe `@Serializable` routes |
| State collection | Lifecycle-aware `Flow` collection | `org.jetbrains.androidx.lifecycle:lifecycle-runtime-compose:2.10.0` | ✅ — `collectAsStateWithLifecycle`; **no ViewModel** (state lives in the QueryClient + state-holders) |
| DI | Koin | `io.insert-koin:koin-core` + `koin-compose` (4.x) | ✅ — explicit constructor wiring |
| REST | Ktor client | `io.ktor:ktor-client-core:3.5.0` (+ engines below) | engine per target |
| REST engine (desktop) | Ktor CIO | `io.ktor:ktor-client-cio:3.5.0` (jvmMain) | — |
| REST engine (web) | Ktor JS/Fetch | `io.ktor:ktor-client-js:3.5.0` (wasmJsMain) | ✅ Fetch-backed |
| Content negotiation | Ktor + kotlinx JSON | `io.ktor:ktor-client-content-negotiation:3.5.0`, `io.ktor:ktor-serialization-kotlinx-json:3.5.0` | ✅ |
| Serialization | kotlinx.serialization | `org.jetbrains.kotlinx:kotlinx-serialization-json` (≥1.7) | ✅ |
| Realtime (SignalR) | **hand-rolled** over Ktor WebSockets | `io.ktor:ktor-client-websockets:3.5.0` (commonMain) | ✅ one impl, both targets |
| Realtime (native fallback) | SignalRKore | `eu.lepicekmichal.signalrkore:signalrkore:0.9.13` | jvm/android/ios only — **fallback only** |
| Resources / i18n | Compose resources | built into the CMP Gradle plugin (`compose.resources`) | ✅ `values-nl/`, async load |
| Coroutines (desktop main) | kotlinx-coroutines-swing | `org.jetbrains.kotlinx:kotlinx-coroutines-swing` (jvmMain) | n/a |
| LAN discovery (native) | JmDNS | `org.jmdns:jmdns` (jvmMain) | no-op on web |

> **Why hand-rolled SignalR, not SignalRKore everywhere.** SignalRKore (the only mature Kotlin SignalR lib) has **no wasmJs target**, and the MS Java client is JVM-only and heavyweight. The hub JSON protocol is small and stable, so a single `commonMain` implementation (handshake → `0x1E`-framed invocation/completion/ping) gives **identical desktop+web behavior from one file** — the cleanest parity play (§3.2). SignalRKore stays a pinned fallback for native if hand-rolling slips.

---

## 3. The typed shared backend client (`core/network`, `core/realtime`)

The single integration surface. Two halves: **REST** (request/response) and **SignalR** (push). Both consume the **active `ConnectionProfile`** (§6) for base URL + bearer token; both react to a profile switch by re-targeting.

### 3.1 REST

- **Generated layer (`core/network/generated/`).** A Gradle task runs `openapi-generator` (`generatorName=kotlin`, `library=multiplatform`) against the backend's published v1 OpenAPI document, emitting Kotlin `@Serializable` DTOs + raw endpoint stubs into a committed folder. Regenerated on contract change; **never hand-edited** (`// <auto-generated />` first line). This mirrors the backend's NSwag-generated Helix client — external contracts are generated, not transcribed.
- **Hand-written facade (`core/network/`).** Per-subsystem typed API interfaces (`AuthApi`, `CommandsApi`, `PipelinesApi`, `ModerationApi`, `RewardsApi`, …) wrap the generated stubs, returning **`ApiResult<T>`** (single) or **`ApiResult<Page<T>>`** (paginated) — mirroring the backend envelopes:

```kotlin
package bot.nomnomz.dashboard.core.network

// Mirrors the backend StatusResponseDto<T> / PaginatedResponse<T> / RFC-7807 problem details.
sealed interface ApiResult<out T> {
    data class Ok<T>(val value: T) : ApiResult<T>
    data class Failure(val error: ApiError) : ApiResult<Nothing>
}

data class ApiError(
    val status: Int,                 // HTTP status
    val code: String?,               // problem-details "type"/code or backend error code
    val message: String,             // human message (already localized server-side where applicable)
    val traceId: String?,
    val retryAfter: Duration? = null, // parsed Retry-After on 429, consumed by the query-engine retry policy
)

data class Page<T>(val items: List<T>, val page: Int, val pageSize: Int, val total: Long)
```

- **One shared `HttpClient`**, configured in `commonMain` (ContentNegotiation+JSON, default request base URL from the active profile, `Authorization: Bearer <jwt>` via an auth plugin, timeouts, a 401→refresh-once interceptor against the backend session). The **engine** (`CIO` jvm / `Js` wasm) is the only platform piece.
- **Auth refresh.** On 401 the client calls the auth-refresh endpoint once (refresh token from the token vault), retries; a second 401 clears the active token and routes to the setup/connect screen. Matches identity-auth's rotation model.

### 3.2 SignalR (`core/realtime`)

A hand-authored client speaking the **SignalR JSON Hub Protocol over a Ktor `WebSocket`** — WebSockets-only (the backend hubs assume WS; skip the long-polling/SSE fallback).

- **Connection sequence.** `GET {base}/hubs/{hub}?access_token=<jwt>` upgraded to WS → send the handshake frame `{"protocol":"json","version":1}` terminated by the record separator `0x1E` → await the empty handshake response → then exchange messages. Every message is UTF-8 JSON terminated by `0x1E`; the reader splits the stream on `0x1E`.
- **Message types handled:** `1` Invocation (server→client hub method), `3` Completion, `6` Ping (send `{"type":6}` every **15 s**; treat **30 s** of inbound silence as a drop → reconnect), `7` Close (surface reason + trigger reconnect). `2` StreamInvocation / `4` StreamItem / `5` CancelInvocation are **unused** — ignore if received, never sent. Client→server invocations are `type:1` with `target` + `arguments`.
- **Reconnect/backoff.** Exponential backoff with jitter (`1s·2ⁿ`, **cap 30 s**); on reconnect, re-join groups (e.g. `JoinWidget`) and resubscribe. Connection state is a `StateFlow<HubState>` (`Connecting | Connected | Reconnecting | Disconnected`) the UI surfaces.
- **Typed hub clients** wrap the raw connection, exposing cold `Flow`s per server event and suspend functions per server method:

```kotlin
package bot.nomnomz.dashboard.core.realtime

enum class HubState { Connecting, Connected, Reconnecting, Disconnected }

// Server-event DTOs (ChatMessageDto, DashboardStatsDto, AlertDto, TtsSpeakPayload) are OpenAPI-generated
// where the backend exposes them, else hand-authored @Serializable types in core/realtime.
@Serializable data class InvalidateMessage(val key: List<String>, val exact: Boolean = false)

interface DashboardHubClient {                 // /hubs/dashboard
    val state: StateFlow<HubState>
    val chatMessages: Flow<ChatMessageDto>     // server "ChatMessage" invocation
    val statsUpdates: Flow<DashboardStatsDto>
    val alerts: Flow<AlertDto>
    val invalidations: Flow<InvalidateMessage> // server "Invalidate" → query-cache invalidation (frontend-data-layer.md §8)
    suspend fun start(); suspend fun stop()
}

interface OverlayHubClient {                   // /hubs/overlay — OverlayToken auth, not user JWT
    val state: StateFlow<HubState>
    suspend fun joinWidget(widgetId: String)
    suspend fun leaveWidget(widgetId: String)
    val ttsSpeak: Flow<TtsSpeakPayload>        // owned by widgets-overlays.md §7, consumed here
}
```

Hubs: `DashboardHub` `/hubs/dashboard`, `OverlayHub` `/hubs/overlay`, `OBSRelayHub` `/hubs/obs`, `AdminHub` `/hubs/admin` (admin client gated on platform-IAM principals). Token passed as `?access_token=<jwt>` (overlay uses the channel `OverlayToken`).

---

## 4. Presentation architecture

**No ViewModels — the `QueryClient` is the server-state container, with state-holders for local state
and Stores for global state** (the owner's decision; detailed in `frontend-data-layer.md` and
`frontend-structure.md` §2). Three state homes, one responsibility each:

- **Server state → query hooks.** Screens call `useQuery(key) { api.… }` / `useMutation { … }`
  (`core/query`), which read/write the **injected `QueryClient`** cache (stale-while-revalidate,
  dedup, push-invalidation, optimistic writes). A hook returns a `Query<T>` the composable renders
  (`isLoading`/`data`/`error`). The QueryClient *is* the view-model for server data.
- **Local/ephemeral state → Compose + state-holders.** Trivial UI state is `remember` /
  `mutableStateOf`; when a screen's logic outgrows the composable, a plain **state-holder** class
  (`feature/<x>/state/`, exposing `StateFlow` + functions — **not** an androidx `ViewModel`) owns it.
- **Global state → Stores.** Long-lived cross-screen state (active connection, session, locale, active
  channel) lives in injected `Store` singletons (`StateFlow`).
- **UDF + DI.** Data flows down as params, events up as lambdas. Koin modules per feature + a
  `coreModule`; explicit constructor wiring, no reflection (`wasmJs`-safe). Placement: `frontend-structure.md`.

```kotlin
// A screen reads server state through a hook — no ViewModel.
@Composable
fun CommandsScreen() {
    val commands: Query<List<CommandDto>> = useCommands()          // feature/commands/data
    val mutations: CommandMutations = useCommandMutations()
    when {
        commands.state.isLoading -> Skeleton()
        commands.state.isError   -> ErrorState(commands.state.error)
        else -> CommandList(commands.state.data.orEmpty(), onToggle = mutations::toggle)
    }
}
```

---

## 5. Navigation / routing

**Navigation Compose** with **type-safe `@Serializable` route objects** (no string routes). A single `NavHost` lives in the top-level shell; a **connection/auth gate** wraps it.

- **Gate order (in `App.kt`):** (1) no active `ConnectionProfile` → **Connect** screen (pick/add backend; web auto-creates the single-origin profile). (2) profile present but no streamer account configured (probe `GET /api/v1/system/setup` → `{ streamerConfigured: Boolean }`) → **Setup wizard** graph. (3) otherwise → **Main shell** graph.
- **Route graph (sealed):**

```kotlin
@Serializable sealed interface Route {
    // ── Entry gates ───────────────────────────────────────────────────────────
    @Serializable data object Connect : Route
    @Serializable data object Setup : Route                       // nested: ConnectTwitch → ConnectBot → Basics

    // ── Main shell (Plane B) — grouped in the sidebar per frontend-ia.md §3 ────
    @Serializable data object Dashboard : Route                   // Home
    @Serializable data object Commands : Route                    // Chat
    @Serializable data class  PipelineEditor(val pipelineId: String?) : Route   // null = new
    @Serializable data object Timers : Route
    @Serializable data object Moderation : Route
    @Serializable data object Rewards : Route                     // Loyalty — "Channel Points"
    @Serializable data object Economy : Route
    @Serializable data object Games : Route
    @Serializable data object SongRequests : Route                // Media
    @Serializable data object Tts : Route
    @Serializable data object Widgets : Route                     // Stream — "Overlays"
    @Serializable data class  WidgetEditor(val widgetId: String?) : Route       // code editor; null = new
    @Serializable data object Alerts : Route                      // "Alerts & Events"
    @Serializable data object Analytics : Route
    @Serializable data object Community : Route                   // "Viewers"
    @Serializable data object Integrations : Route                // pinned
    @Serializable data object Settings : Route                    // pinned

    // ── Admin area (Plane C) — gated graph, frontend-ia.md §6 ──────────────────
    @Serializable data object Admin : Route                       // root → Tenants
    @Serializable data class  AdminTenant(val tenantId: String) : Route
    @Serializable data object AdminFeatureFlags : Route
    @Serializable data object AdminBilling : Route
    @Serializable data object AdminIamPrincipals : Route
    @Serializable data object AdminIamRoles : Route
    @Serializable data object AdminAuditLog : Route
    @Serializable data object AdminAnalytics : Route
}
```

- The **main shell** is a persistent left-nav + content `NavHost`; top-level destinations are the grouped page inventory in **`frontend-ia.md` §3** (Home · Chat · Loyalty · Media · Stream · Community + pinned Integrations/Settings — 17 pages). The platform **Admin** graph (`Admin*` routes) is a separate gated graph (`frontend-ia.md` §6), reached from the profile menu only for Plane-C principals. The sealed `Route` hierarchy lives centrally in `core/navigation/`; each feature contributes a `fun NavGraphBuilder.<x>Graph()` that wires its routes into the single `NavHost` — the linter fails the build on a `Route` declared but never wired (`frontend-structure.md` §4). Deep params (e.g. `PipelineEditor.pipelineId`, `WidgetEditor.widgetId`, `AdminTenant.tenantId`) ride the type-safe route. Back-stack is per-shell; entering/exiting the Admin graph swaps the shell chrome.
- Desktop and web share the identical graph; the web build maps routes to the browser URL/history via the wasmJs browser navigation integration so links/refresh work.

---

## 6. Connection / profile model (the direct-connect heart)

The feature that makes one app serve self-host + SaaS + LAN. Five `expect`/`actual` seams; everything else is shared.

```kotlin
package bot.nomnomz.dashboard.core.connection

data class ConnectionProfile(
    val id: String,                 // uuid
    val displayName: String,        // "My self-host", "NomNomz SaaS", discovered name
    val baseUrl: String,            // https://api… or http://192.168.x.x:5080 or window.origin
    val source: ProfileSource,      // Manual | Discovered | ServedOrigin
)
enum class ProfileSource { Manual, Discovered, ServedOrigin }

data class SessionTokens(
    val accessToken: String,
    val refreshToken: String?,      // null on web — refresh rides the backend HttpOnly cookie
    val expiresAt: Long?,           // epoch ms; null = unknown → refresh on 401
)
enum class OAuthFlow { Streamer, Bot }   // maps to the backend `state`: user / channel_bot

interface SessionStore {            // the signed-in identity for the active connection (core/connection)
    val userId: StateFlow<String?>  // Twitch user id from the session; null = signed out
    val chatColor: StateFlow<String?>   // the signed-in user's chat color (default theme subject, §8)
}

interface ConnectionStore {                 // active profile + saved list + token wiring
    val active: StateFlow<ConnectionProfile?>
    val saved: StateFlow<List<ConnectionProfile>>
    suspend fun switchTo(profileId: String) // swaps active → reloads token from vault → reconnects REST+SignalR
    suspend fun add(profile: ConnectionProfile)
    suspend fun remove(profileId: String)
}

expect class TokenVault {                   // per-target secure custody of the JWT/refresh token, keyed by profile
    suspend fun read(profileId: String): SessionTokens?
    suspend fun write(profileId: String, tokens: SessionTokens)
    suspend fun clear(profileId: String)
}

expect class LanDiscovery {                 // mDNS — native only
    fun discovered(): Flow<ConnectionProfile>   // _nomnomz._tcp services on the LAN; web returns emptyFlow()
}

expect class OAuthLauncher {                // start the Twitch OAuth dance, return the resulting session
    suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens>
}
```

- **Token custody (the `TokenVault` actual)** — matches the backend secret-custody rule (OS-native vault):
  - **Desktop:** OS keychain — Windows **DPAPI**, macOS **Keychain**, Linux **libsecret** (via the platform actual). Refresh + access tokens at rest, per profile.
  - **Web (wasmJs):** the build is served **first-party by its own bot** (single origin), so the short-lived **access token lives in `sessionStorage`** (cleared on tab close) and the refresh token rides the backend session/`HttpOnly` cookie set on callback — the app never persists a long-lived secret in JS. Documented XSS caveat; acceptable for a first-party origin.
- **mDNS (the `LanDiscovery` actual):** desktop browses `_nomnomz._tcp` and surfaces discovered bots as `Discovered` profiles in the switcher (zero-friction LAN onboarding); web returns `emptyFlow()` (no-op). Self-host bots advertise the service (backend concern).
- **Web single-origin:** on first load the wasmJs build synthesizes a `ServedOrigin` profile from `window.location.origin`, marks it active, and **hides the switcher** (no host picker, mDNS no-op). To use another bot's web dashboard you open that bot's URL.
- **OAuth (the `OAuthLauncher` actual):**
  - **Desktop:** RFC-8252 **loopback** — bind a transient listener on an **OS-assigned ephemeral port** on `127.0.0.1`, open the system browser to `{base}/api/v1/auth/twitch/login?client=desktop&redirect=http://127.0.0.1:<port>/cb`. **Backend contract:** for `client=desktop` the backend whitelists any `http://127.0.0.1:<port>/cb` loopback redirect (RFC-8252 §7.3) — separate from the single registered HTTPS callback — and returns the one-time code/JWT there. The listener captures it; exchange → `SessionTokens` → vault. Bot-account auth reuses the flow with `OAuthFlow.Bot`.
  - **Web:** standard same-origin redirect to the backend login; the backend completes the dance and returns the session to the served origin.
- **Switching** (`switchTo`) atomically: set active profile → load its tokens from the vault → tear down the current REST client base + all hub connections → **`queryClient.clear()`** (drop cached server state) → re-point to the new base → reconnect. Surfaced as a profile-menu action (native only).
- **Web `ConnectionStore`:** single-origin — `saved` holds only the served-origin profile and `active` is always it; `add`/`remove`/`switchTo` are **no-ops** (switcher hidden). The interface is common; only the native impl is multi-origin.

---

## 7. i18n

- **Compose Multiplatform resources** (`Res`, `stringResource`) — first-party, Wasm-supported; **not** moko. `composeResources/values/strings.xml` (en) + `values-nl/strings.xml` (nl). No user-facing string is hardcoded — every label/format goes through `stringResource(Res.string.key)`; keys are dotted by feature (`commands.add.title`).
- **Runtime locale switch** (Settings → Language) drives a **Compose environment / locale override provided via composition** (not `Locale.setDefault`, which won't recompose on web) so the change re-renders without restart on both desktop and web. Selected locale is persisted in app prefs.
- **Async load caveat (web):** resources resolve asynchronously on wasmJs — guard first-frame `painterResource`/string reads so the web build never flashes empty content the desktop build wouldn't.

---

## 8. Design system (`core/designsystem`)

Fully specified in `frontend-design-system.md` (the style guide). In brief:

- **shadcn/ui (new-york) is the source of truth**, ported 1:1 to Compose — a closed **OKLCH** token
  contract, a closed component catalogue (variants-as-data), each component on the most-correct
  primitive (Material3-wrapped or Compose Foundation, correctness-first). Figma is **not** canonical.
- **Neutral base + a dynamic accent derived from the *current theme subject's* Twitch chat color** —
  you by default, the viewed broadcaster/viewer on their page — applied subtly app-wide (light + dark,
  crossfaded) via a deterministic OKLCH function (`frontend-design-system.md` §2–§3).
- A single `NomNomzTheme { }` provides `LocalTokens` (+ spacing/typography); screens never hardcode a
  hex or `dp` — a detekt linter enforces it. Icons come from the designer's pack (`IconKey`/`IconSet`).

---

## 9. Testing — prove behavior (per the project testing standard)

Surface/smoke tests are void; each test must fail if the behavior breaks.

- **Query hooks + the QueryClient** — drive the engine and assert the **resulting `QueryState` sequence** (Pending → Success with the right data *shape*, or → the right `ApiError`) against a fake API: stale-while-revalidate, dedup, retry, optimistic rollback, `gcTime` eviction (`frontend-data-layer.md` §10). Assert the data shape (fields/invariants), not "non-null".
- **REST facade** — run against a Ktor `MockEngine`: assert the request (method, path, `Authorization` header, query/body) **and** that envelopes map correctly to `ApiResult.Ok`/`Failure` (including a 401→refresh→retry path that actually re-issues with the new token).
- **SignalR client** — the highest-risk net-new code gets a protocol round-trip test against a fake WebSocket: assert the handshake frame bytes (incl. the `0x1E` terminator), that an inbound `type:1` invocation surfaces on the correct typed `Flow` with the right payload, that a `type:6` ping is answered, and that a drop triggers backoff + group re-join.
- **Navigation gate** — assert the gate routes to Connect / Setup / Main for each of (no profile) / (profile, no streamer) / (configured).
- **Connection switch** — assert `switchTo` reloads the right token and that the client/base + hub targets actually re-point (observed via the mock engine + fake hub).

---

## 10. Dependencies (the coordinate set)

| Dependency | Party | Use |
|---|---|---|
| `org.jetbrains.compose` (Gradle plugin) | 3rd (Apache-2.0) | Compose Multiplatform UI + `compose.resources`. |
| `org.jetbrains.androidx.navigation:navigation-compose:2.9.2` | 3rd (Apache-2.0) | Type-safe navigation. |
| `org.jetbrains.androidx.lifecycle:lifecycle-runtime-compose:2.10.0` | 3rd (Apache-2.0) | `collectAsStateWithLifecycle` (no ViewModel). |
| `io.insert-koin:koin-core` + `koin-compose` (4.x) | 3rd (Apache-2.0) | DI. |
| `io.ktor:ktor-client-core:3.5.0` (+ `cio` jvm, `js` wasm, `content-negotiation`, `websockets`) | 3rd (Apache-2.0) | REST + WebSocket transport. |
| `io.ktor:ktor-serialization-kotlinx-json:3.5.0` + `kotlinx-serialization-json` (≥1.7) | 3rd (Apache-2.0) | JSON. |
| `org.jetbrains.kotlinx:kotlinx-coroutines-swing` | 3rd (Apache-2.0) | Desktop main dispatcher (jvmMain). |
| `org.jmdns:jmdns` | 3rd (Apache-2.0/EPL) | mDNS LAN discovery (jvmMain). |
| `eu.lepicekmichal.signalrkore:signalrkore:0.9.13` | 3rd (MIT) | **Fallback only** — native SignalR if hand-roll slips. |
| `org.openapitools:openapi-generator` (Gradle/CLI) | build-time (Apache-2.0) | Generate Kotlin REST DTOs/stubs from the v1 OpenAPI doc. |

**Explicitly NOT used:** moko-resources (legacy vs first-party `Res`); MS `com.microsoft.signalr` Java client (JVM-only, no Wasm); Voyager/Appyx (not the JetBrains direction); any MVI lib at the foundation (YAGNI). React/RN (removed; Stoney dislikes React).

---

## 11. Decisions (resolved)

All settled and binding:
- **Desktop + web (wasmJs) are the identical full app** from one `commonMain`; wasmJs parity constrains every choice. Mobile later, no `commonMain` change.
- **Navigation Compose + type-safe routes** (Decompose is the only sanctioned fallback if the experimental status bites).
- **No ViewModels** — server state lives in the injected `QueryClient` (`frontend-data-layer.md`); local state in Compose + plain state-holders; global state in Koin `Store`s. UDF, explicit Koin wiring, no Wasm reflection.
- **Koin 4.x**, explicit constructor wiring.
- **Ktor 3.5 REST with OpenAPI-generated DTOs/stubs** (committed, regenerated, hand-wrapped per subsystem) — external contracts generated, not transcribed.
- **Hand-rolled SignalR JSON-protocol over Ktor WebSockets in `commonMain`** (one impl, both targets, WS-only); SignalRKore native fallback.
- **Token custody:** native OS vault (DPAPI/Keychain/libsecret); web first-party `sessionStorage` + backend session.
- **OAuth:** desktop RFC-8252 loopback; web same-origin redirect.
- **i18n:** first-party Compose resources, `en`/`nl`, runtime locale via Compose environment override.
- **Design:** shadcn/ui (new-york) ported 1:1 (`frontend-design-system.md`) — OKLCH token contract, neutral base + dynamic chat-color accent, correctness-first component bases; shadcn (not Figma) is the source of truth.
- **One `:composeApp` module** under `app/`, feature packages in `commonMain`, thin platform source sets.
