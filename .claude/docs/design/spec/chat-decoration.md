# Chat-message decoration (third-party emotes + enrichment)

**Status:** Implementable — decision-complete.
**Area:** Chat — server-side message decoration (BTTV / FFZ / 7TV emotes, badges, cheermote images, mentions, link previews).
**Conventions:** Clean Architecture, `NomNomzBot.*` namespaces, file-scoped namespaces, `Nullable` on, explicit types (never `var`), `async` all the way, `Result<T>` over throw/null, UUIDv7 keys, AGPL header on every file, CSharpier-formatted, `TreatWarningsAsErrors`. Newtonsoft for provider JSON. Tests prove the fragment-tree shape, not non-null.

> **Decided architecture (do not re-derive).** Decoration is **server-side** and **enrich-and-emit** — it never persists chat. The pipeline: EventSub `channel.chat.message` → `ChatMessageReceivedEvent` (Twitch already gives text / emote / cheermote / mention fragments) → tokenize text fragments to words → enrich with third-party emotes → attach badge/cheermote images, mention colors, and an optional link/OG preview → emit **one** enriched `DashboardChatMessageDto` over the existing `DashboardHub` chat flow. The client only renders; it never fetches emotes.

---

## 0. Pipeline & insertion point

The single chokepoint where every inbound message becomes the client DTO is `ChatMessageBroadcastHandler`
(`Api/Hubs/Broadcasters/DashboardBroadcastHandler.cs`), the `IEventHandler<ChatMessageReceivedEvent>` that maps the
event → `DashboardChatMessageDto` and calls `IDashboardNotifier.SendChatMessageAsync`. Decoration is inserted there:

```
ChatMessageReceivedEvent
  → IChatMessageDecorator.DecorateAsync(evt)        // thin orchestrator — seeds the context, runs the adapter chain
       seed ChatDecorationContext from evt.Fragments (Twitch-native: text/emote/cheermote/mention; emotes Provider=Twitch)
       then run the ordered IChatDecorationAdapter chain, each gated by AppliesTo (the streamer's rules):
         10 ExplodeText · 20 ThirdPartyEmote (7TV→BTTV→FFZ word match) · 30 TwitchEmoteUrl · 40 Badge ·
         50 Cheermote · 60 MentionColor · 70 LinkPreview (gated) · 80 ImplodeText
  → DecoratedChatMessage (enriched fragments + badges)
  → ChatMessageBroadcastHandler maps it to DashboardChatMessageDto → SignalR
```

Decoration is best-effort: any provider/cache/HTTP failure degrades to the un-enriched fragment (the message is never
dropped or delayed on a provider error). The decorator reads only from `ICacheService`; it never makes a provider HTTP
call on the chat hot path (that is the refresh worker's job, §3.6).

## 1. Entities & persisted state

**No new persisted tables, no new columns — this subsystem is enrich-and-emit.** It owns nothing in the locked schema.

| Concern | Where it lives | Schema ref |
|---|---|---|
| Chat messages | **Not persisted by this subsystem.** Decoration runs on the wire, never into the DB. | `G.1 ChatMessages` (untouched) |
| Per-channel toggles | `ChannelFeature` rows — `FeatureKey` ∈ `use_bttv`, `use_ffz`, `use_7tv`, `use_link_preview` (`string(50)`, fits) | `P.x ChannelFeature` (existing; data only) |
| Resolved emote / badge / cheermote sets | `ICacheService` (L1 self-host, L1+L2 SaaS) — **not the DB** | — (cache keys in §7) |
| Enriched fragment tree | Value object `ChatMessageFragment` (extended, §4) + wire DTO `ChatFragmentDto` (extended, §4) — serialized into the SignalR DTO, never a column | — |

**Schema note (must reconcile, not introduced here):** the live `ChatMessage` entity (`Domain/Chat/Entities/ChatMessage.cs`)
diverges from locked `G.1 ChatMessages` — it keys on a `string(255)` Twitch message id with an inline `UserId string(50)`,
whereas `G.1` specifies `bigint Id` + `TwitchMessageId` + a surrogate `AuthorUserId guid`. This subsystem does **not**
persist chat, so it does not depend on the resolution, but the divergence is flagged for the owning chat/persistence slice.

## 2. Domain events

- **Consumes** `ChatMessageReceivedEvent` (`Domain/Chat/Events/`) — the existing hot-path event. Decoration is a transform in
  the broadcast handler, not a new subscriber, so it does **not** add a second handler that re-reads the message.
- **No new domain events.** Set-refresh is internal to the worker (§3.6); it publishes nothing.

## 3. Service interfaces (full signatures)

### 3.1 `IChatMessageDecorator` + `IChatDecorationAdapter` — NEW (`Application/Chat/Services/`)

```csharp
public interface IChatMessageDecorator
{
    // Best-effort: never throws on a provider/cache miss; returns the message with whatever enriched.
    Task<DecoratedChatMessage> DecorateAsync(ChatMessageReceivedEvent message, CancellationToken ct = default);
}

// Every decoration step is a pluggable ADAPTER. Adding a provider, a new enrichment, or any future concern = drop in ONE
// new adapter class — it self-registers (AddImplementationsOf) and slots into the chain by Order. No orchestrator edit.
public interface IChatDecorationAdapter
{
    int Order { get; }                                                  // pipeline position (gaps of 10 leave room)
    bool AppliesTo(ChatDecorationContext context);                      // cheap gate (feature flag / standing / content)
    Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default);  // mutate in place, best-effort
}
```

Impl `ChatMessageDecorator` (`Infrastructure/Chat/`) is a **thin orchestrator** that owns no enrichment logic: it seeds a
mutable `ChatDecorationContext` (the fragment list + badge list + the channel's resolved rules) from the event, then runs
the **discovered, ordered chain of `IChatDecorationAdapter`** — each step gated by its own `AppliesTo`, each best-effort (a
throwing adapter is skipped, the message still emits). The seeded adapters (`Infrastructure/Chat/Adapters/`, all
`IChatDecorationAdapter`): `ExplodeTextAdapter` (10) → `ThirdPartyEmoteAdapter` (20, fans out to the
`IThirdPartyEmoteProvider` registry §3.2, gated per provider by `use_bttv`/`use_ffz`/`use_7tv`) → `TwitchEmoteUrlAdapter`
(30) → `BadgeAdapter` (40) → `CheermoteAdapter` (50) → `MentionColorAdapter` (60) → `LinkPreviewAdapter` (70, gated by
`use_link_preview` + standing) → `ImplodeTextAdapter` (80). The interfaces in §3.3–§3.5 are the adapters' internal helpers.

### 3.2 `IThirdPartyEmoteProvider` — NEW (`Application/Chat/Services/`)

```csharp
public interface IThirdPartyEmoteProvider
{
    EmoteProvider Provider { get; }                                            // Bttv | Ffz | SevenTv
    Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(CancellationToken ct = default);
    // login vs id is provider-specific (FFZ uses login, BTTV/7TV use the Twitch broadcaster id) — both are passed.
    Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(string twitchBroadcasterId, string broadcasterLogin, CancellationToken ct = default);
}
```

Three impls in `Infrastructure/Chat/Providers/`: `BttvEmoteProvider`, `FfzEmoteProvider`, `SevenTvEmoteProvider`. They are
discovered as `IThirdPartyEmoteProvider` impls and resolved through `IThirdPartyEmoteProviderRegistry` (NEW, indexes by
`Provider`) — never a `switch`. Each maps its raw JSON (Newtonsoft) to `ChatEmote` (§4) with `Provider` set to its own provider.

### 3.3 `IChatBadgeResolver` — NEW (`Application/Chat/Services/`)

```csharp
public interface IChatBadgeResolver
{
    // Resolves a message's badges (set_id + version id) to image URLs from the cached global+channel Helix badge sets.
    Task<IReadOnlyList<ResolvedChatBadge>> ResolveAsync(Guid broadcasterId, IReadOnlyList<ChatBadge> badges, CancellationToken ct = default);
}
```

### 3.4 `ICheermoteResolver` — NEW (`Application/Chat/Services/`)

```csharp
public interface ICheermoteResolver
{
    // prefix+bits+tier (from EventSub) → the tier's animated/static image URL set (from cached Helix bits/cheermotes).
    Task<CheermoteImage?> ResolveAsync(Guid broadcasterId, string prefix, int bits, int tier, CancellationToken ct = default);
}
```

### 3.5 `ILinkPreviewService` — NEW (`Application/Chat/Services/`)

```csharp
public interface ILinkPreviewService
{
    // OpenGraph fetch via the SSRF-hardened "egress-allowlisted" client; null on non-preview or failure.
    Task<Result<LinkPreview?>> FetchAsync(Uri url, CancellationToken ct = default);
}
```

### 3.6 `ChatDecorationRefreshService` — NEW background worker (`Infrastructure/Chat/Jobs/`)

`: BackgroundService` (auto-discovered by `AddHostedWorkers`). Keeps `ICacheService` warm:

- **Global** emote sets (per provider) + global Helix badges + bits/cheermotes — refreshed on startup and every **6 h**.
- **Per-channel** emote/badge/cheermote sets — refreshed on `stream.online`, every **5 min while live** (the staleness window
  for a newly-added emote), and **lazily** the first time a message arrives for a channel whose set is absent from cache (a
  single guarded fetch). The cache TTL (§7) is the stale **ceiling**, not the cadence — the worker re-warms before expiry, so
  a cache hit is always fresh-enough.
- All fetches go through the resilient named client (§7); a failure keeps the **last-good** cache entry (stale-OK, never wiped),
  empty only if there has never been a successful fetch — the worker never throws into the host.
- **Near-instant sync (future, drop-in):** a `SevenTvLiveSyncAdapter` may subscribe to the 7TV EventAPI (one shared WebSocket,
  `emote_set.update` per live channel) to push-invalidate a channel's 7TV cache the instant the streamer changes their set,
  cutting the staleness window to seconds for the dominant provider. It plugs in as another refresh source behind the existing
  interfaces — not v1-blocking (§9·13).

### 3.7 `IFeatureService` — EXTEND (`Application/Platform/Services/`)

Add a single-flag runtime accessor so the decorator can gate cheaply (today the service only lists/toggles):

```csharp
Task<bool> IsEnabledAsync(Guid broadcasterId, string featureKey, CancellationToken ct = default);   // NEW
```

Impl resolves the `ChannelFeature` row (`Unique (BroadcasterId, FeatureKey)`); absent ⇒ `false` (fail-closed/off).

## 4. DTOs / contracts (the discriminated-union fragment model)

The fragment is a union by `Type`: `text | emote | cheermote | mention | link`. **Third-party emotes are not a separate
type — they become real emotes.** Twitch and BTTV/FFZ/7TV emotes share ONE `emote` fragment carrying a nested `ChatEmote`;
a `Provider` field names the source and the decorator fills `Urls`/`Animated`/`ZeroWidth` for all of them, so the client
renders any emote identically (KISS — this is how the legacy bot's decorator already works). Changes are additive on the
existing value object + wire DTO.

```csharp
// Domain/Chat/ValueObjects/ChatMessageFragment.cs — EXTEND (sealed, init-only)
//   Type ∈ "text" | "emote" | "cheermote" | "mention" | "link"
public sealed class ChatMessageFragment
{
    public required string Type { get; init; }
    public string? Text { get; init; }

    // emote — Twitch AND third-party are ONE shape; Provider names the source. The decorator fills
    // Urls/Animated/ZeroWidth so every emote renders the same way (a 7TV/BTTV/FFZ emote IS a real emote).
    public ChatEmote? Emote { get; init; }

    // cheermote (existing prefix/bits/tier) + resolved image (NEW)
    public string? CheermotePrefix { get; init; }
    public int? CheermoteBits { get; init; }
    public int? CheermoteTier { get; init; }
    public CheermoteImage? CheermoteImage { get; init; }

    // mention (existing) + resolved chat color (NEW)
    public string? MentionUserId { get; init; }
    public string? MentionUserLogin { get; init; }
    public string? MentionUserName { get; init; }
    public string? MentionColorHex { get; init; }

    // link (NEW)
    public string? LinkUrl { get; init; }
    public LinkPreview? LinkPreview { get; init; }
}

public enum EmoteProvider { Twitch, Bttv, Ffz, SevenTv }

// One emote shape for EVERY provider — the only emote model (Twitch emotes use it too). Urls keyed by scale
// "1".."4"; ZeroWidth (7TV overlay) lets the renderer stack this emote over the preceding one — false for the rest.
// SetId/OwnerId/Formats are populated for Provider=Twitch only.
public sealed record ChatEmote(
    EmoteProvider Provider,
    string Id,
    string Code,
    IReadOnlyDictionary<string, string> Urls,
    bool Animated,
    bool ZeroWidth,
    string? SetId = null,
    string? OwnerId = null,
    IReadOnlyList<string>? Formats = null);

public sealed record CheermoteImage(IReadOnlyDictionary<string, string> Urls, bool Animated, string ColorHex);
public sealed record ResolvedChatBadge(string SetId, string Id, string? Info, IReadOnlyDictionary<string, string> Urls);
public sealed record LinkPreview(string Host, string? Title, string? Description, string? ImageUrl);

// the decorator's output (Application/Chat/Dtos/)
public sealed record DecoratedChatMessage(
    IReadOnlyList<ChatMessageFragment> Fragments,
    IReadOnlyList<ResolvedChatBadge> Badges);
```

Wire DTO (`Api/Hubs/Dtos/ChatDtos.cs`): there stays ONE emote DTO — `ChatEmoteDto` gains `Provider`, `Urls`, `Animated`,
`ZeroWidth` (Twitch and third-party emotes are the same DTO); `ChatFragmentDto` gains `CheermoteImage`, `MentionColorHex`,
`LinkUrl`, `LinkPreview`; `ChatBadgeDto` gains `Urls`. Names stay camelCase to match the frontend `ChatMessagePayload`.

## 5. Controller endpoints

**No new controller or REST surface.** The four per-channel toggles flow through the **existing** `FeaturesController`
(`POST channels/{channelId}/features/{featureKey}/toggle`, gated `feature:write`; `GET …/features`, `feature:read`) using the
keys `use_bttv` / `use_ffz` / `use_7tv` / `use_link_preview`. Decoration itself is a broadcast-path transform, not a request.

## 6. Pipeline actions

None. Decoration is not a pipeline action — it runs in the chat broadcast path, not the command/event pipeline engine.

## 7. DI registration

```csharp
// All auto-discovered (no manual lines) per backend-structure §D5:
//   IChatMessageDecorator, IThirdPartyEmoteProviderRegistry, IChatBadgeResolver, ICheermoteResolver,
//   ILinkPreviewService  → I{X}Service convention scan
//   the 8 *Adapter classes → AddImplementationsOf<IChatDecorationAdapter> (orchestrator runs them ordered by Order)
//   BttvEmoteProvider/FfzEmoteProvider/SevenTvEmoteProvider → AddImplementationsOf<IThirdPartyEmoteProvider>
//   ChatDecorationRefreshService → AddHostedWorkers
// Named HttpClient "chat-emote-providers" (BTTV/FFZ/7TV/Twitch-Helix-badges) with the resilience pipeline
//   (retry=3 exponential+jitter, 10s timeout, circuit breaker) modelled on AddTwitchResilienceHandler.
// Link-preview reuses the SSRF-hardened "egress-allowlisted" client (resolve-then-pin, https-only,
//   internal/metadata IPs blocked) — never a fresh HttpClient.
```

**Cache keys (`ICacheService`):** `chat:emotes:{provider}:global` · `chat:emotes:{provider}:channel:{broadcasterId}` ·
`chat:badges:global` · `chat:badges:channel:{broadcasterId}` · `chat:cheermotes:channel:{broadcasterId}`. **TTL:** 6 h global,
1 h channel (matching the refresh cadence; the worker re-warms before expiry, TTL is the stale ceiling).

## 8. Dependencies

| Dependency | Party | Use here |
|---|---|---|
| BTTV `api.betterttv.net/3` | 3rd | `GET /cached/emotes/global`, `GET /cached/users/twitch/{broadcasterId}` (id) |
| FFZ `api.frankerfacez.com/v1` | 3rd | `GET /set/global`, `GET /room/{login}` (login, **not** id) |
| 7TV `7tv.io/v3` | 3rd | `GET /emote-sets/global`, `GET /users/twitch/{broadcasterId}` → `emote_set` (id) |
| Twitch Helix | 1st-party-ish | `GET /chat/badges` + `?broadcaster_id=`, `GET /bits/cheermotes` (+ id) — via the existing Helix transport |
| Newtonsoft.Json | 2nd (MS-adjacent, project default) | provider JSON parsing |
| `egress-allowlisted` client | internal | SSRF-hardened link-preview fetch |

## 9. Decisions (resolved)

1. **Enrich-and-emit, never persist** (§1). Decoration mutates the wire DTO only; no chat rows are written and no schema
   table is added. The emote/badge/cheermote sets are cached via `ICacheService`, not the DB.
2. **7TV animated + zero-width/overlay are first-class** (the legacy ignored them). `data.animated` ⇒ `Animated`; 7TV
   `flags & 0x100` (256) ⇒ `ZeroWidth`; the URL set is built from `host.url` + `host.files` choosing the best `webp`/`avif`
   per scale. A zero-width emote renders stacked over the preceding emote; the renderer keys off the `ZeroWidth` flag.
3. **Cheermote images are resolved** (the legacy dropped them). `prefix+bits+tier` (EventSub) → the matching tier's
   animated image set + `ColorHex` from cached Helix `bits/cheermotes`.
4. **BTTV/FFZ animated flags are propagated** — `ChatEmote.Animated` is set from BTTV `animated` and (FFZ) the
   presence of an animated url variant; FFZ urls are reused verbatim from the response (the only provider that ships them).
5. **Word-level, case-insensitive exact match** on emote code, on per-word `text` fragments produced by ExplodeText. A
   matched word becomes an `emote` fragment (`Provider` = the matching provider) in place; multi-word fragments are split first.
6. **Precedence is explicit and deterministic** (the legacy's was implicit/inconsistent). Within a provider, the **channel**
   set wins over **global** on a code collision (more specific). Across providers, fixed order **7TV → BTTV → FFZ**; the first
   provider to claim a word wins, later providers skip non-`text` fragments.
7. **Refresh is periodic, not startup-only** (§3.6): global 6 h, channel on `stream.online` + hourly-while-live + lazy-on-first-
   message; failures fall back to stale cache, then empty — never throw into the host or block the chat path.
8. **One unified `emote` type — third-party emotes ARE real emotes (KISS).** No separate `third_party_emote` type/field;
   Twitch and BTTV/FFZ/7TV emotes share one `emote` fragment + one `ChatEmote`/`ChatEmoteDto`, distinguished only by
   `Provider`. The decorator fills `Urls`/`Animated`/`ZeroWidth` so the client renders any emote identically — exactly the
   legacy decorator's model. The existing `"emote"` type and `ChatEmoteDto` are extended in place, not split.
9. **Link/OG preview is double-gated and off by default**: the `use_link_preview` `ChannelFeature` toggle (default off) **and**
   the author's effective `CommunityStanding ≥ Subscriber` (via `IRoleResolver`, the modern equivalent of the legacy
   "subscriber+" gate). Fetch is via the SSRF-hardened egress client; non-http(s)/internal targets yield no preview.
10. **Provider clients are a registry, not a switch** (`IThirdPartyEmoteProviderRegistry`), each auto-discovered; adding a
    provider = a new `IThirdPartyEmoteProvider` impl, no DI edit (backend-structure §D5).
11. **Self-host vs SaaS** differ only in the cache adapter (L1 vs L1+L2) behind the unchanged `ICacheService`; the decorator
    is identical on both. No file-cache (the legacy's approach) — the cache is `ICacheService`.
12. **Every decorator is a pluggable adapter** (`IChatDecorationAdapter`, §3.1): the orchestrator owns no enrichment logic —
    it discovers, orders, and runs the adapter chain. Two independent extension points, both self-registering with **zero
    orchestrator/DI edits**: a new emote provider (an `IThirdPartyEmoteProvider`, Decision 10) or a whole new decoration
    concern (an `IChatDecorationAdapter` at a new `Order`). "Add more later" = add one class.
13. **A missing emote url is structurally impossible; staleness degrades to text, never a broken image.** Twitch emote urls
    are a deterministic CDN template from the payload id (no lookup, nothing to sync). A third-party `emote` fragment is
    created ONLY on a cache hit, and every cached `ChatEmote` carries its full `Urls` (built at fetch) — a miss leaves the
    word as plain text. The only variable is set freshness: bounded to ~5 min while live (§3.6) + stale-OK fallback (last-good
    set on a refresh failure, never wiped), with the optional 7TV EventAPI live-sync adapter cutting it to seconds. No emote
    fragment is ever emitted without a complete url set.
