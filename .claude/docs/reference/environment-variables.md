# Environment Variables

### Backend — `nomnomzbot/.env`

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `POSTGRES_USER` | no | `nomnomzbot` | PostgreSQL username |
| `POSTGRES_PASSWORD` | prod: yes | `nomnomzbot_dev` | PostgreSQL password |
| `JWT_SECRET` | prod: yes | `dev-secret-key-at-least-32-characters-long!!` | JWT signing key (≥32 chars). Generate: `openssl rand -base64 32` |
| `JWT_ISSUER` | no | `nomnomzbot` | JWT issuer claim |
| `JWT_AUDIENCE` | no | `nomnomzbot` | JWT audience claim |
| `ENCRYPTION_KEY` | prod: yes | `ZGV2...` (base64) | AES key for OAuth token storage. Generate: `openssl rand -base64 32`. **Changing this invalidates all stored tokens.** |
| `TWITCH_CLIENT_ID` | recommended | — | From Twitch Developer Console (may be blank: wizard collects it; shared public client works for device-code login; BYOC encouraged) |
| `TWITCH_CLIENT_SECRET` | recommended | — | From Twitch Developer Console |
| `TWITCH_BOT_USERNAME` | no | `NomNomzBot` | Twitch username of the account the bot posts chat as |
| `POSTGRES_DB` | no | `nomnomzbot` | PostgreSQL database name |
| `REDIS_PASSWORD` | no | — | Optional Redis password; when set the API appends it to the connection string automatically |
| `API_BASE_URL` | no | `http://localhost:5080` | Public URL the API is reachable at; OAuth redirect URIs derive from it (`App__BaseUrl`) |
| `INITIAL_ADMIN_TWITCH_ID` | no | — | Numeric Twitch user id promoted to platform admin on next login; blank once an admin exists |
| `TRUSTED_PROXY_NETWORKS` | no | `172.16.0.0/12` | CIDR of the proxy allowed to set `X-Forwarded-For` (`ForwardedHeaders__KnownNetworks__0` in docker-compose; the docker private-bridge range Caddy reaches the API over); loopback trusted by default |
| `DEPLOYMENT_MODE` | no | `self_host_full` (compose) | `self_host_lite` (SQLite, in-memory cache; the desktop/dev default), `self_host_full` (single-tenant Postgres+Redis) or `saas` (multi-tenant — **RESTRICTED**, reserved to NoMercy Labs). Compose maps it to `Deployment__Mode`; the app also reads `App__DeploymentMode`. Unset, the app auto-detects whether Postgres and Redis are reachable |
| `API_IMAGE` | no | `nomnomzbot-api:local` | Container image; set `ghcr.io/nomercylabs/nomnomzbot:latest` to pull the CI image instead of building |
| `SPOTIFY_CLIENT_ID` | no | — | Enables Spotify music integration |
| `SPOTIFY_CLIENT_SECRET` | no | — | Enables Spotify music integration |
| `DISCORD_CLIENT_ID` | no | — | Enables Discord integration |
| `DISCORD_CLIENT_SECRET` | no | — | Enables Discord integration |
| `DISCORD_PUBLIC_KEY` | no | — | Ed25519 public key for the Discord interactions webhook (opt-in buttons); without it the endpoint answers 503 |
| `DISCORD_BOT_TOKEN` | required once Discord is connected | — | The bot's own static token (Developer Portal → Bot tab), sent as `Authorization: Bot <token>` for every guild REST call (notification channels, live role). NOT produced by the guild-connect OAuth flow — that exchange returns a per-user, 7-day-expiring `access_token` that Discord's guild endpoints reject as a Bot token no matter how often it's reauthorized (S-PL4) |
| `YOUTUBE_CLIENT_ID` | no | — | Enables YouTube music provider + YouTube sign-in (device-code) |
| `YOUTUBE_CLIENT_SECRET` | no | — | Enables YouTube music provider + YouTube sign-in |
| `YOUTUBE_API_KEY` | no | — | Enables YouTube search for song requests (app-level, separate from OAuth) |
| `KICK_CLIENT_ID` | no | — | Kick platform connection + sign-in (OAuth 2.1 auth-code + PKCE; callback `/api/v1/auth/kick/callback`) |
| `KICK_CLIENT_SECRET` | no | — | Kick platform connection + sign-in |
| `TWITTER_CLIENT_ID` | no | — | X platform connection + sign-in (OAuth 2.0 auth-code + PKCE; callback `/api/v1/auth/twitter/callback`) |
| `TWITTER_CLIENT_SECRET` | no | — | X platform connection + sign-in |
| `PATREON_CLIENT_ID` | no | — | Supporter events — Patreon membership ingest (callback `/api/v1/integrations/patreon/callback`) |
| `PATREON_CLIENT_SECRET` | no | — | Supporter events — Patreon |
| `SHOPIFY_CLIENT_ID` | no | — | Supporter events — Shopify merch order ingest (callback `/api/v1/integrations/shopify/callback`) |
| `SHOPIFY_CLIENT_SECRET` | no | — | Supporter events — Shopify |
| `TREATSTREAM_CLIENT_ID` | no | — | Supporter events — TreatStream realtime treat ingest (callback `/api/v1/integrations/treatstream/callback`) |
| `TREATSTREAM_CLIENT_SECRET` | no | — | Supporter events — TreatStream |
| `AZURE_TTS_API_KEY` | no | — | Azure Cognitive Services key for TTS |
| `AZURE_TTS_REGION` | no | `westeurope` | Azure region for TTS service |
| `ELEVENLABS_API_KEY` | no | — | ElevenLabs key for TTS |
| `API_HTTP_PORT` | no | `5080` | Host port of the Caddy ingress (`api-blue`/`api-green` publish no port) |
| `API_EXPOSE_DOCS` | no | `false` | Serves `/scalar` outside Development (`Api__ExposeDocs`); off by default in docker |
| `NOMNOMZ_DATA_DIR` | no | per-user data dir (`/app/data` in docker) | Overrides the directory for SQLite, uploaded assets, sound clips and key files |
| `POSTGRES_PORT` | no | `5432` | Host port for Postgres |
| `REDIS_PORT` | no | `6379` | Host port for Redis |
| `ADMINER_PORT` | no | `8082` | Host port for Adminer |

`API_HTTPS_PORT` (default `5081`) is used only by `server/docker-compose.yml`. The root `docker-compose.yml` drops it (see the comment near its line 36): TLS belongs to the operator's own reverse proxy.

### Config-only switches

Not mapped in `.env` or the compose file. Set them as environment variables or in `appsettings`.

| Key | Env var | Default | Description |
|-----|---------|---------|-------------|
| `EventSub:Conduits:Enabled` | `EventSub__Conduits__Enabled` | `false` | Opt-in EventSub conduit mode. Off by default: the 2026-09-29 conduit deploy made the bot deaf |

For local `dotnet run` dev (not Docker): put Twitch credentials in `appsettings.Development.json` instead. All other settings fall back to `appsettings.json` defaults.

### Frontend

The KMP + Compose dashboard is **profile-agnostic** — its only required configuration is the
**backend URL**. The web build talks to the origin that served it (no picker); the native app keeps
a list of saved server connections (mDNS LAN discovery + manual add) with per-server tokens in the
OS keychain, switchable from the profile menu.

### `appsettings.json` structure (config hierarchy — abridged; see `appsettings.json` for the full list)

```json
{
  "ConnectionStrings": { "DefaultConnection": "...", "Redis": "..." },
  "Deployment": { "Mode": "self_host_full" },
  "Jwt": { "Secret": "", "Issuer": "nomnomzbot", "Audience": "nomnomzbot", "ExpiryMinutes": 60 },
  "Encryption": { "Key": "" },
  "Twitch": { "ClientId": "", "ClientSecret": "", "BotUsername": "" },
  "Kick": { "ClientId": "", "ClientSecret": "" },
  "Twitter": { "ClientId": "", "ClientSecret": "" },
  "Spotify": { "ClientId": "", "ClientSecret": "" },
  "Discord": { "ClientId": "", "ClientSecret": "", "PublicKey": "" },
  "YouTube": { "ClientId": "", "ClientSecret": "", "ApiKey": "" },
  "Azure": { "Tts": { "ApiKey": "", "Region": "westeurope" } },
  "ElevenLabs": { "ApiKey": "" },
  "Cors": { "Origins": ["https://bot-dev.nomercy.tv"] }
}
```
