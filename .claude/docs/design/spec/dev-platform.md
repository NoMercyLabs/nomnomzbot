# Developer Platform — Type-Safe Events, API SDK, Multi-File Editor, Forkable Library

**Status:** draft for owner review (2026-07-18). **Owner directive (Bamo call):** Streamer.bot users
resort to absurd hacks because SB *starves them of information and events*. Our answer is the
inverse — **expose everything, fully type-safe** — turning custom code from a feature into a
batteries-included, discoverable developer environment. This spec is the unifying layer over the
existing `custom-code.md`, `code-execution-sandbox.md`, `widgets-overlays.md`, and `automation-api.md`
specs; it does not replace their internals, it makes them coherent and typed.

The four pillars the owner named, plus the keystone they all depend on:

- **Keystone — one Event Catalog** (§1): a single descriptor per event replacing the six ad-hoc
  catalogs that exist today.
- **Pillar 1 — Type-safe event system** (§2): `nnz.on<K>(event, handler)` with a generated
  `NnzEventMap`, following the `nomercy-player-core` pattern.
- **Pillar 2 — Type-safe API SDK in every editor** (§3): a generated `.d.ts` + a TS-capable editor,
  in the widget, game, and script editors alike.
- **Pillar 3 — Multi-file project editor** (§4): a real `src/` project, bundled, not a single
  textarea; and the editor that hosts it (§5).
- **Pillar 4 — Forkable library** (§6): pick any first-party/gallery widget **or game**, take the
  code, customize it per channel.

---

## 1. Keystone — the Unified Event Catalog

### 1.1 The problem (as-built, verified)

There are **six unreconciled event surfaces**, keyed three different ways:

| Surface | Keyed by | Coverage | Source |
|---|---|---|---|
| Domain bus (`IEventBus`/`IDomainEvent`) | CLR **type** | 119 event types across 30 modules | `Domain/**/Events/**` |
| Automation registry (`IAutomationEventDescriptor`) | `PublicName` **string** | **5** of 119 | `AutomationEventDescriptors.cs` |
| Widget/overlay (`WidgetEventDto`) | dotted **string** | denylist-filtered | `OverlayEventFilter.cs` |
| Live-game (`game.*`) | dotted **string** | 3 phases | `LiveGameEngine.cs` |
| Custom-data (`custom.<source>`) | dotted **string** | dynamic | `CustomDataIngestService.cs` |
| Outbound webhooks | reuses overlay filter | — | `OutboundWebhookEventCatalogue.cs` |

No single artifact maps `domain type → stable wire name → payload schema → who may see it`. The
`OverlayEventFilter` even carries a comment that the split *"moves to the event catalog when that
lands"* — the team already knows this is the missing keystone.

### 1.2 Decision — one descriptor, all events, tiered visibility

Generalize the existing `IAutomationEventDescriptor` into the canonical **`IEventDescriptor`**, one
per exposed event, and make every other surface derive from it:

```csharp
public interface IEventDescriptor
{
    string WireName { get; }              // stable dotted name, e.g. "chat.message", "stream.online"
    Type DomainEventType { get; }         // the CLR IDomainEvent this projects from
    Type PayloadType { get; }             // the PUBLIC payload record (the schema source of truth)
    EventVisibility Visibility { get; }   // Public | Moderator | Broadcaster | Internal
    bool ContainsPii { get; }             // gates SaaS exposure + logging
    object Project(DomainEventBase raw);  // PII-safe projection to PayloadType (explicit, never reflected)
}
```

- **Explicit, never auto-projected.** Projection stays hand-written per descriptor (the automation
  registry already does this) — auto-reflecting a domain event to the wire would leak PII and couple
  the wire schema to internal refactors. Decided against attribute-only magic for that reason.
- **Coverage is the goal, tiering is the safety valve.** We describe *as many of the 119 as carry
  user value*, each stamped with a `Visibility` tier. "Every event possible" (owner) = comprehensive
  descriptor coverage, not "every internal event is public." `Internal` events exist in the catalog
  (so tooling sees them) but never reach an untrusted editor/subscription.
- **One registry, fail-fast on collisions** — extend the existing `AutomationEventRegistry` scan to
  the full `IEventDescriptor` set (dup `WireName`/`Type` = startup failure, as today).
- The six surfaces become **views over the catalog**: overlay filter → `Visibility != Internal`;
  automation → the same registry (now 119-capable, not 5); webhooks → catalog subset; `game.*` /
  `custom.*` → descriptors like any other.

### 1.3 Decision — codegen with reflection, **no Roslyn**

The catalog emits two artifacts from the descriptor `PayloadType`s:

1. **`nnz.d.ts`** — the TypeScript `NnzEventMap` + payload interfaces (Pillars 1 & 2).
2. **JSON Schema** per payload (for validation + docs).

Generation is a **reflection-based emitter** (`System.Reflection` walking the `PayloadType`
graph → TS/JSON), **not** a Roslyn source generator (project rule: no Roslyn). It runs two ways:
a build/publish step that writes the static `.d.ts`, and a live dev endpoint
`GET /api/v1/sdk/types.d.ts?context=widget|script` so the editor always fetches types matching the
running server. Payload records use nullable-annotated C# → the emitter honors nullability into TS
`?:`/`| null`.

---

## 2. Pillar 1 — Type-safe event system

### 2.1 Decision — copy the `nomercy-player-core` shape verbatim

The reference (`C:\Projects\NoMercy\packages\nomercy-player-core`) is the proven pattern:

```ts
interface IEventBus<E extends Record<string, any>> {
  on<K extends keyof E>(event: K, fn: (data: E[K]) => void): void;
  once<K extends keyof E>(event: K, fn: (data: E[K]) => void): void;
  off<K extends keyof E>(event: K, fn?: (data: E[K]) => void): void;
  emit<K extends keyof E>(event: K, data?: E[K]): void;
}
```

Our `nnz.on` already has this *shape* but is deliberately `any` (`crash.vue`:
`(window as any).NomNomz`). We make it typed by generating the map:

```ts
// nnz.d.ts (generated from the Event Catalog)
interface NnzEventMap {
  'chat.message':   { user: NnzUser; text: string; emotes: NnzEmote[]; ... };
  'stream.online':  { startedAt: string; title: string; category: string };
  'game.lobby':     { gameKey: string; joinClosesAt: string; ... };
  'custom.<source>': NnzCustomPayload;   // widened bucket for dynamic sources
  // ... one entry per Public/Moderator/Broadcaster descriptor
}
declare const nnz: {
  on<K extends keyof NnzEventMap>(event: K, fn: (data: NnzEventMap[K]) => void): void;
  // once / off / onAny mirror this
};
```

- **Cancellable events** copy the reference's `BeforeEvent<T>` convention — `before*` events carry
  `{ ...data, prevent(reason): void }` so a script can veto (e.g. `before.command`, `before.tts`).
  Which events are cancellable is a per-descriptor flag.
- **Visibility narrows the emitted map per context** — a widget's `.d.ts` only contains
  `Public`-tier events; a broadcaster-owned script's `.d.ts` also contains `Moderator`/`Broadcaster`
  events. Same generator, `?context=` selects the tier set.

### 2.2 Decision — the transport stays as-is, only the types are new

No runtime change to delivery: widgets keep the null-origin iframe **postMessage bridge**
(`OverlaySdkController` SDK), scripts keep the **capability-broker host bridge**. We are adding a
generated type layer over both, not re-plumbing transport. `nnz.on` in a widget still receives
postMessage frames; the map just makes the frame typed.

---

## 3. Pillar 2 — Type-safe API SDK in every editor

### 3.1 Decision — `nnz.api.*`, two capability profiles, one generator

Beyond events, the SDK exposes the bot's **actions** — the thing SB users hack around missing:

```ts
nnz.api.chat.send(text): Promise<void>
nnz.api.user.get(id): Promise<NnzUser>
nnz.api.currency.add(userId, amount): Promise<NnzBalance>
nnz.api.http.get(url): Promise<T>          // routed through the SSRF egress allowlist (already built)
nnz.api.music.nowPlaying(): Promise<NnzTrack>
nnz.units.convert(5, 'km', 'mi')           // the literal thing Bamo's example shelled npm for
nnz.time, nnz.math, nnz.random, nnz.str, nnz.json, nnz.store   // pure-JS batteries, no host call
```

- **Two contexts, one type pipeline.** The **widget** profile is read-mostly (events, settings,
  a small safe action set) since widget code is browser-side and untrusted; the **script** profile
  is the full capability surface (server-side Jint/Wasmtime, capability-broker-gated). The generator
  emits `?context=widget` vs `?context=script` `.d.ts` from the **same** catalog + the capability
  catalogue — a method appears in a context's types **only if** that context can be granted it. Types
  and runtime gating come from one source, so they can never disagree.
- **Batteries (`nnz.units`/`math`/`time`/`str`/`json`/`random`) are pure JS** shipped inside the
  sandbox bootstrap — no host call, no budget cost, always available. This is the "never reach for
  npm" surface. Seed set = the **top pain points from Bamo's list** (owner to supply; unit
  conversion, date math, string/format, number format are the confirmed floor).
- `nnz.store` = a per-channel, per-artifact persistent KV (survives runs) over a new small table.

### 3.2 Decision — land the capability broker (this is the current hard blocker)

`IScriptCapabilityBroker.BuildGrantAsync` is a **stub returning an empty grant** — so today scripts
can compute + `bot.send` but **cannot reach the wired chat/currency/music/http powers**. The SDK is
worthless until this lands. The broker becomes real: declared `nnz.api.*` calls (detected at save
time, as today) → validated against the **capability catalogue** + feature-flag/visibility gates →
a concrete grant; deny-by-default, per-run host-call budget unchanged. Each `nnz.api.*` method maps
1:1 to a capability key.

---

## 4. Pillar 3 — Multi-file project model + build

### 4.1 The problem (as-built)

Everything is **single-source**: widget build input is one `SourceCode` string piped to esbuild over
**stdin** (no file tree, no import resolution — `EsbuildWidgetBuildService`); Vue is single-SFC
(`IVueSfcCompiler.Compile(source, filename)`); a `CodeScriptVersion` is one `SourceCode` → one
`CompiledJs`. No project, no modules, no shared imports anywhere.

### 4.2 Decision — a virtual project + manifest, materialized for esbuild

A project (widget, game, or script) is a **file set + manifest**:

```
src/
  index.ts        // entry (from manifest)
  lib/util.ts
  components/…
nnz.manifest.json // { entry, kind: widget|game|script, framework, dependencies: [allowlisted] }
```

- **Storage:** the version entity's single `SourceCode` becomes **`FilesJson`** (a `path → content`
  map) + a `Manifest`. Applies to `CodeScriptVersion` and `WidgetVersion` alike (migration ×2). Entry
  + kind live in the manifest.
- **Build:** the esbuild service **materializes the file set to a temp dir**, runs esbuild from the
  manifest `entry` with `--bundle`, captures the single bundle, deletes the temp dir. (esbuild's CLI
  can't load JS resolve-plugins, so a virtual-FS-over-stdin approach is out; temp-dir materialization
  is the robust, fast choice — decided.) Framework handling (`vanilla`/`vue`/`react`) is unchanged
  except it now resolves cross-file imports.
- **Dependencies** are an **allowlist**, not npm. A curated set of vetted libraries (the bot ships
  them, esbuild resolves them as externals/vendored) — never arbitrary registry fetch (supply-chain
  + SaaS isolation). The allowlist grows by owner decision; `nnz.*` covers most needs so deps stay
  rare.
- **Compile still parse-checks at save** (as today) so syntax/type errors are caught before publish;
  now it also runs the TS diagnostics (§5) across the whole project.

---

## 5. Pillar 3 (cont.) — the editor

### 5.1 Decision — CodeMirror 6 + TypeScript language service over a **same-origin** worker

Today the editor is **CodeMirror 6 with syntax highlighting only** (no completion, no diagnostics),
web impl importing highlighting from esm.sh; desktop is a Swing text area. Monaco was reverted
earlier due to **worker CORS in the Wasm build** ([[widget-editor-is-codemirror-not-monaco]]).

- **Stay on CodeMirror 6**, add a real TypeScript language service via **`@typescript/vfs`** (runs
  `tsc` in-browser for completion + inline diagnostics against the generated `nnz.d.ts`) wired to
  `@codemirror/lang-javascript` + a completion source. This gives the autocomplete + red-squiggles
  that make "every API, discoverable" real.
- **Resolve the Monaco-CORS lesson, don't repeat it:** the language-service worker is served
  **same-origin by the bot** (the bot already serves the editor assets), not from a CDN — so the CORS
  wall that killed Monaco's workers doesn't apply. The TS `.d.ts` (§1.3) is fetched from the bot's
  `GET /sdk/types.d.ts?context=…` so types always match the server.
- **Project explorer:** the single-textarea editor becomes a small file tree (the `src/` project) +
  tabbed files, over the multi-file model (§4). Desktop mirrors the same via the shared component.
- **i18n + design system** apply as normal (frontend track).

### 5.2 Track note

Pillars 2–3 editor work is **frontend (aaoa's track)** — it consumes the backend's generated `.d.ts`
+ build/compile endpoints. The catalog, codegen, broker, multi-file build, and content model are
**backend**. This spec is the contract between them.

---

## 6. Pillar 4 — Forkable library (widgets *and* games)

### 6.1 As-built — widgets fork, games don't

Widgets already have **genuine fork-to-edit**: `WidgetService.CreateAsync` clones a gallery item or
installed widget's source **verbatim into a new per-channel `Widget`** (`Source="custom"`, detached),
distinct from read-only `InstallFromGalleryAsync`. ~21 first-party widgets are seeded clone-able.

**Games are compiled C#** (`ILiveGame` assembly-scan; `Crash/Drop/Heist/Raffle`). Only their overlay
*skins* are Vue assets — the **logic/rules cannot be forked or edited per channel**. This is the
biggest content gap.

### 6.2 Decision — scripted games: a game becomes a forkable JS project

Introduce a **data-driven, sandboxed game runtime** so a game is a forkable multi-file JS project
implementing a game contract, running in the existing sandbox — not compiled C#:

```ts
// a user game project (forkable), typed against nnz.d.ts
export default nnz.game({
  key: 'my-heist', displayName: 'Custom Heist', inputKeywords: ['!heist'],
  onJoin(player, stake) { ... },          // called on chat input
  onTick(ctx) { ... },                    // 1s runner tick (existing LiveGameRunner drives it)
  onResolve(ctx): NnzPayout[] { ... },    // settlement → economy stake/settle (existing)
});
```

- The compiled **engine/runner/economy stay** (`LiveGameEngine`, `LiveGameRunner`, stake/settle/
  refund — all built + tested). What changes: `ILiveGame` gains a **`ScriptedGame` implementation**
  that delegates `onJoin/onTick/onResolve` to a sandboxed script via the broker. A 1-second JS tick
  under the resource budget is well within Jint/Wasmtime headroom.
- **First-party games ship as JS reference implementations** seeded into the gallery, forkable like
  widgets. The existing compiled C# games can remain as trusted built-ins *and* get JS reference
  twins to fork — decided: seed JS twins; keep C# built-ins until parity is proven, then optionally
  retire them.
- **Unified fork flow:** one clone path produces an **editable, typed, multi-file per-channel
  project** for any artifact kind (widget | game | script). "Choose from a large library → take the
  code → change it for your channel" (owner) becomes one verb across all three.

### 6.3 The library itself

The library = the first-party gallery (widgets) + first-party scripted games + starter scripts, all
clone-seeded and typed. It grows as content, not code. Community submissions keep the existing
GitHub-pinned review pipeline; first-party stays immutable-at-source, forkable-to-own.

---

## 7. Security & isolation (reuse, don't rebuild)

The isolation boundary is **done and hardened** — do not touch it:

- **Jint (self-host)** + **Wasmtime (SaaS)** executors, shared `bot.*`/bridge contract; Wasmtime is
  Cranelift/fuel/epoch/CVE-hardened, WASI unlinked, proven by escape tests (pending only the
  QuickJS-in-WASM engine module for SaaS JS dispatch).
- **Save-time capability declaration** (regex scan of host calls) + **deny-by-default grant** +
  per-run budgets stay. The broker (§3.2) fills the grant; it does not weaken the model.
- **Visibility tiers** (§1.2) gate which events/APIs even *appear* in a given context's types, so an
  untrusted widget can't subscribe to a `Broadcaster` event or call a privileged API — enforced at
  grant time, not just hidden in types.
- **`nnz.api.http`** routes through the existing SSRF egress allowlist + hardened client. Multi-file
  **dependencies are an allowlist**, never arbitrary npm.

---

## 8. REST / endpoint surface (new + changed)

Middleware: tenant-resolved, Gate-2 `[RequireAction]`. Vocabulary per `canonical-authz-vocabulary`.

| Route | Method | Gate-2 | Purpose |
|---|---|---|---|
| `/api/v1/sdk/types.d.ts` | GET | Plane-A / any authed · `sdk:read` | Generated `.d.ts` for `?context=widget\|script` |
| `/api/v1/sdk/event-catalog` | GET | `sdk:read` | The Event Catalog (wire name, payload schema, tier) |
| `channels/{id}/scripts/{sid}/files` | GET/PUT | `scripts:read` / `scripts:write` | Multi-file project CRUD (replaces single-source) |
| `channels/{id}/widgets/{wid}/files` | GET/PUT | `widgets:read` / `widgets:write` | Multi-file widget project CRUD |
| `channels/{id}/games` | GET/POST | `games:read` / `games:write` | Scripted-game projects (list/create/fork) |
| `channels/{id}/games/{gid}/files` | GET/PUT | `games:write` | Scripted-game project CRUD |
| `channels/{id}/{kind}/{id}/fork` | POST | `{kind}:write` | Unified fork: gallery/first-party artifact → editable per-channel project |
| `channels/{id}/{kind}/{id}/build` | POST | `{kind}:write` | Multi-file build + TS diagnostics (compile check) |

Existing single-source endpoints are migrated in place (no back-compat needed — no users yet):
`SourceCode` → `FilesJson`+`Manifest`.

## 9. Decisions (load-bearing forks — owner red-line here)

1. **One Event Catalog is the keystone; build it first.** Everything else (typed events, SDK,
   codegen) depends on generalizing `IAutomationEventDescriptor` to all events with visibility tiers.
2. **Codegen by reflection, not Roslyn** — honors the no-Roslyn rule; emits `.d.ts` + JSON Schema
   from descriptor payload types, live via `/sdk/types.d.ts`.
3. **Copy `nomercy-player-core`'s typed-emitter shape** (`on<K extends keyof NnzEventMap>`,
   single generated map, `BeforeEvent<T>`) — proven, and already the shape of our `nnz.on`.
4. **CodeMirror 6 + `@typescript/vfs`, worker served same-origin** — keeps CM6 (Monaco's Wasm worker
   CORS is why it was reverted) while gaining real TS completion/diagnostics; same-origin worker
   sidesteps the CORS wall.
5. **Multi-file via temp-dir materialization for esbuild** — CLI can't do JS resolve-plugins;
   dependencies are an allowlist, never npm. Storage moves `SourceCode` → `FilesJson`+`Manifest`.
6. **Scripted games** — game logic becomes a forkable sandboxed JS project over the existing
   engine/runner/economy; first-party games get forkable JS twins; unified fork flow for
   widget | game | script.
7. **Land the capability broker** — the current empty-grant stub is the hard blocker; the SDK is
   inert until it maps declared `nnz.api.*` calls to real, gated grants.

## 10. Phasing (suggested build order — each a settled sub-slice)

1. **Event Catalog + reflection codegen** (`/sdk/types.d.ts`, `/sdk/event-catalog`) — the keystone.
2. **Capability broker** (unblocks all `nnz.api.*`) + the `nnz.api.*`/batteries surface over it.
3. **Multi-file model + esbuild temp-dir build** (`FilesJson`+`Manifest`, migrations ×2).
4. **Typed editor** (frontend: CM6 + `@typescript/vfs`, project explorer) consuming 1–3.
5. **Scripted games** + unified fork flow + first-party JS twins.
6. **SaaS**: the QuickJS-in-WASM engine module for Wasmtime JS dispatch.

Nothing here is built until this spec is settled ([[settle-specs-before-implementing]]).
