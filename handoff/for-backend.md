# Handoff — work for the Backend track (Stoney_Eagle)

The frontend track (`aaoa-dev`) leaves backend work orders here. The backend track picks up
**Open** items automatically at session start. See `CLAUDE.md` → *Handoff TODOs*.

<!-- Entry template — copy under Open:

### YYYY-MM-DD — short title
- **From:** aaoa-dev
- **What:** the concrete change needed (endpoint, field, behavior)
- **Why:** what it unblocks on the frontend
- **Where:** files / endpoints / spec sections involved
- **Done when:** acceptance criteria

-->

## Open

_None._

## Done

### 2026-07-18 — Live-game catalog + active-session reads need typed 200 schemas (contract-guard)
- **Commit:** `0e92041e`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** `GameSessionsController.GetActive` and `GetCatalog`
  (`server/src/NomNomzBot.Api/Controllers/V1/GameSessionsController.cs`) returned an untyped
  `IActionResult`, so their DTOs never reached `openapi/v1.json`. Each now carries the typed
  `[ProducesResponseType<StatusResponseDto<T>>(200)]` (matching the codebase convention) —
  `GetActive` → `StatusResponseDto<GameSessionDto>`, `GetCatalog` →
  `StatusResponseDto<IReadOnlyList<LiveGameCatalogEntryDto>>`. Behaviour unchanged.
- **OpenAPI:** `server/openapi/v1.json` NOT regenerated (owner's snapshot pass). ADDS two response schemas:
  `GameSessionDto` (on `GET .../games/sessions/active`) and `LiveGameCatalogEntryDto` (on
  `GET .../games/sessions/catalog`). No route changes.
- **Test:** `GameSessionsControllerContractTests` — both actions declare the typed 200 schema (reflection)
  and still return the typed envelope with the right catalog-entry / session shape.

### 2026-07-18 — Schedule iCalendar needs a token-query auth path for a live webcal subscription
- **Commit:** `b15fc8a5`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** the Bearer-only `.../live-ops/schedule/icalendar` served a one-time snapshot only. Added a
  sibling PUBLIC action `SubscribeScheduleICalendar`
  (`GET .../live-ops/schedule/icalendar/subscribe`, `[AllowAnonymous]`, `Produces("text/calendar")`) in
  `server/src/NomNomzBot.Api/Controllers/V1/LiveOpsController.cs`, authorized by the per-channel
  `OverlayToken` as a `?token=` query param — the stable, read-only credential for a
  `webcal://.../live-ops/schedule/icalendar/subscribe?token=<OverlayToken>` subscription URL. The token must
  belong to the channel in the route (`TenantResolutionMiddleware` sets the tenant from the anonymous
  `{channelId}` selector, so the feed reads only that channel's public schedule). The Bearer snapshot action
  and its `[RequireAction("live-ops:schedule:read")]` gate are untouched. Reuses the existing `OverlayToken`
  (via `IChannelService.GetByOverlayTokenAsync`) — no new column, no EF migration.
- **OpenAPI:** `server/openapi/v1.json` NOT regenerated (owner's snapshot pass). ADDS one route:
  `GET /api/v1/channels/{channelId}/live-ops/schedule/icalendar/subscribe?token=<OverlayToken>` (public,
  `text/calendar`; no JSON body schema). No DTO changes.
- **Frontend note:** build the subscribe URL from the channel's `OverlayToken` (already surfaced on the
  channel summary) — `webcal://<host>/api/v1/channels/{channelId}/live-ops/schedule/icalendar/subscribe?token=<OverlayToken>`.
- **Test:** `LiveOpsScheduleICalendarTests` — the token path serves the ics for the channel's own token and
  rejects an absent / unknown / foreign-channel token (401, schedule API never called); the Bearer path still
  serves the ics; both gates asserted by reflection.

### 2026-07-18 — Expose `gallery:review` held-key in the render manifest (SKIPPED — cross-plane)
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** SKIPPED (task marked optional / low-value). The existing self-introspection surface
  `ResolvedAccessDto.HeldActionKeys` (in the render manifest) lists ONLY channel-scoped **Gate-2** action keys.
  `gallery:review` is a platform-global **Plane-C** IAM permission key (`IamPermissionKeys.GalleryReview`,
  enforced via `[Authorize(Policy = IamPermissionKeys.GalleryReview)]`, resolved by `IPlatformIamService`) — a
  deliberately separate authorization plane (per the canonical authz vocabulary: Gate-2 vs Plane-C). No existing
  DTO lists held *platform* keys, so the task's precondition ("an existing ResolvedAccess/render-manifest that
  lists held platform keys") does not hold. Adding it would mean injecting `IPlatformIamService` into the
  channel-scoped session/manifest assembly (`CurrentUserDto` is a pure EF projection today), resolving the
  caller's IAM principal, and branching self-host (no principals → implicitly full) vs SaaS
  (effective-permission union) — a non-trivial cross-plane change to a contract DTO. The coarse
  `user.isAdmin` (`User.IsPlatformPrincipal`) gate the frontend uses today remains a correct superset. If precise
  platform-key gating is wanted later, the right home is a dedicated platform self-introspection endpoint
  (e.g. `GET /platform-iam/me` returning the caller's held IAM keys), NOT the channel render manifest.

### 2026-07-18 — The `/obs-bridge` browser-source page (OBS + VTS legs) is a server-served static asset
- **Commit:** `cc424fe7`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** new `ObsBridgeHostController` (`server/src/NomNomzBot.Api/Controllers/ObsBridgeHostController.cs`)
  serves the control-only bridge browser source at `GET /obs-bridge` (`[AllowAnonymous]`,
  `[ApiExplorerSettings(IgnoreApi=true)]`, embedded HTML/JS `const string` returned via
  `Content(html, "text/html")` — mirrors `OverlayHostController` exactly, incl. the hand-rolled SignalR
  JSON-protocol client: record-separator framing, `{protocol:"json",version:1}` handshake, type-6 keep-alive
  ping every 15s, reconnect with exponential backoff). It:
  1. Reads `?token=` and connects to `/hubs/obs` (the `OBSRelayHub`) with `?token=` (the `BridgeToken`, NOT a
     JWT); renders a 1×1 invisible surface (debug text via `textContent` only — no markup injection).
  2. **OBS leg:** on `ExecuteObsRequest(commandId, payloadJson)` opens/maintains `ws://127.0.0.1:4455` (v5
     Hello→Identify; `computeAuth` mirrors `DirectObsTransport`'s `base64(sha256(base64(sha256(pw+salt))+challenge))`
     via `crypto.subtle`, but defaults passwordless since the page carries no secret and the hub delivers none),
     runs `{kind:"request"}` (op 6) / `{kind:"batch"}` (op 8) correlated by `requestId=commandId`, calls
     `AckCommand(commandId, ok, responseDataJson, error)`, and forwards op-5 events via `ForwardObsEvent`.
  3. **VTS leg (same page, same relay):** `{kind:"vts_request"}` payloads talk to `ws://localhost:8001` using
     the VTS envelope (`apiName`/`apiVersion`/`requestID`/`messageType`/`data`, mirroring `DirectVtsTransport`),
     the page authenticating itself as a VTS plugin (`AuthenticationTokenRequest`→`AuthenticationRequest`, token
     cached in `localStorage` — a local plugin token, never a NoMercy secret), acking with the response `data`,
     and forwarding `*Event` frames via `ForwardVtsEvent`. Note the relay stringifies `payload.data`, so the
     bridge `JSON.parse`s it back into the envelope's `data` object.
- **Where:** confirmed `ObsConnectionService` builds `bridgeUrl` = `{backendUrl}/obs-bridge?token={BridgeToken}`
  (matches the new route). No EF migration; no DI change (controller auto-discovered like `OverlayHostController`).
- **OpenAPI:** no snapshot change — the route is `[ApiExplorerSettings(IgnoreApi=true)]` (like `OverlayHostController`).
- **Test:** `ObsBridgeHostControllerTests` (`server/tests/NomNomzBot.Api.Tests/Controllers/`) — `GET /obs-bridge`
  returns 200 `text/html` carrying the relay wiring (`/hubs/obs`, token gate, SignalR framing, `ExecuteObsRequest`,
  `AckCommand`) and both local legs (`ws://127.0.0.1:4455` + `ForwardObsEvent`; `vts_request` +
  `ws://localhost:8001` + `VTubeStudioPublicAPI` + `ForwardVtsEvent`; `textContent`).

### 2026-07-18 — Community per-viewer stats can't reach the analytics endpoint (id-type mismatch)
- **Commit:** `0a1f977e`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** option (a). `CommunityUserDto` and `UserDetailDto` (CommunityController) and the chat-activity
  leaderboard row `LeaderboardEntryDto` (RewardsController.GetLeaderboard) now each carry `InternalUserId`
  (`Guid?`, the internal `User.Id`, or `null` when the viewer has no local `User` row yet). `Id` stays the
  Twitch user id. The client can now call `GET /channels/{channelId}/analytics/viewers/{viewerUserId:guid}`
  (keyed on `User.Id`) with the id the community list / leaderboard provides — foreign-viewer stats no longer 403.
- **OpenAPI:** `server/openapi/v1.json` NOT regenerated (owner's snapshot pass). Changed response DTOs:
  `CommunityUserDto`, `UserDetailDto`, `LeaderboardEntryDto` each gain `internalUserId` (nullable string/uuid).
  No route changes.
- **Test:** `CommunityControllerTests.ListMembers_exposes_the_internal_user_id_for_the_analytics_viewer_endpoint`
  asserts `InternalUserId` equals the seeded `User.Id` (and is `null` for a follower with no local row).

### 2026-07-18 — Let a channel-point reward run a pipeline (for reward-triggered sounds)
- **Commit:** `88879de9`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution:** the `Reward` entity gains an optional `PipelineId` (`Guid?`). `CreateRewardRequest` /
  `UpdateRewardRequest` / `RewardDetail` carry it (on update, `Guid.Empty` clears it, absent leaves it
  unchanged). `RewardRedeemedHandler` now, on redemption, loads the bound saved pipeline's `GraphJsonCache`
  and dispatches it through `IPipelineEngine` — taking precedence over the inline `PipelineJson` / `Response`
  fallbacks (a missing graph degrades to those). Mirrors the timer pipeline-dispatch pattern. A `play_sound`
  step now fires on redemption.
- **Migrations:** `20260718041802_AddRewardPipelineBinding` (Postgres, `NomNomzBot.Infrastructure`) +
  `20260718041852_AddRewardPipelineBinding` (SQLite, `NomNomzBot.Migrations.Sqlite`) — add the nullable
  `Rewards.PipelineId` column (both model snapshots updated).
- **OpenAPI:** `server/openapi/v1.json` NOT regenerated (owner's snapshot pass). Changed request/response DTOs:
  `CreateRewardRequest`, `UpdateRewardRequest`, `RewardDetail` each gain `pipelineId` (nullable uuid). Routes
  unchanged (`POST/PUT/PATCH .../channels/{channelId}/rewards`). The frontend can now add a pipeline picker to
  the reward form (like the timer one).
- **Test:** `RewardRedeemedHandlerTests` — a reward bound to a pipeline dispatches that graph on redemption
  (engine spy), and a reward with no binding falls through to the generic redemption event response unchanged.

### 2026-07-18 — Discord guild endpoints 500 with "Discord request failed (401)"
- **Commit:** `bfa35e39`
- **From:** aaoa-dev (via Claude, frontend track)
- **Resolution (code):** the `DISCORD_*` error codes fell through `BaseController.ResultResponse` to
  `InternalServerErrorResponse` (500). They now map to their true class: `DISCORD_UNAUTHORIZED` /
  `DISCORD_NOT_CONNECTED` → **409** (a reconnect-the-bot state), `DISCORD_NOT_FOUND` → 404,
  `DISCORD_RATE_LIMITED` → 429, `DISCORD_ERROR` / `DISCORD_TRANSPORT` → 503. `DiscordRestBotGateway` also
  surfaces a clear reconnect message on a 401/403 ("Discord authorization is invalid or expired. Reconnect the
  Discord bot to continue.") so the client can show a reconnect prompt instead of a generic failure.
- **Ops note (out of code scope):** the dev Discord bot token being rejected (401) is a credential/ops matter —
  the stored token for that guild connection is invalid/expired on dev (re-invite / token refresh / possible
  `ENCRYPTION_KEY` rotation). The code fix here is the clean 4xx; making the dev token valid is a separate ops step.
- **OpenAPI:** no contract change (status-code mapping only).
- **Test:** `DiscordGuildErrorMappingTests` — a `DISCORD_UNAUTHORIZED` result → 409 with the reconnect message
  (not 500); a `DISCORD_ERROR` result → 503.
