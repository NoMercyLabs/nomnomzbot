---
name: build-app
description: Build and test the NomNomzBot dashboard (app/ — Kotlin Multiplatform + Compose). Use when asked to build the app, run frontend tests, compile the Wasm target, produce a web bundle or desktop installer, or check the API contract snapshot for drift.
---

# Build & test the app

Kotlin Multiplatform + Compose Multiplatform, one codebase → desktop **and** web (Wasm).
Run from `app/`. On Windows use `.\gradlew.bat`; elsewhere `./gradlew`.

## The gate — both halves, always

```powershell
& app\gradlew.bat -p app :composeApp:jvmTest
& app\gradlew.bat -p app :composeApp:compileKotlinWasmJs
```

**`jvmTest` alone is not enough.** It cannot catch a wasmJs break — JVM-only stdlib
(`getOrDefault`, `toSortedMap`, and friends) compiles happily on JVM and fails on Wasm. Running
only `jvmTest` and reporting "the app builds" is a false green.

What `jvmTest` enforces:

| Test | Fails on |
|---|---|
| `ApiContractTest` | a Kotlin DTO drifting from the committed `server/openapi/v1.json` |
| `ApiRouteContractTest` | a client route the committed `server/openapi/v1.json` does not serve (a hand-edited snapshot with DTOs but no paths) |
| `DesignSystemStyleGuardTest` | new raw hex / `dp` in feature screens; off-catalogue Material3 primitives. The raw hex/dp baseline may only go down, never up |
| `StringResourceEscapingTest` | `\'` / `\"` in string resources — they render literally |
| `*GuardTest` family (`RowLabel`, `TruncatedPrimaryLabel`, `OpaqueIdRender`, `SideBySideField`, `CompactMultiColumnRow`, `DialogCompactWidth`, `LocalePinnedRenderTest`, `PipelineAccentScarcity`, `WebhooksRowAccentScarcity`) | layout, label, id-rendering and accent-scarcity defects in feature screens |

Duplication is **not** gated. Grep before you add a screen or a DTO; two poll-creation surfaces
once coexisted because nothing caught the copy.

## Run and package

```powershell
& app\gradlew.bat -p app :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t   # dev, http://localhost:5090
& app\gradlew.bat -p app :composeApp:run                                        # desktop dev
& app\gradlew.bat -p app :composeApp:wasmJsBrowserDistribution --rerun-tasks     # prod web bundle
& app\gradlew.bat -p app :composeApp:packageDistributionForCurrentOS             # MSI / DMG / DEB
```

`--rerun-tasks` on the prod bundle: without it Gradle serves a stale distribution and you verify
yesterday's code. The dev server proxies `/api` + `/hubs` to `http://localhost:5080`
(`webpack.config.d/proxy.js`), falling back to the deployed dev backend when nothing is
listening — so a frontend-only session never needs `dotnet`.

## Contract changes

A backend contract change lands as: refresh `server/openapi/v1.json` → sync the Kotlin DTOs →
register any new DTO in `ApiContractTest`. **Never commit a consumer without its contract.**
DTOs live flat in `core.network` and are shared across features — grep for an existing one
before adding a near-duplicate.

## House rules the gates cannot check for you

- **No hardcoded user-facing strings.** Every new string gets `values/strings.xml` (en) **and**
  `values-nl/strings.xml` (nl). A new `Res.string.X` also needs its own import in the file.
- **Design-system components only** — no raw hex, no raw `dp`. Tokens from
  `frontend-design-system.md` + its catalogue.
- Role-gate per `frontend-ia.md` §7: **hide** pages below the read floor, **disable** (with a
  reason tooltip) actions below the manage floor. Never show users numbered permission levels —
  role names only.
- DOM overlays on Wasm must mount inside `body.shadowRoot`, not `body`.

## Report back

The two commands, pass/fail for each, and on failure the first real compile error or failing
test name with its file and line. Say explicitly whether `compileKotlinWasmJs` ran — a report
that omits it will be read as "not run".
