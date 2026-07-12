# Interface Specification — Widget SDK & In-House Event Vocabulary

**Status:** Implementable. The client library a widget author writes against, the backend seam that feeds it, and the codegen that keeps them in lockstep. Complements `widgets-overlays.md` (which owns widget persistence, versioning, compile-on-save, and serving) — this doc owns the **author-facing runtime**.
**Sources of truth:** `widgets-overlays.md` (Widget entity, `OverlayHub`, `OverlayManifest`, esbuild compile); `automation-api.md` (`IAutomationEventRegistry`/`IAutomationEventDescriptor` — the event catalogue seam this reuses); `chat-decoration.md` (the decorated fragment tree `render.emotes` consumes); `roles-permissions.md` (IAM the `actions.*` surface obeys).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 on the backend; the SDK is a published TypeScript package (`@nomnomzbot/widget`); no hand-rolled per-widget sockets; every author-facing name is understandable without documentation.

> **Motivation.** The reference bot (`nomercy-bot`) hand-maintains three parallel widget sockets (vue/react/vanilla), every event handler is `(...args: any[])`, and settings are an empty `{}` filled in per widget from scattered `window.WIDGET_*` globals. This spec replaces all of that with **one typed core**: the author writes `widget.on('cheer', e => …)`, `e` is fully typed with no import, and the connection/reconnect/subscribe/config plumbing is written once. This is studied from the reference for *behaviors*, not copied.

---

## 1. Scope & relationship to `widgets-overlays.md`

`widgets-overlays.md` stays the source of truth for the `Widget`/`WidgetVersion` entities, the compile-on-save (esbuild) pipeline, and the token-authed serving. This spec adds the layer **above** it — the SDK the compiled bundle is built against — and **simplifies** one thing:

- **Widgets are self-authored and tied to a single user; they act as that user.** The elaborate `TrustTier` / gallery-review / per-CSP-tier machinery in `widgets-overlays.md` §1 is **not** applied to the self-authored case: a widget obeys nothing more than its owner's normal IAM permissions (§8). The gallery/community-submission surface remains for the *shared-gallery* future, but the default widget is the owner's own and carries no bespoke trust gating. This deletes a large amount of would-be plumbing.

Everything else in `widgets-overlays.md` is unchanged and referenced, not redefined.

---

## 2. The in-house event vocabulary

The single hardest requirement: **a reliable, in-house event-name system simpler than Twitch's (or any provider's) internal topics, understandable without docs.**

### 2.1 Naming scheme (binding)

Canonical form is **`domain.action`**, lower-case, dotted, the action a past-tense "what happened". The `domain` is always a short human word; the pair reads as a fact and never collides. **No aliases** (an alias re-introduces the ambiguity this scheme exists to remove).

| In-house name | Fires when | Hides (raw provider topic) |
|---|---|---|
| `chat.message` | A viewer sent a chat message | `channel.chat.message` |
| `chat.cleared` | Chat was cleared | `channel.chat.clear` |
| `viewer.followed` | New follower | `channel.follow` |
| `viewer.subscribed` | New / renewed sub | `channel.subscribe`, `channel.subscription.message` |
| `viewer.gifted` | Someone gifted subs | `channel.subscription.gift` |
| `bits.cheered` | Bits cheered | `channel.cheer` |
| `channel.raided` | A raid arrived | `channel.raid` |
| `reward.redeemed` | Channel-point reward redeemed | `channel.channel_points_custom_reward_redemption.add` |
| `song.changed` | Now-playing track changed | `music.now_playing` (SR / Spotify / YouTube engine) |
| `tip.received` | A tip / donation | `supporter.tip` (Ko-fi, …) |
| `goal.changed` | A creator-goal moved | `channel.goal.progress` |
| `poll.updated` | Poll begun / progressed / ended | `channel.poll.*` |
| `prediction.updated` | Prediction begun / locked / resolved | `channel.prediction.*` |
| `stream.started` / `stream.ended` | You went live / offline | `stream.online` / `stream.offline` |
| `hypetrain.updated` | Hype train begun / progressed / ended | `channel.hype_train.*` |
| `custom.<name>` | A custom data-source value updated (`custom-events.md`) | `custom.<name>` (already clean) |

The table is the seed set; a new user-facing event adds one clean row. Codegen (§4) validates the shape mechanically (`^[a-z]+(\.[a-z_]+)+$`).

### 2.2 Backend seam (reuse `IAutomationEventRegistry`)

The clean names ARE the canonical `PublicName` on the existing auto-discovered registry (`automation-api.md` §Event stream) — **one vocabulary for widgets AND the automation API**, not two. The registry's `PublicName` scheme is set to the `domain.action` form above (superseding the old `Twitch.ChatMessage` PascalCase); `IAutomationEventDescriptor.ProjectPayload` already yields the PII-safe public projection that becomes the typed payload.

```csharp
// EXTEND IAutomationEventDescriptor (automation-api.md) — add the machine-readable payload schema codegen reads.
public interface IAutomationEventDescriptor
{
    string PublicName { get; }               // now the domain.action name, e.g. "chat.message"
    string Description { get; }              // one-line, surfaced in docs + the dashboard picker
    Type DomainEventType { get; }            // source DomainEventBase subtype
    Type PayloadType { get; }                // NEW — the projected payload's CLR type (the codegen source)
    object ProjectPayload(DomainEventBase e);// PII-safe projection (unchanged)
}
```

`PayloadType` is the render-ready DTO the projection returns (e.g. `ChatMessagePayload` = the decorated `DashboardChatMessageDto` shape). It is the single source the TS types are generated from (§4).

---

## 3. The SDK — `@nomnomzbot/widget`

Framework-agnostic core. `createWidget()` reads the injected manifest (§7), self-connects (§6), and returns a `Widget`. Everything an author touches:

```ts
interface Widget<Cfg = Record<string, unknown>> {
  // ── events in (typed by generation, §4) ──────────────────────────────
  on<K extends keyof WidgetEvents>(event: K, fn: (data: WidgetEvents[K]) => void): Unsub
  once<K extends keyof WidgetEvents>(event: K, fn: (data: WidgetEvents[K]) => void): Unsub
  off<K extends keyof WidgetEvents>(event: K, fn?: (data: WidgetEvents[K]) => void): void
  on(event: 'all', fn: (name: keyof WidgetEvents, data: unknown) => void): Unsub  // firehose
  history<K extends keyof WidgetEvents>(event: K, count: number): Promise<WidgetEvents[K][]>  // backfill on load

  // ── config (typed to THIS widget's settings schema) ──────────────────
  readonly config: Cfg                       // live — updated on WidgetSettingsChanged
  onConfigChange(fn: (config: Cfg) => void): Unsub

  // ── on-demand reads (request/response, typed) ────────────────────────
  get<K extends keyof WidgetResources>(resource: K): Promise<WidgetResources[K]>
  readonly channel: ChannelContext           // reactive: title, game, isLive, uptime, viewerCount

  // ── persist (server-backed per-widget KV) ────────────────────────────
  state: { get<T>(key: string): Promise<T | null>; set<T>(key: string, value: T): Promise<void> }

  // ── media (the overlay audio bus, typed) ─────────────────────────────
  sound: { play(url: string, opts?: SoundOpts): void; stop(handle?: string): void }
  tts: { speak(text: string, opts?: TtsOpts): void }

  // ── render helpers (so no widget re-solves emotes/templates) ──────────
  render: {
    emotes(fragments: ChatFragment[]): Node[]           // decorated Twitch/BTTV/FFZ/7TV + badges → nodes
    template(str: string, vars?: Record<string, unknown>): string   // the 90+ {{...}} template variables
  }
  format: { number(n: number): string; duration(ms: number): string; currency(n: number, ccy?: string): string }

  // ── act (obeys the owner's IAM, §8 — NOT a separate trust surface) ───
  actions: {
    sendChat(text: string, opts?: { replyTo?: string }): Promise<void>
    run(command: string, args?: string[]): Promise<void>
    addToQueue(input: string): Promise<void>
    setGoal(metric: string, value: number): Promise<void>
    // …one typed method per exposed action; each server-checks the owner's permission
  }

  // ── lifecycle ────────────────────────────────────────────────────────
  readonly connected: boolean
  onConnect(fn: () => void): Unsub
  onDisconnect(fn: () => void): Unsub
}
type Unsub = () => void
```

`get`/`history`/`state`/`actions` are request/response over the same socket (SignalR `invoke`); `on`/`config`/`channel`/`sound`/`tts` are push. Nothing here is a separate connection — one socket, one manifest.

---

## 4. Typed event map — generated, drift-guarded

The "never hand-type events" guarantee. A codegen step emits `events.generated.ts` from the backend registry (§2.2), exactly mirroring the openapi-snapshot + `ApiContractTest` pattern already used for the KMP dashboard client.

```ts
// events.generated.ts — EMITTED, never hand-edited
export interface WidgetEvents {
  'chat.message': ChatMessage
  'viewer.followed': ViewerFollowed
  'bits.cheered': BitsCheered
  'song.changed': NowPlaying
  // …one entry per IAutomationEventDescriptor
}
export interface NowPlaying {
  title: string; artist: string; artUrl: string | null
  durationMs: number; positionMs: number; isPlaying: boolean; serverTime: number  // §9 anchor
}
```

- **Source:** `dotnet run --project tools/WidgetTypeGen` walks `IAutomationEventRegistry` + each `PayloadType`, emits the interface map + payload interfaces. Runs in CI.
- **Drift guard:** a committed `widget-events.snapshot.json` (the emitted shape) + a `WidgetEventContractTest` that regenerates and diff-asserts — a backend payload change that isn't reflected fails CI, same as `ApiContractTest`. The SDK types therefore can **never** lie about what the server sends.
- The `.on()` overload set is lifted verbatim from your `player-core` `IEventBus` (`on<K extends keyof E>(event: K, fn: (data: E[K]) => void)` + an `'all'` firehose + a bare-string escape hatch with a rename-warning), with `E = WidgetEvents`.

---

## 5. Framework adapters — one core, idiomatic stores

The core is framework-agnostic; each adapter exposes the connection + subscription state the idiomatic way over a **single shared instance** (never one socket per component). Same generated types flow through all.

- **`@nomnomzbot/widget/vue`** → `useWidget()` returns one **stable reactive store** (module singleton): `{ on, once, off, config, channel, connected, get, actions, … }`; `config`/`channel`/`connected` are `reactive`/`ref` so templates track them.
- **`@nomnomzbot/widget/react`** → `<WidgetProvider>` opens the one connection; `useWidget()` reads it from context. `connected`/`config` are state; `on()` inside `useEffect` returns the `Unsub` for cleanup.
- **`@nomnomzbot/widget/svelte`** → a Svelte store per reactive field.
- **vanilla** → the core `Widget` object is the store; no wrapper.

---

## 6. Connection & the subscribe-via-handlers handshake

The widget page is served with `?widgetId={id}&token={overlayToken}` (unchanged). The SDK:

1. Opens the SignalR connection to `/hubs/overlay` with the token in the query; the hub validates it against `Channels.OverlayToken` (never a user JWT) and resolves the owning channel + user.
2. On connect, **subscribes at runtime to exactly the events the author registered** — the SDK derives the set from the live `on()` handlers: `widget.on('cheer', …)` → hub `Subscribe('cheer')`; dropping the last handler for an event → `Unsubscribe('cheer')`. The author **never** hand-manages subscriptions; registering a handler *is* subscribing, and the server sends that connection only what it asked for.
3. Reconnects with backoff (behaviour studied from the reference: a subtle connection-state indicator + a grace period before signalling "stale"), re-subscribing the current handler set on every re-establish.

`Widget.EventSubscriptions` (the entity field, `widgets-overlays.md`) becomes an optional **dashboard hint** (what a widget typically wants, for display) — not the gate. The runtime subscription is authoritative per connection.

**Hub surface (extend `OverlayHub`, `widgets-overlays.md` §7):** add `Subscribe(string eventName)` / `Unsubscribe(string eventName)` (validate against the registry; a session subscription set filters the push fan-out), and the request/response methods backing `get`/`history`/`state`/`actions` (`GetResource`, `GetHistory`, `StateGet`/`StateSet`, `InvokeAction`).

---

## 7. Config injection — typed, consolidated

Load-time injection stays (it works), but as **one typed manifest object**, not scattered globals. The overlay host page injects:

```html
<script>window.__NOMNOMZ__ = { widgetId, name, version, settings, hubUrl, token };</script>
```

`createWidget()` reads it, exposes `config = settings` typed to the widget's declared settings schema, and self-connects with `hubUrl`+`token`. Live changes arrive via `WidgetSettingsChanged` and update `config` in place (→ `onConfigChange`). Unknown keys are ignored. Settings are validated server-side against the widget's declared schema on save.

---

## 8. Actions obey the owner's IAM — no trust tiers

A widget is the owner's own code, tied to their identity; it acts **as them**. Every `actions.*` call is checked against the **owner's normal IAM permissions** (`roles-permissions.md` Gate-2) exactly as if they did it from the dashboard — if their role can send chat, the widget can; if not, it's denied with a typed error. There is **no** widget-specific policy, trust tier, or per-action grant.

The entire security story is one sentence: **the overlay token authorizes acting as you, so it is private and rotatable** (`Channels.OverlayToken`, already rotatable). Reads/events/render/state carry no risk and are ungated; the `actions.*` writes are gated only by the owner's IAM. `InvokeAction` on the hub resolves the channel owner from the token and calls the same `IActionAuthorizationService` the REST controllers use.

---

## 9. Now-playing without streaming position (anchor + extrapolation)

A progress bar must read the *correct* position at every moment without a per-second push. The `song.changed` payload (and `get('nowPlaying')`) carries a **position anchor**, not a live stream:

- Payload: `{ title, artist, durationMs, positionMs, isPlaying, serverTime }`.
- The widget records `positionMs` at `receivedAt = performance.now()` and draws `current = positionMs + (performance.now() − receivedAt)` in a local `requestAnimationFrame` loop — smooth, zero network.
- `pause` freezes the anchor; `resume`/seek re-anchors; the next `song.changed` (or poll) re-anchors and corrects drift.
- `serverTime` lets the SDK compute the server↔client clock offset once, so a wrong local clock can't skew the bar.

A pure-polling surface (no socket — the public SR page, or a dropped connection) does the identical thing, re-anchoring every poll instead of every push. The SDK exposes a `nowPlaying` helper that owns this loop so no widget re-implements it.

---

## 10. Authoring

- **Dashboard editor is the friendly, primary path** — the compile-on-save editor (`widgets-overlays.md`) scaffolds a new widget from a per-framework template pre-wired to the SDK + generated types, and compiles via esbuild.
- **A scaffold CLI (`create-nomnomz-widget`) is dev-facing only** — for local/IDE authoring and to make SDK testing easier; never the required route.
- Both stamp identical SDK-wired templates, so a widget moves between them unchanged.

---

## 11. Deliverables (build order)

1. `IAutomationEventDescriptor.PayloadType` + the `domain.action` `PublicName` rename across the registry; `WidgetEventContractTest` + snapshot.
2. `tools/WidgetTypeGen` (walks the registry → `events.generated.ts`), wired into CI.
3. `@nomnomzbot/widget` core (`createWidget`, event bus lifted from `player-core`, config, connection + subscribe-via-handlers, `get`/`history`/`state`/`sound`/`tts`/`render`/`actions`).
4. Framework adapters (`/vue`, `/react`, `/svelte`).
5. `OverlayHub` extensions (`Subscribe`/`Unsubscribe`, `GetResource`/`GetHistory`/`StateGet`/`StateSet`/`InvokeAction`); manifest injection in the overlay host page.
6. First-party widgets rebuilt on the SDK (chat box, now-playing, alerts, … — the `widgets-overlays.md` §1.1 catalogue), proving the SDK end-to-end.
7. `create-nomnomz-widget` CLI (dev tooling).

---

## 12. Decisions (resolved)

1. **Type source = backend contract, generated + drift-guarded** (not hand-authored). Single source of truth; mirrors the openapi/`ApiContractTest` flow.
2. **Distribution = published, versioned package** `@nomnomzbot/widget`, in-repo, installable by first-party and community authors alike.
3. **Naming = `domain.action`, past-tense, no aliases** — reliable and doc-free; codegen-validated.
4. **Subscribe = runtime, derived from handlers** — `on()`/`off()` drive `Subscribe`/`Unsubscribe`; the static entity field is a hint, not a gate.
5. **Security = the owner's IAM only** — no trust tiers, no gallery gating for self-authored widgets; the overlay token is the private, rotatable credential.
6. **Now-playing = position anchor + local extrapolation** (`positionMs` + `serverTime`), never a per-second stream.
7. **Authoring = dashboard editor primary, CLI dev-facing.**
