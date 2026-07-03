# Twitch API + EventSub Rebuild — Design (DRAFT)

Source: design dialogue 2026-06-16 (decisions via Q&A) + the twitch-rebuild-architecture notes. Scope: rebuild the Twitch integration to cover ~144 Helix endpoints + ~74 EventSub events, multi-platform-ready (Kick / YouTube later).

## Client surface
- **Grouped sub-clients by domain**, mapping to the ~30 Helix OpenAPI tags: `helix.Channels.GetInfo(...)`, `helix.Moderation.Ban(...)`, `helix.Streams.x(...)`. Discoverable via IntelliSense, ~5 endpoints per group.
- Each method is thin: build request → send through the shared HTTP pipeline (auth + rate limit + retry) → map response DTO. Returns `Result<T>`.
- A top-level `ITwitchHelixClient` exposes the sub-clients.

## Codegen
- **Generate DTOs only** (request/response models) from the Twitch OpenAPI spec; **hand-write the client methods.**
- Generated DTOs are **committed and organized by domain** (e.g. `Twitch/Helix/Channels/Dtos/...`) — reviewable, navigable, **no `Generated/` dump**. Regenerate + re-curate when the spec changes.
- Tool: a model-only generator (NSwag / NJsonSchema / quicktype). NOT runtime Roslyn (spec→DTO codegen at dev time is fine; it isn't runtime codegen).

## Rate limiting
- **Adaptive limiter** reading `Ratelimit-Limit / Ratelimit-Remaining / Ratelimit-Reset` from each response; throttle proactively, **queue + exponential backoff on 429**.
- Per-token buckets (Helix limits are per app/user token). Priority/fairness so background polls don't starve user-triggered calls.

## EventSub transport (deployment-mode split)
- **Self-host:** WebSocket EventSub (`wss://eventsub.wss.twitch.tv/ws`) — no public URL; reconnect w/ backoff, re-subscribe on reconnect.
- **SaaS:** Conduits + webhooks — shards, HMAC signature verification, challenge-response; scales to many channels.
- **Shared, transport-agnostic subscription layer:** one subscription registry/model; the transport (WS vs conduit/webhook) is an adapter selected by deployment profile. Same `IEventSource` seam as the event-bus adapter.

## Multi-platform readiness
- **Twitch-first, thin seams.** Introduce platform interfaces (`IChatPlatform` / `IEventSource` / `IPlatformApi`) only where Kick/YouTube clearly diverge; do **not** abstract everything up front (YAGNI / Rule of Three). Twitch is the concrete first impl; the seams make Kick/YouTube additive, not a rewrite.

## Auth / scopes
- **Progressive scopes** — request a scope only when the feature needing it is enabled. The client checks scope before a call and surfaces a clear "needs scope X" result.
- Token refresh handled by the auth layer (OpenIddict-backed); the Helix client just asks for a valid token.

## Resilience
- `Result<T>` over exceptions; typed Twitch error mapping; retries with backoff (transient + 429); circuit-breaker on sustained failure.
