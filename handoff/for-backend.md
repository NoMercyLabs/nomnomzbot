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

### 2026-07-13 — Chat feed can't tag by provider — `ChannelSummaryDto` has no `provider` field
- **From:** aaoa-dev (via Claude, frontend track)
- **What:** the Kick ("chat feed provider tag") and YouTube ("tag the feed by channel provider") handoff
  entries both say to key each chat line's source tag off `provider` from `GET /api/v1/channels`. But the
  committed `server/openapi/v1.json` `ChannelSummaryDto` exposes no `provider` (fields: chatColor, displayName,
  id, isLive, login, overlayToken, profileImageUrl, role, viewerCount) — and neither does `ChannelDto`. The
  backend source (`ChannelSummaryDto` in `Application/Identity/Dtos/ChannelDtos.cs`) has no Provider member.
- **Why:** the frontend routes/renders per channel by `channelId` (works today), but cannot show a per-channel
  platform tag (twitch / youtube / kick) without the channel's provider on the wire. `ApiContractTest` would
  reject a Kotlin `provider` field that isn't in the schema, so the client can't add it speculatively.
- **Where:** add `Provider` (string: "twitch" | "youtube" | "kick") to `ChannelSummaryDto` (and ideally
  `ChannelDto`), populated when the channel is provisioned per platform; refresh `server/openapi/v1.json`.
- **Done when:** `GET /api/v1/channels` rows carry `provider`; the frontend then tags each chat line + the
  multi-watch panes by it (the Kick + YouTube feed-tag entries unblock).

### 2026-07-13 — Community per-viewer stats can't reach the analytics endpoint (id-type mismatch)
- **From:** aaoa-dev (via Claude, frontend track)
- **What:** the 2026-07-04 handoff ("Send channel context on user lookups") asked the frontend to stop calling
  the self-only `GET /users/{id}/stats` for foreign viewers and use `GET /channels/{channelId}/analytics/
  viewers/{viewerUserId}` instead. Blocker: the analytics route is typed `{viewerUserId:guid}` (an internal
  `User.Id`), but the community list DTO (`CommunityUserDto`) exposes only the **Twitch user id** — its first
  field is `f.UserId`, and `CommunityController` builds it from Twitch-id space (line ~180 keys `users` by
  `TwitchUserId`; line ~302 note: "Candidate ids live in Twitch-user-id space"). So the client holds a Twitch
  id and cannot call a guid-typed endpoint. `X-Channel-Id` auto-injection (part 1 of that entry) is already
  done client-side; only this per-viewer stats swap is blocked.
- **Why:** the community per-user detail panel's stats section currently 403s for every viewer except the
  operator themself (the self-only endpoint). It degrades to null (no crash), but shows no stats.
- **Where:** pick one — (a) add the internal `userId` (GUID) to `CommunityUserDto` (+ the leaderboard rows that
  feed the detail panel), so the client can call the analytics viewer endpoint; OR (b) add a Twitch-id-accepting
  analytics viewer endpoint (mirroring how the viewer-data endpoint resolves `TwitchUserId`). Refresh
  `server/openapi/v1.json`.
- **Done when:** the frontend can fetch a foreign viewer's engagement stats (messages / watch time / follower /
  sub) for the community detail panel via a manager-allowed endpoint keyed off an id the community list provides.

### 2026-07-13 — Dev: Discord guild endpoints 500 with "Discord request failed (401)"
- **From:** aaoa-dev (via Claude, frontend track)
- **What:** on dev (`dev.nomnomz.bot`, owner channel `019f146e-8303-71ef-b698-18d1098d7d7e`, guild
  connection `01KWR909XXF17A0HRVYK2Z4Y3X` "NoMercy Entertainment"), all three guild-picker GETs return
  HTTP 500 `{"status":"error","message":"Discord request failed (401)."}`:
  `.../discord/connections/{connectionId}/guild`, `/guild/roles`, `/guild/channels`. The upstream Discord
  API rejects the bot token with 401 — the stored/configured Discord bot token for that guild is invalid or
  expired on dev (same shape as the Twitch stale-token footgun; possibly an `ENCRYPTION_KEY`-rotation or a
  re-invite/token-refresh need).
- **Why:** the frontend guild role/channel pickers (shipped this session) call these endpoints. They degrade
  gracefully — a failure falls back to manual snowflake entry, so nothing is broken in the UI — but the
  dropdowns can't populate on dev until the Discord bot token is valid. Not reproducible client-side; it's a
  backend/ops credential issue.
- **Where:** the Discord bot-token resolution behind `DiscordController`'s guild reads (whatever calls Discord
  with the bot token). Also worth surfacing a cleaner 4xx (e.g. `needs_reauth`) instead of a raw 500 when
  Discord answers 401, so the client can show a reconnect prompt rather than a generic failure.
- **Done when:** the three guild GETs return 200 with real role/channel data on dev for that connection (or a
  structured re-auth signal), and the frontend pickers populate.

### 2026-07-13 — Let a channel-point reward run a pipeline (for reward-triggered sounds)
- **From:** aaoa-dev (via Claude, frontend track)
- **What:** channel-point rewards currently have no `pipelineId` — a reward is pure Twitch CRUD
  (`CreateRewardBody`/`UpdateRewardBody` carry no pipeline, and there is no reward→pipeline dispatch). Add
  an optional `pipelineId` to the reward create/update DTOs + run that pipeline when the reward is redeemed
  (the redemption event already flows through EventSub). Mirror how timers now dispatch their `pipelineId`.
- **Why:** qtkitte item — "attach a sound to a specific channel-point reward". The frontend shipped the rest
  (timers pipeline binding + rotation list, `play_sound` in the pipeline builder, overlay URLs), and a
  sub/command can already play a sound via an event-response/command pipeline with a `play_sound` step. Only
  the reward path is unreachable because a reward cannot reference a pipeline.
- **Where:** `RewardsController` DTOs + the reward-redemption handler (dispatch the bound pipeline). Refresh
  `server/openapi/v1.json`; the frontend then adds a pipeline picker to the reward form (like the timer one).
- **Done when:** a reward can be bound to a pipeline and redeeming it runs the pipeline (so a `play_sound`
  step fires on redemption).

## Done

_(completed entries move here, with their commit hashes)_
