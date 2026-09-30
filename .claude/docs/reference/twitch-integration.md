# Twitch Integration

### OAuth Flow

1. Backend redirects user to `https://id.twitch.tv/oauth2/authorize` with required scopes
2. Twitch calls back to `/api/v1/auth/twitch/callback`
3. API exchanges the code, routes the result by `state` (`user`, `bot`, or `channel_bot`), stores encrypted tokens (`ENCRYPTION_KEY`), and returns JWTs or success redirects

**Redirect URIs are computed at runtime from `App:BaseUrl`** — do not set them in config or env vars. All Twitch OAuth flows now share one callback path:
- `{App:BaseUrl}/api/v1/auth/twitch/callback`

Register only that single callback URL in the Twitch Developer Console using your actual API base URL. For the deployed-dev, local-tunnel and production domains, see the Domain table in `.claude/docs/design/PRODUCT-ALIGNMENT.md`.

**Progressive scopes** — don't request everything up front. Request scopes when the user enables the relevant feature (e.g., `channel:manage:raids` when they enable raid responses).

### Streamer Account Scopes

A fresh login requests only `AuthService.MinimalLoginScopes` (`user:read:email`, `user:read:chat`, `user:write:chat`, `user:read:moderated_channels`); every other scope is requested on demand (progressive, additive re-grant). `TwitchScopeRegistry.FullCatalogue` is what the missing-scope sweep and re-grant check against.

### Bot Account Scopes

All six `AuthService.BotScopes`:

```
user:read:chat       # read chat via EventSub channel.chat.message
user:write:chat      # send chat via Helix Send Chat Message
user:bot             # chatbot badge on an app-token send
chat:read            # legacy IRC scope, still requested
chat:edit            # legacy IRC scope, still requested
user:read:whispers   # the bot's own whisper inbox (user.whisper.message)
```

IRC is retired for transport.

### EventSub (WebSocket — not webhooks)

The bot uses `wss://eventsub.wss.twitch.tv/ws` — **no public HTTPS URL required** during local dev.

Default transport: per-owner EventSub WebSocket sessions. Conduit mode (WebSocket shards, blue/green handover) is OFF by default; enable it with `EventSub:Conduits:Enabled=true` (env `EventSub__Conduits__Enabled`).

- `TwitchEventSubHostedService` runs as `IHostedService`
- Manages WebSocket lifecycle automatically
- Reconnects with exponential backoff on disconnect
- Re-registers all subscriptions after an unplanned disconnect (a Twitch `session_reconnect` is different, see below)
- Twitch sends a `reconnect` message every ~5 minutes (normal behavior, not a bug). `session_reconnect` is a handoff: subscriptions migrate to the new session and nothing is re-created.
- When conduit mode is enabled and a Twitch app secret is configured, subscriptions live on an EventSub **conduit** (2 WebSocket
  shards, one per running instance) so a blue/green deploy hands over without losing an event — see
  `.claude/docs/design/spec/twitch-eventsub.md` §10

**EventSub topics (sample — 74 topics subscribed):**
- `stream.online` / `stream.offline`
- `channel.follow`
- `channel.subscribe` / `channel.subscription.gift`
- `channel.cheer`
- `channel.raid`
- `channel.channel_points_custom_reward_redemption.add`
- `channel.poll.begin` / `channel.poll.end`
- `channel.prediction.begin` / `channel.prediction.end`
- `channel.chat.message` (requires bot `user:read:chat` scope)

### Twitch chat

- Chat **send** via `IChatProvider` = `ChatPlatformRouter` → `HelixChatProvider` for Twitch: **Helix Send Chat Message** (`POST /helix/chat/messages`, `user:write:chat`) on **every** profile — stateless, no per-channel socket, no sharding. Whispers via `POST /helix/whispers`.
- Chat **read** via EventSub `channel.chat.message` (bot `user:read:chat` scope) on every profile (`spec/scaling-qos.md` §6).
- IRC is **fully retired** — there is no `TwitchIrcService` and no TLS IRC socket; no chat flows over IRC on any profile (decision: Helix everywhere). The bot **types** via Helix Send Chat Message on its own token (`user:write:chat`) and **reads** via EventSub (`user:read:chat`); no IRC scopes are requested.
- **Note:** If `ENCRYPTION_KEY` changes, the stored bot token becomes unreadable — the bot needs to re-auth.

### Kick / YouTube chat

- Kick: send via `KickChatPlatform`; read via Kick webhook ingest (`KickWebhookController` / `IKickWebhookIngest`).
- YouTube: send via the YouTube `IChatPlatform`; read via `YouTubeLiveChatPollWorker`.
- X: not built (S031).

### Cloudflare Tunnel (for OAuth redirects)

Twitch requires HTTPS redirect URIs. For local dev:

```bash
cloudflared tunnel --url http://localhost:5080
```

`appsettings.Development.json` is gitignored and absent from a clone, so create your own. `App:BaseUrl` is set via `API_BASE_URL` → `App__BaseUrl` (`docker-compose.yml`); set it to the tunnel URL and add that URL to your Twitch app's redirect URIs. For the `ResolvePublicOrigin` precedence, see `backend-architecture.md`.

The owner's local-dev tunnel `bot-dev-api.nomercy.tv` is a dev convenience only (set in the owner's own `appsettings.Development.json`); self-host needs no NoMercy domain. See the Domain table in `PRODUCT-ALIGNMENT.md` for the deployed and planned domains.
