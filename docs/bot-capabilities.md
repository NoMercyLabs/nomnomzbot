# NomNomzBot — What the Bot Can Do (Designer Reference)

**For:** the frontend/design track.
**Rule of thumb:** every capability in this document is a page, panel, dialog, or component. If it's
listed here and has no design, that's a gap. Two tiers exist throughout:

- **Built** — live in the backend today (~56 controllers, ~356 endpoints). Verify any endpoint at
  `http://localhost:5080/scalar`.
- **Specced only** — fully designed in the specs but no backend yet (§14). These still need UI design;
  the backend will catch up to the spec, not the other way around.

Authoritative sources if something here seems off: `.claude/docs/design/spec/frontend-ia.md` (the page
inventory), `roles-permissions.md` (permissions), and each domain's spec (named per section).

---

## 1. The permission system — read this before designing anything

Every page and every button in the dashboard is permission-aware. Getting this wrong isn't cosmetic —
it decides which of the three shells a user even sees.

### 1.1 Three separate "planes" of who-can-do-what

| Plane | Plain meaning | Decides |
|---|---|---|
| **A — Community standing** | what a **viewer** is in the channel (sub, VIP…) | what they can use in chat and on the viewer pages |
| **B — Management role** | what a **team member** may administer | which dashboard pages/actions they get |
| **C — Platform IAM** | NoMercy **staff** running the SaaS | the separate Admin area (never shown on self-host) |

**Plane A values (exact names):** `Everyone` → `Subscriber` → `Vip` → `Artist` → `Moderator`.
(Sub tier 1/2/3 is extra data on a subscriber, **not** separate rungs.)

**Plane B values (exact names):** `Moderator`(10) → `SuperMod`(20) → `Editor`(30) → `Broadcaster`(40).
A Twitch mod automatically maps to `Moderator`; a Twitch channel editor to `Editor`. VIPs and subs get
**no** management access — Plane A never opens the dashboard.

**How they combine:** one ladder of numbers — `Everyone(0) < Subscriber(2) < Vip(4) < Artist(6) <
Moderator(10) < SuperMod(20) < Editor(30) < Broadcaster(40)`. A user's effective level = the **highest**
of (their standing, their management role, any active `!permit` grant).

### 1.2 The three shells the app renders

1. **Participant surface** (Plane A, no management role): **My Channel, Now Playing, Leaderboards,
   Points & Store, Games, Me**. All pages open to `Everyone`; being a sub/VIP unlocks more *within* a
   page — never by hiding the page.
2. **Management shell** (Plane B): the main dashboard. A `Moderator` sees the full shell **except**
   Broadcaster-floored pages (Integrations, Roles & Permits, some Settings tabs).
3. **Admin area** (Plane C, SaaS staff only): reached via "Switch to Admin" in the profile menu — the
   item only renders if the account holds an IAM role. Never appears on self-host.

### 1.3 The three golden UI rules

- **Pages: hide below the read floor.** A sidebar item renders only if the user's role ≥ the page's
  read floor (most pages: `Moderator`).
- **Actions: disable, don't hide, below the manage floor** — with a reason tooltip. Example: a
  Moderator on Commands sees the list (read floor Moderator) but "New command" is disabled with
  *"Requires Editor"* (manage floor Editor).
- **Never show the ladder numbers.** The values in §1.1 (`0/2/4/6/10/20/30/40`) are an **internal**
  comparison mechanism only — users think in role **names**. Every user-facing surface (copy, tooltips,
  dropdowns, badges, role pickers, action-permission rows) shows the **name** only — `Moderator`,
  `Editor`, `Broadcaster`, `VIP`, `Subscriber`, `Everyone` — never the number, never `Moderator (10)`
  or `Editor30`. The role/effective-role DTOs carry **both** the level (for the client's `≥` gating) and
  a name; render the name, use the level only for comparisons. (Enforced today on the Roles & Permits
  action-permission screen — mirror it on any new role surface.)

So **every management page needs at least two designed states**: read-only (viewer role ≥ read floor
but < manage floor) and write-enabled. Add the usual empty/loading/error states on top.

The frontend gate is UX only — the backend re-checks everything. Don't design flows that depend on
hiding something for security.

### 1.4 Dangerous capabilities — special treatment

Some actions are **Critical tier**: they can never be unlocked for a whole role, only granted to one
named person by the broadcaster (via `!permit` in chat or the Roles & Permits page). The set:

`giveaways:codes:write` (secret giveaway codes) · `roles:manage` · `moderation:nuke` (mass ban) ·
`moderation:sharedban:write` · `moderation:moderator:write` · `music:token:rotate` ·
`obs:config:write` · `obs:control:broadcast` · `supporters:config:write` · `automation:tokens:write` ·
`code:script:author` (write sandboxed code — the one Critical capability that IS grantable).

**Design implication:** the permission matrix (Roles & Permits page) must render these rows as
locked-to-Broadcaster with per-user grants listed separately — a floor that cannot be lowered.

### 1.5 The Roles & Permits page (Broadcaster only)

- **Team list:** who has which `ManagementRole`, where it came from (Twitch mod badge, Twitch editor,
  granted in the bot, owner), grant/remove. The owner row is not removable.
- **Action-permission matrix:** every action key (~150 management + ~26 community) with its default
  **required role** and the channel's **override** — both shown and picked by role **name**, never a
  number (§1.3). The override is a role picker offering the named rungs at or above the action's floor:
  overrides can only **raise** a requirement, never drop below the floor; Critical rows can't be changed
  at all.
- **Permits:** active per-user grants (role or single capability) with optional expiry; revoke;
  everything mirrors the `!permit` / `!unpermit` chat commands.

### 1.6 Twitch-scope gating (the "action-required" flow)

Features map to Twitch OAuth scopes. When a user enables a feature whose scopes aren't granted yet,
the backend answers with the **missing scopes + a one-click re-grant URL**. The UI must:

- surface an **action-required prompt** (dialog/banner) listing what's missing, with the re-grant
  button — never fail silently;
- render "missing progressive scope" in diagnostics as **"unlocks when you enable the feature"**, not
  as an error;
- handle **scopes dropped** (user revoked on Twitch): affected features auto-disable — pages need a
  "disabled: permission was revoked, re-grant" state.
- Never design a forced logout for scope changes — re-grant is always in-place.

---

## 2. Commands & built-ins *(built — `commands-pipelines.md`)*

- CRUD custom commands; three tiers: **T1** template response, **T2** attached pipeline, **T3**
  widget/code-backed. Cooldowns and per-command permission floors.
- Built-in catalogue (`!sr`, `!skip`, `!queue`, `!volume`, `!song`, …) with per-command enable/disable.
- Live use-count updates (`CommandExecuted` hub event).
- Read Moderator / manage Editor.

## 3. Pipelines — the visual automation builder *(built — `commands-pipelines.md`, `pipeline-control-flow.md`)*

The centerpiece editor. A pipeline = blocks (≈23 action types: send chat/reply, TTS, play sound,
moderation actions, adjust currency, widget event, run code, send webhook, set stream info, …) +
conditions (~4 evaluators) + control flow (`run_pipeline`, `break`, `continue`) + 90+ template
variables (`{{user.name}}`, `{{payload.*}}`, pronouns…). Pipelines are triggered by commands, timers,
events, redemptions, webhooks, custom events. Server-side **validate** before save — design a
validation-errors surface. Read Moderator / manage Editor.

## 4. Timers & Quotes *(built)*

- **Timers:** CRUD + on/off toggle; fire on interval/message count into chat or a pipeline.
- **Quotes:** CRUD + random/by-number recall (viewers use `!quote` in chat).

## 5. Events *(built — `twitch-eventsub.md`, `custom-events.md`, `event-store.md`)*

- **Event responses:** one bot reaction per Twitch event type (follow, sub, resub, gift, cheer, raid,
  online…), toggle + edit.
- **EventSub subscriptions:** list/create/delete/**reconcile** against Twitch; missing scopes surface
  the action-required prompt (§1.6).
- **Custom data sources / events:** connect external sources (presets available), test them, fire
  `custom.*` pipeline triggers — the generic "bring your own event" hook (e.g. heart-rate).
- **Event store:** export/import the channel's history, rebuild projections — an admin-ish data panel
  with confirm-heavy actions.

## 6. Chat *(built — `chat-decoration.md`)*

Live chat console: rich messages (Twitch + 7TV/BTTV/FFZ emotes, badges, cheermotes, colored mentions,
opt-in link previews — delivered as ready-to-render fragments), send-as-bot, delete message,
announcements, chat settings (slow/follower/emote-only…). Live feed via `ChatMessage` hub event.

## 7. Moderation *(built core — `moderation.md`)*

Rules CRUD, AutoMod config, blocked terms, Shield Mode, banned-user list + unban, mod-action log
(live via `ModAction`), stats, direct actions (ban/timeout/delete), shoutouts.
**Specced-only extensions (§14):** network-nuke, shared-ban lists, viewer reports + evidence bundles,
per-user mod panel (notes/history/trust), unban-request queue, suspicious users, escalation ladder.

## 8. Channel points, live-ops & stream tools *(built — `rewards.md`, `broadcaster-liveops.md`)*

- **Rewards:** CRUD + Twitch sync; **managed vs unmanaged** rewards must look different; redemption
  queue with fulfill/refund (live via `RewardRedeemed`); reward leaderboard. Create/delete = Broadcaster.
- **Live-ops quick actions** (Dashboard panels, not a page): polls create/end, predictions create/end,
  raid start/cancel, ad schedule/run/snooze, create clip.
- **Stream tools:** title/game/tags editing with category search; stream status.
- **Specced-only:** stream schedule editor + vacation, stream markers.

## 9. Economy *(built — `economy.md`)* — the largest domain

- **Currency:** config, earning rules, account list, per-account ledger, adjust/freeze, transfers.
- **Store/catalog:** item CRUD + toggle, purchases, refunds.
- **Savings jars:** shared pools — create, invite, accept/revoke membership, contribute, withdraw, history.
- **Games:** chat mini-games + fun-money gambling; per-game config; **per-viewer 18+ consent**
  (grant/revoke) — an explicit consent UI, off by default.
- **Leaderboards:** configs + rankings + per-viewer opt-in/out.
- Viewer side (participant rung): balance, store purchases, jars, transfers, play + history, leaderboards.

## 10. Music & song requests *(built — `music-sr.md`)*

- **Music:** now-playing, full playback remote (queue add/remove, skip, pause/resume, seek, shuffle,
  repeat), device list + transfer, playlists/play-context. Live via `MusicStateChanged`.
- **Song requests:** queue moderation, blocklist, **fair-queue + trust** settings, SR-page token + rotate.
- **Public page (no login):** viewers open the tokenized song-request page and submit tracks — a
  standalone public web surface to design.
- **Specced-only:** media share queue (approve/reject/skip video requests), request bumps.

## 11. TTS *(built base — `tts.md`)*

Config, voice catalogue (Azure + ElevenLabs), test/preview. Audio plays through the overlay.
**Specced-only:** mod approval queue, per-viewer voice assignment, profanity filter config, BYOK
provider keys, usage/quota view.

## 12. Overlays, widgets & sounds *(built core — `widgets-overlays.md`, `sound-system.md`)*

- **Widgets:** install/enable/rename/clone/delete from the first-party catalogue; widgets render as
  OBS browser-source pages (tokenized URLs); pipelines push `widget_event`s to them.
- **Sound clips:** upload/manage/preview; `play_sound` pipeline action; plays via overlay.
- **Specced-only:** widget gallery (curated community widgets), overlay manifest endpoint, widget
  versioning/build pipeline.

## 13. Integrations, webhooks, code & community *(built)*

- **Integrations (Broadcaster):** connect/disconnect Spotify, YouTube, Discord, TTS providers; BYOC
  Twitch app credentials; channel white-label **bot account** connect/status/disconnect; Twitch scope
  **diagnostics** page (per-feature scope matrix — see §1.6). OAuth ends on a branded popup relay page.
- **Discord (deep):** connection + server consent, notification configs with **preview**, role
  management + opt-in buttons posted to Discord, dispatch log. Read Moderator / manage SuperMod.
- **Webhooks:** inbound endpoints (token ingest URLs + rotate) feeding pipelines; outbound endpoints
  (signed, rotate secret, test, re-enable after failure).
- **Custom code (T3):** script list, editor with versions, publish/enable/disable — Broadcaster-floor,
  delegable per user (§1.4).
- **Community/viewers:** member list (real Twitch data only — never fake), per-user detail, trust
  level, ban/VIP management; analytics (daily trends, summary, top viewers, per-viewer engagement +
  watch streaks, opt-out); GDPR self-service (my-data export, delete).
- **Pronouns:** viewer-set pronouns surface across chat/templates.
- **Settings & setup:** first-run wizard (Twitch device-code connect → bot account → basics →
  integrations), channel lifecycle (onboard/join/leave/reset/delete — destructive ⇒ confirm dialogs),
  per-channel feature toggles, Settings tabs (Bot, Bot Account, Appearance, Account, Billing).
- **Billing (SaaS):** plan/usage/invoices, checkout/change/cancel via Stripe portal, founder badge,
  invite codes. Tiers meter **quantity** (how many commands/timers), never expressiveness; self-host
  is always unlimited.
- **Platform Admin (Plane C):** tenants, users, system health, feature flags + per-channel overrides,
  billing grants, federation peering, platform analytics. Roles: `platform-super-admin`,
  `platform-support`, `platform-trust-safety`, `platform-billing`, `platform-iam-admin`,
  `platform-analyst`. Break-glass channel access shows a **"platform access"** marker in the shell.

## 14. Specced but NOT built yet — still needs design

| Capability | Spec | Design surface |
|---|---|---|
| Giveaways | `giveaways.md` | CRUD, open/close, draw/redraw winners, masked secret code pools |
| OBS control | `obs-control.md` | Connection + bridge setup, scene/input control, ~20 pipeline actions |
| VTube Studio | `vtube-studio.md` | Connect/authorize, model inventory, control actions |
| Media share | `media-share.md` | Video request queue: approve/reject/skip/reorder + overlay |
| Automation API | `automation-api.md` | External API tokens (create/rotate/delete), event catalog |
| Stream Deck | `stream-deck.md` | Pairing-code flow |
| Marketplace/bundles | `marketplace.md` | Export/import bundles, browse/install/publish |
| Supporter events | `supporter-events.md` | Ko-fi/Patreon/etc. connections + event feed |
| Per-viewer data | `per-viewer-data.md` | Key/value store browser per viewer |
| Engagement triggers | `engagement.md` | Auto-engagement config |
| Live overlay games | `live-games.md` | Session start/cancel + game catalog (overlay-rendered) |
| Schedule & markers | `broadcaster-liveops.md` | Stream schedule editor, vacation, markers |
| Widget gallery | `widgets-overlays.md` | Curated community widget browsing/publishing |
| TTS advanced | `tts.md` | Approval queue, per-viewer voices, filters, BYOK, usage |
| Advanced moderation | `moderation.md` | Nuke, shared bans, reports/evidence, mod panel, unban queue |
| GDPR/compliance admin | `gdpr-crypto.md` | Dedicated compliance + IPC dev-mode surfaces |

## 15. Real-time — design the live states

| Hub event | Where it lands |
|---|---|
| `ChatMessage` | chat console feed |
| `StreamStatusChanged` | live/offline badge (Dashboard, top bar) |
| `ChannelEvent` | Dashboard activity feed |
| `MusicStateChanged` | now-playing (dashboard + participant page) |
| `RewardRedeemed` | redemption queue counter/rows |
| `ModAction` | mod-log prepend |
| `CommandExecuted` | command use counts |
| `AlertTriggered` | shell-level toast (e.g. "integration disconnected") |

A connection-state dot (`HubState`) lives in the top bar; every live surface needs a
"connection lost / reconnecting" treatment.

## 16. Public surfaces (no login, outside the app shell)

- **Song-request page** (built) — viewers, tokenized URL.
- **OAuth relay popup** (built) — branded "connected" close-me page.
- **Widget/overlay pages** (built core) — OBS browser sources; TTS + sounds + alerts render here.
- **Inbound webhook ingest** (built) — machine-only, no UI.
- **OBS bridge page** (specced) — browser-source that relays OBS control.
