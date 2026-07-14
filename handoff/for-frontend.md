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

### 2026-07-14 — Widgets/Overlays editor: rewire to compile-on-save (BREAKING contract change)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the widget subsystem was rebuilt to the `widgets-overlays.md` spec. The old
  "PUT `customCode`" save path is **gone** — the editor now creates a widget, then **compiles**
  authored source into an append-only version that the overlay serves + hot-reloads. Rewire the
  "Overlays" page + code editor to this flow:
  1. **Create** → `POST /channels/{id}/widgets` with `{ name, framework }` where `framework` ∈
     `vanilla|react|vue|svelte` (**renamed from `type`**; only `vanilla` + `react` compile today).
     Offer a template picker from **`GET /channels/{id}/widgets/templates`** →
     `WidgetTemplate{ key, name, description, framework, source }` (3 starter vanilla widgets; seed
     the editor with `source`).
  2. **Load source to edit** → `GET /channels/{id}/widgets/{widgetId}/versions/{versionId}` →
     `WidgetVersionDetail.sourceCode` (use the widget's `activeVersionId`, or the newest from
     `GET .../versions`). `WidgetDetail` **no longer has `customCode`**.
  3. **Save = compile** → `POST /channels/{id}/widgets/{widgetId}/compile` with `{ sourceCode }` →
     `WidgetVersionDetail{ buildStatus: pending|success|error, buildError?, buildLog?, contentHash }`.
     Always 200; if `buildStatus == "error"`, show `buildError` in the editor (the overlay keeps the
     last good version). On success the overlay hot-reloads itself.
  4. **Version history / rollback** → `GET .../versions` (newest first) + `POST .../rollback/{versionId}`.
  5. **Clone** → replace the client-side "Copy of…" re-POST with **`POST /channels/{id}/widgets/clone`**
     `{ installedWidgetId }` → a new live custom widget (source copied + compiled).
- **Why:** `WidgetDetail` changed shape — now `{ id, name, description?, framework, source, isEnabled,
  overlayUrl?, activeVersionId?, galleryItemId?, settings, eventSubscriptions, lastRuntimeError?,
  lastRanAt?, createdAt, updatedAt }` (was `{ type, customCode, … }`). `CreateWidgetRequest` uses
  `framework` (not `type`) and dropped `customCode`; `UpdateWidgetRequest` dropped `customCode`
  (it now patches name/description/settings/subscriptions/enabled only). The old editor's `saveCode`
  (PUT customCode) is now a silent no-op → the editor is broken against the new backend until rewired.
- **Where:** `feature/widgets/` (WidgetsScreen/WidgetsController), `core/network/WidgetsApi.kt`
  (DTOs + calls), `core/editor` (unchanged — the CodeMirror editor is reused; just feed it the version
  source + save via /compile). The overlay runtime (host page + SDK + bundle serving) is **100% backend**
  — no frontend work: the OBS browser source is `WidgetDetail.overlayUrl`, unchanged.
- **Gate-2 keys:** `widget:read` (list/get/versions/templates), `widget:write` (create/update/delete/
  clone), `widget:compile`, `widget:version:read`, `widget:rollback` — all seeded.
- **✅ Backend refreshed `server/openapi/v1.json`** for these DTO changes (this commit). Now in the
  snapshot: `WidgetDetail`/`CreateWidgetRequest`/`UpdateWidgetRequest` (CustomCode dropped;
  framework/source/activeVersionId/eventSubscriptions/settings added), plus `CompileWidgetRequest`,
  `CloneWidgetRequest`, `WidgetVersionSummary`/`WidgetVersionDetail`, `WidgetTemplate`,
  `OverlayManifest`/`OverlayWidgetEntry`. Resync the Kotlin DTOs against it and register the new shapes in
  `ApiContractTest` (`:composeApp:jvmTest`). Backend-verified live: create→compile→overlay URL renders the
  bundle in the sandboxed iframe with the SDK bridge + saved settings.
- **Done when:** create-from-template → edit in the code editor → Save compiles + the OBS overlay URL
  renders the widget and hot-reloads on save; a build error shows in the editor; version list + rollback
  work; clone produces a live, independently-editable copy; en + nl strings.

### 2026-07-11 — Media-share page: mod queue + overlay widget (viewer clip/video queue)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** two surfaces. (1) A **mod queue** page: list submissions (filter by status), approve/reject/
  skip/reorder pending+approved items, edit the config. (2) A **`media_share` OBS overlay widget** that
  plays the current clip and shows the upcoming queue. Endpoints (in `server/openapi/v1.json`, tag
  "Media Share", under `/media-share`): `GET queue?status=`, `GET next` (the overlay pulls this — it flips
  the item to *playing*), `POST {id}/approve|reject|skip|reorder|played`, `GET/PUT config`
  (`UpdateMediaShareConfigRequest`). Submissions come from viewers via the `!media <url>` chat command and
  a `submit_media` pipeline action (redeem-to-submit) — NOT from this page.
- **Why:** parity item 11 (StreamElements media-share). Safe-by-default: approval on, Twitch-clip +
  YouTube only, a hard duration cap. Items carry `title`, `durationSeconds`, `thumbnailUrl`, `sourceType`
  (`twitch_clip`|`youtube`), `mediaRef` (clip slug / YouTube id) — render the embed from those.
- **Where:** new `feature/mediaShare` page + the overlay widget (the overlay host already exists; drive it
  off `GET next` → play the embed → `POST {id}/played` on completion). Register the DTOs in
  `ApiContractTest`. Role gating: read + moderate the queue at Moderator (`media:read` / `media:moderate`);
  config write at Editor (`media:write`). Destructive skip/reject confirm.
- **Done when:** a submitted clip appears pending → approve → the overlay plays it → marks played →
  advances; reject/skip refund shows; the config (enable, approval, sources, cap, cost, cooldown)
  round-trips; en + nl strings.

### 2026-07-11 — Stream schedule & markers (item 17) — DONE except the .ics link
- **✅ Markers — DONE:** one-tap "Mark moment" button in the Home live-ops quick-actions (gated on
  `stats.isLive`; surfaces the Twitch error on failure). E2E-verified against dev.
- **✅ Schedule page — DONE:** a new **Schedule** page under the Stream nav group — reads the live Twitch
  schedule and manages it (add / edit / delete a segment + the vacation window). `ScheduleController` +
  `ScheduleScreen` + full nav wiring + `ScheduleControllerTest`; en + nl. Datetimes are ISO-8601 text /
  durations minutes (Compose has no native date picker yet; the backend validates). Compile + jvmTest green.
- **⏳ Remaining (small):** the **.ics subscribe link** (`GET .../live-ops/schedule/icalendar`) — deferred
  because the endpoint needs auth a calendar app can't send; pending a token-query mechanism (logged for
  backend). (The earlier dev 502 on `GET /live-ops/schedule` is **fixed** — it was the page-size default
  exceeding Twitch's schedule cap of 25; the page now renders the real schedule.)

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
