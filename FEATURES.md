# NomNomzBot — Complete User-Facing Feature Inventory

> Every user-facing feature in the project, checked against the frontend Information Architecture
> (`.claude/docs/design/spec/frontend-ia.md`), all 67 domain specs in `.claude/docs/design/spec/`, all
> 100 concrete API controllers (94 versioned plus six public hosts), the 68 Compose API facades, all 50
> shipped Compose feature roots, and the Stream Deck manifest. Last full code pass: 2026-09-04.
>
> NomNomzBot is an open-source, multi-tenant, multi-platform bot platform — one channel, many
> platform connections (Twitch, Kick, YouTube, X Live) managed from one uniform dashboard for
> streamers, moderators and viewers. This document lists **what a person can do**, grouped by
> where they do it.

---

## Table of Contents

1. [Who uses the product — the three planes / rungs](#1-who-uses-the-product)
2. [Onboarding & setup](#2-onboarding--setup)
3. [Identity, accounts & authentication](#3-identity-accounts--authentication)
4. [The dashboard shell](#4-the-dashboard-shell)
5. [Home / Dashboard (live home)](#5-home--dashboard)
6. [Chat](#6-chat)
7. [Commands, pipelines, timers & quotes](#7-commands-pipelines-timers--quotes)
8. [Moderation](#8-moderation)
9. [Loyalty — Channel Points, Economy, Games, Giveaways](#9-loyalty)
10. [Music, Song Requests & TTS](#10-music-song-requests--tts)
11. [Stream — Overlays/Widgets, Event Responses, Analytics, Sounds, Media, Assets, Code](#11-stream)
12. [Community / Viewers](#12-community--viewers)
13. [Connect — integrations & external surfaces](#13-connect)
14. [Setup — configure-once owner area](#14-setup)
15. [Settings](#15-settings)
16. [The participant (viewer) rung](#16-the-participant-viewer-rung)
17. [Platform Admin area (SaaS)](#17-platform-admin-area)
18. [Public and outside-the-shell surfaces](#18-public-and-outside-the-shell-surfaces)
19. [Built-in chat commands](#19-built-in-chat-commands)
20. [Pipeline actions, conditions & template variables](#20-pipeline-actions-conditions--template-variables)
21. [Event triggers (EventSub / supporter / custom)](#21-event-triggers)
22. [Companion apps & developer surfaces](#22-companion-apps--developer-surfaces)
23. [Cross-cutting features](#23-cross-cutting-features)

---

## 1. Who uses the product

Three planes of access, one shell, role-gated (never role-forked):

- **Community (Plane A)** — viewers, with a read-only standing inferred from the platform
  (`Everyone` · `Subscriber` · `VIP` · `Artist` · `Moderator`, plus sub tier). Gates the participant rung.
- **Management (Plane B)** — the streamer + anyone they delegate: `Moderator` · `SuperMod` · `Editor` ·
  `Broadcaster`. Uses the main dashboard shell.
- **Platform IAM (Plane C)** — NoMercy Labs staff / service principals (SaaS only): `platform-super-admin` ·
  `platform-iam-admin` · `platform-analyst`. Uses the gated Admin area.

Persona priority for every decision: **streamer → moderator of many channels → viewer**.

---

## 2. Onboarding & setup

First-time setup wizard (`onboarding-setup.md`, `feature/setup`):

- **Connect your first platform** — Twitch / Kick / YouTube / X (device-code where available); this
  creates the channel.
- **Connect a bot account** — optional separate account the bot types from; until connected the bot
  types as the streamer's own account with a user-defined line prefix.
- **Configure basics** — bot command prefix, default language, timezone.
- **Enable integrations** — Spotify, Discord, etc. (skippable, done later from Settings).
- **Returning users** — quick login or remembered-session restore; never a repeat device-code dance.
- Lands on the dashboard home after completion.

---

## 3. Identity, accounts & authentication

From `identity-auth.md`, `platform-identity.md`, `AuthController`, `UsersController`:

- **Twitch Device Code Flow login** (secret-free) — approve on twitch.tv/activate.
- **Bot account device-code connect** — separate identity the bot posts as.
- **OAuth code flow** for integrations and redirect-based logins.
- **Multi-platform sign-in** — Kick (OAuth 2.1 + PKCE), X/Twitter (OAuth 2.0 + PKCE), YouTube (device-code).
- **Linked platforms** — link/unlink Twitch/Kick/YouTube/X to one viewer identity (`UserIdentity`).
- Choose the **primary linked identity**; edit the signed-in profile; inspect the channels associated
  with an identity.
- **Progressive scopes** — enabling a feature that needs new permissions triggers a one-click additive
  re-grant (chat + dashboard prompt), never a forced logout.
- **Session management** — JWT bearer tokens, refresh, logout; web keeps the refresh token in an
  HttpOnly cookie, native in the OS keychain.
- **Session revocation** — log out the current session or every session.
- **Token custody** — AES-encrypted at rest; BYOC (bring-your-own Twitch client) supported.
- **Pronouns** — self-set pronouns surfaced in chat/templates (`pronouns.md`, `PronounsController`).

---

## 4. The dashboard shell

From `frontend-ia.md` (`feature/shell`):

- **Grouped, collapsible sidebar** — feature workspaces (top, scrolling) + pinned Setup group (bottom).
- **Top bar** — page title, active-channel chip (native: channel switcher), SignalR connection-state
  indicator dot, reserved global search.
- **Profile menu** — identity + role badge, connection switcher (native only), "Switch to Admin" (IAM
  holders only), theme (Light/Dark/System), language (en/nl), Account, Log out.
- **Connection switcher** (native app) — saved server connections, mDNS LAN auto-discovery + manual add,
  per-server tokens; switch backend, forget a saved connection, reconnect, and recover from an
  unreachable server.
- **Channel switcher** — switch among owned channels or enter a channel the user moderates without
  onboarding it as their own.
- **Role-gated visibility** — hide pages below the read floor; disable (with reason tooltip) actions
  below the manage floor.
- **Theme** — dynamic accent derived from the signed-in user's Twitch chat color (light + dark).
- **Runtime language switch** — English / Dutch, no reload.
- **Operational state UI** — reconnect banner, proactive Twitch reauthorization dialog, action-required
  notifications, admin impersonation banner/exit, and broadcaster preview-as-viewer mode.

---

## 5. Home / Dashboard

`Dashboard` route (`community-dashboard.md`, `feature/home`, `DashboardController`):

- Live chat feed, stream status, stat tiles, alerts — the live home.
- Activity feed with event replay to widgets, top-command usage, first-run shortcuts, plus a
  dismissible **attention inbox** for connection/scope and moderation actions.
- Review held AutoMod messages directly from the attention inbox: allow, block, timeout, ban, or add
  the triggering text as a blocked term, including bulk actions for the same user.
- **Bot-run chat polls** are created, monitored and closed in an embedded Dashboard card (they are not
  a separate sidebar page and are distinct from Twitch-native polls).
- **Stream live-ops quick-action panels** (run-while-live, `stream-admin.md`, `broadcaster-liveops.md`):
  - Set stream title / game / tags.
  - Start / end **polls**.
  - Start / lock / resolve / cancel **predictions**.
  - Start / cancel **raids**.
  - Start commercial / snooze **ad schedule**.
  - Create **stream markers**.
  - Create **clips**.
  - Manage the **stream schedule** (segments + vacation).

---

## 6. Chat

`Chat` route (`chat-client.md`, `chat-decoration.md`, `feature/chat`, `ChatController`):

- **Live chat console** — send as yourself (the operator), bot optional.
- Reply to a specific message, delete messages, send colored announcements, and choose the sending
  identity where available.
- Read and update channel chat-mode settings.
- **Emote composer + autocomplete**.
- **Multi-channel feed** (`MultiChat`) — provider-merged view across channels a moderator works.
- **Cross-channel moderation** — act on chat from within the console.
- Quick per-user actions inline (timeout/ban/etc).
- **Chat-message decoration** (`chat-decoration.md`) — third-party emotes (7TV / BTTV / FFZ), badges,
  cheermotes, and link previews rendered inline in the chat feed.
- **Emoji / emote style preference** (`feature/emoji`, `EmojiStyleController`) — the viewer picks how
  emoji/emotes render (style/set), stored as a personal preference.
- **Chat polls** (`feature/chatpolls`, `ChatPollsController`) — bot-run text polls in chat (distinct
  from Twitch-native polls under live-ops), created/read/closed from the Dashboard card.

---

## 7. Commands, pipelines, timers & quotes

From `commands-pipelines.md`, `quotes.md` (`feature/commands`, `pipelines`, `timers`, `quotes`,
`chattriggers`, `picklists`).

- **Custom commands** — list + create/edit/delete; three tiers:
  - T1 simple text response, T2 opens the visual pipeline editor, T3 the widget/code editor.
- **Built-in commands** — toggle list of shipped commands, per-command enable/config.
- Override an individual built-in's response template; command use counts update live.
- **Visual pipeline builder** (`Pipelines`, `pipeline-tree-and-editor.md`) — drag/branch action chains
  with conditions; control-flow (branches, loops-guarded, stop).
- **Pipeline execution history** (`PipelineHistoryScreen`, `PipelineExecutionsController`).
- **Pipeline test runs and validation** — supply variables, inspect output/errors before saving, and
  browse failure-only execution history with per-run detail.
- **Timers** — recurring messages/pipelines by interval and/or message count, with enable/disable and
  pipeline test-run controls (`TimersController`).
- **Chat triggers** — keyword/pattern-triggered responses (`ChatTriggersController`).
- **Pick lists** — reusable random/sequential list picker builtin (`PickListsController`).
- Preview a pick list draw from the dashboard.
- **Quotes** — quote book CRUD + recall command (`QuotesController`).
- **Cooldowns** — per-command, per-user, global.
- **Template variables** (90+) in every message.
- Context-aware helper autocomplete/insert menus for command, timer and event-response templates;
  event-only variables narrow to the selected EventSub type.
- One-shot StreamElements export import for commands, quotes and timers.

---

## 8. Moderation

Four posture-split pages (`moderation.md`, `spam-defense.md`, `feature/moderation`):

- **Moderation (live desk)** — stats, shield mode, quick action on a person, moderators list, bans.
- **Review Queue** — unban requests, viewer reports (+ evidence), AutoMod queue, spam review /
  detections / campaigns, follow-bot blocks.
- **Enforcement Rules** — own AutoMod + Twitch AutoMod levels, blocked terms, chat filters, rules,
  trust policy, spam-defense knobs, escalation ladder, shared/network bans.
- **History** — mod log, nuke batches (cross-channel mass ban + reversal).

Additional moderation features:

- **Timeout / ban / unban / delete message** direct actions (`ModerationController`).
- **VIP / moderator management**, block list, suspicious-user marking.
- **Per-user context panel** — notes CRUD, history, trust score/standing, warnings and escalation reset.
- **Chat controls** — slow / followers-only / subs-only / emote-only / clear chat.
- **Shoutouts and announcements** — channel default plus per-viewer shoutout templates.
- **Layered spam defense** — normalizer, account-risk, content-signals, correlation (campaign
  detection), trust tiers, enforcement; a signature network; dry-run default.
- **Shared-chat ban propagation** + trust list.
- **Multi-channel moderator** — a mod working many channels from one surface.
- **Permits** — temporary per-viewer permission grants (`PermitsController`).

---

## 9. Loyalty

### Channel Points / Rewards (`rewards.md`, `RewardsController`)
- Create / edit / delete custom channel-point rewards (managed + unmanaged).
- Redemption queue — fulfill / refund / read.
- Redemption → pipeline trigger.
- Twitch sync/import and managed-reward recreation; reward leaderboard.
- Redemption countdown timers with pause, resume, complete and cancel controls.

### Economy (`economy.md`, `feature/economy`)
- **Currency** — define channel currency, earning rules (per event/engagement).
- **Wallets & ledger** — balances, transactions, points transfers.
- **Account operations** — search/page accounts, inspect ledgers, adjust balances and freeze/unfreeze.
- **Store / catalog** — redeemables, purchases, enable/disable items and refunds (`CatalogController`).
- **Mini-games & gambling** — optional 18+ toggle, self-confirm consent (`GamesController`).
- **Savings jars** — pooled cross-channel accounts, federation-trust gated (`SavingsJarsController`).
- **Leaderboards** — channel + jar rankings, opt-out respected (`EconomyLeaderboardsController`).
- Configurable leaderboards and earning rules, including viewer opt-in/out.

### Games (`live-games.md` + economy chat games, `GameSessionsController`)
- **Chat games** (economy) + **interactive overlay games** (live-games engine).
- Per-game config; drop-in game contract; overlay delivery; live-game catalog and session start/cancel.
- Viewers read, play, and see their own play history.
- Explicit per-viewer 18+ consent grant/revoke for gated games.

### Giveaways (`giveaways.md`, `GiveawaysController`, `GiveawayCodePoolsController`)
- Create/update/delete, open/close, inspect entries, draw and redraw winners, and fulfillment.
- Secret-safe code pools with masked inventory, CSV import and owner-only reveal/assignment (including
  auto-DM prize codes).

---

## 10. Music, Song Requests & TTS

Music is a first-class area (`music-sr.md`, `tts.md`, `sound-system.md`, `media-share.md`).

### Music (`Music`, `MusicController`)
- Dashboard playback remote: play/pause, skip-next, seek, shuffle, repeat and queue removal.
- Devices + playback transfer, playlists + play-context, queue add, and live provider capability gates.
- Library + now-playing feed, with live state updates.
- Track blocklist management.
- Provider-backed: Spotify, YouTube.

### Song Requests (`SongRequests`, `PublicSongRequestController`)
- Request queue + moderation.
- Blocklist, trust/fair-queue config (Bamo's fair-queue + exponential-decay trust scoring).
- Bump/reorder, sequencing.
- Public SR-page token (viewers request without the app).
- Pause/resume/skip, remove, promote and ban queue entries; rotate the public-page token.

### TTS (`Tts`, `TtsConfigController`, `TtsQueueController`)
- Voice catalog, per-viewer voice assignment, `!voice` self-service.
- Approval queue (mod approve/reject), profanity censor, filters.
- Provider keys (Azure Cognitive Services, ElevenLabs), BYOK.
- Pronunciation lexicon + bulk voice-assignment import.
- Moderation retraction — un-say deleted messages.
- Voice and overlay tests; pause/resume/skip/clear the overlay playback queue.

### Sounds (`sound-system.md`, `SoundClipsController`)
- Sound clips / soundboard, play/stop via commands/pipeline/overlay.

### Media Share (`media-share.md`, `MediaShareController`)
- Viewer-submitted media (e.g. video clips) with moderation + overlay playback.

---

## 11. Stream

### Overlays / Widgets (`widgets-overlays.md`, `widget-sdk.md`, `feature/widgets`)
- First-party overlay catalogue — install, configure settings.
- Clone-to-edit + the code editor (custom widgets).
- Global widget gallery (curated/verified GitHub-sourced) (`WidgetGalleryController`).
- Public overlay manifest for OBS browser sources (`OverlayController`, `RenderManifestController`).
- Widget test events (`WidgetTestEventController`).
- Enable/rename/delete with dependency **blast-radius** previews; schema-driven settings forms.
- Project editor, compile/version history, rollback, gallery update checks, overlay-token rotation, and
  reviewer submit/review/pin workflow.
- Link preview (OG card + YouTube trust score) for rendered links.

### Event Responses / Alerts (`EventResponses`, `EventResponsesController`)
- Follow / sub / raid / cheer / gift event responses with chat-message, overlay or pipeline response
  types; choose/create a bound pipeline or target widget, insert event variables, toggle, and reset to
  defaults.
- The separate Alerts page provides a focused create/edit/toggle/delete view of on-air alert messages
  while preserving pipeline bindings owned by Event Responses/Pipelines.

### Analytics (`analytics.md`, `AnalyticsController`)
- Per-channel projection dashboards.
- Stream list and per-stream drill-down; daily trends, summaries and top viewers.
- Searchable/paged per-viewer analytics with engagement and watch-streak drill-down plus opt-out.
- Watch-session presence and daily rollups.

### Schedule (`ScheduleScreen`, live-ops schedule).

### Code Scripts (`custom-code.md`, `code-execution-sandbox.md`, `CodeScriptsController`)
- T3 sandboxed multi-file project editor with SDK types, test runs, versions, publish/rollback,
  enable/disable and dependency-aware deletion (Broadcaster floor).

### Assets (`AssetsController`) — asset/image library for overlays and responses.

---

## 12. Community / Viewers

`Community` route (`community-dashboard.md`, `roles-permissions.md`, `CommunityController`):

- Viewer list / standings / leaderboards.
- Real platform data only — provider-fanned Twitch + Kick + YouTube, merged per viewer identity;
  never fabricated.
- Per-viewer drill-in (standing, activity, roles, moderation context, analytics and watch streaks).
- Search/filter/paging and top-chatters views.
- Manage trust, bans, VIP status and shoutouts from the viewer; inspect/edit custom viewer data and
  personalized messages; privileged operators can export/erase that subject's data.

---

## 13. Connect

External surfaces (`feature/connect`, `discord`, `webhooks`, `federation`, `customevents`,
`supporters`, `obs`, `vts`, `automation`):

- **Discord** (`discord.md`, `DiscordController`) — guild link (both-opt-in handshake),
  notification rules (event → channel → template), self-assign notify roles, member opt-in,
  "currently live" role, config preview, postable role buttons, guild role/channel discovery,
  dispatch log + dedupe.
- **Webhooks** (`webhooks.md`) — inbound endpoints (verified ingest → pipeline trigger) +
  outbound endpoints (signed delivery, event catalogue, test, secret/token rotation, re-enable,
  delivery log and manual retry/dead-letter) (`WebhooksController`, `InboundWebhookController`).
- Inbound adapters for Ko-fi, GitHub, Fourthwall, Shopify, Patreon, Buy Me a Coffee and configurable
  generic signed payloads.
- **Federation** (`federation-oidc.md`, `FederationController`, `ChannelFederationController`) —
  cross-instance trust directory, peer registration/trust/revoke, key rotation/deactivation, mTLS
  handshake, signed events, per-channel opt-in.
- **Custom events** (`custom-events.md`, `CustomDataSourcesController`) — user-defined event
  sources/presets → pipeline triggers + overlays, with searchable sources, sample-payload tests and
  live fetch/field-mapping inspection.
- **Supporter events** (`supporter-events.md`, `SupportersController`) — Patreon, Shopify,
  TreatStream ingest → alerts + economy.
- **OBS control** (`obs-control.md`, `ObsController`) — scene/source control, typed ops + raw,
  browser-source bridge setup/status/token rotation, connection probe, scene switching, input mute/
  volume, streaming/recording controls, and OBS events as triggers.
- **VTube Studio** (`vtube-studio.md`, `VtsController`) — model/hotkey control + events as triggers.
- VTube Studio also exposes connection setup/authorization, bridge-token rotation and live inventory.
- **Automation API** (`automation-api.md`, `AutomationDataController`, `AutomationTokensController`,
  `AutomationPairingController`) — scoped external API tokens (create/rotate/revoke), expiring pairing
  codes/device approval, event catalogue, and REST + WebSocket data plane.

---

## 14. Setup

Pinned, configure-once owner area (`frontend-ia.md` §3):

- **Integrations** (`integrations-oauth.md`, `IntegrationsController`, `IntegrationOAuthController`) —
  Spotify / Discord / YouTube / TTS OAuth, Twitch and Kick bot accounts, provider reconnect/disconnect,
  BYOC Twitch client setup, and Twitch EventSub subscription inspection/reconciliation.
- **Roles & Permits** (`roles-permissions.md`, `RolesController`, `PermissionsController`,
  `ActionPermissionsController`, `PermitsController`) — Plane-B role memberships, action-permission
  matrix (per-action override, broadcaster can lower safe-action floors), permit grants.
- **Bundles / Marketplace** (`marketplace.md`, `BundlesController`, `MarketplaceController`) — install
  bundles of commands/pipelines/widgets; export, inspect-before-import, conflict policy, uninstall
  blast radius, browse/install, publish/status and publisher-token management.
- **Features** (`FeaturesController`) — per-channel feature toggles.
- **Bot Account** — dedicated bot-identity OAuth (in Settings/Integrations).

---

## 15. Settings

Tabbed page (`frontend-ia.md` §5, `feature/settings`):

- **Bot basics** — command prefix, default language, timezone.
- **Bot personality** — select the bot's response tone.
- **Engagement automation** — opt-in welcome-first-timer, returning-regular and loyalty reactions.
- **Bot Account** — connect/disconnect the dedicated bot account.
- **Appearance** — sidebar collapse default, theme, accent behavior (per-subject vs pinned).
- **Account** — signed-in identity, session/logout.
- **Billing** (SaaS) — tier/subscription, usage vs limits, invoices (`monetization-billing.md`,
  `BillingController`).
- **Channel lifecycle / danger zone** — join/leave chat, disconnect the bot account, reset configuration
  and delete the channel with confirmation.
- **Twitch permissions diagnostics** — per-feature scope matrix and additive device-code re-grant.
- **Event-journal portability** — export/import a deduplicated channel ledger and rebuild projections.
- **Resource limits** — current usage, soft/hard limits and entitlement state.
- **My Data** (separate Setup sidebar page) — downloadable GDPR export, erasure blast-radius preview/
  request/status, participation opt-out, and consent grant/withdrawal records (`GdprController`,
  `ComplianceController`).

---

## 16. The participant (viewer) rung

A viewer with no management role still gets a real six-page surface (`frontend-ia.md` §3a,
`feature/participant`, `ViewerDataController`, `per-viewer-data.md`):

- **My Channel** — own profile/standing/activity + the channel's public summary.
- **Now Playing** — live now-playing + queue; submit a song request.
- **Leaderboards** — channel leaderboards + own opt-in/opt-out toggle.
- **Points & Store** — balance, catalog (view + purchase), community jars, points transfers.
- **Games** — read, play, own play history.
- **Me** — editable profile, pronouns, activity summary, participation footprint, own TTS voice and
  linked platforms/channels; community standing is shown here and on My Channel.
- Progressive unlocks (sub-only lanes, higher pending limits) decided on the page, never by hiding it.

The GDPR **My Data** capability is signed-in self-service, but its current Compose navigation route is
in the management Setup group with a Moderator read floor; `ParticipantNav` does not currently expose
it to a role-less viewer. That is an implementation/navigation gap, not a seventh participant page.

---

## 17. Platform Admin area

SaaS-only, IAM-gated (`platform-admin.md`, `stream-admin.md`, `feature/admin`,
`PlatformAdminController`, `PlatformIamController`, `PlatformAnalyticsController`,
`FeatureFlagAdminController`, `AdminBillingController`, `AdminSpamDefenseController`):

- **Tenants** — list/search channels, status, plan, health.
- **Tenant detail** — suspend/resume; **break-glass access** into a channel shell (audited with
  justification).
- **Feature flags** — staged-rollout flags.
- **Billing** — invite-code issue/revoke, tier grants, founder badges and platform billing/metering.
- **IAM — principals** — promote users, create service accounts, activate/deactivate principals and
  inspect effective permissions.
- **IAM — roles** — role/permission assignment.
- **Audit log** — every privileged action (incl. break-glass).
- **Platform analytics** — cross-tenant metrics.
- **Spam-defense defaults** — platform-level spam config.
- **Provider credentials** — inspect, configure/rotate and clear platform OAuth/API credentials.
- **Live operations overview** — system/event feed and live registry/log streams over the Admin hub.

Self-host builds never reach this graph (no-op IAM adapter).

---

## 18. Public and outside-the-shell surfaces

Served by the bot outside the Compose dashboard shell (some are anonymous; pairing approval requires
the operator to sign in before approving):

- **Song-request page** — viewers submit songs via a per-channel page token or a human-shareable
  `/sr/@channel-name` link.
- **Overlays / widgets** — OBS browser sources (alerts, now-playing, games, etc.).
- **OAuth callback landing** — sign-in / integration redirect landing.
- **Device-pairing approval page** — approve an Automation/Stream Deck pairing code.
- **OBS bridge host** — browser-source bridge that connects a local OBS instance to the bot.
- **Inbound webhook ingest** — token + signature-gated external event endpoints.
- **Discord interactions webhook** — opt-in role buttons (`DiscordInteractionsController`).
- **Kick webhook** ingest (`KickWebhookController`).
- **Billing webhook** — Stripe inbound (`BillingWebhookController`).

---

## 19. Built-in chat commands

Viewer/mod-facing chat commands shipped with the bot (from the built-in command catalogue and the
per-subsystem specs). Representative set:

- `!voice` — self-service TTS voice pick.
- Song requests — request / current / queue / skip / volume (via the music surface).
- Quotes — add / delete / recall.
- Economy — balance / give / gamble / games.
- Shoutout, uptime, followage/watchtime, commands/help, pronouns.
- Media share submit.
- Pick-list draw.

All are toggleable and reconfigurable from the Commands page; the exact catalogue is discovered at
runtime (`IBuiltinCommandCatalog`), not a hardcoded list.

---

## 20. Pipeline actions, conditions & template variables

From `commands-pipelines.md` §6, `pipeline-control-flow.md`:

**Actions** (108 registered snake_case `ActionType` values, grouped for readability):

- **Chat/moderation/community:** `send_message`, `send_reply`, `announce`, `delete_message`, `timeout`,
  `ban`, `shoutout`, `permit`, `unpermit`, `post_quote`, `pick_from_list`, `set_pronoun`,
  `submit_media`.
- **State/economy/games/giveaways/rewards:** `set_variable`, `set_counter`, `adjust_counter`,
  `set_viewer_data`, `adjust_viewer_data`, `clear_viewer_data`, `check_balance`, `grant_currency`,
  `deduct_currency`, `jar_contribute`, `play_game`, `open_giveaway`, `enter_giveaway`,
  `draw_giveaway`, `start_live_game`, `cancel_live_game`, `redemption_fulfill`, `redemption_refund`.
- **Sound/TTS/widgets/outbound:** `play_sound`, `stop_sound`, `play_tts`, `tts_synthesize`,
  `widget_event`, `send_webhook`, `send_discord_notification`.
- **Song requests:** `song_request`, `song_current`, `song_queue`, `song_skip`, `song_previous`,
  `song_pause`, `song_resume`, `song_volume`, `song_ban`, `song_wrong`, `playlist_add`.
- **Music remote:** `music_play`, `music_pause`, `music_play_pause`, `music_next`, `music_previous`,
  `music_seek`, `music_set_volume`, `music_volume_up`, `music_volume_down`, `music_volume_mute`,
  `music_set_shuffle`, `music_toggle_shuffle`, `music_set_repeat`, `music_cycle_repeat`,
  `music_transfer_device`, `music_save_track`, `music_unsave_track`, `music_toggle_saved`,
  `music_add_to_playlist`, `music_remove_from_playlist`, `music_follow_artist`,
  `music_unfollow_artist`.
- **OBS:** `obs_switch_scene`, `obs_set_preview_scene`, `obs_transition`, `obs_set_source`,
  `obs_filter`, `obs_input_mute`, `obs_input_volume`, `obs_media`, `obs_hotkey`,
  `obs_refresh_browser`, `obs_screenshot`, `obs_recording`, `obs_streaming`, `obs_replay_buffer`,
  `obs_save_replay`, `obs_virtual_cam`, `obs_request`, `obs_request_batch`, `obs_call_vendor`.
- **VTube Studio:** `vts_load_model`, `vts_trigger_hotkey`, `vts_set_expression`, `vts_move_model`,
  `vts_color_tint`, `vts_request`.
- **Flow/runtime/live ops:** `run_pipeline`, `schedule_pipeline`, `run_code`, `return_value`, `wait`,
  `wait_until_raid_fires`, `wait_for_event`, `require_tier`, `start_raid`, `break`, `continue`, `stop`.

The tree editor additionally supplies structural `if`, `switch`, weighted `random_branch`, guarded
`loop` and `try` blocks. **Registered conditions** are `user_role`, `random`, and `comparison`.

**Template variables** (90+): `{{user.name}}`, `{{channel.title}}`, `{{stream.uptime}}`, `{{args.1}}`,
`{{random.number:1:100}}`, `{{view_count}}`, `{{viewer_count}}`, pronoun/viewer-data/webhook-payload
namespaces, etc. (`payload.*` from inbound webhooks is treated as untrusted/tainted.)

---

## 21. Event triggers

Anything that can start a pipeline / event response:

- **Twitch EventSub** (`twitch-eventsub.md`, 74 topics) — stream.online/offline, follow, subscribe,
  gift subs, cheer, raid, channel-point redemption, polls, predictions, chat message, and more.
- **Supporter events** — Patreon membership, Shopify orders, TreatStream treats.
- **Custom events** — user-defined sources.
- **Inbound webhooks** — external systems.
- **OBS events** and **VTube Studio events**.
- **Reward redemptions**, **timers**, **chat triggers**, **federation** inbound events.

---

## 22. Companion apps & developer surfaces

- **Stream Deck plugin** (`stream-deck.md`, `streamdeck-plugin.md`, `tools/streamdeck/`) — self-pairing
  physical controls for play, pause, play/pause, now-playing cover/time, next/previous, fixed/step/
  mute volume, seek, set/toggle shuffle, set/cycle repeat, device transfer, save/unsave/toggle favorite,
  playlist add/remove, and artist follow/unfollow.
- **IPC developer mode** (`IpcDevModeController`) — local IPC key registry / socket for dev tooling.
- **SDK** (`SdkController`, `widget-sdk.md`, `dev-platform.md`) — widget/dev SDK surface.
- **Import** (`ImportController`) — import commands, quotes and timers from a StreamElements chatbot export.
- **Templates** (`TemplatesController`) — reusable command/pipeline templates.
- **System / health** (`SystemController`, `NotificationsController`, `TwitchDiagnosticsController`,
  `EventSubController`, `EventStoreController`) — diagnostics, in-app notifications, event store.
- **EventSub operations** — inspect subscriptions, reconcile desired vs actual Twitch subscriptions,
  and surface missing-scope reauthorization.

---

## 23. Cross-cutting features

- **Multi-tenancy** — unlimited channels per deployment, each a fully isolated dashboard.
- **Multi-platform** — Twitch, Kick, YouTube, X Live connections simultaneously per channel.
- **Real-time** — SignalR dashboard / overlay / OBS-relay / admin hubs.
- **Live UI updates** — chat, stream status, activity, music, redemptions, moderation actions, command
  use counts and alerts update without a page refresh, with reconnecting/lost-state treatment.
- **i18n** — English + Dutch throughout, runtime switch.
- **Design system** — shadcn (new-york) ported to Compose; dynamic accent from user's chat color.
- **Deployment profiles** — self-host lite (SQLite), self-host full (Postgres+Redis), SaaS (restricted).
- **GDPR / privacy** — per-viewer export & erasure, consent records, crypto-shredding, analytics opt-out.
- **Billing & entitlements** (SaaS) — tiers, usage metering, quotas, invite codes / founders badge.
- **Federation** — cross-instance trust for shared bans, savings jars, and events.
- **Dependency-aware destructive actions** — blast-radius previews before deleting linked assets,
  commands/pipelines, widgets, rewards, bundles, economy objects and personal data.

---

*Generated by scanning the information architecture and 67 domain specs, 100 concrete API controllers, 68
Compose API facades, 50 Compose feature roots, shell/participant route maps, public hosts and the
Stream Deck manifest. Domain specs remain the authoritative contract for exact subsystem behavior.*
