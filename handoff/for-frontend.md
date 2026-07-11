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

### 2026-07-11 — Giveaways page (new backend module, full REST surface live)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** a Giveaways management page. The backend module is complete: campaign CRUD +
  open/close/draw/redraw lifecycle, live entry counts, append-only winner history, and secret-safe
  code pools. Endpoints (all in `server/openapi/v1.json`, tag "Giveaways"):
  `GET/POST /giveaways`, `GET/PUT/DELETE /giveaways/{id}`, `POST /giveaways/{id}/open|close|draw`,
  `POST /giveaways/{id}/winners/{winnerId}/redraw`, `GET /giveaways/{id}/winners`,
  `GET /giveaways/{id}/winners/{winnerId}/code` (broadcaster-only code reveal), and
  `GET/POST /giveaways/code-pools`, `GET/DELETE /giveaways/code-pools/{poolId}`,
  `POST /giveaways/code-pools/{poolId}/codes`.
- **Why:** parity item 12 (StreamElements/Streamer.bot baseline). Viewers already enter via the chat
  keyword or the `enter_giveaway` pipeline action; the streamer needs the management surface.
- **Where:** new `feature/giveaways`; register the DTOs in `ApiContractTest`. Role gating:
  read/write floors at Moderator (`giveaways:read`/`giveaways:write`); the code-pool routes + code
  reveal are Broadcaster-only (`giveaways:codes:write`) — hide/disable per frontend-ia §7. Code pool
  reads are MASKED by design (label + status; never the code) — do not add a "show code" affordance
  outside the winner-reveal flow.
- **Done when:** create → open → (viewers enter) → draw → winner list renders end-to-end against a
  real channel; a code-pool giveaway shows delivery state (whispered vs needs-manual-reveal) and the
  reveal works; destructive actions confirm; en + nl strings.

### 2026-07-11 — Kick integration: connect tile + chat feed provider tag
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** Kick is now a full chat platform on the backend. Two frontend touches:
  1. The integrations screen: Kick now appears in `GET /channels/{id}/integrations/status` (provider
     `"kick"`) exactly like Spotify/YouTube — render its tile and wire the connect button to
     `POST /channels/{id}/integrations/kick/connect` with `scopeSetKey: "kick.chat"` (the generic
     connect flow the other tiles already use — no new plumbing).
  2. Chat feed: messages can now arrive with provider `"kick"` (same canonical shape as twitch/youtube)
     — include it wherever the feed tags messages by channel provider (same treatment as the earlier
     YouTube entry below).
- **Why:** slice 3b-2c shipped — Kick send + native replies + moderation + webhook chat read are live
  on the backend; a streamer who connects Kick with the `kick.chat` scopes gets their Kick chat in the
  combined feed and the bot replying/moderating there.
- **Where:** `feature/integrations` (tile renders from the status read model — possibly zero work if
  tiles are data-driven), chat feed provider tagging. No API contract change beyond the extra status row.
- **Done when:** the Kick tile connects end-to-end against a real Kick account and a Kick chat message
  renders in the feed with its provider tag.

### 2026-07-11 — Credential component DRY unification (item 24c hand-off)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the client-setup credential components (BYOC client-id/secret entry used by the setup
  wizard and the integrations screens) are still duplicated — unify into one shared component.
- **Why:** ROADMAP "Small decided items"; duplicated forms drift (validation, paste-trimming, masked
  reveal) and every fix lands twice.
- **Where:** `app/composeApp/src/commonMain/.../feature/setup/` + `feature/integrations/` (find the
  duplicated credential input blocks); design-system components only.
- **Done when:** one shared credential component renders in both places, behavior identical, no
  duplicated credential-form code remains.

### 2026-07-11 — Optional: adopt render-manifest + hub event classes for a lighter dashboard
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** two backend surfaces exist that the client can adopt when convenient (both optional —
  nothing breaks if you don't):
  1. `GET /api/v1/channels/{channelId}/render-manifest` returns access (effective role +
     heldActionKeys) + tier-gated features + integration states + missing-scope gaps in ONE call —
     replaces the current 4-endpoint boot fan-out.
  2. The dashboard hub now supports **push classes**: `JoinChannelClasses(channelId, classes)` with
     classes ⊂ `["chat","activity","liveops","music","moderation"]` subscribes only those pushes for
     that channel (core pushes — stream status, config/permission/reward invalidations, alerts — are
     always on). Plain `JoinChannel` still subscribes everything, so the current client keeps working
     unchanged. For multi-watch chat panes, `JoinChannelClasses(id, ["chat","moderation"])` cuts the
     per-channel push volume substantially.
- **Why:** faster boot (one request), less hub traffic per watched channel.
- **Where:** `core/network` (manifest DTO + endpoint), `core/realtime` (hub method), shell boot +
  multi-watch panes. Manifest is in `server/openapi/v1.json`.
- **Done when:** (whenever adopted) the shell boots off the manifest and chat panes join with class
  sets — both verified live.

### 2026-07-11 — Cross-platform chat: YouTube messages now flow — tag the feed by channel provider
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the chat feed (and the coming multi-watch UI) should tag each message's SOURCE by the
  channel it arrived on. When a streamer with a connected YouTube account goes live on YouTube, the
  backend now provisions their YouTube presence as its **own channel** (`provider: "youtube"` in
  `GET /api/v1/channels`) and pushes its live chat through the same hub as Twitch —
  `DashboardChatMessageDto` with `channelId` = that YouTube channel. **No contract change**: routing
  and tagging key off `channelId` → the channel's `provider` from the channels list. Notes:
  - YouTube messages carry role flags (`isBroadcaster` = channel owner, `isModerator`,
    `isSubscriber` = channel member) but **no badges, no color, no pronouns/avatar enrichment** —
    render with the defaults; a small platform icon per line (from the channel's provider) is the
    intended source tag.
  - Chat history for the YouTube channel comes from the same `GET .../chat/messages` endpoint under
    that channel id.
  - Sending/replying into YouTube chat is NOT wired yet (slice 3) — hide or disable the composer for
    `provider != "twitch"` channels with a "read-only for now" hint.
- **Why:** combined-chat item 6 (backend `6beaa12b`): streamers see chat from every platform they
  stream on in one place. The multi-watch UI you already have a handoff for is the natural home —
  a YouTube channel is just one more channel in the picker.
- **Where:** `feature/chat/` (+ the multi-watch surface), channel picker; provider comes from the
  existing `ChannelDto.provider`. No `v1.json` change.
- **Done when:** with a YouTube-connected account live on YouTube, its messages appear in the
  dashboard feed under the YouTube channel, visually tagged as YouTube, and the composer is
  read-only for that channel.

### 2026-07-11 — Streamer-requested features (qtkitte): config surfaces for auto-shoutouts + walk-in sounds + overlay URLs
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** three small dashboard affordances that turn freshly-shipped backend capabilities into
  one-click streamer features (requested live by qtkitte):
  1. **Timers page — pipeline binding + "auto-shoutout" shape.** `TimerDto`/`CreateTimerDto`/
     `UpdateTimerDto` have always carried `pipelineId`; the backend NOW actually dispatches it
     (previously only the message leg worked). When `pipelineId` is set, the timer executes that
     pipeline every interval with the current `Messages[...]` rotation entry riding as the pipeline
     variable `{timer.message}`. UI: let the user bind a pipeline to a timer, and present the
     Messages list as the "rotation list" in that mode. A rotating auto-shoutout = timer with
     channel names in Messages + a pipeline containing `shoutout(user_id="{timer.message}")` —
     the shoutout action now accepts logins/channel names (leading @ ok), not just numeric ids.
     Consider a one-click "Auto-shoutout" preset that creates both.
  2. **Event responses / rewards — sound-bearing responses.** The overlay audio bus is now REAL:
     the bot serves the OBS browser-source page at `/overlay?token={overlayToken}` (same URL shape
     the widgets API returns), and `play_sound` pipeline steps are audible end-to-end. UI: on the
     event-responses page (e.g. channel.subscribe) and on a reward's pipeline, make "play a sound
     clip" easy to add (the `play_sound` action takes `clip` = sound-clip id or name, optional
     `volume`, `wait_for_finish`, `handle`).
  3. **Widgets/overlays page — surface the overlay URL.** `WidgetDetail.overlayUrl` (from
     `GET /api/v1/channels/{channelId}/widgets`) is the copy-paste OBS browser-source URL; show it
     with a copy button + "add this in OBS as a Browser Source" hint. A base URL without a widget
     (`/overlay?token=…`) is the channel-wide audio bus — every overlay page for the channel plays
     walk-in sounds, with or without a widgetId.
- **Why:** qtkitte asked for rotating auto-shoutouts, walk-in sounds on subs + point redeems, and
  overlay management. The backend halves shipped (commits `80d9c936`, `cbb7c8de`); only the config
  UX remains. No API contract change — all DTOs already carried these fields.
- **Where:** `feature/timers/` (pipeline binding + rotation-list presentation), `feature/eventresponses/`
  + `feature/rewards/` (pipeline/sound affordance), `feature/widgets/` (overlay URL + copy). No
  `v1.json` change.
- **Done when:** a streamer can, without touching raw JSON: (a) create a rotating auto-shoutout from a
  list of names + an interval; (b) attach a sound clip to subs and to a specific channel-point reward;
  (c) copy their overlay URL for OBS — and all three demonstrably work live.

### 2026-07-10 — Multi-channel chat watch: let a mod monitor several channels at once
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** build the **multi-watch** chat surface — the operator picks several channels and watches all
  their chats at once (side-by-side panes, or one merged feed), so a moderator monitors multiple channels in
  one session. Owner requirement (2026-07-10): "a viewer+ should be able to view multiple chats at once if
  they please so mods can monitor multiple channels at the same time." This is the **cross-channel** half of
  combined chat; the streamer **cross-platform** half (all platforms merged) is a later backend slice (item 6)
  and will reuse the same UI.
- **Why the backend is ready (no contract change):**
  - **Watchable-channel list:** `GET /api/v1/channels` already returns the channels the caller owns **and
    moderates** (`ChannelSummaryDto`, incl. `role`); `GET /api/v1/channels/moderated` lists Twitch-moderated
    channels specifically. Use these to populate the channel picker.
  - **Live feed, many channels at once:** `DashboardHub` now supports a **single connection watching many
    channels concurrently** — call `JoinChannel(channelId)` once per channel to add it, `LeaveChannel(channelId)`
    to drop just that one; disconnect cleans up all. (Previously the hub tracked only the last-joined channel.)
    Every `ChatMessage` hub push already carries **`channelId`** on `DashboardChatMessageDto`, so you can route
    each message to its pane / tag it in a merged feed.
  - **Scrollback per channel:** `GET /api/v1/channels/{channelId}/chat/messages` (same `DashboardChatMessageDto`
    shape) for each watched channel's history.
- **Where:** `feature/chat/` (new multi-pane / merged mode + channel picker); `core/realtime` hub client
  (`JoinChannel`/`LeaveChannel` per selected channel; route by `channelId`). No `v1.json` change — contract is
  unchanged; nothing to re-sync.
- **Note (backend follow-up, not blocking you):** the hub's `JoinChannel` currently gates on Gate-1 entry
  (`CanResolveTenantAsync`) only, while the REST chat-history path also requires Gate-2 `chat:read`. That
  consistency + the exact read floor for multi-watch ("viewer+" vs the current `chat:read` Mod-default) is an
  open backend decision; build the picker from the channels the list endpoints return (already access-scoped)
  and it'll be correct regardless.
- **Done when:** an operator can select 2+ channels they own/moderate and see all their live chats at once
  (panes or merged, each line clearly tagged with its channel), add/remove a channel without dropping the
  others, and scrollback loads per channel.

### 2026-07-05 — Standing rule: users never see numeric permission levels (names only)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** owner rule — **no user-facing surface ever renders the numeric ladder value** of a role.
  Users see the role **name** only (`Moderator`, `Editor`, `Broadcaster`, `VIP`, `Subscriber`,
  `Artist`, `Everyone`), never the number, never `Moderator (10)` / `Editor30`. The unified ladder
  (`Everyone 0 · Subscriber 2 · Vip 4 · Artist 6 · Moderator 10 · SuperMod/LeadModerator 20 ·
  Editor 30 · Broadcaster 40`, roles-permissions §0) is an **internal** `≥`-comparison mechanism only.
- **Already applied (this commit, backend track made the code-only frontend edit with owner's OK):**
  `feature/roles/ui/RolesScreen.kt` — the action-permission **row** now shows the role name
  ("Default: Moderator"), and the override **dialog** is a role **picker** (reuses `DropdownMenu`,
  offers named rungs ≥ the action's floor) instead of a free-form numeric field. New en+nl rung
  strings added; numeric-entry strings retired. API contract unchanged (`setOverride` still sends the
  rung's `Int` level).
- **What you own going forward:** apply the same on **any** role/permission surface you build or
  touch. The effective-role DTO (`ResolvedAccess` / backend `ResolvedAccessDto`, `GET
  /roles/effective/me`) already carries **both**: names (`communityStanding`/`managementRole` — render
  these) AND `*Level` ints (`effectiveLevel`/`communityLevel`/`managementLevel` — internal `≥` gating
  only, never render). `ActionPermission` currently exposes only ints; map a level→name with the same
  ladder table `RolesScreen` uses (§0) — or ask backend to add a name field to `ActionPermissionDto`
  if you'd rather not mirror the table.
- **Where:** `docs/bot-capabilities.md` §1.3 (new third golden rule) + §1.5; `RolesScreen.kt`.
- **Done when:** no dashboard surface renders a permission number; role pickers/badges/gates read by name.

### 2026-07-05 — Features endpoint now reports ENTITLEMENT, not just opt-in (gate the Features UI on it)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** `FeatureStatusDto` (returned by `GET /api/v1/channels/{channelId}/features` and
  `POST .../features/{featureKey}/toggle`) gained **three additive, nullable-safe** fields, appended after
  `requiredScopes` (none added to the schema `required` array — all optional):
  - `entitled: Boolean` (default `true`) — whether the channel's **tier / deployment / platform flag** actually
    ALLOWS the feature. This is a **separate axis** from `isEnabled` (the channel's own opt-in choice). A feature
    is only usable when `entitled && isEnabled`. Treat `isEnabled` alone as "the toggle position", never as "the
    feature is available".
  - `entitlementReason: String?` — set only when `entitled == false`; closed vocabulary:
    `"REQUIRES_TIER"` (upgradable — pair with `requiredTier`), `"DEPLOYMENT"` (excluded on this deployment, e.g.
    self-host vs saas — not upgradable), `"UNAVAILABLE"` (global off / not yet in rollout / admin override).
  - `requiredTier: String?` — the minimum tier key to unlock, present only for `"REQUIRES_TIER"` (e.g. `"pro"`).
- **How to gate the UI (mirror the role-gating pattern in `frontend-ia.md` §7):** when `entitled == false`,
  **disable** the feature's toggle/entry with a reason tooltip rather than showing a live switch — for
  `"REQUIRES_TIER"` show "Upgrade to {requiredTier} to unlock", for `"DEPLOYMENT"`/`"UNAVAILABLE"` show a generic
  "Not available" tooltip. A page/button whose backing feature is not entitled should be hidden/disabled the same
  way an out-of-role one is. `isEnabled` still reflects the stored opt-in (can read ON even when not entitled —
  legacy state), so do **not** render it as an active switch when `entitled == false`.
- **Toggle refusal:** `POST .../toggle` now **refuses to turn a non-entitled feature ON** — it returns
  `403 Forbidden` with error code `NOT_ENTITLED`. Turning a feature **OFF is always allowed** (revoking after a
  downgrade). Handle the 403 by keeping the toggle in its prior state and surfacing the upgrade/where-to path.
- **Why:** the endpoint previously reported a feature as "enabled" purely from the opt-in row, so the dashboard
  could show a working button for a feature the tier/deployment doesn't include (it would 403 on use, or the page
  couldn't function). The gate (`IFeatureFlagService`: tier floor + deployment mode + staged rollout + tenant
  override) is now composed into the DTO so the client can hide/disable those surfaces up front. Self-host
  resolves every tier to unlimited, so `entitled` is always `true` there — no self-host UX change.
- **Where:** re-sync the `FeatureStatusDto` mirror in KMP `core/network` from the refreshed
  `server/openapi/v1.json` (schema `FeatureStatusDto`) and confirm `ApiContractTest` (`jvmTest`) passes. Kotlin
  DTO adds `val entitled: Boolean = true, val entitlementReason: String? = null, val requiredTier: String? = null`.
- **Done when:** the Features screen disables (not silently shows) non-entitled features with the correct
  reason/upgrade tooltip, the toggle handles a `NOT_ENTITLED` 403 gracefully, and `ApiContractTest` is green.

### 2026-07-05 — Owned ids are now ULID strings on the wire (refresh openapi + confirm ApiContractTest)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** every **owned** identifier is now encoded as a 26-char Crockford base32 **ULID string** at the API
  boundary (was a raw GUID string). Storage is unchanged (UUIDv7 `Guid`) — this is purely the wire form. It is
  fully round-trip tolerant: the backend **accepts BOTH** a ULID and a raw GUID string inbound (route, query,
  JSON body, and the `channelId` path segment / `X-Channel-Id` header), so nothing breaks during the transition.
  The refreshed `server/openapi/v1.json` snapshot now renders every id field as `"type":"string","format":"ulid"`
  (was `"format":"uuid"`) — 242 fields, format-only change, no paths/operations/schemas added or removed.
- **Why:** shorter, URL-safe, sortable public ids that never expose the raw UUIDv7. External ids (Twitch/Spotify/
  Discord/YouTube ids, `{userId}` = Twitch user id, `{rewardId}`/`{redemptionId}`/`{pollId}`/`{messageId}`) are
  **unchanged** — they were always `string`-typed and are not owned ids.
- **Where:** re-sync KMP `core/network` DTOs from the refreshed `server/openapi/v1.json` and run `ApiContractTest`
  (`jvmTest`). Kotlin ids are already `String`, so the `uuid→ulid` format change should map to `String`
  identically and require no DTO type changes — but **confirm** `ApiContractTest` still passes and update its
  expectations if it asserts on the `uuid` format string specifically. Treat all ids as **opaque strings**: do not
  parse, validate as UUID, or lower/upper-case them; string-compare as received. Ids read from one response can be
  sent back verbatim in any path/query/body.
- **Done when:** `ApiContractTest` (`jvmTest`) is green against the refreshed snapshot and no client code assumes
  a UUID shape for an id (no `UUID.fromString`, no uuid regex) — ids flow through as opaque strings.

### 2026-07-05 — Discord guild pickers: 3 new endpoints + DTOs for role/channel selection
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the Discord config screens can now populate real role/channel pickers instead of asking the
  streamer to paste raw snowflake ids. Three new GETs on `DiscordController` (snapshot refreshed):
  `.../discord/connections/{connectionId}/guild` → `DiscordGuildInfoDto`, `.../guild/roles` →
  `DiscordGuildRoleDto[]`, `.../guild/channels` → `DiscordGuildChannelDto[]`. Gated `discord:role:read`
  (roles) / `discord:connection:read` (guild + channels).
- **Why:** the opt-in-role feature is now fully live end-to-end — Discord's interactions webhook (backend,
  Ed25519-verified) makes the opt-in **buttons actually work** when a viewer clicks them; the pickers let
  the streamer configure which role/channel a button targets without hunting for ids in Discord.
- **Where:** add `DiscordGuildInfoDto`, `DiscordGuildRoleDto`, `DiscordGuildChannelDto` to the KMP
  `core/network` DTOs + register each in `ApiContractTest` (guarded against `server/openapi/v1.json`,
  just refreshed). Field names are in the snapshot schemas.
- **Done when:** the Discord role/channel config UI offers dropdowns sourced from these endpoints, and
  `ApiContractTest` passes with the three DTOs registered.

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

### 2026-07-10 — Activity feed: show the actor name on follow/sub/cheer/raid events — DONE (backend, option b)
- Resolved backend-side: `NotifyChannelAsync` was hardcoding the top-level `userId`/`userDisplayName`
  to null; the actor-bearing broadcasters (follow, subscription/resub/gift, cheer, raid, shoutouts,
  moderator/VIP role changes) now pass the actor through, and the Kotlin `HubChannelEvent` already
  parsed those fields — the feed renders names with **zero frontend work**. Anonymous gifts/cheers
  arrive as "Anonymous". No `v1.json` change (hub-only contract).

### 2026-07-10 — i18n string bundle re-fetched ~30× on boot — DONE `b6dbfbb1`
- Done by the backend track directly (owner directed the UI work). `core/i18n/BundleCachingResourceReader.kt`
  caches the `.cvr` bundle once per session behind `LocalAppLocale`; boot now reads the bundle a single time
  instead of per-string. Verified in `:composeApp:jvmTest` (green).

### 2026-07-10 — VIP-lowerable actions + quotes:delete split — DONE (frontend consume + `ba9167a4` `5c56dc05` `8a9e305e`)
- Done by the backend track directly (owner directed the UI work). **Superseded the original framing:** the
  final model is NOT "default floors lowered to VIP". Defaults stay at the Twitch base; the broadcaster
  **lowers via a per-action override** down to a VIP floor for non-harmful actions (`ba9167a4`). The dashboard
  reflects this through the new `ResolvedAccessDto.heldActionKeys` (`8a9e305e`, `GET /roles/effective/me`):
  page visibility = `role clears readFloor` **OR** `readActionKey ∈ heldActionKeys`, so a broadcaster-lowered
  page surfaces to a VIP/Sub without changing the two-plane default. Quote add/edit gate on `quotes:write`,
  delete on `quotes:delete` (`5c56dc05`) via disable-with-reason. `ShellNav`/`ShellAccessController`/
  `QuotesAccess` + tests (`ShellNavTest` 14/0, `QuotesAccessTest` 3/0, `ShellAccessControllerTest` 10/0);
  Kotlin DTO field registered in `ApiContractTest` (1/0). `:composeApp:jvmTest` + `compileKotlinWasmJs` green.

### 2026-07-05 — `ShellNavTest` red after sidebar reorder (18159a7) — DONE (reconciled; jvmTest green 14/0)
- Reconciled as part of the `heldActionKeys` shell work: `ShellNav.pages`, the `NavGroup` order, and
  `ShellNavTest` now agree, and `:composeApp:jvmTest` is green (14/14) — verified locally before push. The
  local-only red is cleared.
