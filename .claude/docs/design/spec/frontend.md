# Frontend — Interface Specification

**Status:** Implementable. Build the dashboard from this directly.
**Subsystem:** The NomNomzBot dashboard — one **Kotlin Multiplatform (KMP) + Compose Multiplatform** codebase shipping the **identical** app to **JVM desktop** and **web (wasmJs)** (Android/iOS later). Profile-agnostic, direct-connect: REST (v1) + SignalR are reached through one typed shared client; there is no broker. Public viewer/OBS pages (song-request, overlays, OAuth landing) are **not** this app — they are served by the API host and are out of scope here.

## Grounding & locked decisions (binding)

- **One codebase, two first-class targets.** Desktop (`jvm`) and web (`wasmJs`) build from the **same** `commonMain` — the web build is a full dashboard, not a cut-down view. Every decision below is constrained by **wasmJs parity**: no JVM-only shortcut (reflection, `Locale.setDefault`, raw sockets) may leak into `commonMain`.
- **Profile-agnostic, direct-connect.** The app needs only a backend **base URL** and talks REST + SignalR straight to it — no central orchestrator, so a self-host bot needs zero NoMercy infrastructure. **Native is multi-origin** (a saved-connection switcher fed by mDNS LAN discovery + manual add; switching swaps the active backend + its keychain token and reconnects). **Web is single-origin** — it only talks to the origin that served it (`window.location.origin`); no host picker, mDNS is a no-op.
- **The typed shared client is the *only* integration point.** Screens fetch/mutate exclusively through it (REST + SignalR). No screen constructs an `HttpClient`, URL, or hub connection ad hoc.
- **i18n: `en` + `nl`, never hardcode user-facing strings.** Compose Multiplatform resources; runtime locale switch without restart.
- **shadcn/ui (new-york) is the design source of truth** — ported 1:1 to Compose; fully specified in `frontend-design-system.md`. The previous Figma file is discarded (it did not represent a viable dashboard); a fresh Figma, if ever minted, is derived *from* this spec, never the reverse. The OKLCH token contract, component catalogue, and the dynamic chat-color accent live in `frontend-design-system.md` (§8 below is a summary).
- **Hand-synced DTOs, guarded against drift.** REST DTOs and endpoint facades are hand-written `@Serializable` Kotlin types in `core/network`. The backend's committed OpenAPI snapshot (`server/openapi/v1.json`) is the source of truth. `ApiContractTest` fails when a Kotlin DTO field is missing from the matching backend schema. `ApiRouteContractTest` fails when a client URL is not a route the API serves. SignalR has no schema → hand-authored.
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
    ├── build.gradle.kts                     # kotlin { jvm(); wasmJs { browser() } }, compose, serialization
    └── src/
        ├── commonMain/
        │   ├── kotlin/bot/nomnomz/dashboard/
        │   │   ├── App.kt                    # root composable: theme + connection/session gate (Destination) + shell
        │   │   ├── core/
        │   │   │   ├── network/              # Ktor client config, auth, ApiResult mapping, hand-written DTOs + facades
        │   │   │   ├── realtime/             # HubSocket (expect/actual transport) + hand-rolled SignalR hub clients
        │   │   │   ├── connection/           # ConnectionProfile, store, mDNS (expect), token vault (expect)
        │   │   │   ├── di/                   # AppGraph — explicit constructor wiring (Koin: owner question pending)
        │   │   │   ├── navigation/           # Destination gate, RouteStore (URL sync), ShellRouteSlug
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
| Language | Kotlin | `org.jetbrains.kotlin.multiplatform` + `.plugin.serialization` + `.plugin.compose` (2.2.21) | first-class |
| UI + targets | Compose Multiplatform | `org.jetbrains.compose` (CMP plugin, 1.9.0) | first-class |
| State collection | Lifecycle-aware `Flow` collection | `org.jetbrains.androidx.lifecycle:lifecycle-runtime-compose:2.9.4` (`lifecycle-viewmodel-compose:2.9.4` is also on the classpath) | ✅ — `collectAsStateWithLifecycle`; the state model is an owner question (§4) |
| DI | **Koin — owner question pending.** Not in the catalogue today | `AppGraph` wires everything by explicit constructor injection | ✅ |
| REST | Ktor client | `io.ktor:ktor-client-core:3.3.0` (+ engines below) | engine per target |
| REST engine (desktop) | Ktor CIO | `io.ktor:ktor-client-cio:3.3.0` (jvmMain) | — |
| REST engine (web) | Ktor JS/Fetch | `io.ktor:ktor-client-js:3.3.0` (wasmJsMain) | ✅ Fetch-backed |
| Content negotiation | Ktor + kotlinx JSON | `io.ktor:ktor-client-content-negotiation:3.3.0`, `io.ktor:ktor-serialization-kotlinx-json:3.3.0` | ✅ |
| Serialization | kotlinx.serialization | `org.jetbrains.kotlinx:kotlinx-serialization-json:1.9.0` | ✅ |
| Date/time | kotlinx-datetime | `org.jetbrains.kotlinx:kotlinx-datetime:0.6.2` | ✅ |
| Realtime (SignalR) | **hand-rolled** over a `HubSocket` expect/actual | jvm: `io.ktor:ktor-client-websockets:3.3.0`; wasmJs: the browser-native `WebSocket` | ✅ one protocol implementation, two thin transports |
| Coroutines | kotlinx-coroutines | `org.jetbrains.kotlinx:kotlinx-coroutines-core:1.10.2`; `-swing` (jvmMain, desktop main dispatcher); `-test` (tests) | ✅ |
| Images | Coil 3 | `io.coil-kt.coil3:coil-compose:3.1.0`, `io.coil-kt.coil3:coil-network-ktor3:3.1.0` | ✅ |
| Browser interop | kotlinx-browser | `org.jetbrains.kotlinx:kotlinx-browser:0.3` (wasmJsMain) | web only |
| Resources / i18n | Compose resources | built into the CMP Gradle plugin (`compose.components.resources`) | ✅ `values-nl/`, async load |
| LAN discovery (native) | JmDNS | `org.jmdns:jmdns:3.6.3` (jvmMain) | no-op on web |
| Code-editor host (desktop) | SwingWebView | `ca.weblite:webview:1.7.0` (jvmMain) — the OS's own web view, no bundled browser engine | n/a |
| Native helper (desktop) | JNA | `net.java.dev.jna:jna:5.19.1` (jvmMain) — points WebView2's user-data folder at the app-data dir | n/a |

> **Why hand-rolled SignalR.** No Kotlin SignalR library has a wasmJs target, and the MS Java client is JVM-only and heavyweight. The hub JSON protocol is small and stable, so one `commonMain` implementation (handshake → `0x1E`-framed invocation/ack/ping) gives **identical desktop+web behavior** — the cleanest parity play (§3.2). Only the raw text socket differs per target (`HubSocket`): Ktor's WebSockets plugin never opens a socket on the wasmJs Fetch engine, so the web actual uses the browser's native `WebSocket`.

---

## 3. The typed shared backend client (`core/network`, `core/realtime`)

The single integration surface. Two halves: **REST** (request/response) and **SignalR** (push). Both consume the **active `ConnectionProfile`** (§6) for base URL + bearer token; both react to a profile switch by re-targeting.

### 3.1 REST

- **Hand-synced DTOs (`core/network/`).** Every DTO is a hand-written `@Serializable` type beside the API that uses it. There is no code generation. The committed snapshot `server/openapi/v1.json` (regenerated by the backend `OpenApiSpecSnapshotTest`) is the contract, and two jvm tests guard it. `ApiContractTest` asserts every typed DTO's serialized field names exist on the matching backend schema; extra backend fields are allowed, a Kotlin field the backend dropped fails. `ApiRouteContractTest` scans the client sources for `api/v1/…` URL literals and asserts each is a route the spec serves. A new typed response DTO gets a line in `ApiContractTest.contracts`.
- **Hand-written facade (`core/network/`).** Per-subsystem typed API interfaces (`AuthApi`, `CommandsApi`, `PipelinesApi`, `ModerationApi`, `RewardsApi`, …) each with a `Rest…Api` implementation over the shared `ApiClient`, returning **`ApiResult<T>`** (single) or **`ApiResult<Page<T>>`** (paginated) — mirroring the backend envelopes:

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

A hand-authored client speaking the **SignalR JSON Hub Protocol over a raw text WebSocket** — WebSockets-only (the backend hubs assume WS; there is no long-polling/SSE fallback). The transport is `HubSocket` (`expect class`: `open(url)`, `send(text)`, `receive(): String?`, `close()`). The jvm actual uses Ktor CIO WebSockets. The wasmJs actual uses the browser-native `WebSocket`. Everything above it is shared `commonMain` code.

- **Connection sequence.** `{ws|wss}://{base}/hubs/{hub}?access_token=<jwt>` → send the handshake frame `{"protocol":"json","version":1,"useStatefulReconnect":true}` terminated by the record separator `0x1E` → await the handshake response (an `error` key aborts the attempt) → then exchange messages. Every message is UTF-8 JSON terminated by `0x1E`; the reader splits on `0x1E`. The token comes from a `tokenProvider` lambda read on **every** (re)connect, never captured once. When an attempt fails to establish (typically an expired JWT), the client calls the injected `refreshToken` lambda before the next retry.
- **Message types handled:** `1` Invocation (server→client hub method), `6` Ping (client sends `{"type":6}` every **15 s**; **60 s** — four ping intervals — with no inbound frame counts as a dead socket and triggers reconnect), `7` Close (surface + reconnect), and the stateful-reconnect pair `8` Ack / `9` Sequence. `2`/`3`/`4`/`5` (stream and completion messages) are unused. Client→server invocations are `type:1` with `target` + `arguments`.
- **Stateful reconnect.** The server runs `WithStatefulReconnect()`. Outbound `JoinChannel`/`LeaveChannel` invocations are numbered and buffered until acked. A reconnect after a prior successful connection sends `Sequence` first and resends only the unacked buffer, so the server's persisted group membership is reused. Inbound invocations are numbered and acked. A resent duplicate (id at or below the highest processed) is dropped, never redelivered. A resume with no confirming frame within **5 s** resets the resume state, and the next attempt is a plain fresh connect with a full group replay.
- **Reconnect/backoff.** Exponential backoff, `1 s · 2ⁿ`, **cap 30 s**, reset once a session establishes; no jitter. Only an explicit `disconnect()` stops the loop.
- **Connection state** is a `StateFlow<HubConnectionState>` (`Connected | Reconnecting | Disconnected`). `Connected` holds only while the handshake is complete. `Reconnecting` covers the first attempt and every retry. `Disconnected` holds only before the first `connect` or after `disconnect()`. The shell's live indicator reads it.

```kotlin
package bot.nomnomz.dashboard.core.realtime

enum class HubConnectionState { Connected, Reconnecting, Disconnected }

class DashboardHubClient {                      // /hubs/dashboard — user JWT
    val events: SharedFlow<HubEvent>            // every server invocation, decoded to a HubEvent
    val connectionState: StateFlow<HubConnectionState>
    val isConnected: Boolean
    fun connect(baseUrl: String, tokenProvider: () -> String?, channelId: String,
                refreshToken: (suspend () -> Boolean)? = null)   // opens, handshakes, invokes JoinChannel
    fun join(channelId: String)                 // add a channel group on the same connection (multi-chat)
    fun leave(channelId: String)
    fun disconnect(); fun dispose()
}

sealed interface HubEvent {                     // ChatMessage, StreamStatusChanged, StreamInfoChanged, AlertTriggered,
                                                // ModAction, CommandExecuted, RewardRedeemed, RedemptionStatusChanged,
                                                // MusicStateChanged, ChannelEvent, PermissionChanged, ObsBridgeStateChanged,
                                                // ObsLiveStateChanged, ConfigChanged, RewardChanged, AutoModQueueChanged,
                                                // Unknown(target, rawArgs)
}
```

- `ConfigChanged(domain, entityId, action)` is the backend's announcement that any operator (or the bot) mutated config. A page subscribes with `events.onConfigChange("commands", …) { reload }` and **refetches**. It never patches state from the payload, because the rendered list is filtered, sorted and paged server-side.
- `AdminHubClient` (`/hubs/admin`) has no channel group and no `JoinChannel` step. The handshake is gated on the `iam:manage` platform grant. It exposes `events: SharedFlow<AdminHubEvent>` and mirrors the same reconnect and resume logic.

Hubs on the server: `DashboardHub` `/hubs/dashboard`, `OverlayHub` `/hubs/overlay`, `OBSRelayHub` `/hubs/obs`, `AdminHub` `/hubs/admin`. This app connects only to `/hubs/dashboard` and `/hubs/admin`; the overlay and OBS hubs serve the overlay pages and the OBS bridge. The token is passed as `?access_token=<jwt>`.

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
- **UDF + DI.** Data flows down as params, events up as lambdas. Wiring is explicit constructor injection (`AppGraph`), no reflection (`wasmJs`-safe). Whether to adopt Koin modules is an owner question (pending). Placement: `frontend-structure.md`.

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

Navigation is **state-driven** today: a connection/session gate resolves a `Destination`, and the shell shows one `ShellRoute` at a time. A Navigation Compose `NavHost` with type-safe `@Serializable` routes is an **owner question (pending)**; it is not in the dependency catalogue.

- **Gate (in `App.kt`).** The `Destination` enum is `Splash | Connect | Unreachable | Setup | Shell`. It resolves from a boot flag and the `SessionPhase` (`NotConnected | NeedsSetup | Connected`):
  1. **Boot.** `Splash` holds for at least 1.2 s. In parallel, `connectController.restoreSession()` restores a remembered session, so a returning operator lands on the shell without a new login. The gate lifts only after both finish, so there is no Connect→Shell flash.
  2. **Onboarding probe.** If the phase is still `NotConnected` and the build has a served origin (web is single-origin), the gate pins that origin's profile and calls the anonymous `GET /api/v1/system/status` → `SystemStatus { onboardingComplete, checks }`. `onboardingComplete == false` means no platform app credentials exist yet, so the gate calls `sessionStore.enterSetup(profile)` (phase `NeedsSetup`) and the operator never sees a sign-in affordance before setup. A failed probe falls through to Connect. The probe never depends on the platform bot (that is per-channel work done after login). Native's multi-origin picker has no fixed backend to probe, so it goes to Connect, where the operator enters one.
  3. **Resolve.** `booting` → `Splash`; phase `Connected` → `Shell`; phase `NeedsSetup` → `Setup`; a remembered session whose backend is unreachable → `Unreachable` (retries the restore every 4 s); otherwise → `Connect`.
- **Setup wizard (`feature/setup`).** The wizard is self-describing: it renders from `GET /api/v1/system/setup/wizard` (`SetupWizard` → steps, fields, actions), so a new backend step needs no new client code. All calls are anonymous during the first-run window: `PUT /system/setup/credentials/twitch`, `PUT /system/setup/credentials/{provider}` (spotify, discord, youtube), `POST /system/setup/credentials/twitch/use-shared` (the explicit choice of the shared public Twitch app), `GET /system/setup/bot/oauth-url`, `GET /system/setup/bot/status`, and `POST /system/setup/complete`. The streamer sign-in follows. On web the OAuth redirect tears the wizard down, so `SetupController.finish()` leaves a pending record and `resumePendingSetupFinish` completes it once the session is `Connected`.
- **Route inventory — `ShellNav.pages` / `ShellRoute` is the single home.** The `ShellRoute` enum (`feature/shell/nav/ShellNav.kt`) lists every sidebar page. `ShellNav.pages` gives each one its `NavGroup`, `readFloor`, `manageFloor` and `readActionKey`, in sidebar order. The spec keeps no second route list: read the inventory from those two types, and the group layout from `frontend-ia.md` §3. Two sections exist: the feature groups (Home · Chat · Moderation · Loyalty · Music · Stream · Community · Connect) and the pinned `Setup` area (Roles, Integrations, Settings, …).
- **Shell.** A persistent left nav plus a content area that renders the selected `ShellRoute`. A parameter such as which pipeline or widget is open lives in the page's own state; there is no per-page route object. The platform **Admin** surface is one `ShellRoute.Admin` page with tabs (Tenants, IAM, Audit, Support, Platform defaults, …). It appears only when `SessionUser.isAdmin`.
- **URL sync.** `RouteStore` (`expect class`) mirrors the selected page to the address bar on web as `#/<slug>` and feeds browser Back/Forward back in as `externalChanges`. `ShellRouteSlug` derives each slug from the lower-cased enum name, so a new `ShellRoute` gets a slug for free; an empty or unknown slug lands on Dashboard. On jvm the route stays in memory and nothing is emitted.

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
| `org.jetbrains.compose` (Gradle plugin, 1.9.0) + `org.jetbrains.kotlin.plugin.compose` | 3rd (Apache-2.0) | Compose Multiplatform UI + `compose.components.resources`. |
| `org.jetbrains.androidx.lifecycle:lifecycle-runtime-compose:2.9.4` (+ `lifecycle-viewmodel-compose:2.9.4`) | 3rd (Apache-2.0) | `collectAsStateWithLifecycle`. The state model is an owner question (§4). |
| `io.insert-koin:koin-*` | 3rd (Apache-2.0) | **Owner question pending — not in the catalogue.** DI is explicit constructor wiring in `AppGraph`. |
| `io.ktor:ktor-client-core:3.3.0` (+ `cio` jvm, `js` wasm, `content-negotiation`, `websockets` jvm transport) | 3rd (Apache-2.0) | REST + WebSocket transport. |
| `io.ktor:ktor-serialization-kotlinx-json:3.3.0` + `kotlinx-serialization-json:1.9.0` | 3rd (Apache-2.0) | JSON. |
| `org.jetbrains.kotlinx:kotlinx-coroutines-core:1.10.2` (+ `-swing` jvmMain, `-test`) | 3rd (Apache-2.0) | Coroutines; desktop main dispatcher (jvmMain). |
| `org.jetbrains.kotlinx:kotlinx-datetime:0.6.2` | 3rd (Apache-2.0) | Date/time. |
| `org.jetbrains.kotlinx:kotlinx-browser:0.3` | 3rd (Apache-2.0) | Browser interop (wasmJsMain). |
| `io.coil-kt.coil3:coil-compose:3.1.0` + `coil-network-ktor3:3.1.0` | 3rd (Apache-2.0) | Image loading. |
| `org.jmdns:jmdns:3.6.3` | 3rd (Apache-2.0) | mDNS LAN discovery (jvmMain). |
| `ca.weblite:webview:1.7.0` | 3rd (MIT) | SwingWebView: hosts the code-editor page in the OS's own web view (jvmMain). |
| `net.java.dev.jna:jna:5.19.1` | 3rd (Apache-2.0/LGPL) | One kernel32 call for WebView2's user-data folder (jvmMain). |

**Explicitly NOT used:** moko-resources (legacy vs first-party `Res`); MS `com.microsoft.signalr` Java client and SignalRKore (JVM/native only, no Wasm); OpenAPI codegen (DTOs are hand-synced, §3.1); Voyager/Appyx (not the JetBrains direction); any MVI lib at the foundation (YAGNI). React/RN (removed; Stoney dislikes React).

---

## 11. Decisions (resolved)

All settled and binding:
- **Desktop + web (wasmJs) are the identical full app** from one `commonMain`; wasmJs parity constrains every choice. Mobile targets: owner question (pending).
- **Navigation:** state-driven `Destination` gate + `ShellRoute` shell + `RouteStore` URL sync today (§5). Navigation Compose / NavHost: owner question (pending).
- **State model** (no ViewModels / `QueryClient` / Stores, §4): owner question (pending). UDF and explicit constructor wiring, no Wasm reflection, hold today.
- **DI:** explicit constructor wiring in `AppGraph`. Koin: owner question (pending).
- **Ktor 3.3 REST with hand-synced DTOs** wrapped per subsystem, guarded by `ApiContractTest` (field names) and `ApiRouteContractTest` (routes) against the committed `server/openapi/v1.json` snapshot.
- **Hand-rolled SignalR JSON-protocol in `commonMain`** (one protocol implementation, WS-only, stateful reconnect) over a `HubSocket` expect/actual: Ktor WebSockets on jvm, the browser-native `WebSocket` on wasmJs.
- **Token custody** (§6: native OS vault, web `sessionStorage` + backend session): owner question (pending).
- **OAuth:** desktop RFC-8252 loopback; web same-origin redirect.
- **i18n:** first-party Compose resources, `en`/`nl`, runtime locale via Compose environment override.
- **Design:** shadcn/ui (new-york) ported 1:1 (`frontend-design-system.md`) — OKLCH token contract, neutral base + dynamic chat-color accent, correctness-first component bases; shadcn (not Figma) is the source of truth.
- **One `:composeApp` module** under `app/`, feature packages in `commonMain`, thin platform source sets.
