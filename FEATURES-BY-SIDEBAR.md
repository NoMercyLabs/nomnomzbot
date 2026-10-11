# NomNomzBot — Features by Sidebar Group and Floor

> The management-sidebar subset of `FEATURES.md`, sorted by the new sidebar of
> `.claude/docs/design/spec/frontend-ia.md` (the approved restructure of 2026-10-11).
>
> The shipped sidebar (`ShellNav.pages`, old groups Home, Chat, Moderation, Loyalty, Music, Stream,
> Community, Connect, Setup) stays in the app until the slices S-IA-ROUTES, S-IA-PEOPLE and the
> related build slices land. This file describes the target. The spec replaces the old group list.
>
> **Floors.** Each page has a read floor (lowest role that sees it) and a manage floor (lowest role that
> changes it). Role names, low to high: Viewer, Subscriber, VIP, Artist, Moderator, Lead Moderator,
> Editor, Broadcaster. Users never see numbered permission levels. Floors are defaults. A broadcaster can
> raise or lower an action for the channel. Below its read floor, a page is hidden. Below its manage floor,
> an action is disabled with the reason.
>
> **Owner decisions of 2026-10-11 that set floors (verbatim).**
> "moderators get granted the permission to manage stream and channel behaviors".
> "we will leave the risky items to the channel manager and up".
> Arc's reading: content labels, branded content, ads and raids stay at Editor. Slice S-IA-MOD-FLOORS
> moves the other stream and channel actions to Moderator in the server.

---

## Audience summary (at a glance)

Proposal, not settled: the audience lean below is Arc's reading of the manage floors in the spec.

| Lean | Pages |
|---|---|
| Streamer (owner) | Integrations, Features, Settings, Webhooks, Federation, Supporters, API & devices |
| Shared (moderator manages day to day, streamer shapes) | Home, Deck, Stream, OBS, VTube Studio, Commands, Event Responses, Triggers & Timers, Pipelines, Scripts, Overlays, Library, Gallery, Channel Points, Economy, Games, Giveaways, Music, TTS, Media Share, Custom Events, Discord |
| Moderator | Chat, People (Overview, Queue, Viewers, History), Analytics (read) |

---

## HOME

| Page | Modes | Read / Manage | What it does |
|---|---|---|---|
| **Home** | Streamer: Prep (offline) and Live (on air). Moderator: Mod home. Viewer: My channels | Everyone / per card | One page shaped by role and stream state. Live: stream health in the header, the attention inbox, the top 8 Deck buttons, the activity feed and a full-height chat column. Prep: the Stream info editor, the schedule and the checklist before going live. Embedded: Chat Polls card (bot-run text polls, distinct from Twitch polls). |

---

## LIVE

Things you use while the stream runs, with chat open.

| Page | Tabs | Read / Manage | What it does |
|---|---|---|---|
| **Chat** | One channel (Shield mode toggle in header); All my channels | Moderator / Moderator | Live chat console: send, reply and delete as you or the bot, announcements, chat settings, emote composer, moderation, third-party emotes, badges, cheermotes and link previews. "All my channels" was Multi-Chat. Platform chips filter Twitch, Kick, YouTube and X. |
| **Deck** | Grid; Edit | Moderator / Moderator (each button has its own floor) | New. Buttons bound to catalogue actions: pipelines, sounds, TTS, event tests, overlays, chat modes, OBS scenes, VTube hotkeys, games, timers, music. Risky buttons ask for a second tap. Raid and ad buttons need Editor. |
| **Stream** | Info & presets; Interactions; Schedule | Moderator / Moderator (content labels, branded content, ads, raids: Editor) | Title, category and tags per platform. Polls, predictions, raids, ads, markers, clips, goals, hype train, charity, Guest Star. The stream schedule. |
| **OBS** | Live control; Advanced | Moderator / Moderator | Scenes, sources, recording, replay, input mute and volume, event triggers. The connection lives in Integrations. |
| **VTube Studio** | Hotkeys; Expressions | Moderator / Moderator | Model, hotkey and expression control and event triggers. |
| **Analytics** | Stream; Chat; Growth | Moderator / read-only | Daily and summary dashboards, stream drill-down, viewer engagement and watch streaks. |

---

## BUILD

Things you make once. The bot then runs them.

| Page | Tabs | Read / Manage | What it does |
|---|---|---|---|
| **Commands** | Custom; Built-in | Moderator / Moderator | Custom commands (text, pipeline, code), cooldowns and permission floors. The 36 built-in commands, each with its own floor and response override. |
| **Event Responses** | by event type | Moderator / Moderator | Follow, sub, raid, cheer, reward and donation responses as chat, overlay or pipeline actions. Absorbs Alerts. |
| **Triggers & Timers** | Chat triggers; Voice triggers; Timers | Moderator / Moderator | Keyword auto-replies, voice triggers, recurring messages by interval or message count. |
| **Pipelines** | List; Editor | Moderator / Moderator | Visual builder with conditions, branches, loops, validation, test runs and per-run history. |
| **Scripts** | the editor itself | Moderator / Moderator | Sandboxed multi-file code editor with the file list and test helpers inside it, SDK types, versions, publish and rollback. |
| **Overlays** | Gallery; My overlays | Moderator / Moderator | Was Widgets. Install, configure, clone to edit, code editor, test events, OBS URL and manifest. |
| **Library** | Sounds; Files; Random responses; Quotes | Moderator / Moderator | Replaces Sound Clips, Assets, Pick Lists and Quotes. Every picker in Build can upload inline. |
| **Gallery** | Templates; Bundles | Moderator / Moderator | New. Templates and bundles (export, import with conflict policy, marketplace). Every create flow also offers Start from a template. |

---

## PEOPLE

One page with six tabs (slice S-USER-HUB, built by S-IA-PEOPLE). Replaces Community, Moderation, Review Queue, History and Roles.

| Tab | Read / Manage | What it does |
|---|---|---|
| **Overview** | Moderator / read-only | Standing of the chat, actions needed, active holds, mass-ban batches. |
| **Queue** | Moderator / Moderator | One queue: held messages, AutoMod queue, reports, appeals, unban requests, spam review and follow-bot blocks. |
| **Viewers** | Moderator / Moderator | Searchable viewer list and the viewer card: identity on every linked platform, history, notes, points, roles, per-user settings, export and erase. Real provider data only. |
| **History** | Moderator / read-only | Every moderation action: by whom, on which platform. Nuke batches. |
| **Rules** | Moderator / Lead Moderator | AutoMod, blocked terms, chat filters, trust policy, spam defense, escalation, mass-ban settings and opt-in. |
| **Roles (with Individual permissions)** | Moderator / Broadcaster (assign Moderator and VIP: Lead Moderator) | Role memberships, the action-permission matrix, permit grants and per-user overrides. Moved from Setup. |

Proposal, not settled: the Rules and Roles manage floors conflict with the Editor column of the paper. See spec section 15.

---

## LOYALTY

| Page | Tabs | Read / Manage | What it does |
|---|---|---|---|
| **Channel Points** | Rewards; Redemptions | Moderator / Moderator | Reward CRUD, Twitch sync, live redemption queue (fulfill or refund). |
| **Economy** | Currency; Store; Savings jars; Leaderboards | Moderator / Moderator | Currency and earning rules, ledgers, transfers, store and refunds, savings jars, leaderboards and opt-outs. |
| **Games** | Games; Live sessions | Moderator / Moderator | Chat games and interactive overlay games, per-game config, play history, session start and cancel. A game start is also a Deck button. |
| **Giveaways** | Active; History | Moderator / Moderator | Open, close, entries, draw, redraw and winners. Prize-code pools are masked. |

---

## MUSIC & VOICE

| Page | Tabs | Read / Manage | What it does |
|---|---|---|---|
| **Music** | Now playing; Song requests; Settings | Moderator / Moderator | Transport, queue, devices, playlists, blocklist, live now-playing, song request queue moderation, trust and fair-queue config, public request page token. |
| **TTS** | Live; Voices; Rules; Provider | Moderator / Moderator (Provider tab: Editor) | Playback pause, resume, skip and clear. Voice catalog and per-viewer voice. Filters and lexicon. Provider keys. |
| **Media Share** | Queue; Settings | Moderator / Moderator | Viewer-submitted media queue and overlay playback. |

---

## CONNECT

Other services that send things in or receive things out.

| Page | Read / Manage | What it does |
|---|---|---|
| **Discord** | Moderator / Lead Moderator | Guild link, notification rules, self-assign roles, role buttons and dispatch log. The label is Lead Moderator, never Super Mod. |
| **Webhooks** | Broadcaster / Broadcaster | Generic inbound and outbound webhooks. Ko-fi moved to Supporters. |
| **Custom Events** | Moderator / Editor | Custom event sources to pipeline or overlay, with sample tests and field mapping. |
| **Supporters** | Moderator / Broadcaster | Ko-fi, Patreon, Shopify and TreatStream feeds to alerts and economy. |
| **Federation** | Broadcaster / Broadcaster | Peer registration, trust, keys and per-channel opt-in. |
| **API & devices** | Editor / Broadcaster | Was Automation. API tokens, Stream Deck pairing and device codes. |

---

## SETUP *(pinned to the bottom, starts collapsed)*

| Page | Tabs | Read / Manage | What it does |
|---|---|---|---|
| **Integrations** | - | Broadcaster / Broadcaster | The one home of every credential: Twitch app credentials, the bot account, Spotify, OBS, VTube Studio, Discord, YouTube, Kick and the TTS providers. EventSub reconcile. |
| **Features** | - | Broadcaster / Broadcaster | Per-channel feature toggles. |
| **Settings** | General; Language & fonts; Notifications; Bot behavior; Privacy; Danger zone | Moderator / Broadcaster | Channel basics and bot behavior. Billing is a seventh tab in saas mode only. `saas` mode is a RESTRICTED option: it is reserved to NoMercy Labs under the licence. Self-hosting your own bot is always free. |

---

## Not management-sidebar rows

- **Profile menu:** My data (GDPR download, erasure, consent; always the signed-in user's own data;
  floor Everyone), linked accounts, theme, language and Switch to Admin (platform admins only).
- **Viewer shell** (separate from the operator shell): My channels, Now playing, Leaderboards, Points and
  store, Games, Me.
- **Platform Admin:** 23 tabs, unchanged. Entering swaps the whole chrome. IAM-gated, reached from the
  profile menu and Ctrl+K.
- **Public surfaces:** tokenized song requests, the public commands page, leaderboards, OBS overlays,
  OAuth relay, device-pairing approval, OBS bridge, Discord interactions and inbound provider endpoints.
- **Companion surfaces:** the Stream Deck plugin shows the Deck through `POST /automation/v1/invoke`.
  See `FEATURES.md` sections 18 to 23.

---

## Notes

- **Pins.** Each role gets 8 default pins. The choice is stored on the account. Spec section 3.5.
- **Search.** Ctrl+K indexes pages, tabs, Deck buttons, commands, overlays, pipelines, scripts, viewers,
  channels and Admin pages. It respects floors.
- **Counts.** The broadcaster sees 34 sidebar rows plus 3 Setup rows. 11 old pages became tabs.
- **V0.** The new routes replace the old ones. There are no alias routes and no redirects.

---

*Source: `.claude/docs/design/spec/frontend-ia.md`. Full cross-surface descriptions live in `FEATURES.md`.*
