# Frontend Structure — Kotlin Multiplatform + Compose Multiplatform (research record, 2026-06)

> **⚠️ SUPERSEDED for two headline decisions — `spec/frontend.md` is the authority.**
> This doc is kept as the **research record** (sources, version checks, the wizard/source-set
> investigation). Two of its decisions were **reversed** after it was written; follow
> `spec/frontend.md` instead:
> 1. **Web is first-class, not out of scope.** The dashboard ships the *identical* app to JVM
>    desktop **and** web (`wasmJs`) from one `commonMain`. Do **not** treat `wasmJsMain` as
>    out-of-scope or build desktop-only.
> 2. **SignalR is hand-rolled in `commonMain`, not SignalRKore.** SignalRKore has no wasmJs target,
>    so the realtime client is a hand-authored JSON-hub-protocol client over Ktor WebSockets (one
>    impl, both targets); SignalRKore is a **native-only fallback**, not the primary.
> Everything below that doesn't touch those two points (Koin, Ktor/CIO→engine-per-target, ViewModel
> + StateFlow + UDF, Navigation Compose, package root `bot.nomnomz`) still holds.

Companion to `2026-06-16-frontend.md` (the locked design). This was the **project structure
research** for the NomNomzBot dashboard client: a **KMP + Compose Multiplatform universal client**
that points at a backend URL and talks to the v1 REST API and SignalR hubs over a typed shared
client.

> Researched against the official JetBrains / Kotlin / Ktor / Koin docs (June 2026). Versions move
> fast — every version number below is marked with its source. Treat versions as "latest confirmed",
> not as a frozen lockfile; pin via a Gradle version catalog (`gradle/libs.versions.toml`) and bump
> deliberately. Items I could not fully confirm from a primary source are explicitly marked
> **[UNCERTAIN]**.

---

## 0. What changed recently (why this doc exists)

The KMP project shape changed over the last year. Do **not** reproduce older multi-module tutorials
from memory:

- **Old layout (deprecated guidance):** separate `shared/`, `androidApp/`, `desktopApp/`, `webApp/`
  modules; desktop entry in `desktopApp/src/main/kotlin/main.kt`. Still shown in some older doc pages
  and the `multiplatform-discover-project` page. **Don't start here.**
- **Current wizard default:** a **single `composeApp` module** holding shared logic *and* shared
  Compose UI, with per-target source sets (`commonMain`, `desktopMain`/`jvmMain`, `androidMain`,
  `iosMain`, `wasmJsMain`). This is what the Kotlin Multiplatform wizard
  (https://kmp.jetbrains.com) and the Koin CMP quickstart generate today.
- **Compose Multiplatform 1.10.x** (released Jan 2026) brought: unified `@Preview` in `commonMain`,
  **Compose Hot Reload stable + enabled by default**, and **Navigation 3** support on non-Android
  targets. Source: https://blog.jetbrains.com/kotlin/2026/01/compose-multiplatform-1-10-0/

---

## 1. Module layout (single `composeApp` module)

Generate from the wizard with **Desktop** selected (Android/iOS can be added later without
restructuring — the design is desktop-first, mobile later).

```
nomnomzbot/
└─ app-kmp/                              # KMP root (replaces the deleted RN `app/`); name TBD
   ├─ settings.gradle.kts
   ├─ build.gradle.kts                   # root: plugin versions via `plugins { ... apply false }`
   ├─ gradle/
   │  └─ libs.versions.toml              # version catalog — single source of truth for all deps
   ├─ gradlew / gradlew.bat / gradle/wrapper/
   └─ composeApp/                        # THE module: shared logic + shared Compose UI
      ├─ build.gradle.kts
      └─ src/
         ├─ commonMain/
         │  ├─ kotlin/bot/nomnomz/app/   # package root (see §2 on naming)
         │  │  ├─ App.kt                 # root @Composable App() — entry into the UI tree
         │  │  ├─ di/                     # Koin modules
         │  │  ├─ navigation/             # nav graph / back-stack wiring
         │  │  ├─ network/                # Ktor client, REST services, SignalR client
         │  │  ├─ feature/                # feature modules (dashboard, commands, chat, …)
         │  │  └─ ui/                     # shared design-system composables, theme
         │  └─ composeResources/          # multiplatform resources (drawable/, font/, values/)
         ├─ commonTest/kotlin/
         ├─ desktopMain/                  # JVM/Desktop target (see §3 for the exact name)
         │  └─ kotlin/.../main.kt         # fun main() = application { Window { App() } }
         ├─ desktopTest/kotlin/
         ├─ androidMain/    (later)       # Android entry + Manifest, only when mobile lands
         ├─ iosMain/        (later)       # iOS entry (MainViewController), only when mobile lands
         └─ wasmJsMain/     (OUT OF SCOPE) # do NOT add — web is a separate lightweight app
```

**Why single-module:** the wizard default, fewer Gradle moving parts, and `commonMain` already holds
both logic and UI — there is no benefit to splitting `shared` out for a desktop-first app. Split into
extra Gradle modules later **only** if build times or team boundaries demand it (Rule of Three on
modules, not speculative).

Sources for the layout:
https://kotlinlang.org/docs/multiplatform/compose-multiplatform-new-project.html ,
https://kotlinlang.org/docs/multiplatform/multiplatform-discover-project.html ,
https://insert-koin.io/docs/quickstart/cmp/

---

## 2. Package / namespace naming

- C# backend namespace is `NomNomzBot.*` (per CLAUDE.md), but that's irrelevant to Kotlin.
- **Decided:** the Kotlin package root is **`bot.nomnomz.app`** (matches the product domain
  `nomnomz.bot`). This is the reverse-DNS root baked into every file and the desktop `mainClass` —
  generate against it.

---

## 3. Desktop target & entry point — the one naming gotcha

There are **two valid shapes**, and which one you get depends on how the target is declared. Get this
right up front because the source-set folder name and the `mainClass` depend on it.

**A. Desktop-only template** (`kotlin("jvm")` plugin, default JVM target):
- Source set is **`jvmMain`**, entry file `src/jvmMain/kotlin/Main.kt`, and
  `compose.desktop { application { mainClass = "MainKt" } }`.
- This is exactly the JetBrains desktop template:
  https://github.com/JetBrains/compose-multiplatform-desktop-template/blob/main/build.gradle.kts
  (`mainClass = "MainKt"`, `targetFormats(Dmg, Msi, Deb)`).

**B. Multiplatform target with a custom name** (`kotlin { jvm("desktop") { … } }`):
- Source set is **`desktopMain`** (the target name + `Main`), entry under
  `src/desktopMain/kotlin/main.kt`, `mainClass = "bot.nomnomz.app.MainKt"`.
- This is what you get when Desktop coexists with Android/iOS in one `composeApp` and the JVM target
  is named `desktop` to disambiguate from Android's JVM.

**Decided:** use **shape B (`jvm("desktop")` → `desktopMain`)** from the start, even while
desktop-only. It's the form that survives adding Android/iOS later without renaming source sets, which
matches the design's "desktop first, mobile later" path. The `mainClass` is **`bot.nomnomz.app.MainKt`**
(top-level `main()` in `desktopMain/kotlin/bot/nomnomz/app/main.kt`); document it in the build file.

Desktop entry point (canonical form, from the docs):

```kotlin
// composeApp/src/desktopMain/kotlin/bot/nomnomz/app/main.kt
fun main() = application {
    Window(
        onCloseRequest = ::exitApplication,
        title = "NomNomzBot",
    ) {
        App()   // the shared commonMain composable
    }
}
```

`compose.desktop.application.mainClass` is the entry point; for a top-level `main()` in `main.kt`
inside package `bot.nomnomz.app` the class is `bot.nomnomz.app.MainKt`.

Sources: https://kotlinlang.org/docs/multiplatform/compose-multiplatform-create-first-app.html ,
https://github.com/JetBrains/compose-multiplatform-desktop-template/blob/main/build.gradle.kts ,
https://www.jetbrains.com/help/kotlin-multiplatform-dev/compose-native-distribution.html

---

## 4. Compose Multiplatform setup

- **Compose plugin:** `org.jetbrains.compose` Gradle plugin + the Kotlin Compose compiler plugin
  (`org.jetbrains.kotlin.plugin.compose`). Both applied in `composeApp/build.gradle.kts`.
- **Current versions (confirm/bump at generate time):**
  - Compose Multiplatform **1.10.x** — source:
    https://blog.jetbrains.com/kotlin/2026/01/compose-multiplatform-1-10-0/ and
    https://kotlinlang.org/docs/multiplatform/whats-new-compose-110.html
  - Kotlin — the wizard output in the create-first-app doc referenced **2.1.21 / CMP 1.8.2** at one
    point, but 1.10 is current; **[UNCERTAIN]** exact paired Kotlin version → take whatever the
    wizard emits and pin it. Don't hand-pick.
- **Compose Hot Reload:** stable and **bundled + enabled by default** in 1.10 — no extra config.
  Use the `composeApp [hot] 🔥` run config for desktop dev.
- **`@Preview`:** unified single annotation usable in `commonMain` as of 1.10 (older split
  annotations are deprecated with an IDE quick-fix).
- **Native distribution (packaging the desktop app):** `compose.desktop { application {
  nativeDistributions { targetFormats(Msi, Dmg, Deb) } } }`. **Decided:** the first packaging
  deliverable ships **all three formats from day one** — Windows `.msi`, macOS `.dmg`, and Linux
  `.deb` — not Windows-only. (Each format is produced on its own OS by the build/CI matrix; jpackage
  cannot cross-build.)
  Source: https://www.jetbrains.com/help/kotlin-multiplatform-dev/compose-native-distribution.html

---

## 5. Navigation — two official options, pick deliberately

Both are JetBrains-maintained multiplatform ports. Per the minimize-deps rule, pick **one**.

| Option | Artifact | Status (2026-06) | Notes |
|---|---|---|---|
| **AndroidX Navigation Compose** (DECIDED) | `org.jetbrains.androidx.navigation:navigation-compose` **2.9.2** | **Stable**, mature, type-safe routes | Single `NavHost` + `NavController`; well-documented; JetBrains' official recommendation; safe for a shipping desktop app. |
| **Navigation 3** (not adopted) | `org.jetbrains.androidx.navigation3:navigation3-ui` + `navigation3-common` (≈ `1.0.0-alpha0x`) | **Alpha on multiplatform**, non-Android targets supported from CMP 1.10 | You own the back stack as a plain list; more control, less settled API. Alpha — not adopted as the foundation. |

**Decided:** ship on **`org.jetbrains.androidx.navigation:navigation-compose` 2.9.2** (stable) —
JetBrains' official recommendation. Navigation 3 is promising (direct back-stack manipulation suits a
dashboard with deep, app-like nav) but is **alpha and not adopted** as the foundation of the primary
client.

Sources: https://kotlinlang.org/docs/multiplatform/compose-navigation.html ,
https://kotlinlang.org/docs/multiplatform/compose-navigation-3.html ,
https://kotlinlang.org/docs/multiplatform/whats-new-compose-110.html

---

## 6. State management & ViewModels

- **Compose state** (`remember`, `mutableStateOf`, `StateFlow` + `collectAsStateWithLifecycle`) is
  the baseline — no third-party state lib needed.
- **Multiplatform ViewModel:** JetBrains ships `androidx.lifecycle:lifecycle-viewmodel`
  (multiplatform) and a Compose integration; inject via Koin's `koinViewModel()`. This replaces the
  Zustand/TanStack-Query split from the old RN app — a ViewModel per screen holding `StateFlow`
  state, fed by the network layer. Source:
  https://kotlinlang.org/docs/multiplatform/compose-viewmodel.html
- **No Redux/MVI framework dependency.** Use plain ViewModel + `StateFlow`. Add an MVI lib only if a
  real need appears (Rule of Three).

---

## 7. Dependency injection — Koin

- **Koin** is the de-facto KMP DI choice and is what the JetBrains-aligned Koin CMP quickstart uses.
- Artifacts (commonMain): `io.insert-koin:koin-core`, `io.insert-koin:koin-compose`,
  `io.insert-koin:koin-compose-viewmodel` (and `-navigation` if using Koin's nav integration). Use
  the **Koin BOM** to align versions. Source: https://insert-koin.io/docs/quickstart/cmp/ ,
  https://insert-koin.io/docs/setup/koin/
- **DI lives in `commonMain`** (`di/` package). One `appModule` (or a few per feature), started once
  from each platform entry point via `initKoin()` → `startKoin { modules(appModule) }`, with
  `KoinContext`/`KoinApplication` wrapping the Compose tree.
- **Decided:** use Koin **plain-DSL modules** (`module { single { … } }`), not the annotations
  compiler-plugin path. The annotations path adds an extra plugin + codegen step for no benefit at
  this scale; the plain DSL is the binding choice.

---

## 8. HTTP — Ktor client (REST)

- **Ktor Client** is the multiplatform HTTP client (the only realistic choice for KMP).
- commonMain artifacts: `io.ktor:ktor-client-core`, `ktor-client-content-negotiation`,
  `ktor-serialization-kotlinx-json`. Plus **a per-platform engine**.
- **Engine for desktop/JVM — decided: CIO** (pure-Kotlin, coroutine-based, multiplatform,
  WebSocket-capable, HTTP/1.x only). CIO is the picked engine because it also covers
  Android/Native/Wasm later, so one engine choice spans every target. OkHttp/Java (HTTP/2 + WebSockets)
  is the contingency only if HTTP/2 ever becomes a hard requirement. Source:
  https://ktor.io/docs/client-engines.html
- **Serialization:** `kotlinx.serialization` (`@Serializable` DTOs) + `ContentNegotiation { json(...) }`
  with `ignoreUnknownKeys = true`, `isLenient`. Source:
  https://ktor.io/docs/client-serialization.html
- **Typed REST layer:** wrap the `HttpClient` in service classes (e.g. `CommandsApi`, `DashboardApi`)
  returning `Result<T>`-style sealed outcomes — mirrors the backend's `Result<T>`/`StatusResponseDto`
  convention and keeps controllers' shapes honest. These services are Koin singletons.

---

## 9. Realtime — SignalR on KMP (the reality)

**There is NO official Microsoft SignalR client for Kotlin / KMP.** Microsoft ships official SignalR
clients for C#, JavaScript/TypeScript, and Java only. Confirmed via
https://github.com/topics/signalr-client?l=kotlin (no Microsoft entry).

The backend exposes SignalR hubs (`/hubs/dashboard`, `/hubs/overlay`, `/hubs/obs`, `/hubs/admin`)
with the JWT as `?access_token=<jwt>`. The decided client is option 1; options 2–3 are the documented
fallbacks in order of preference:

1. **DECIDED — KMP SignalR client `SignalRKore` 0.9.13 (Apache-2.0)** (`lepicekmichal/SignalRKore`):
   a Kotlin Multiplatform, coroutine-based SignalR client (Android/iOS/JVM/JS). Implements the SignalR
   JSON hub protocol (handshake, `invoke`, streaming) over Ktor WebSockets, so we don't reimplement
   it. **Pin v0.9.13.** It is third-party with a low bus factor, so vendor-review the source and keep
   the hand-rolled path (option 2) ready as the fallback if it goes unmaintained.
   Source: https://github.com/topics/signalr-client?l=kotlin
2. **Roll our own thin SignalR-over-Ktor-WebSockets client** in `network/realtime/`. Feasible because
   SignalR's default transport is WebSockets and the JSON hub protocol is small and documented (record
   separator `0x1e`, handshake `{ "protocol":"json","version":1 }`, then typed invocation messages).
   Use `ktor-client-websockets` + a `KotlinxWebsocketSerializationConverter`. More code, zero extra
   dependency, full control of reconnect/backoff (which we want anyway). Sources:
   https://ktor.io/docs/client-websockets.html ,
   https://ktor.io/docs/client-websocket-serialization.html , SignalR Hub Protocol spec:
   https://learn.microsoft.com/aspnet/core/signalr/hubprotocol
3. **Negotiate the protocol away:** if we control the backend, we *could* expose plain WebSocket
   endpoints (or `kotlinx.rpc`) and skip SignalR for the desktop client. **[DECISION for Stoney]** —
   bigger backend change; only worth it if the SignalR client path proves painful. Not recommended
   now since the hubs already exist.

**Decided:** ship on **(1) SignalRKore 0.9.13**, with **(2)** the hand-rolled SignalR-over-Ktor-WebSocket
client as the documented fallback we keep in our back pocket if SignalRKore goes unmaintained. Either
way the realtime client is a Koin singleton behind our own `RealtimeClient` interface in `commonMain`,
so swapping implementations later doesn't touch feature code (depend on the interface, per SOLID).

---

## 10. Config / backend-URL supply (the core "profile-agnostic" requirement)

The design's central idea: the client is **profile-agnostic — it just needs a backend URL**
(self-host → `localhost`, SaaS → SaaS API). So backend URL is **runtime user input**, not a
compile-time constant.

- **Primary mechanism: user-entered base URL, persisted locally.** On first launch (or via a
  Settings/Connection screen) the user types the backend base URL + completes Twitch OAuth; the URL +
  JWT are stored on disk and reused on next launch. This is the equivalent of the old RN app's
  `EXPO_PUBLIC_API_URL`, but **moved to runtime** because one binary must point anywhere.
- **Storage:** desktop has no `expo-secure-store`. Options:
  - Plain config file in the OS app-data dir (e.g. via `java.util.prefs.Preferences` or a small JSON
    in `%APPDATA%/NomNomzBot/`) for the **base URL** (non-secret).
  - For the **JWT/secret tokens — decided:** **`russhwolf/multiplatform-settings`** (key-value) for
    the settings surface, **bound to the OS keychain/credential store** for the secret values —
    **Windows Credential Locker** on Windows, **macOS Keychain** on macOS, **libsecret** on Linux.
    The JWT never lands in a plaintext prefs file; multiplatform-settings holds non-secret prefs and
    the per-OS keychain binding holds the token.
- **Optional dev override:** a build-time default base URL (e.g. `http://localhost:5080`) baked via a
  `BuildConfig`-style generated constant or Gradle property, used only as the prefilled default in the
  connection screen — never the sole source. Keep it to a dev convenience, not the mechanism.
- **No `.env` files.** That was the Expo model; KMP desktop reads runtime config from disk, not from
  `process.env`. CORS origins on the backend already cover the old web ports; the desktop client isn't
  subject to browser CORS.

---

## 11. Third-party dependency ledger (minimize-deps rule)

Every non-JetBrains-core lib the KMP path pulls in, with justification. Pin all via
`gradle/libs.versions.toml`.

| Dependency | Layer | Necessity | Flag |
|---|---|---|---|
| Kotlin + Compose Multiplatform plugins | build/UI | **Required** (the platform itself) | JetBrains-official |
| `navigation-compose` (AndroidX MP) **2.9.2** | navigation | **Required** (decided nav lib) | JetBrains-official, stable |
| `lifecycle-viewmodel` (MP) + Compose integ. | state | **Required** for ViewModel pattern | JetBrains-official |
| **Koin** (`koin-core`, `koin-compose`, `koin-compose-viewmodel`) | DI | **Strongly recommended** (no good first-party KMP DI) | **3rd-party**, mature/standard |
| **Ktor Client** (`-core`, engine **`cio`**, `-content-negotiation`, `-websockets`) | HTTP + WS | **Required** (only real KMP HTTP client; CIO is the decided engine) | JetBrains-official |
| `ktor-serialization-kotlinx-json` + `kotlinx-serialization-json` | serialization | **Required** | JetBrains-official |
| **SignalRKore 0.9.13** | realtime | **Decided** (hand-rolled Ktor-WS client is the fallback) | **3rd-party (Apache-2.0), low bus factor — vendor-review / be ready to fork** |
| Secure storage: **`multiplatform-settings`** + OS keychain binding (Windows Credential Locker / macOS Keychain / libsecret) | config | **Required for token persistence** (decided) | **3rd-party, picked** |
| Logging (e.g. `kotlin-logging` / Napier) | infra | **Optional** | add only when needed |

**Deps to NOT add now:** Wasm/web target, an MVI framework, Koin annotations compiler, image-loading
libs, or anything for mobile — all premature for a desktop-first first slice.

---

## 12. First vertical slice (proposed, for the next step)

Smallest end-to-end proof, matching the workflow rule (one validated slice, then commit):

1. Wizard-generate `composeApp` (Desktop), `jvm("desktop")` → `desktopMain`, package `bot.nomnomz.app`.
2. Desktop `main.kt` → `Window { App() }`; `App()` renders a **Connection screen**: base-URL field +
   "Connect".
3. Ktor (CIO) `HttpClient` Koin singleton; one `SystemApi.getHealth()` hitting `GET {baseUrl}/health`.
4. Show the health JSON in the UI. **This proves**: module layout, desktop entry, Compose render, DI,
   typed Ktor REST against a real backend URL, runtime config. Commit when it actually shows live
   health from a running backend (not a mock).

SignalR and navigation come in the *second* slice, once the REST+config spine is proven.

---

## Sources (all consulted 2026-06-16)

Project structure / wizard / source sets:
- https://kotlinlang.org/docs/multiplatform/compose-multiplatform-create-first-app.html
- https://kotlinlang.org/docs/multiplatform/compose-multiplatform-new-project.html
- https://kotlinlang.org/docs/multiplatform/multiplatform-discover-project.html
- https://kotlinlang.org/docs/multiplatform/multiplatform-hierarchy.html
- https://insert-koin.io/docs/quickstart/cmp/

Desktop entry point / packaging:
- https://github.com/JetBrains/compose-multiplatform-desktop-template/blob/main/build.gradle.kts
- https://www.jetbrains.com/help/kotlin-multiplatform-dev/compose-native-distribution.html

Compose Multiplatform 1.10 / Hot Reload / @Preview / Navigation 3:
- https://blog.jetbrains.com/kotlin/2026/01/compose-multiplatform-1-10-0/
- https://kotlinlang.org/docs/multiplatform/whats-new-compose-110.html

Navigation:
- https://kotlinlang.org/docs/multiplatform/compose-navigation.html
- https://kotlinlang.org/docs/multiplatform/compose-navigation-3.html

State / ViewModel:
- https://kotlinlang.org/docs/multiplatform/compose-viewmodel.html

DI (Koin):
- https://insert-koin.io/docs/quickstart/cmp/
- https://insert-koin.io/docs/setup/koin/
- https://insert-koin.io/docs/reference/koin-compose/compose/

HTTP / WebSockets (Ktor):
- https://ktor.io/docs/client-engines.html
- https://ktor.io/docs/client-serialization.html
- https://ktor.io/docs/client-websockets.html
- https://ktor.io/docs/client-websocket-serialization.html

SignalR-on-KMP reality:
- https://github.com/topics/signalr-client?l=kotlin  (community clients; no official Microsoft Kotlin client)
- https://learn.microsoft.com/aspnet/core/signalr/hubprotocol  (SignalR JSON hub protocol, for the hand-rolled path)
