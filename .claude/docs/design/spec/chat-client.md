# Dashboard Chat Client — Interface Specification

**Status:** Implementable. Code from this directly.
**Scope:** The dashboard **Chat** page as a full operator chat client — the message feed contract (live + scrollback, one decorated shape), the **send composer** (send identity, emote-enabled rich input, emote autocomplete), the pronoun / avatar / local-time / animated **render contract**, and the **cross-channel** moderation quick-actions. This spec owns the *client-facing chat surface*; it **renders** what `chat-decoration.md` decorates, **performs** what `moderation.md` defines, **sends** through `twitch-helix.md`, and is **gated** by `roles-permissions.md`.
**Grounds:** `chat-decoration.md` (fragment/emote/badge model), `moderation.md` (ban/timeout/delete + `INetworkNukeService`), `identity-auth.md` (`ICurrentUserService`, token vault), `twitch-helix.md` (`ITwitchChatAssetsApi`, `ITwitchModerationApi`, `ITwitchModeratorsApi`, `ITwitchTokenResolver`), `roles-permissions.md` (Gate-2 keys, `IRoleResolver`), `pronouns.md`, `frontend-ia.md` (the Chat page).

## Conventions inherited (binding)

Clean Architecture, `NomNomzBot.*` namespaces, file-scoped namespaces, `Nullable` on, **explicit types (never `var`)**, `async` all the way, `Result<T>` over throw/null, UUIDv7 keys, AGPL header on every source file, CSharpier-formatted, `TreatWarningsAsErrors`. Tests prove behaviour/shape, not non-null. **Every new Gate-2 action key introduced here is seeded in `roles-permissions.md` §7.1 and `ActionDefinitionSeeder` in the same slice** — a §5 cell whose key is absent from the seed catalogue is a seed bug.

> **Decided architecture (do not re-derive).** The client is a **thin renderer over a fully-server-decorated payload** and a **thin composer over server-provided identity + emote catalogue**. The server already emits one enriched `DashboardChatMessageDto` per message (chat-decoration.md §0); this spec makes **history emit that identical shape**, adds the **operator send identity** and the **emote catalogue**, and wires **cross-channel ban** onto Twitch's own "channels I moderate". No message is ever persisted by this surface beyond what `moderation.md` already writes.

---

## 0. Surfaces, planes & the client render contract

The Chat page is three surfaces on one screen, all scoped to the joined `channelId`:

1. **Feed** (read) — live (`DashboardHub` push) + scrollback (`GET …/chat/messages`). Both MUST carry the **same** decorated+enriched shape (Decision 9).
2. **Composer** (send) — a rich, emote-aware input that sends **as the operator** by default, optionally **as the bot** (Decision 1), with emote **autocomplete** and **inline emote images** (Decision 5).
3. **Quick-mod** (moderate) — ban / timeout / delete on a message or user, single-channel or **every channel the operator moderates** (Decision 6).

**Client render contract (the server guarantee ⇒ what the client MUST render).** These fields already ride the wire on every live `DashboardChatMessageDto`; the client's only job is to render them. Where the client fails to today, it is a *client* fix, not a server gap:

| Payload field (server guarantees) | Client must render | Today's gap (#) |
|---|---|---|
| `Fragments[].Emote.Urls` + `Animated=true` + `/animated/` url | play the animated image (WebP/GIF), not a static first frame | **#3** |
| `Fragments[].Emote.ZeroWidth=true` | stack the emote over the preceding one (7TV overlay) | — |
| `Pronouns` (e.g. `"He/Him"`, via `IHubUserEnricher` → alejo.io) | a pronoun **badge/chip** beside the name | **#7** |
| `AvatarUrl` | the chatter avatar | — |
| `Timestamp` (UTC ISO-8601 `"O"`) | formatted in the **viewer's local time** | **#8** |
| one push per message, unbatched (`ChatMessageBroadcastHandler`) | append on receive — **no buffer that withholds the newest message** | **#2** |
| `Badges[].Urls` (chat-decoration §3.3) | badge images | (shipped `b7b7eab`) |

The feed is **not** a place the client may hold, debounce, or re-order messages; ordering is server emit order, append-only. The composer renders emotes **inline** by tokenising the draft against the catalogue (§3.2) and swapping matched codes for their images — purely local, no round-trip per keystroke.

---

## 1. Entities & persisted state

**No new tables, no new columns — this surface owns nothing in the locked schema.** It renders wire DTOs and reuses existing writes:

| Concern | Where it lives | Schema ref |
|---|---|---|
| Chat messages | Persisted by the existing chat-persistence handler; this surface only reads/decorates them | `G.1 ChatMessages` (untouched) |
| Emote / badge / cheermote sets | `ICacheService` (chat-decoration §7) + on-demand Helix (`ITwitchChatAssetsApi`) — never the DB | — |
| Ban / timeout / delete effects | `ModerationAction` rows written by `moderation.md` §3.1 (one per channel acted), plus its audit + `UserModerationHistory` | `M.x ModerationAction` (existing) |
| Operator Twitch token | Existing `IntegrationConnection`(`Provider='twitch'`) + `IntegrationTokens` — read via the vault | `E.1 / E.2` (existing) |
| Progressive scope grants | Existing scope-grant flow (`FeatureScopeMap`) — data only, no schema | — |

## 2. Domain events

- **Consumes** `ChatMessageReceivedEvent` — via the existing `ChatMessageBroadcastHandler` (no second handler).
- **Reuses** the moderation events (`BanIssuedEvent`, audit, `SharedChatBanIssuedEvent`) — **one per channel actually acted on** by the cross-channel fan-out; the fan-out is orchestration, not a new domain concept, so it adds **no new event**.
- **No new domain events.**

## 3. Service interfaces (full signatures)

### 3.1 `ITwitchTokenResolver` — EXTEND (`Application/Contracts/Twitch/`)

Today the resolver knows only the **bot** token and the **broadcaster (tenant-owner)** token, both keyed by `broadcasterId`. Sending as the *logged-in operator* (who may be a moderator, not the owner) needs their **own** token. Add one method:

```csharp
// Resolves the operator's OWN Twitch user connection (Provider "twitch", ProviderAccountId = their TwitchUserId,
// ConnectedByUserId = userId), independent of any tenant. Returns their token + their Twitch user id as the send
// identity. no_token when the caller has no Twitch connection (impossible for a Twitch-logged-in user, handled anyway).
Task<Result<TwitchAccessContext>> GetUserTokenAsync(Guid userId, CancellationToken ct = default);   // NEW
```

`TwitchAccessContext` is unchanged; `BroadcasterId` is null for an operator token (it is not tenant-scoped). The bucket key derives from the connection identity exactly as the existing paths (twitch-helix §3.5), so per-operator rate-limit buckets stay stable across refresh.

### 3.2 `IChatEmoteCatalogue` — NEW (`Application/Chat/Services/`)

The composer's source of truth: the emotes the **operator can actually send in this channel**, unified across providers into the one `ChatEmote` shape (chat-decoration §4) so the composer renders them identically to the feed.

```csharp
public interface IChatEmoteCatalogue
{
    // Assembles: Twitch global + this channel's Twitch emotes + the operator's own usable Twitch emotes
    // (Get User Emotes) + BTTV/FFZ/7TV global+channel. Deduped by code with chat-decoration precedence
    // (channel-before-global; 7TV→BTTV→FFZ). Best-effort: a provider miss omits that provider, never fails the call.
    Task<Result<IReadOnlyList<ChatEmote>>> GetForChannelAsync(
        Guid broadcasterId, Guid operatorUserId, CancellationToken ct = default);
}
```

Impl `ChatEmoteCatalogue` (`Infrastructure/Chat/`): reads BTTV/FFZ/7TV + Twitch-channel/global sets from `ICacheService` (already warm — chat-decoration §3.6); fetches the operator's usable Twitch emotes on demand via `ITwitchChatAssetsApi.GetUserEmotesAsync` (scope `user:read:emotes`) and caches them per operator for a short TTL (§7). Twitch global emotes are warmed once (§3.4). Matching/precedence reuses the existing `ChannelEmoteIndex`. The catalogue is **returned whole** for the channel (a few hundred–low-thousands of emotes); the client filters by prefix locally for instant autocomplete — no per-keystroke round-trip.

### 3.3 `IOperatorChatSender` — NEW (`Application/Chat/Services/`)

The composer's send verb, distinct from the bot's `IChatProvider.SendMessageAsync` (which stays for automation/announcements):

```csharp
public interface IOperatorChatSender
{
    // Sends to broadcasterId AS operatorUserId — the operator's own token + their Twitch user id as sender_id
    // (POST /helix/chat/messages, user:write:chat). Reply-parent optional. Honest Result: a dead token / a
    // channel the operator is banned/timed-out in surfaces as a failure, never a silent success.
    Task<Result> SendAsUserAsync(
        Guid operatorUserId, Guid broadcasterId, string message, string? replyToMessageId,
        CancellationToken ct = default);
}
```

Impl `OperatorChatSender` (`Infrastructure/Chat/`): `GetUserTokenAsync(operatorUserId)` → `ITwitchIdentityResolver.GetTwitchChannelIdAsync(broadcasterId)` → `POST /helix/chat/messages` on the operator context with `sender_id` = the operator's Twitch id. A `403` (banned / not permitted in that channel) maps to a typed failure the composer surfaces plainly.

### 3.4 `IChatEmoteCatalogueWarmer` — NEW background contribution (`Infrastructure/Chat/Jobs/`)

Twitch **first-party** emotes are not cached today (only reactively resolved from message payloads). The catalogue needs the channel + global Twitch sets warm. Fold this into the existing `ChatDecorationRefreshService` cadence (chat-decoration §3.6) as an additional warm source (not a second worker): **Twitch global** emotes on startup + every 6 h; **Twitch channel** emotes on `stream.online` + every 5 min while live + lazy-on-first-composer-open. The operator's **user-emotes** are never globally warmable (per-operator) — fetched on demand (§3.2), cached per operator for 60 s.

### 3.5 Cross-channel ban — EXTEND `moderation.md` §3.4 `INetworkNukeService`

`INetworkNukeService.NukeAsync` already "bans a target across every channel the actor holds ban rights on", but resolves that channel set from the **local DB**. Rewire its resolution to Twitch's authority and expose it as the ban dialog's "every channel I moderate" option:

```csharp
// EXTEND: the actor's channel set is Twitch's Get Moderated Channels for the actor's Twitch id
// (GetModeratedChannelsAsync, user:read:moderated_channels), NOT the local DB — so it covers EVERY channel Twitch
// says the operator moderates, tenant or not. Each ban is issued AS THE OPERATOR (their token, moderator_id = them,
// moderator:manage:banned_users). Best-effort, per-channel outcome; a channel that fails (rate-limit / no longer mod)
// is reported, never aborts the rest.
Task<Result<NetworkBanResult>> BanAcrossModeratedAsync(
    Guid operatorUserId, string targetTwitchUserId, string? reason, CancellationToken ct = default);   // NEW verb
```

`NetworkBanResult` carries per-channel outcomes (channel id/login, succeeded, error). Single-channel ban stays `moderation.md` §3.1 `IModerationService.BanAsync` unchanged. **Relationship (do not conflate):** this operator-scoped, Twitch-gated fan-out is distinct from `moderation:nuke` (a SuperMod *platform* power over tenant channels regardless of the actor's per-channel Twitch mod status). Twitch is the sole authority here — the operator can only ban where Twitch already made them a moderator, so there is zero privilege escalation.

### 3.6 `IChatController` history parity — EXTEND

`ChatController.GetMessages` currently returns a **reduced** `ChatMessageDto` (no pronouns, no avatar, broadcast-time only). Upgrade it to emit the **same** `DashboardChatMessageDto` shape as the live hub: run the decorator (already does) **and** the `IHubUserEnricher` per row (pronouns/avatar), and carry the row's real `CreatedAt` as `Timestamp`. This removes the live-vs-history drift (Decision 9); enrichment is cache-gated (30 s) so a page of 25–50 rows collapses to few reads.

## 4. DTOs / contracts

The feed DTO is the **existing** `DashboardChatMessageDto` (chat-decoration §4) — unchanged, already fully decorated (fragments, badges, color, roles, reply, `AvatarUrl`, `Pronouns`, `Timestamp`). History switches to it (§3.6). New contracts are the composer + moderation surfaces only:

```csharp
// Composer catalogue (Api/Controllers/V1 chat DTOs) — the ChatEmote shape flattened for the wire, camelCase.
public sealed record ChatEmoteCatalogueDto(
    string Code, string Provider, IReadOnlyDictionary<string,string> Urls, bool Animated, bool ZeroWidth,
    string? SetId);

// Send — identity selector defaults to the operator ("you"); "bot" routes to the existing bot send.
public sealed record SendChatMessageRequest(string Message, string SenderIdentity = "you", string? ReplyToMessageId = null);
//   SenderIdentity ∈ "you" | "bot"

// Cross-channel ban — scope defaults to this channel; "all_moderated" fans out (§3.5).
public sealed record BanUserRequest(string TargetTwitchUserId, string? Reason = null, int? DurationSeconds = null,
    string Scope = "this_channel");
//   Scope ∈ "this_channel" | "all_moderated"

public sealed record NetworkBanResultDto(int Attempted, int Succeeded, IReadOnlyList<ChannelBanOutcomeDto> Channels);
public sealed record ChannelBanOutcomeDto(string BroadcasterLogin, bool Succeeded, string? Error);

// The operator's moderated-channel list for the "every channel I moderate (N)" prompt.
public sealed record ModeratedChannelDto(string BroadcasterId, string BroadcasterLogin, string BroadcasterName);
```

## 5. Controller endpoints

All under `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/…")]`, `[ApiVersion("1.0")]`, `[Authorize]`, results via `BaseController`. **Gate 1** (entry, no floor): `[Authorize]` + `TenantResolutionMiddleware` / `IChannelAccessService`. **Gate 2** (per-row floor): `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` before the service call (403 below floor). Roles are `ManagementRole` names (PascalCase, never snake-case).

**`…/chat` (ChatController — EXTEND):**

| Method | Route (suffix under `…/chat`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|--------|-------------------------------|-------------|--------------|-----------------------------------|
| GET | `/messages` | — | `StatusResponseDto<List<DashboardChatMessageDto>>` | management / Moderator · `chat:read` |
| POST | `/messages` | `SendChatMessageRequest` | `StatusResponseDto<bool>` | management / Moderator · `chat:send` |
| GET | `/emotes` | — | `StatusResponseDto<IReadOnlyList<ChatEmoteCatalogueDto>>` | management / Moderator · `chat:read` |
| DELETE | `/messages/{messageId}` | — | `StatusResponseDto<bool>` | management / Moderator · `moderation:delete_message` |

**`…/moderation` (ModerationController — EXTEND, `moderation.md` §5):**

| Method | Route (suffix under `…/moderation`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|--------|-------------------------------------|-------------|--------------|-----------------------------------|
| POST | `/actions/ban` | `BanUserRequest` | `StatusResponseDto<NetworkBanResultDto>` | management / Moderator · `moderation:ban` |
| GET | `/moderated-channels` | — | `StatusResponseDto<IReadOnlyList<ModeratedChannelDto>>` | management / Moderator · `moderation:action:read` |

`POST /actions/ban` returns a `NetworkBanResultDto` for both scopes — `this_channel` is a one-row result. The `all_moderated` scope requires the operator token to carry `user:read:moderated_channels` + `moderator:manage:banned_users`; a missing scope yields the standard progressive-scope action-required response (never a logout). The composer's send hub verb (`DashboardHub.SendChatMessage`) is updated to the same identity contract as `POST /messages` (Decision 1) — it already enforces `chat:send`.

## 6. Pipeline actions

None new. The composer, feed, and quick-mod are dashboard request/hub surfaces, not command/event-pipeline actions. The existing `Ban`/`Timeout`/`SendMessage` pipeline actions are unaffected (they remain bot-identity automation).

## 7. DI registration

```csharp
// All auto-discovered (no manual lines) per backend-structure §D5:
//   IChatEmoteCatalogue, IOperatorChatSender  → I{X}Service / interface scan
//   ChatEmoteCatalogue warm source            → folded into ChatDecorationRefreshService (§3.4), not a new worker
//   GetUserTokenAsync                          → method on the existing ITwitchTokenResolver impl
//   BanAcrossModeratedAsync                    → method on the existing INetworkNukeService impl
// New Gate-2 action keys seeded in ActionDefinitionSeeder AND roles-permissions §7.1 (Management plane):
//   chat:read   — Moderator(10), Low,  Grant=true   (feed + emote catalogue + settings-read)
//   chat:send   — Moderator(10), Low,  Grant=true   (composer send, as operator or bot)
//   (reused, already seeded: moderation:ban, moderation:delete_message, moderation:action:read)
// FeatureScopeMap additions (progressive scopes; enabling the feature triggers the additive re-grant, never a logout):
//   "chat_send"            → ["user:write:chat"]              (already granted at login — RequiredScopes)
//   "chat_emote_catalogue" → ["user:read:emotes"]            (operator's own usable Twitch emotes)
//   "moderation_network"   → ["user:read:moderated_channels","moderator:manage:banned_users"]
```

**Cache keys (`ICacheService`):** reuse chat-decoration's — `chat:emotes:{provider}:global`, `chat:emotes:{provider}:channel:{twitchBroadcasterId}` (Twitch-id keyed), `chat:badges:*`. Add `chat:emotes:twitch:global`, `chat:emotes:twitch:channel:{twitchBroadcasterId}` (warmed §3.4) and `chat:emotes:twitch:user:{operatorUserId}` (60 s, per operator). **TTL:** 6 h global, 1 h channel, 60 s user.

## 8. Dependencies

| Dependency | Party | Use here |
|---|---|---|
| Twitch Helix — Send Chat Message | 1st | `POST /chat/messages` as the operator (`user:write:chat`) — §3.3 |
| Twitch Helix — Chat assets | 1st | `ITwitchChatAssetsApi` Get Global/Channel/User Emotes — §3.2 (`user:read:emotes` for user emotes) |
| Twitch Helix — Moderation | 1st | `ITwitchModerationApi.BanUserAsync` (`moderator:manage:banned_users`) — §3.5 |
| Twitch Helix — Get Moderated Channels | 1st | `ITwitchModeratorsApi.GetModeratedChannelsAsync` (`user:read:moderated_channels`) — §3.5 |
| BTTV / FFZ / 7TV | 3rd | emote catalogue rows from the warm cache (chat-decoration §8) — §3.2 |
| alejo.io pronouns | 3rd | `Pronouns` on the enriched DTO (existing `IHubUserEnricher`) — §0 |

## 9. Decisions (resolved)

1. **The composer sends as the OPERATOR, not the bot** — the logged-in user's own Twitch identity (their token + their Twitch id as `sender_id`). This is the multi-mod-correct default: a moderator typing in a channel they moderate appears as themselves; the streamer in their own channel appears as themselves. An explicit **identity selector** in the composer switches to **Bot** for bot-voice posts. **Automation** (commands, timers, event responses, announcements) is unchanged and still speaks as the bot. Capability is never removed — bot-send stays reachable, it is just not the default.
2. **A new per-operator token path** (`GetUserTokenAsync(Guid userId)`, §3.1) — `ITwitchTokenResolver` previously knew only bot + broadcaster tokens. `user:write:chat` is already granted to every Twitch login (`AuthService.RequiredScopes`), so send-as-you needs **no re-auth**.
3. **`chat:send` and `chat:read` are introduced and seeded** (§7). They were used in code but absent from the seed catalogue; this makes them real Gate-2 keys at the Moderator floor (matching the Chat page's `frontend-ia.md` floor).
4. **One emote catalogue endpoint, unified shape, client-side filter** (§3.2, §5). `GET …/chat/emotes` returns the operator's usable set for the channel — Twitch global+channel+user-emotes and BTTV/FFZ/7TV — as the single `ChatEmote` shape, deduped with chat-decoration precedence. The composer filters locally for instant `:prefix` autocomplete; no per-keystroke round-trip.
5. **Rich, emote-inline composer is a client contract over the catalogue** (§0). The draft is tokenised against the catalogue and matched codes render as inline images. On send, the **wire text is the emote code** — Twitch re-parses first-party emote codes the operator can use; third-party (BTTV/FFZ/7TV) codes travel as plain text and are re-emoted on the return trip by the decoration pipeline, so the sent message renders with emotes for every viewer of our feed.
6. **Cross-channel ban is Twitch-gated and operator-scoped** (§3.5). "Every channel I moderate" resolves from Twitch *Get Moderated Channels* (not the local DB) and bans as the operator in each — best-effort, per-channel result. It reuses `moderation:ban` (Moderator floor) with a `scope` field + an explicit UI confirm, and is **distinct from `moderation:nuke`** (the SuperMod platform power). Because the operator can only act where Twitch already trusts them as a moderator, there is no privilege escalation; the larger blast radius is handled by explicit confirm + full per-channel audit, not a higher floor.
7. **The moderated-channel list is its own read endpoint** (`GET …/moderation/moderated-channels`, `moderation:action:read`) so the ban dialog can show "Every channel I moderate (N)".
8. **Pronoun badge, avatar, local-time timestamp, and animated emote are CLIENT render fixes, not server gaps** (§0). The server already emits `Pronouns`, `AvatarUrl`, `Animated`+the `/animated/` url, and a UTC ISO-8601 `Timestamp` on every live message. The client renders the pronoun chip, the avatar, plays the animated image, and formats the timestamp to the viewer's local time.
9. **Feed live/history parity** (§3.6). `GET …/chat/messages` is upgraded to emit the **same** `DashboardChatMessageDto` (pronouns, avatar, real timestamp, animated fragments) as the live hub, so scrollback and live render identically — no drift.
10. **"One message late" is a client bug, not the server** (§0). The server broadcasts each message immediately and unbatched; the client must append on receive with no buffer that withholds the newest message.
11. **Owns nothing new in the locked schema** (§1). No tables, no columns, no migration. New behaviour is services + endpoints + seed rows (action keys) + `FeatureScopeMap` entries — all data/DI, not schema.
12. **`frontend-ia.md` reconciliation.** The Chat row is repointed from the non-existent `chat.md` to **`chat-client.md`**, and its "send-as-bot" note is corrected to "send as **you** (operator); bot optional" (Decision 1). Floors stay Moderator/Moderator.
13. **Emote cache-key keying is inconsistent but left as-is** — third-party emote channel keys use the **Twitch id**, badges/cheermotes use the tenant **Guid** (chat-decoration §7). The catalogue reads both correctly by using the id each key expects; unifying the keys is a chat-decoration concern, not re-opened here.
