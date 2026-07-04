# Handoff — work for the Frontend track (aaoa-dev)

The backend track (`Stoney_Eagle`) leaves frontend work orders here. The frontend track picks up
**Open** items automatically at session start. See `CLAUDE.md` → *Handoff TODOs*.

<!-- Entry template — copy under Open:

### YYYY-MM-DD — short title
- **From:** Stoney_Eagle
- **What:** the concrete change needed (screen, component, wiring)
- **Why:** what changed on the backend / what this enables
- **Where:** files / endpoints / spec sections involved (incl. server/openapi/v1.json if the contract changed)
- **Done when:** acceptance criteria

-->

## Open

### 2026-07-04 — Send channel context on user lookups; use analytics endpoints for participant stats
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** two client changes in the KMP network layer. (1) When the dashboard is operating on a
  channel, send that channel's id with user-lookup calls (`X-Channel-Id` header or `?channelId=`) —
  affects `GET /api/v1/users` (search) and `GET /api/v1/users/{id}` / `{id}/profile`. (2) In
  `ParticipantApi`, stop calling `GET /api/v1/users/{id}/stats` for *other* users — that endpoint is
  self-only and returns 403 for foreign ids (always has). Use the per-viewer analytics endpoints
  instead: `GET /api/v1/analytics/viewers/{viewerUserId}` (+ `/engagement`, `/streak`), which allow
  the viewer themself **or** channel managers.
- **Why:** a security pass locked every endpoint to an explicit permission. User search and foreign-
  profile reads are now authorized against the *channel you manage* — without the channel id the
  backend assumes your own channel, so a moderator browsing someone else's dashboard would get 403s.
  The stats swap fixes the participant panel showing errors for every viewer except yourself.
- **Where:** `core/network` (client + DTOs unchanged — no contract change, only which endpoint is
  called and the channel-context header/query); permission floors in `docs/bot-capabilities.md` §1.
- **Done when:** user search + participant profile/stats panels work when signed in as a moderator
  managing another user's channel (not just as the broadcaster), and the participant panel shows
  stats for any viewer via the analytics endpoints.

### 2026-07-04 — New SignalR hub events to consume (live-update surfaces)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the backend now pushes events that previously never reached clients. Two dedicated hub
  methods: `StreamInfoChanged { broadcasterId, broadcasterDisplayName, title, gameName }` (title/
  category edits) and `RewardChanged { broadcasterId, action: created|updated|removed, rewardId,
  title, cost?, isEnabled?, timestamp }`. Plus new `ChannelEvent` types: `poll_begin/progress/end`,
  `prediction_begin/progress/lock/end`, `hype_train_begin/progress/end`, `shoutout_sent`,
  `shoutout_received`, `ad_break_begin`, `shield_mode_begin/end`, `moderator_added/removed`,
  `vip_added/removed` (payload DTOs in `server/src/NomNomzBot.Api/Hubs/Dtos/AlertDtos.cs`).
- **Why:** pages currently go stale — stream-info panel, rewards page, live-ops poll/prediction
  panels, mod roster, and the activity feed can now update live instead of on refetch.
- **Where:** consume via the existing SignalR client (`HubEvent.kt`); unknown events are currently
  ignored so nothing breaks until you wire each one.
- **Done when:** each listed event updates its owning page/panel live (stream info, rewards queue/
  list, polls, predictions, hype train, moderation roster, activity feed + ad-break countdown).
- **Update (same day) — `ConfigChanged` hub event:** every config CRUD now broadcasts
  `ConfigChanged { broadcasterId, domain, entityId?, action: created|updated|deleted|toggled }` to the
  channel group. Wire the SignalR client to **invalidate/refetch that domain's query** so a second
  open dashboard never goes stale. Domain strings (closed set): `commands`, `timers`, `pipelines`,
  `event-responses`, `rewards`, `economy-config`, `earning-rules`, `catalog`, `moderation-rules`,
  `blocked-terms`, `automod`, `tts-config`, `music-config`, `sr-config`, `webhooks`, `widgets`,
  `features`, `quotes`, `builtins`, `channel-settings`, `roles-permits`. Also: `MusicStateChanged`
  now actually fires (poller + instant on skip/pause/resume), and now-playing finally reports real
  play/pause + progress.
- **Update (same day):** hub payloads now also carry hydrated user info — additive nullable fields,
  omitted from JSON when null: `avatarUrl`, `pronouns` (display string like `"they/them"`),
  `communityStanding` (`Everyone|Subscriber|Vip|Artist|Moderator`) on `FollowAlertDto`,
  `RewardRedeemedDto`, `RoleChangedAlertDto`, `ShoutoutReceivedAlertDto`, and the chat message DTO
  (`avatarUrl` + `pronouns`); `ModActionDto` gets the same as `targetDisplayName`/`targetAvatarUrl`/
  `targetPronouns`/`targetCommunityStanding` (the moderated viewer). Render avatars + pronoun badges
  wherever these events surface.

### 2026-07-04 — Read the bot capability & permission reference before designing new pages
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** `docs/bot-capabilities.md` is the designer-facing inventory of everything the bot can do —
  every domain, every page/panel implied, built vs specced-only, real-time surfaces, public pages.
  **Section 1 (permission system) is required reading**: read floors hide pages, manage floors disable
  actions with a reason tooltip, Critical capabilities are Broadcaster-locked, and the Twitch-scope
  "action-required" flow needs its own UI states on every feature that can be enabled.
- **Why:** every feature missed is a page or component not designed; the permission model changes the
  states every page needs (read-only vs write vs scope-gated).
- **Where:** `docs/bot-capabilities.md`; deeper detail in `.claude/docs/design/spec/frontend-ia.md`
  and `roles-permissions.md`.
- **Done when:** you've read it and the §14 "specced but not built" table is on your design backlog.

## Done

_(completed entries move here, with their commit hashes)_
