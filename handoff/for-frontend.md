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

### 2026-07-05 — `ShellNavTest` is red on master after the sidebar reorder (18159a7) — please reconcile
- **From:** Stoney_Eagle (via Claude, backend track — flagging, not fixing: this is your IA design)
- **What:** `jvmTest` fails on `ShellNavTest.pages_are_grouped_in_the_binding_ia_order_with_setup_pinned_last`
  (`feature/shell/nav/ShellNavTest.kt:65`). Commit **18159a7 "reorder sidebar IA and rename Music group
  to Audio"** reordered `ShellNav.pages` so the group order is now
  `Home, Moderation, Community, Chat, Loyalty, Music, Stream, Connect, Setup`, but the test still asserts
  the **old** order `Home, Chat, Moderation, Loyalty, Music, Stream, Community, Connect, Setup`, and the
  `NavGroup` enum declaration order (ShellNav.kt:56) also still reflects the old order. Three things now
  disagree (pages list vs enum order vs test).
- **Why it matters:** there is **no frontend CI job** (only the Docker/wasm build runs in CI), so this
  red is **local-only** and silently sitting on master — a `jvmTest` gate would be flagging it.
- **Where:** `feature/shell/nav/ShellNav.kt` (`pages`, `NavGroup`), `feature/shell/nav/ShellNavTest.kt`.
- **Done when:** the intended canonical IA order (per `frontend-ia.md` §3) is the single truth across
  the `pages` list, the `NavGroup` enum order, and the test — and `jvmTest` is green. (Consider wiring
  `jvmTest` into CI so this can't recur.)

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
