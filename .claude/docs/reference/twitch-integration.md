# Twitch Integration

### OAuth Flow

1. Backend redirects user to `https://id.twitch.tv/oauth2/authorize` with required scopes
2. Twitch calls back to `/api/v1/auth/twitch/callback`
3. API exchanges the code, routes the result by `state` (`user`, `bot`, or `channel_bot`), stores encrypted tokens (`ENCRYPTION_KEY`), and returns JWTs or success redirects

**Redirect URIs are computed at runtime from `App:BaseUrl`** — do not set them in config or env vars. All Twitch OAuth flows now share one callback path:
- `{App:BaseUrl}/api/v1/auth/twitch/callback`

Register only that single callback URL in the Twitch Developer Console using your actual API base URL. Domains (PRODUCT-ALIGNMENT.md):

| Purpose | Domain |
|---|---|
| Deployed dev (Proxmox) dashboard + API | `https://dev.nomnomz.bot` (LAN `http://192.168.2.60:5080`) |
| Local-dev tunnel (owner's machine, OAuth redirects) | `https://bot-dev-api.nomercy.tv` — dev convenience only; self-host needs no NoMercy domain |
| Planned production | `https://api.nomnomz.bot` |

**Progressive scopes** — don't request everything up front. Request scopes when the user enables the relevant feature (e.g., `channel:manage:raids` when they enable raid responses).

### Streamer Account Scopes

Computed at runtime — no hardcoded list: `AuthService.ResidualNonHelixGatedScopes` ∪
`TwitchScopeRegistry.AllDeclaredScopes` (reflected from `[RequiresTwitchScope]` on the Helix sub-clients).
Adding a scope = tagging the Helix method; never edit a list.

### Bot Account Scopes

```
user:write:chat
user:read:chat
```

IRC is retired; no IRC scopes are requested.

### EventSub (WebSocket — not webhooks)

The bot uses `wss://eventsub.wss.twitch.tv/ws` — **no public HTTPS URL required** during local dev.

- `TwitchEventSubHostedService` runs as `IHostedService`
- Manages WebSocket lifecycle automatically
- Reconnects with exponential backoff on disconnect
- Re-registers all subscriptions after reconnect
- Twitch sends a `reconnect` message every ~5 minutes (normal behavior, not a bug)
- When a Twitch app secret is configured, subscriptions live on an EventSub **conduit** (2 WebSocket
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

### Chat Send & Read (Helix everywhere — IRC retired)

- Chat **send** via `IChatProvider` → `HelixChatProvider`: **Helix Send Chat Message** (`POST /helix/chat/messages`, `user:write:chat`) on **every** profile — stateless, no per-channel socket, no sharding. Whispers via `POST /helix/whispers`.
- Chat **read** via EventSub `channel.chat.message` (bot `user:read:chat` scope) on every profile (`spec/scaling-qos.md` §6).
- IRC is **fully retired** — there is no `TwitchIrcService` and no TLS IRC socket; no chat flows over IRC on any profile (decision: Helix everywhere). The bot **types** via Helix Send Chat Message on its own token (`user:write:chat`) and **reads** via EventSub (`user:read:chat`); no IRC scopes are requested.
- **Note:** If `ENCRYPTION_KEY` changes, the stored bot token becomes unreadable — the bot needs to re-auth.

### Cloudflare Tunnel (for OAuth redirects)

Twitch requires HTTPS redirect URIs. For local dev:

```bash
cloudflared tunnel --url http://localhost:5080
```

Then update `App__BaseUrl` in `appsettings.Development.json` and add the tunnel URL to your Twitch app's redirect URIs.

The owner's local-dev tunnel `bot-dev-api.nomercy.tv` is pre-configured in `appsettings.Development.json` — a dev convenience only; self-host needs no NoMercy domain. The deployed dev instance is `https://dev.nomnomz.bot`; `api.nomnomz.bot` is the planned production domain (see the domain table under *OAuth Flow*).
