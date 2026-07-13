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

### 2026-07-11 — TTS now plays: `play_tts` pipeline block + (later) client-edge widget (item 16)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** (1) Add a **`play_tts`** block to the pipeline-builder palette (`feature/pipelines`). Params:
  `text` (template string, e.g. `{{args}}` — required) and optional `voice` (a voice id). It reads a message
  out loud on the overlay. No API-contract change — it's a pipeline action, configured in the existing pipeline
  editor exactly like `play_sound`/`send_message`. (2) *(Later / lower priority)* the spec's DEFAULT TTS mode is
  `client_edge` — the OVERLAY widget synthesizes speech in the browser (Web Speech API) from an `OverlayHub`
  `TtsSpeak` push, no audio bytes on the wire. This slice ships the **self_host** path instead (the bot
  synthesizes server-side and plays the audio URL through the SAME overlay sound bus SR2 already built) — so
  **TTS already works end-to-end today with zero widget changes**. The client_edge widget handler is a future
  enhancement, not a blocker.
- **Why:** item 16. Before this, TTS could synthesize but never actually played anywhere. Now a streamer can add
  a `play_tts` step to any command/event pipeline (e.g. a channel-point redemption "read my message") and it
  speaks on the overlay. The dispatch gates on the channel's TTS-enabled flag + character cap and resolves the
  per-viewer voice (`UserTtsVoice`) → channel default automatically.
- **Where:** `feature/pipelines` block palette (add the `play_tts` block with its two params + i18n labels). The
  overlay host that plays it is the SR2 sound-overlay bus (already handling `PlaySound` by URL) — nothing to
  change there for self_host. No `ApiContractTest`/openapi change (no new DTO or endpoint). i18n: en + nl for the
  block label + param labels.
- **Done when:** a pipeline with a `play_tts` step (text `{{args}}`) added in the builder, bound to a command,
  reads the args aloud on the overlay when triggered (TTS enabled on the channel); a disabled channel / too-long
  text shows the pipeline step's failure reason; en + nl strings.

### 2026-07-12 — Per-viewer TTS voice picker (item 16)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** on the TTS settings page (`feature/tts` / wherever the TTS config lives), add a **per-viewer voice
  override** control — pick a specific voice for a named viewer, so that viewer's messages are always read in
  their chosen voice (overrides the channel default). Three new endpoints, all under the existing TTS controller:
  - `GET  /api/v1/channels/{channelId}/tts/users/{userId}/voice` → `StatusResponseDtoOfUserTtsVoiceDto`
    (`userId`, `voiceId`) — **404** when the viewer has no override (i.e. uses the channel default; treat 404 as
    "default", not an error to surface).
  - `PUT  /api/v1/channels/{channelId}/tts/users/{userId}/voice` — body `SetUserVoiceDto { voiceId }` → 200 with
    the `UserTtsVoiceDto`. **400/404** if the voice id isn't one the channel can synthesize (validated against the
    same `GET .../tts/voices` list you already populate the voice dropdown from — so reuse that list for the picker).
  - `DELETE /api/v1/channels/{channelId}/tts/users/{userId}/voice` — clears the override (viewer falls back to
    channel default). 200 on success, 404 if there was nothing set.
- **Why:** item 16. The dispatch already RESOLVES a per-viewer voice when reading a message; this exposes the
  WRITE side so streamers can actually assign one. Closes the loop end-to-end.
- **Where:** TTS settings surface. Read gate = `tts:voice:read` (same as the voices list), write gate =
  `tts:uservoice:write` — disable the picker (with a reason tooltip) below the write floor per `frontend-ia.md` §7.
  New DTOs `UserTtsVoiceDto` / `SetUserVoiceDto` → register in `ApiContractTest`; refresh `server/openapi/v1.json`
  is already committed on the backend side (this snapshot has the new path + schemas).
- **Done when:** picking a voice for a viewer persists and survives reload; the picker only offers synthesizable
  voices; clearing returns the viewer to the default; picker disabled (not hidden) below the manage floor; en + nl.

### 2026-07-12 — TTS "filter profanity" toggle (item 16)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** on the TTS settings page, add a **"Filter profanity"** switch bound to the new
  `profanityCensorEnabled` field. It's just a new boolean on the EXISTING TTS config DTOs — no new endpoint:
  - `GET /api/v1/channels/{channelId}/tts/config` → `TtsConfigDto` now includes `profanityCensorEnabled: bool`.
  - `PUT /api/v1/channels/{channelId}/tts/config` → `UpdateTtsConfigDto` now accepts `profanityCensorEnabled: bool?`
    (partial update — omit to leave unchanged, like the other fields).
- **Why:** item 16 §3.5. When on, mild swears in a spoken message are masked before the bot reads it aloud. It's an
  **opt-OUT** setting — default ON for new channels — so surface it as ON unless the config says otherwise.
- **Where:** TTS settings surface, next to the other toggles (skip-bot-messages, read-usernames). Same read/write
  gates as the rest of the TTS config (`tts:config:read` / `tts:config:write`). Register the new DTO field in
  `ApiContractTest`; the committed `server/openapi/v1.json` already carries it (additive). i18n: en + nl label +
  helper text ("Masks mild swearing before it's read aloud").
- **Done when:** toggling the switch persists and survives reload; a new channel shows it ON by default; en + nl.

### 2026-07-12 — TTS moderator approval queue (item 16)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** (1) a **"Require moderator approval"** switch on the TTS settings page, bound to the new
  `modApprovalRequired` field (same `GET/PUT .../tts/config` DTOs as the profanity toggle; opt-IN, default OFF).
  (2) a **TTS approval queue** panel/page where a moderator reviews pending utterances and approves or rejects each:
  - `GET  /api/v1/channels/{channelId}/tts/queue?page=1&pageSize=25` → `PaginatedResponse<TtsQueueEntryDto>`
    (`id`, `requestedByTwitchUserId`, `requestedByDisplayName`, `originalText`, `censoredText`, `voiceId`,
    `wasCensored`, `status`, `createdAt`, `expiresAt`, `sourceMessageId`) — pending only, newest-first.
  - `POST /api/v1/channels/{channelId}/tts/queue/{entryId}/approve` → 200 (it's then synthesized + played).
  - `POST /api/v1/channels/{channelId}/tts/queue/{entryId}/reject` → 200 (discarded, nothing plays).
  Show `censoredText` (fall back to `originalText`) as the text that WILL be spoken; flag `wasCensored`; entries
  auto-expire after ~10 min (show `expiresAt`). A live push for new entries is a later enhancement — poll for now.
- **Why:** item 16 P.1a. A cautious streamer can gate every TTS message behind a mod's yes/no.
- **Where:** TTS settings (the toggle) + a moderator queue surface (list + approve/reject buttons). All three queue
  endpoints gate on `tts:queue:review` (Mod); the toggle uses the TTS config write gate. Register `TtsQueueEntryDto`
  + the new config field in `ApiContractTest`; committed `server/openapi/v1.json` already carries them. i18n en + nl.
- **Done when:** with approval on, a triggered TTS shows in the queue and does NOT play until approved; approve plays
  it; reject discards it; the toggle persists across reload; en + nl.

### 2026-07-11 — Viewer reports + moderator triage queue — new endpoints
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** viewers report a chatter; mods triage. Three endpoints (in `server/openapi/v1.json`, tag "Moderation",
  under `/channels/{channelId}/moderation`): `POST /reports` (`FileViewerReportRequest` = `reportedTwitchUserId`,
  `reportedUsername`, `reportedDisplayName?`, `reason`) → the created `ViewerReportDto` (a VIEWER-facing action —
  any viewer in the channel); `GET /reports?status=open` → `List<ViewerReportDto>` (`id`, `reportedTwitchUserId`,
  `reportedUsername?`, `reason`, `status` open|dismissed|escalated, `reporterName?`, `createdAt`, `resolvedAt?`,
  `resolvedByName?`); `PATCH /reports/{reportId}` (`ResolveViewerReportRequest` = `action` dismiss|escalate) →
  resolved `ViewerReportDto`. Reason trimmed, non-empty, ≤500 chars.
- **Why:** item 15 (§3.8 viewer reports). The report is a truthful queue record; ESCALATE doesn't auto-punish —
  the mod then acts via the existing ban/timeout tools (no phantom enforcement). New `ViewerReports` table
  (migration ships in the API image; auto-applies on deploy).
- **Where:** two surfaces. (1) A viewer-facing "Report" action (e.g. on a chat message / user popover) → `POST
  /reports` with the reported user's id + name from the chat context. (2) A mod **reports queue** page:
  `GET /reports?status=open`, then dismiss/escalate each. Register `ViewerReportDto`, `FileViewerReportRequest`,
  `ResolveViewerReportRequest` in `ApiContractTest` (v1.json refreshed). Role gating: file =
  `moderation:report:file` (viewer-level — the report action, keep it low), read = `moderation:report:read` (Mod),
  triage = `moderation:report:triage` (Lead Mod). Confirm escalate/dismiss.
- **Done when:** a viewer files a report → it appears in the mod's open queue → escalate/dismiss moves it out of
  open and records who/when; blank reason + unknown status/action show the validation error; en + nl strings.

### 2026-07-11 — Moderator notes on a viewer (mod panel write side) — new endpoints
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** free-text notes the mod team shares about a viewer. `GET /channels/{channelId}/moderation/users/{userId}/notes`
  → `List<UserNoteDto>` (`id`, `subjectUserId`, `content`, `pinned`, `authorName?`, `createdAt`, `updatedAt`; pinned
  first then newest); `POST /channels/{channelId}/moderation/users/{userId}/notes` (`CreateUserNoteRequest` =
  `content`, `pinned`) → the created `UserNoteDto`; `PUT /channels/{channelId}/moderation/notes/{noteId}`
  (`UpdateUserNoteRequest` = `content?`, `pinned?`) → updated note; `DELETE /channels/{channelId}/moderation/notes/{noteId}`
  → 204. Content trimmed, non-empty, ≤2000 chars (backend rejects otherwise).
- **Why:** item 15 (§3.7 per-user panel, write side) — pairs with the per-user context (read side) from the sibling
  entry. Notes + history + the warn/suspicious/ban actions = a complete per-user mod panel.
- **Where:** the per-user panel — a notes list with add/edit/pin/delete. Register `UserNoteDto`,
  `CreateUserNoteRequest`, `UpdateUserNoteRequest` in `ApiContractTest` (v1.json refreshed). Role gate: read at
  Moderator (`moderation:usercontext:read`), write/edit/delete at Moderator (`moderation:note:write`). Confirm
  delete. Pinned notes float to the top.
- **Done when:** add a note → appears in the list; pin → floats to top; edit content persists; delete removes it;
  empty/too-long content shows the validation error; en + nl strings.

### 2026-07-11 — Network un-nuke: reverse a mass ban across moderated channels — new endpoint
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** the reversal of the existing network ban. `POST /channels/{channelId}/moderation/actions/unban` with
  `UnbanUserRequest` (`targetTwitchUserId`, `scope` = `this_channel` (default) | `all_moderated`) →
  `NetworkBanResultDto` (`attempted`, `succeeded`, `channels[]` = per-channel `broadcasterLogin`/`succeeded`/
  `error`), exactly mirroring the ban endpoint (`POST actions/ban`) shape you already consume. `all_moderated`
  lifts the ban in every channel Twitch says the operator moderates, as the operator (their own token), best-effort.
- **Why:** item 15 — the network ban (`all_moderated`) had no undo. Same "act on any channel you moderate, no
  install required" model; the per-channel result lets the UI show which channels the un-nuke reached.
- **Where:** wherever the network-ban action lives (moderation actions / user context). Register `UnbanUserRequest`
  in `ApiContractTest` (`NetworkBanResultDto` is already registered from the ban side; v1.json refreshed). Role
  gate: `moderation:unban` (Mod), same as the single-channel unban. `all_moderated` is consequential — confirm it
  and show the per-channel outcome list on return.
- **Done when:** an `all_moderated` un-nuke returns the per-channel outcomes and a `this_channel` unban returns the
  one-row result; en + nl strings.

### 2026-07-11 — Per-user enforcement: warn + suspicious status (moderation) — new endpoints
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** three new per-user mod actions (in `server/openapi/v1.json`, tag "Moderation", under
  `/channels/{channelId}/moderation`): `POST /warn` (`WarnUserRequest` = `targetUserId`, `reason`) → issues a
  Twitch warning the viewer must acknowledge before chatting again, returns `ModerationActionResult`;
  `POST /suspicious` (`SetSuspiciousStatusRequest` = `targetUserId`, `status` ∈ `active_monitoring` |
  `restricted`) → flags them, returns `SuspiciousStatusDto` (`userId`, `status`, `types`, `updatedAt`);
  `DELETE /suspicious/{userId}` → clears the flag, returns `SuspiciousStatusDto`.
- **Why:** item 15 — completes the Twitch per-user enforcement set alongside ban/timeout/unban. Warn = a soft
  escalation step; suspicious = watch (`active_monitoring`) or hold-their-messages (`restricted`). All live
  Twitch (Warn Chat User / Update Suspicious User), honest degradation on missing scope.
- **Where:** best as row actions / a per-user mod panel (on the chat user popover, community page, or a future
  user-context panel). Register `WarnUserRequest`, `SetSuspiciousStatusRequest`, `SuspiciousStatusDto` in
  `ApiContractTest` (v1.json already refreshed). Role gating: warn at Moderator (`moderation:warn`), suspicious
  at Lead Moderator (`moderation:suspicioususer:write`). Warn needs a non-empty reason (backend rejects blank);
  confirm `restricted` (it holds all their messages). New scopes `moderator:manage:warnings` +
  `moderator:manage:suspicious_users` requested on next re-grant (progressive, no logout).
- **Done when:** warn posts + shows an ack-pending state; set/clear suspicious round-trips and reflects the
  returned status; blank-reason + unknown-status show the backend validation error; en + nl strings.

### 2026-07-11 — Unban-request queue (moderation page) — new endpoints, build the UI
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** a **pending unban-request queue** on the moderation page. Two new endpoints (in
  `server/openapi/v1.json`, tag "Moderation", under `/channels/{channelId}/moderation`):
  `GET /unban-requests?status=pending` → `List<UnbanRequestDto>` (`id`, `userId`, `userLogin`, `userName`,
  `text` = the viewer's appeal, `status`, `createdAt`, `resolvedAt?`, `resolvedBy?`, `resolutionText?`), and
  `POST /unban-requests/{unbanRequestId}/resolve` with body `ResolveUnbanRequestRequest` (`approve` bool,
  `note?` string) → the resolved `UnbanRequestDto`. Status filter accepts pending / approved / denied /
  acknowledged / canceled (default pending — the actionable queue).
- **Why:** item 15 (advanced moderation) — viewers can appeal a ban on Twitch; mods had no way to see or
  action the queue in the dashboard. Reads/writes are LIVE Twitch (Get / Resolve Unban Request). Same honest
  degradation as the rest of the moderation page: a missing scope / no broadcaster token → error, show the
  regrant/unavailable state, not an empty queue.
- **Where:** moderation page, a new "Unban requests" section/tab. Register `UnbanRequestDto` +
  `ResolveUnbanRequestRequest` in `ApiContractTest` (v1.json already refreshed on the backend). Role gating:
  read at Moderator (`moderation:unbanrequest:read`), approve/deny at Lead Moderator
  (`moderation:unbanrequest:resolve`) — disable-with-reason below that floor. Approve/deny are consequential —
  confirm deny; approve lifts the ban. New streamer scope `moderator:manage:unban_requests` is requested on
  next re-grant (progressive, no logout).
- **Done when:** the pending queue renders each appeal (viewer + text + when), approve lifts the ban and drops
  it from the queue, deny resolves it; a missing-scope channel shows the regrant state; en + nl strings.

### 2026-07-11 — Moderation page now shows/controls REAL Twitch state — handle the regrant/error path
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** three moderation endpoints changed behaviour (NOT their contract — no `v1.json` change, no DTO
  change, nothing to re-sync): `GET /channels/{id}/moderation/bans`, the `blocked-terms` GET/POST/DELETE,
  and the `shield` GET/PATCH now read and write **live Twitch state** instead of a local mirror. The bans
  list can now be much larger (it includes bans made outside the bot — Twitch UI, other mods) and carries the
  real reason/moderator. Blocked-terms + Shield toggles now actually take effect on Twitch. **The only UI work:**
  these endpoints can now legitimately **fail** where before they always returned a value — when the channel's
  Twitch grant is missing a scope, or (for a channel you moderate but the bot isn't installed on) there's no
  broadcaster token. Treat a failure as "needs permission / not available here" and show the existing regrant
  prompt, **not** a blank "no bans / no terms" state (that would be the old phantom lie).
- **Why:** the page was cosmetic — the bans list showed only bot-recorded bans, and adding a blocked term or
  flipping Shield Mode wrote a local flag Twitch never saw. Now it's truthful (item 15 groundwork; owner-reported
  "moderation page is empty and useless").
- **Where:** the moderation feature page. Two new streamer scopes are requested on next auth/regrant
  (`moderator:manage:blocked_terms`, `moderator:manage:shield_mode`) — existing streamers will see the
  standard "permissions needed" prompt until they re-grant; that's expected and self-heals via the progressive
  scope flow (no logout). No `ApiContractTest` change (shapes identical).
- **Done when:** the bans/blocked-terms/shield sections render live Twitch data on a fully-granted channel, and
  a missing-scope / no-token channel shows a regrant/unavailable state instead of an empty list.

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

### 2026-07-11 — Stream schedule + markers controls (completes the live-ops surface)
- **From:** Stoney_Eagle (via Claude, backend track)
- **What:** UI for the broadcaster's stream schedule (the weekly calendar of upcoming streams) and a
  one-tap "mark this moment" button for the live VOD. New `LiveOpsController` routes (in
  `server/openapi/v1.json`, tag "LiveOps", under `/channels/{channelId}/live-ops`): `GET schedule`
  (segments + vacation window), `GET schedule/icalendar` (an .ics feed — offer a subscribe/download
  link), `POST schedule/segment` (`CreateScheduleSegmentRequest`), `PATCH schedule/segment/{id}`
  (`UpdateScheduleSegmentRequest`), `DELETE schedule/segment/{id}`, `PUT schedule/settings`
  (vacation toggle/window), `POST markers` (`{ description? }`).
- **Why:** parity item 17 — rounds out the live-ops surface (polls/predictions/raids/ads/clips already
  had pages; schedule + markers were the gap). Markers are a streamer favourite: one button during the
  stream drops a VOD bookmark for later editing.
- **Where:** the live-ops / stream area. Register the new request DTOs in `ApiContractTest`. Role
  gating: schedule read at Moderator (`live-ops:schedule:read`), schedule writes at Editor
  (`live-ops:schedule:write`), marker create at Moderator (`live-ops:marker:create`). Markers require
  the channel to be LIVE (Twitch rejects otherwise) — disable/hide the button when offline and surface
  the backend's Twitch error if it fails.
- **Done when:** the schedule renders + segment add/edit/delete + vacation round-trip against a real
  channel; the marker button posts and confirms while live; the .ics link works; en + nl strings.

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
