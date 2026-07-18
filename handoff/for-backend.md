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

_(none)_

## Done

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
