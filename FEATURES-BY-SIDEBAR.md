# NomNomzBot — Features by Sidebar Order × Audience (Streamer vs Moderator)

> The management-sidebar subset of `FEATURES.md`, re-sorted two ways:
> 1. **Primary sort — the live sidebar order** as shipped in `ShellNav.pages`
>    (`app/composeApp/.../feature/shell/nav/ShellNav.kt`): groups Home → Chat → Moderation → Loyalty →
>    Music → Stream → Community → Connect → **Setup** (pinned).
> 2. **Secondary sort — audience lean** within each group: **🎥 Streamer** features first, then
>    **🤝 Shared**, then **🛡️ Moderator**.
>
> **How the lean is decided (not a guess).** Each page carries a **read floor** and a **manage floor** in
> `ShellNav`. The *manage* floor is the tell:
> - **manage = Moderator** → a **🛡️ Moderator** tool (mods act on it live, out of the box).
> - **manage = Editor** → **🤝 Shared**: mods read it, the streamer/editor configures it.
> - **manage = SuperMod / Broadcaster**, or a read-only owner page → a **🎥 Streamer** (owner) tool.
>
> Legend: **🎥 Streamer** = leans owner/config · **🛡️ Moderator** = leans live moderation · **🤝 Shared** =
> both, config vs. live-use split.
>
> Audited against the shipped route enum, `ShellNav.pages`, `ShellContent`, all Compose feature roots
> and API facades on 2026-09-04. Embedded cards, participant/admin pages and public/companion surfaces
> are called out separately so they are not mistaken for management-sidebar rows.

---

## Audience summary (at a glance)

| Lean | Pages |
|---|---|
| 🎥 **Streamer** | Overlays, Alerts, Schedule, Pipelines, Code Scripts, Analytics, Channel Points, Economy, Enforcement Rules, Event Responses, Sound Clips, Assets, Discord, Webhooks, Federation, Custom Events, Supporters, OBS, VTube Studio, Automation, Bundles, Integrations, Roles & Permits, Features, Settings |
| 🤝 **Shared** | Dashboard, Commands, Chat Triggers, Timers, Pick Lists, Games, Giveaways, Music, Song Requests, TTS |
| 🛡️ **Moderator** | Chat, Multi-Chat, Quotes, Moderation, Review Queue, History, Media Share, Viewers |

---

## HOME

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Dashboard** | 🤝 Shared | Moderator / read-only (live-ops quick-actions = Broadcaster) | Live home: chat, stream/stats, top commands, first-run shortcuts, activity replay to widgets, alerts and attention inbox. Held AutoMod messages can be allowed/blocked/timed out/banned or turned into blocked terms. Streamer runs title/game/tags, Twitch polls/predictions, raids, ads, markers, clips and schedule; an embedded card runs bot-native chat polls. |

> **Embedded, not a sidebar row:** **Chat Polls** is a card on Dashboard (`HomeScreen` →
> `ChatPollsCard`). It creates/monitors/closes bot-run text polls and is distinct from Twitch-native
> live-ops polls. There is deliberately no `ShellRoute.ChatPolls`.

---

## CHAT

*🎥 Streamer → 🤝 Shared → 🛡️ Moderator*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Event Responses** | 🎥 Streamer | Moderator / Editor | Follow/sub/raid/cheer/gift responses as chat, overlay or pipeline actions; bind/create pipelines, target widgets, insert variables, toggle and reset defaults. |
| **Commands** | 🤝 Shared | Moderator / Moderator | Custom commands (T1 text / T2 pipeline / T3 code), cooldowns and permission floors; built-in enable/disable + response overrides; live use counts. Mods manage day-to-day; deeper tiers are owner work. |
| **Chat Triggers** | 🤝 Shared | Moderator / Editor | Keyword/pattern auto-replies. Mods read; editor/streamer authors. |
| **Timers** | 🤝 Shared | Moderator / Editor | Recurring messages/pipelines by interval and/or message count; enable/disable and test the attached pipeline. |
| **Pick Lists** | 🤝 Shared | Moderator / Editor | Reusable random/sequential list picker builtin, with dashboard preview draws. |
| **Chat** | 🛡️ Moderator | Moderator / Moderator | Live chat console — send/reply/delete as you or the bot, announcements, chat settings, emote composer + autocomplete, moderation, third-party-emote/badge/cheermote/link-preview decoration. |
| **Multi-Chat** | 🛡️ Moderator | Moderator / read-only | Provider-merged multi-channel feed for a mod watching several channels. |
| **Quotes** | 🛡️ Moderator | Moderator / Editor | Quote book CRUD + recall command (mods add quotes live). |

> **Also in Chat (viewer-facing, not sidebar rows):** **chat-message decoration** — 7TV/BTTV/FFZ
> emotes, badges, cheermotes and link previews in the feed — and a per-viewer **emoji/emote style
> preference** (`feature/emoji`), an appearance setting for how emotes render.

---

## MODERATION

*🎥 Streamer → 🛡️ Moderator*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Enforcement Rules** | 🎥 Streamer | Moderator / **Editor** | AutoMod (own + Twitch levels), blocked terms, chat filters, rules, trust policy, spam-defense knobs, escalation ladder, shared/network bans. Setting policy is a different act from applying it. |
| **Moderation** (live desk) | 🛡️ Moderator | Moderator / Moderator | Stats, shield mode, bans, moderators, warnings, announcements, shoutouts/personalized templates and per-user context/notes/trust. |
| **Review Queue** | 🛡️ Moderator | Moderator / Moderator | Unban requests, viewer reports + evidence, AutoMod queue, spam review/detections/campaigns, follow-bot blocks. |
| **History** | 🛡️ Moderator | Moderator / Moderator | Mod log, nuke batches (cross-channel mass ban + reversal). |

---

## LOYALTY

*🎥 Streamer → 🤝 Shared*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Channel Points** | 🎥 Streamer | Moderator / Editor (create/delete reward = **Broadcaster**) | Reward CRUD, Twitch sync/import/recreate, leaderboard, live redemption queue (fulfill/refund) and redemption countdown timers. |
| **Economy** | 🎥 Streamer | Moderator / Editor (payout/earn rules = **Broadcaster**) | Currency/earning rules, account search + ledgers + adjust/freeze, transfers, store/catalog + refunds, savings-jar membership/movements and configurable leaderboards/opt-outs. |
| **Games** | 🤝 Shared | Moderator / Editor | Chat games + interactive overlay games; per-game config, 18+ consent, play history, live catalog and session start/cancel. Streamer configures, viewers play, mods can run. |
| **Giveaways** | 🤝 Shared | Moderator / Moderator (code pools = **Broadcaster**) | CRUD/open/close, entries, draw/redraw and winners; masked prize-code pools/import/assignment. Mods run draws; code reveal is owner-only. |

---

## MUSIC

*🎥 Streamer → 🤝 Shared → 🛡️ Moderator*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Sound Clips** | 🎥 Streamer | Moderator / Editor | Soundboard / sound clips played via commands/pipeline/overlay. |
| **Assets** | 🎥 Streamer | Moderator / Editor | Overlay/widget media library (shares the sound-clip gates). |
| **Music** | 🤝 Shared | Moderator / Editor (queue = Moderator) | Play/pause/skip-next, queue add/remove, seek/shuffle/repeat, devices + transfer, playlists/play-context, track blocklist and live now-playing (Spotify/YouTube). |
| **Song Requests** | 🤝 Shared | Moderator / Editor (**queue moderation = Moderator**) | Queue pause/resume/skip/remove/promote/ban, blocklist, trust/fair-queue config, public SR-page token + rotation. |
| **TTS** | 🤝 Shared | Moderator / Editor (**approval queue = Moderator**) | Voice catalog/test, per-viewer voice + `!voice`, approval queue, profanity filters, BYOK provider keys, lexicon, overlay test and playback pause/resume/skip/clear. |
| **Media Share** | 🛡️ Moderator | Moderator / Moderator | Viewer-submitted media queue — mods moderate; overlay playback. |

---

## STREAM

*🎥 Streamer (this whole group is owner/creative work)*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Overlays / Widgets** | 🎥 Streamer | Moderator / Editor | Catalogue/gallery install, enable/rename/configure, clone-to-edit, project/code editor + compile/version/rollback, update checks, test events, reviewer workflow, token rotation and OBS manifest. |
| **Alerts** | 🎥 Streamer | Moderator / Editor | Focused create/edit/toggle/delete view of on-air alert messages; pipeline-bound alerts point back to Event Responses/Pipelines. |
| **Schedule** | 🎥 Streamer | Moderator / Editor | Live-ops stream schedule (segments + vacation). |
| **Pipelines** | 🎥 Streamer | Moderator / Editor | Visual builder (conditions, branches, loops, try/control flow), validation/test runs, dependency previews and paged per-run execution history. |
| **Code Scripts** | 🎥 Streamer | **Broadcaster / Broadcaster** | Sandboxed multi-file project editor, SDK types, tests, versions/publish/rollback, enable/disable and dependency-aware deletion. Owner-only. |
| **Analytics** | 🎥 Streamer | Moderator / read-only | Daily/summary/top-viewer dashboards, stream drill-down, searchable viewer engagement + watch streaks and analytics opt-out. |

---

## COMMUNITY

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Viewers** | 🛡️ Moderator | Moderator / Moderator | Searchable viewer list/top chatters + drill-in: standing, trust, activity, roles, moderation, analytics/streaks, custom data, personal messages and subject export/erase — real provider data only. |

---

## CONNECT

*🎥 Streamer (external wiring is owner work; a couple have mod-usable live controls)*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Discord** | 🎥 Streamer | Moderator / **SuperMod** | Guild consent/link, notification rules + preview, guild discovery, self-assign notify/currently-live roles, postable role buttons and dispatch log. |
| **Webhooks** | 🎥 Streamer | **Broadcaster / Broadcaster** | Inbound verified ingest + token rotation using Ko-fi/GitHub/Fourthwall/Shopify/Patreon/Buy Me a Coffee/generic adapters; outbound signed delivery with catalogue, test, secret rotation, re-enable, log and manual retry/dead-letter. |
| **Federation** | 🎥 Streamer | **Broadcaster / Broadcaster** | Peer registration/trust/revoke, mTLS/key management, signed events and per-channel opt-in. |
| **Custom Events** | 🎥 Streamer | Moderator / Editor | User-defined/preset event sources → pipeline/overlay, with source search, sample tests and fetched-payload field mapping. |
| **Supporters** | 🎥 Streamer | Moderator / **Broadcaster** | Patreon / Shopify / TreatStream ingest → alerts + economy. Connect/disconnect owner-only. |
| **OBS** | 🎥 Streamer | **Broadcaster** / Broadcaster (scene/input control = Moderator) | Connection probe, bridge setup/status/token rotation, scenes, input mute/volume, stream/record control and OBS event triggers. |
| **VTube Studio** | 🎥 Streamer | Moderator / **Broadcaster** (model/hotkey control = Moderator) | Connection/authorization, bridge-token rotation, model inventory, model/hotkey/expression control and event triggers. |
| **Automation** | 🎥 Streamer | **Editor** / **Broadcaster** | Scoped external API tokens (create/rotate/revoke), event catalogue, expiring device pairing and REST + WebSocket data plane. |

---

## SETUP *(pinned, configure-once owner area)*

*🎥 Streamer / owner — plus signed-in self-service data rights*

| Page | Lean | Read / Manage floor | What it does |
|---|---|---|---|
| **Bundles** | 🎥 Streamer | Moderator / Editor (publish = Broadcaster) | Export, inspect/import with conflict policy, uninstall preview, marketplace browse/install/publish/status and publisher token. |
| **Integrations** | 🎥 Streamer | **Broadcaster / Broadcaster** | Spotify/Discord/YouTube/TTS OAuth, Twitch/Kick bot accounts, reconnect/disconnect, BYOC Twitch client setup and EventSub subscription reconcile. |
| **Roles & Permits** | 🎥 Streamer | **Broadcaster / Broadcaster** | Role memberships, action-permission matrix (lower safe-action floors), permit grants. |
| **Features** | 🎥 Streamer | **Broadcaster / Broadcaster** | Per-channel feature toggles. |
| **Settings** | 🎥 Streamer | Moderator / per-section | Bot basics/personality, engagement automations, Bot Account, Appearance, channel lifecycle, Twitch scope diagnostics/re-grant, Billing + resource limits, event-journal export/import/rebuild and danger actions. |
| **My Data** | 👤 Self | **Moderator** route / self-service actions | GDPR download, erasure preview/request/status, participation opt-out and consent grant/withdrawal — always the signed-in user's own data. |

---

## Not management-sidebar rows

- **Participant rung:** My Channel, Now Playing/song submission, Leaderboards + opt-in/out, Points &
  Store, Games, and Me/profile/pronouns/activity. `My Standing` is information within these views, not
  a seventh `ParticipantPage`. The current `My Data` sidebar route floors at Moderator and is not in
  `ParticipantNav`; the GDPR API itself is signed-in self-service.
- **Platform Admin:** overview/live registry, channels, users, system health, feature flags, billing/
  invite/tier/founder operations, IAM users/service accounts/roles, tenant suspend/reinstate/break-glass,
  audit, spam defaults and provider credentials. It is IAM-gated and appended separately from
  `ShellNav.pages`.
- **Public surfaces:** tokenized song requests, OBS overlays/widgets, OAuth relay, device-pairing
  approval, OBS bridge, Discord interactions, and inbound provider/billing/webhook endpoints.
- **Companion/developer surfaces:** Stream Deck's concrete music controls, Automation REST/WebSocket,
  widget/overlay SDK types and runtime, IPC developer mode, imports/templates, notifications,
  diagnostics and EventSub/event-journal operations. See `FEATURES.md` §§18–23.

---

## Notes on the classification

- **The manage floor is the objective signal**, not opinion: a page mods can *mutate* out of the box
  (`manage = Moderator`) is a moderator tool; a page only editors/broadcasters mutate is owner
  configuration that mods merely read.
- **"Shared" is real**, not a cop-out: Commands, Music, Song Requests, TTS, Games and Giveaways all
  have a *live* half a mod uses and a *config* half the streamer owns — the floors encode exactly that
  split (e.g. Song Requests reads/configures at Editor but **queue moderation** drops to Moderator).
- **Every Stream, Connect and Setup page leans Streamer** because they are creative/wire-up surfaces —
  the two exceptions with mod-usable live controls (OBS scene switch, VTS model control) are noted inline.
- **Admin (Plane C)** and the **participant/viewer rung** are separate surfaces (not in the management
  sidebar) — see `FEATURES.md` §16–§17.

---

*Ordering source: `ShellNav.pages` (`app/composeApp/.../feature/shell/nav/ShellNav.kt`) and its
`ShellContent` route mapping. Full cross-surface descriptions live in `FEATURES.md`.*
