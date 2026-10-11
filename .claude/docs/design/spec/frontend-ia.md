# Frontend — Information Architecture & Shell

**Status:** Implementable. Build the dashboard shell from this directly. Sections 3 to 16 come from the
approved restructure paper `ui-restructure-research-2026-10-11.md` (owner clearance 2026-10-11). The
placement ledger `ui-placement-ledger-2026-10-11.md` and the wireframes `ui-page-drawings-2026-10-11.md`
back it.
**Subsystem:** The navigation *semantics* of the KMP + Compose dashboard: the groups, pages and tabs, the
floors, the pins, the Deck, the profile menu, the Settings surface and the unlockable platform **Admin**
area. This is the fourth frontend sibling: `frontend.md` (the typed client + nav mechanics),
`frontend-structure.md` (file placement), `frontend-design-system.md` (style). **Single-home rule:** the
`Route` sealed interface itself lives in `frontend.md` section 5. This spec names pages and tabs. The slice
S-IA-ROUTES defines the routes.

## Grounding & locked decisions (binding)

- **One app, two planes of navigation.** The same KMP client serves both the per-channel **management
  dashboard** (Plane B) and — only when the connected principal holds a platform-IAM role — the
  cross-tenant **Admin** area (Plane C). One codebase, one design system, one query engine; the Admin
  area is a gated graph, not a second app. (Owner decision.)
- **Feature/Setup split, first-class pages.** Every backend-rich feature gets its own page; the sidebar
  groups them into labelled sections split into **daily-driver FEATURE workspaces** (the top, scrolling) and a
  pinned **SETUP** owner area (the bottom — configure-once). No feature is buried as an unlabeled tab inside
  another; **feature pages carry NO wire-up controls** — provider/credential setup lives in SETUP. (Owner decision.)
- **One shell, three rungs — role-gated, never role-forked.** The streamer, a delegated Mod/LeadModerator/Editor,
  and a role-less viewer all use the **same** shell; the resolved rung just selects the page set and which
  controls are live. A non-null `ManagementRole` (Plane B) gets the management rungs (nav items + actions
  show/hide/disable per role, §7); a **null** management role is the **PARTICIPANT rung (Rung 0)** — a real,
  non-empty viewer surface gated by Plane-A community standing (§3a), never a dead-end. There is no separate
  "managing someone else's channel" UI and no separate viewer app.
- **Sidebar = what you manage; profile menu = who you are + which backend + app prefs.** The sidebar is
  channel-scoped feature workspaces; the profile block holds identity, the connection switcher, My data, theme,
  language, and the Admin switch.
- **Plane A standing gates the participant rung; it never renders a management dashboard.** Community standing
  (`Everyone`…`Broadcaster`) decides the participant page set and viewer-facing actions only. The roles
  vocabulary is `roles-permissions.md` (canonical).

---

## 1. The three planes → three nav surfaces

| Plane | Vocabulary (`roles-permissions.md`) | Nav surface | Who |
|---|---|---|---|
| **A — Community** | `CommunityStanding`: `Everyone` `Subscriber` `Vip` `Artist` `Moderator` (sub tier = separate `SubTier` column, not enum values) | **none** (gates only) | viewers — read-only standing inferred from Twitch |
| **B — Management** | `ManagementRole`: `Moderator`(10) `LeadModerator`(20) `Editor`(30) `Broadcaster`(40) | **Main shell** (§2–§5) | the streamer + anyone they delegate a `ManagementRole` (or an active `PermitGrant`, or a Plane-C `tenant:access`) |
| **C — Platform IAM** | `IamRole`: `platform-super-admin` `platform-iam-admin` `platform-analyst` | **Admin area** (§6) | NoMercy Labs staff / service principals (SaaS only; self-host = `OwnerIsFullIamService` no-op) |

**Entry resolution (in the shell, after the `frontend.md` §5 connection/auth gate):**
1. The signed-in principal's **Plane-B `ManagementRole`** for the active channel decides which shell pages
   and actions are visible (§7). A SaaS admin with `tenant:access` enters a channel shell at an effective
   `Broadcaster` floor via break-glass (audited, §6).
2. If the principal additionally holds **any Plane-C `IamRole`**, the profile menu shows **"Switch to Admin"**
   (§4) which routes to the Admin graph (§6). Absent a Plane-C role the item does not render.

---

## 2. Shell layout

The main shell is one persistent frame around the content `NavHost` (`frontend.md` §5):

```
┌──────────────┬───────────────────────────────────────────────┐
│  Sidebar     │  Top bar: page title · active-channel chip ·   │
│  (grouped,   │           HubState dot · Ctrl+K search (3.6)  │
│  collapsible)├───────────────────────────────────────────────┤
│              │                                                │
│  §3 groups   │            content NavHost (the page)          │
│              │                                                │
│              │                                                │
│ ─ profile ── │                                                │
│  block (§4)  │                                                │
└──────────────┴───────────────────────────────────────────────┘
```

- **Sidebar** — grouped sections (§3), each a label + its items; collapsible to an icon rail (state persisted
  in app prefs). Built from the design-system primitives; icons from the `IconKey` pack (`frontend-design-system.md`).
- **Top bar** — current page title, the **active-channel chip** (which channel this shell manages; a menu on
  native to switch via the connection switcher), the SignalR `HubState` indicator dot (`frontend.md` §3.2),
  and the Ctrl+K search (section 3.6).
- **Profile block** — pinned bottom-of-sidebar (`NavUser` pattern): avatar + name + role badge; opens the
  profile menu (§4).
- **Web vs native:** identical layout; the connection switcher and Admin-switch obey the same `frontend.md` §6
  rules (web is single-origin → the channel chip is static, no switcher).

---

## 3. Sidebar groups, pages and tabs (management rungs)

This section replaces the S072 group list (Home, Chat, Moderation, Loyalty, Music, Stream, Community,
Connect, Setup). The source is `ui-restructure-research-2026-10-11.md` sections 3 and 11. The placement
ledger `ui-placement-ledger-2026-10-11.md` gives the place of every feature. The wireframes are in
`ui-page-drawings-2026-10-11.md`.

This spec names pages and tabs. It does not name routes. The slice S-IA-ROUTES defines the route and the
address `#/c/<login>/<page>[/<tab>]` (section 13). The `Route` interface stays in `frontend.md` section 5.

### 3.1 The rule that places every feature

Each feature goes to the group of the job the user does when they need it.

- **Live**: things you use while the stream runs, with chat open.
- **Build**: things you make once. The bot then runs them by itself.
- **People**: everything about a person in your chat: who they are, what they did, what they may do.
- **Loyalty**: what viewers earn and spend.
- **Music & Voice**: what plays and what speaks.
- **Connect**: other services that send things in or receive things out.
- **Setup**: things you set once for the whole channel.

A tie goes to the job that happens most often. Example: the OBS scene switch happens on every stream, so
OBS control is in Live. The OBS connection happens once, so it is in Setup, Integrations.

### 3.2 Floors

"Read" is the lowest role that sees the page. "Manage" is the lowest role that changes it. The names are
the role names the user sees. Users never see numbered permission levels.

Floors are defaults, not the effective gate. Section 7 explains the effective gate.

Owner decisions that set floors (2026-10-11, verbatim):

- "moderators get granted the permission to manage stream and channel behaviors"
  Arc's reading: every page that shapes how the stream and the channel behave has a Moderator manage
  floor: Live (Stream, OBS, VTube Studio, editing the Deck), Build (all of it), Loyalty, and Music & Voice
  (the TTS Provider tab stays Editor). Setup, Connect, and People (Rules, role assignment) keep the floors
  in the tables below. A broadcaster can still raise any action for their channel (`ChannelActionOverride`,
  `ActionDefinitionSeeder.cs:131-132`).
- "we will leave the risky items to the channel manager and up"
  Arc's reading: the channel manager is the Editor role. Content labels, branded content, ads and raids
  stay at Editor. In the server these are `channel:ccl`, `channel:brandedcontent`, `live-ops:ads` and
  `live-ops:raids`. The Deck raid and ad buttons need Editor.

### 3.3 The groups

#### Home (no group header)

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Home | Streamer: Prep (offline) and Live (on air); Moderator: Mod home; Viewer: My channels | Everyone | per card | One page, shaped by role and by stream state (section 10.3). |

#### Live

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Chat | One channel (Shield mode toggle in header); All my channels | Moderator | Moderator | "All my channels" was Multi-Chat. The Shield mode toggle sits in the header (owner 2026-09-08: "chat also needs a shield mode toggle for direct access."). Platform chips filter Twitch, Kick, YouTube and X. |
| Deck | Grid; Edit | Moderator | Moderator (each button has its own floor) | New page. Section 9. |
| Stream | Info & presets; Interactions; Schedule | Moderator | Moderator (content labels, branded content, ads, raids: Editor) | Info & presets: title, category, tags, per platform, with "All platforms". Interactions: polls, predictions, raids, ads, markers, clips, goals, hype train, charity, Guest Star. Schedule was its own page. |
| OBS | Live control; Advanced | Moderator | Moderator | Scenes, sources, recording, replay. The connection moves to Integrations. Without a connection, the page shows the connect step inline. |
| VTube Studio | Hotkeys; Expressions | Moderator | Moderator | The same inline-connect pattern. |
| Analytics | Stream; Chat; Growth | Moderator | read-only | |

#### Build

One place to make a command, an overlay or an action. This answers the owner ask of 2026-09-08 (section 8).

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Commands | Custom; Built-in | Moderator | Moderator | Built-in: the 36 built-in commands, each with its own floor. |
| Event Responses | by event type | Moderator | Moderator | Absorbs Alerts (Alerts absorbed). One page answers every stream event: follow, sub, raid, cheer, reward, donation. The ledger lists the event groups. |
| Triggers & Timers | Chat triggers; Voice triggers; Timers | Moderator | Moderator | Three small pages become three tabs. |
| Pipelines | List; Editor | Moderator | Moderator | |
| Scripts | the editor itself (no list page) | Moderator | Moderator | Opens straight into the code editor. The file list and the test helpers are inside it (owner 2026-09-08). The server already lets a Moderator write scripts (`ActionDefinitionSeeder.cs:174-179`). |
| Overlays | Gallery; My overlays | Moderator | Moderator | Was Widgets. The 23 overlay kinds. Each overlay shows its OBS URL and a test button. |
| Library | Sounds; Files; Random responses; Quotes | Moderator | Moderator | Sounds was Sound Clips. Files was Assets. Random responses was Pick Lists. Every picker in Build can upload inline. |
| Gallery | Templates; Bundles | Moderator | Moderator | Bundles moves here from Setup. Every create flow also offers "Start from a template". |

#### People

One page (slice S-USER-HUB, built by S-IA-PEOPLE). The tabs are the old Community, Moderation and Roles pages.

| Tab | Read | Manage | Notes |
|---|---|---|---|
| Overview | Moderator | read-only | Standing of the chat, actions needed, active holds, mass-ban batches. |
| Queue | Moderator | Moderator | One queue for held messages, reports, appeals and unban requests. |
| Viewers | Moderator | Moderator | Was Community. The viewer card: identity on every linked platform, history, notes, points, roles, per-user settings (owner 2026-09-08). |
| History | Moderator | read-only | Every moderation action: by whom, on which platform. |
| Rules | Moderator | Lead Moderator | AutoMod, blocked terms, spam defense, mass-ban settings and opt-in. |
| Roles (with Individual permissions) | Moderator | Broadcaster (assign Moderator and VIP: Lead Moderator) | Moved from Setup. "Individual permissions" are the per-user overrides. |

#### Loyalty

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| Channel Points | Rewards; Redemptions | Moderator | Moderator | Was Rewards. |
| Economy | Currency; Store; Savings jars; Leaderboards | Moderator | Moderator | |
| Games | Games; Live sessions | Moderator | Moderator | A game start is also a Deck button. |
| Giveaways | Active; History | Moderator | Moderator | |

#### Music & Voice

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| Music | Now playing; Song requests; Settings | Moderator | Moderator | Song requests was its own page. |
| TTS | Live; Voices; Rules; Provider | Moderator | Moderator (Provider tab: Editor) | Owner 2026-09-08: "the tts page needs to have tabs because there is too much on one page, properly split by type." |
| Media Share | Queue; Settings | Moderator | Moderator | |

#### Connect

| Page | Sections | Read | Manage | Notes |
|---|---|---|---|---|
| Discord | - | Moderator | Lead Moderator | The label "Super Mod" is wrong. Use "Lead Moderator". |
| Webhooks | - | Broadcaster | Broadcaster | Generic webhooks only. Ko-fi moves to Supporters. |
| Custom Events | - | Moderator | Editor | |
| Supporters | - | Moderator | Broadcaster | Owns Ko-fi and the other supporter feeds. |
| Federation | - | Broadcaster | Broadcaster | |
| API & devices | API tokens; Stream Deck pairing; device codes | Editor | Broadcaster | Was Automation. |

#### Setup (pinned to the bottom, collapsed)

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| Integrations | - | Broadcaster | Broadcaster | The one home of every credential: Twitch app credentials (3 places become 1), the bot account, Spotify, OBS, VTube Studio, Discord, YouTube, Kick, the TTS providers. |
| Features | - | Broadcaster | Broadcaster | Per-channel feature toggles. |
| Settings | General; Language & fonts; Notifications; Bot behavior; Privacy; Danger zone | Moderator | Broadcaster | Billing is a seventh tab in saas mode only. `saas` mode is a RESTRICTED option: it is reserved to NoMercy Labs under the licence. Self-hosting your own bot is always free. |

#### Outside the sidebar

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| My data | - | Everyone | Everyone | In the profile menu. Every viewer owns their data. |
| Admin | 23 tabs unchanged; entering swaps the whole chrome | Platform admin | Platform admin | In the profile menu and in Ctrl+K, for platform admins only. Section 6. |

#### Viewer shell (kept separate from the operator shell)

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| My channels | cards, one per channel | Everyone | - | |
| Now playing | per channel | Everyone | - | |
| Leaderboards | per channel | Everyone | - | |
| Points and store | per channel | Everyone | - | |
| Games | per channel | Everyone | - | |
| Me | TTS voice, pronouns, linked accounts, my standing; My data also in the profile menu | Everyone | - | |

### 3.4 Counts

- The broadcaster sees 34 sidebar rows: Home 1, Live 6, Build 8, People 6, Loyalty 4, Music & Voice 3,
  Connect 6. Setup adds 3 rows when expanded. The most is 37, instead of 45 plus Admin.
- 11 old pages become tabs: Multi-Chat, Schedule, Alerts, Chat Triggers, Voice Triggers, Timers, Sound
  Clips, Assets, Pick Lists, Quotes, Song Requests.
- My Data and Admin leave the sidebar. Bundles joins Gallery. The 4 Moderation routes and Community
  become People tabs. Roles joins People.
- 2 pages are new: Deck and Gallery. Library replaces 4 pages.

### 3.5 Pins

Each role gets 8 default pins at the top of the sidebar. The user can change them. The choice is stored on
the account, so it follows the user to every device. Pins start from these defaults. The restructure does
not carry old saved pins over (section 13).

| Role | Default pins |
|---|---|
| Broadcaster | Home, Chat, Deck, Commands, Event Responses, Overlays, People, Music |
| Moderator | Home, Chat, People: Queue, People: Viewers, People: History, Music: Song requests, TTS, Deck |
| Lead Moderator | The Moderator pins, with People: Rules in place of TTS |
| Editor | Home, Commands, Event Responses, Triggers & Timers, Overlays, Pipelines, Library, Chat |

### 3.6 Search (Ctrl+K)

One palette indexes:

- every page and every tab;
- every Deck button (run it from the palette);
- every command, overlay, pipeline and script, by name;
- every viewer, by name, on every platform;
- every channel the user can reach (switch channel);
- the Admin pages, for platform admins.

The palette respects floors. It never lists what the user cannot open.


## 3a. The participant rung (Rung 0)

Section 3.3 (Viewer shell) names the viewer pages of the restructure: My channels, Now playing, Leaderboards,
Points and store, Games, Me. Section 10.4 gives the rules.

A **null** `ManagementRole` is not a dead-end: the same shell renders the **participant surface**, gated by
Plane-A `CommunityStanding` rather than a management role (`ParticipantNav`). The base surface floors at
`Everyone` (every signed-in viewer sees it); a sub/VIP unlocks MORE within it. The page set below is the
**target**. `ParticipantNav.pages` ships six entries today: **My Channel**, **Now Playing**, **Leaderboards**,
**Points & Store**, **Games**, **Me** (all floor `Everyone`). **My Standing** and **My Data** are TARGET pages
and do not ship yet; `Me` ships pronouns, activity summary and participation footprint, while its own-TTS-voice
and linked-platforms sections are TARGET. The page set:

| Participant page | Floor | Purpose |
|---|---|---|
| **My Channel** | Everyone | the caller's own profile/standing/activity + the channel's public summary |
| **Now Playing** | Everyone | live now-playing + queue; submit a song request (`music:request:submit`) |
| **Leaderboards** | Everyone | the channel's leaderboards (read) + the caller's own opt-in/opt-out toggle |
| **Points & Store** | Everyone | the caller's balance, the catalog (read + purchase), community jars, points transfers |
| **Games** | Everyone | the channel's games: read, play, and the caller's own play history |
| **Me** | Everyone | the caller's own data: pronouns, activity summary, participation footprint (shipped); **own TTS voice** (`tts.md` per-viewer voice) and **linked platforms** (`UserIdentity` rows — link/unlink Twitch/Kick/YouTube/X, `platform-identity.md`) are TARGET |
| **My Standing** (TARGET) | Everyone | the caller's `CommunityStanding` on this channel (name only, never a level number), active permits, ban/timeout state across the channels they participate in |
| **My Data** (TARGET) | Everyone | `MyData` — GDPR export / erasure request, consent records (`gdpr-crypto.md`); the shipped route sits in Setup today and is re-homed here (D4 — a viewer right, not a Moderator-floor page) |

On SaaS a viewer signs in **free** and this rung spans every channel on the instance they participate in (D4); on
self-host it is the same surface for the operator's own viewers.

Progressive unlocks (a sub-only lane, sub leaderboards, higher pending limits) are decided **on the page**
from the caller's standing — never by hiding the whole page.

**Effective-level surfacing (broadcaster-lowered pages).** The participant rung is the default for a caller
who holds **no** management-page capability — out of the box a VIP/sub clears no management page (the §3
defaults floor at `Moderator`), so nothing management leaks. But when the broadcaster **lowers** a management
page's action to a level the caller's standing clears (e.g. drops `quotes:read`/`quotes:write` to `Vip` —
both seed with a `Vip` floor), that caller now holds that page's read capability, so the page **surfaces to
them** and its read works — the lowering has a visible effect. The rung fork is therefore on the caller's **effective held-capabilities**, not on holding a
`ManagementRole`: the shell renders the management rung when the caller can reach **≥1** management page
(showing exactly the pages they hold), else the participant rung. A caller with a real `ManagementRole`
(Moderator+/Editor/Broadcaster) is unchanged — they hold every page at/below their level, exactly as today.

---

## 4. Profile menu

Opened from the profile block. Contents, top to bottom:

- **Identity** — avatar, display name, the active channel and the caller's role badge.
- **Connection switcher** — *native only*; the saved-connections list (mDNS-discovered + manual), switching
  via `ConnectionStore.switchTo` (`frontend.md` §6). **Hidden on web** (single-origin).
- **My data** — the GDPR export and erasure page. Floor: Everyone.
- **Linked accounts** — link and unlink Twitch, Kick, YouTube and X.
- **Switch to Admin** — *only if the principal holds any Plane-C `IamRole`*; routes to the Admin graph (§6).
- **Theme** — Light / Dark / System (the dynamic chat-color accent still derives per `frontend-design-system.md`
  §8; this toggles only the light/dark base).
- **Language** — runtime switch (`frontend.md` §7). The same control also appears in onboarding.
- **Log out** — clears the active-connection token (`TokenVault.clear`) and returns to the Connect/Setup gate.

The newer plan wins over the S080 profile menu contents (conflict table, section 14).

---

## 5. Settings

`Settings` is a tabbed page in Setup. Read floor: Moderator. Manage floor: Broadcaster.

| Tab | Contents |
|---|---|
| General | Channel basics: command prefix, default language, timezone (`onboarding-setup.md`). |
| Language & fonts | The interface language and the font for each language (owner 2026-09-08: "the ui should have a font for each language..."). |
| Notifications | Notification choices for the signed-in user. |
| Bot behavior | How the bot speaks and acts. The bot uses one voice per channel. |
| Privacy | Privacy and data choices for the channel. |
| Danger zone | Disconnect the bot, replay events, delete and restore the channel. |
| Billing | A seventh tab, in saas mode only. `saas` mode is a RESTRICTED option reserved to NoMercy Labs under the licence. |

Proposal, not settled: the paper names the six tabs but does not list the controls of each tab. The old
tabs (Bot basics, Bot Account, Appearance, Account, Billing, Danger zone) map to the new tabs as the
S-IA-SETUP slice decides. The bot account connection moves to Integrations (Bot account).

Integrations is the one home of every credential (`integrations-oauth.md`). It is a Setup page, not a
Settings tab. Roles moved to People (section 3.3).

---

## 6. Admin area (Plane C)

A nested graph reached via profile-menu **Switch to Admin**, gated on a Plane-C `IamRole`. Backed by
`AdminHub` (`/hubs/admin`) and the platform-IAM endpoints (`roles-permissions.md` §3.7, `stream-admin.md` §3.2).
It is **not** in the channel sidebar; entering it swaps the shell to the admin chrome (a distinct nav set) and
back via "Exit Admin". Pages:

| Admin page | Route | Min `IamRole` | Purpose |
|---|---|---|---|
| **Tenants** | `Admin` (root) | `platform-analyst` (read) | list/search channels (tenants), status, plan, health |
| **Tenant detail** | `AdminTenant(tenantId)` | `platform-super-admin` to act | suspend/resume; **break-glass access** into the channel shell (writes `IamAuditLog` with justification, `IPlatformIamService.AuthorizePlatformAsync`) |
| **Feature flags** | `AdminFeatureFlags` | `platform-super-admin` | staged-rollout flags (`FeatureFlagAdministeredEvent`) |
| **Billing** | `AdminBilling` | `platform-super-admin` | platform billing/metering view (`monetization-billing.md`) |
| **IAM — principals** | `AdminIamPrincipals` | `platform-iam-admin` | employee/service principals; `CreatePrincipalAsync` |
| **IAM — roles** | `AdminIamRoles` | `platform-iam-admin` | role/permission assignment |
| **Audit log** | `AdminAuditLog` | `platform-iam-admin` (`audit:read`) | the `IamAuditLog` stream (every privileged action, incl. break-glass) |
| **Platform analytics** | `AdminAnalytics` | `platform-analyst` | cross-tenant metrics (`platform:analytics:read`) |

Every admin mutation flows through `IPlatformIamService.AuthorizePlatformAsync` (authorize **and** audit in
one call — no decision un-audited). Self-host builds never reach this graph (the no-op IAM adapter denies the
Plane-C gate, so "Switch to Admin" never appears).

---

## 7. Role-gated visibility (Plane B)

One rule, applied everywhere: **resolve the caller's effective access once per active channel; gate nav and
actions off the capabilities it grants — not off the raw `ManagementRole` alone.** The resolver folds the
three planes (community standing, management role, permits) AND the broadcaster's per-action overrides into
one answer, so a lowered action reaches the UI. Mechanics:

- **Effective, not default.** Every page/action floor in §3 is a **default**. The binding gate is the
  action's **effective** required level = `clamp(override ?? default, floor, Broadcaster)`
  (`roles-permissions.md` §4). A caller "holds" a page/action when their resolved level clears its effective
  level OR they hold a direct permit for it. Out of the box (no override) the defaults ARE the effective
  levels, so behaviour is identical to a pure `ManagementRole` gate until a broadcaster lowers something.
- **Page visibility** — a sidebar item renders only if the caller **holds** that page (its read capability,
  effective). A `Moderator` sees the full shell except the Broadcaster-floored Setup pages (Integrations,
  Features) and the Broadcaster-only Settings tabs; a `Moderator` SEES Discord (read floor Moderator)
  but cannot mutate it (manage floor LeadModerator). A role-less VIP normally holds **no** management page (→
  participant rung, §3a); if the broadcaster lowers a page's action to the VIP's standing, that page — and
  only that page — surfaces to them.
- **Action gating** — within a visible page, mutating controls are **disabled with a reason tooltip** (not
  hidden) when the caller does not hold that action's **effective** manage capability — so a Mod browsing
  Commands sees the list but the "New command" button is disabled ("Requires Editor"). A page may carry
  **several action keys at different floors**: e.g. Quotes gates add/edit on `quotes:write` (Editor by
  default, **broadcaster-lowerable**) but delete on `quotes:delete` (Moderator, a moderation-grade floor), so
  a caller granted quote-editing via a lowered `quotes:write` still cannot delete. Visibility-hide for
  *pages*, disable-with-reason for *actions* — consistent, never silent.
- **Source of truth** — the gate reads the caller's **resolved held-capability set** for the active channel
  from the unfloored `/effective/me` self-introspection (the same envelope that already carries the caller's
  `effectiveLevel` and permit capabilities). It lists the action keys the caller clears **after** overrides,
  so the frontend never has to fetch — nor is it allowed to, that matrix is `roles:read`/Moderator+ — the
  channel-wide override table to gate a participant. The backend re-checks every write (the frontend gate is
  UX, never the security boundary — `roles-permissions.md` enforces server-side).
- **Break-glass admins** — a Plane-C principal who entered via `tenant:access` operates at an effective
  `Broadcaster` floor; the active-channel chip shows a "platform access" marker so the context is never
  ambiguous.

---

## 8. Owner decisions that bind this layout (verbatim)

Each sentence below is binding input. Each is on disk in `ui-restructure-research-2026-10-11.md` section 2.

- 2026-09-08: "there should be a beter organization in navigating, in the sense that all stream features and behaviors are in one place, administative features for the bots behavior from a users perspecitve close together and so on. i currently feel that there is too much juggeling required to find features related to getitng a new command working or adding a custom overlay with special actions and so on."
- 2026-09-08: "chat also needs a shield mode toggle for direct access."
- 2026-09-08: "the tts page needs to have tabs because there is too much on one page, properly split by type."
- 2026-09-08: "the main code script edit page should not exist, this should directly be the code editor with the testing helpers inside the editor..."
- 2026-10-10: "EVERYTHING must update live and fast, that includes all other pages on the dashboard, no delayed states".
- 2026-06-23: "Use the same terminology as twitch does please so lead moderator and not super moderator."
- 2026-10-11: "moderators get granted the permission to manage stream and channel behaviors".
- 2026-10-11: "we will leave the risky items to the channel manager and up".
- 2026-10-11: "and we are V0 so no backwards compattible bullshit tangled in the code".
- 2026-10-11, clearance for S-IA-SPEC: "if you are sure that this matches human psychology for dashboards then you have my clearance to execute".
  Arc's reading: the layout passed the check in `ui-page-drawings-2026-10-11.md` section 10, after three
  fixes to Home Live. The plan runs. Nothing is pushed until the owner is happy with how it runs.

Settled rules this spec keeps:

- One shell, gated by role. Never a separate app per role.
- Hide a page when the user is below its read floor. Disable an action, with the reason, when the user is
  below its manage floor.
- Feature pages carry no connection (wire-up) controls. Integrations owns connections.
- The profile menu holds identity, theme, language, My data and the switch into Admin.
- Persona order: streamer first, then a moderator of many channels, then the viewer.
- Users see role names, never numbers.
- A consequence is shown before saving (blast radius).

## 9. The Deck: everything on stream, one tap away

The owner asked that everything is "right in front of you when you need it the most or when you want to
create some shenanigans on stream with all of its features". The Deck is a new page in Live.

### 9.1 What a button can do

A Deck button is a generic primitive. It binds to one action from the existing action catalogue. No feature
gets a bespoke button type.

- Run a pipeline (this covers every multi-step shenanigan).
- Play a sound from the Library.
- Speak a TTS line.
- Test an event response (fire a fake follow, sub, raid).
- Show or hide an overlay.
- Send a chat message or an announcement.
- Start a poll or a prediction from a preset.
- Raid, run an ad, place a marker, make a clip.
- Chat modes: emote-only, sub-only, slow, followers-only, Shield mode.
- Switch an OBS scene, toggle a source, start or stop recording.
- Fire a VTube Studio hotkey.
- Start a game or a giveaway.
- Pause or resume a timer.
- Skip a song, pause the music, clear the TTS queue.

### 9.2 Rules

- Each button carries its own floor. A Moderator sees the buttons at or below their role. A button above
  their role is not shown.
- Editing the Deck needs Moderator (owner 2026-10-11).
- A button with a risky action (ban wave, raid, ad) asks for a second tap. Raid and ad buttons need Editor
  (owner 2026-10-11: "we will leave the risky items to the channel manager and up").
- A button shows its live state from the event stream (for example "Shield on", the current scene, the song
  playing).
- The layout is stored per channel, with an optional private layout per user. It needs one small table in
  BOTH migration sets (Postgres and SQLite).
- No tile on the grid is the primary action. Every tile is equal by design. Edit mode holds the primary.

### 9.3 Where the Deck shows

- Live, Deck: the full grid.
- Home, Live layout: the first 8 buttons.
- Phone: a full-screen grid, one tap per action.
- Desktop: a pop-out window for the second monitor. It replaces a TV layout.
- Stream Deck: the plugin from `spec/stream-deck.md` shows the same Deck. It calls
  `POST /automation/v1/invoke` and reads key state from the event stream. No new server table is needed
  beyond the layout.
- Ctrl+K: every button by name.

## 10. Who sees what

### 10.1 The ladder

Viewer, Subscriber, VIP, Artist, Moderator, Lead Moderator, Editor, Broadcaster. A higher role holds
everything a lower management role holds. Platform admin is a separate plane (IAM), not a channel role.
Users see the names, never numbers.

### 10.2 What each role uses most (importance rank, 1 is highest)

The rank comes from the owner persona order, the benchmark (`benchmarks/dashboard-ia.md`) and the stream
state (live or offline).

- **Broadcaster, live:** 1 Chat, 2 Deck, 3 Home Live layout (stream health, attention inbox), 4 Stream:
  Interactions, 5 OBS, 6 Music, 7 People: Queue, 8 TTS Live.
- **Broadcaster, offline:** 1 Home Prep layout, 2 Commands, 3 Event Responses, 4 Overlays, 5 Pipelines,
  6 Library, 7 Analytics, 8 Integrations.
- **Moderator:** 1 Chat (with "All my channels" for moderators of many channels), 2 People: Queue,
  3 People: Viewers, 4 People: History, 5 Song requests, 6 TTS Live, 7 Deck (moderator buttons),
  8 Commands (read, simple replies).
- **Lead Moderator:** the Moderator list, plus People: Rules, assigning Moderators and VIPs, and Discord
  settings.
- **Editor:** 1 Commands, 2 Event Responses, 3 Overlays, 4 Pipelines, 5 Triggers & Timers, 6 Library,
  7 Stream: Info & presets, 8 Loyalty pages.
- **Viewer:** 1 chat commands in the streamer's chat (the main viewer UI), 2 the public commands page,
  3 the song request page, 4 points and store, 5 games, 6 leaderboards, 7 Me (TTS voice, pronouns, linked
  accounts, my standing), 8 My data.
- **Platform admin:** the Admin plane (23 tabs), entered from the profile menu.

### 10.3 Role homes (the Home page)

Home is one page. The role and the stream state shape it.

- **Streamer, Live (on air).** Stream health sits in the header, one word per platform. A red card shows
  only on a problem. The attention inbox is top left and holds the one primary action. When nothing waits,
  the inbox shrinks to one line, "Nothing waits for you", with no button. The top 8 Deck buttons and the
  activity feed follow. Chat is a full-height column on the right.
  (Owner-approved fixes to Home Live, 2026-10-11: health in the header, a red card only on a problem; the
  empty inbox shrinks to one line; chat is a full-height column.)
- **Streamer, Prep (offline).** The Stream info editor (the same component as Live, Stream), the schedule
  and the checklist "Before you go live".
- **Moderator, Mod home.** Arc's reading: the paper names the Mod home but gives no layout. The Moderator
  ranking in 10.2 and the "Needs you" inbox decide its content.
- **Viewer, My channels.** One card per channel. The live channel gets the primary Open. The others are outline.

### 10.4 The viewer side

- The viewer shell stays separate from the operator shell: My channels (cards), and per channel Now
  playing, Leaderboards, Points and store, Games, Me.
- My data is in the viewer profile menu too.
- Public pages that need no sign-in stay plain HTML served by the server, not the Compose app: the channel
  commands page (`/c/<login>`), the song request page, the leaderboard.
- A viewer never sees a moderator tool. A viewer who is also a moderator in another channel sees the
  operator shell for that channel only.

### 10.5 Differences laid out

This is the paper matrix, cell by cell.

| Need | Viewer | Moderator | Lead Moderator | Editor | Broadcaster |
|---|---|---|---|---|---|
| Chat and Shield toggle | own chat client | yes | yes | yes | yes |
| Deck | no | moderator buttons, and edit | moderator buttons, and edit | all, and edit | all, and edit |
| Stream info and interactions | no | edit, except content labels, branded content, ads and raids | edit, except content labels, branded content, ads and raids | edit | edit |
| Build pages | public commands list | edit | edit | edit | edit |
| Scripts | no | yes | yes | yes | yes |
| People: Queue, Viewers, History | own standing only | yes | yes | yes | yes |
| People: Rules | no | read | edit | read | edit |
| People: Roles | no | read | assign Moderator, VIP | read | all |
| Loyalty | points, store, games | edit | edit | edit | edit |
| Music & Voice | request songs, pick TTS voice | queue control and settings (not TTS Provider) | queue control and settings (not TTS Provider) | settings | settings |
| Connect | no | Discord read | Discord edit | Custom events, API read | all |
| Setup | no | Settings read | Settings read | Settings read | all |
| My data | yes | yes | yes | yes | yes |

The Editor cells for People: Rules, People: Roles and Connect conflict with section 3.3 and the ladder.
See section 15, points 1 to 4.

## 11. Layout per screen size

- Wide (840 dp and up): sidebar with groups and pins. The viewer card opens in a side panel, so chat stays
  visible.
- Medium (600 to 839 dp): an icon rail. The viewer card opens in a sheet.
- Phone (under 600 dp): a bottom bar with 4 role-pinned items and "More". "More" opens a sheet with the
  full sidebar. The Deck is a full-screen grid.
- No TV layout. The Deck pop-out covers the second monitor.

## 12. More than one platform

- Chat and People have platform chips: Twitch, Kick, YouTube, X. "All" is the default.
- Stream: Info & presets edits each platform connection, with "All platforms" to set one title everywhere.
- A platform-specific action is disabled with the reason ("Not on Kick"). It is never hidden.
- The viewer card shows the person on every linked platform.

## 13. Moving from today to the new layout

- V0 (owner 2026-10-11: "and we are V0 so no backwards compattible bullshit tangled in the code"). The
  new routes replace the previous ones outright. There is no alias map, no redirect of old addresses and
  no mapping of saved pins. `scripts/check-no-compat.py` blocks such code at commit.
- The new address shape is `#/c/<login>/<page>[/<tab>]`, so a link always opens the right channel.
- Saved pins and expanded groups start from the role defaults (section 3.5). They are not carried over.
- No user is logged out (standing rule: never log out for a schema or scope change).
- A test proves that every page and tab in section 3.3 opens at its new address.
- Empty states: a feature page that needs a connection shows the connect step inline. Integrations stays
  the one home of the settings. Every create flow starts with "Start from a template" or "Start empty".
  A new channel gets starter content: a few commands, event responses and a starter Deck.
- This spec, `FEATURES-BY-SIDEBAR.md` and `FEATURES.md` change in the same slice.

## 14. Conflicts this spec settles

The left side loses.

| Conflict | Settled as | Why |
|---|---|---|
| Slice S072's sidebar groups vs the owner's 2026-09-08 navigation sentence | The new groups (section 3), with Build | The owner sentence is newer and names the pain. This spec replaces the S072 group list. |
| S106 (stream info on its own page) vs the usability finding H1c (stream info on Home) | Both: one Stream Info editor component, shown on Live, Stream and on Home Prep | No second editor. Two entry points for two moments. |
| Alerts page vs Event Responses page | Event Responses absorbs Alerts | One page per job: answer an event. |
| Sound Clips vs Assets | Library with tabs Sounds and Files | Same read key, same users. |
| Ko-fi on Webhooks and Supporters | Supporters | Ko-fi is a supporter feed. Webhooks stays generic. |
| Twitch app credentials in 3 places | Setup, Integrations only | The settled rule: Integrations owns connections. |
| My Data floor Moderator | Everyone, in the profile menu | Every viewer owns their data. |
| "Super Mod" label | "Lead Moderator" | Owner 2026-06-23. |
| Stream presets inside a dialog on Home | Live, Stream: Info & presets | The same component on Home Prep. |
| Templates page vs create flows | Gallery page plus "Start from a template" in every create flow | Templates are found where you create. |
| S080 profile menu contents vs the newer plan | The newer plan: identity, theme, language, My data, Admin | The newer decision wins. |
| OBS connection on the OBS page | Integrations, with the connect step inline on OBS when not connected | Feature pages carry no wire-up controls. |
| Commands manage floor Moderator today | Moderator for every command | Owner 2026-10-11: "moderators get granted the permission to manage stream and channel behaviors". The server already has `commands:write` at Moderator (`ActionDefinitionSeeder.cs:139`). |
| 35 server actions sit at Editor (stream presets and schedule, title, category, tags, language, extensions, content labels, branded content, predictions, raids, ads, music settings, economy and bundles) | Moderator for the stream and channel behavior actions (slice S-IA-MOD-FLOORS) | Owner 2026-10-11: "moderators get granted the permission to manage stream and channel behaviors". Connect wiring (webhooks, egress, integrations, API tokens, EventSub) stays at Editor. Content labels, branded content, ads and raids stay at Editor: "we will leave the risky items to the channel manager and up". |

## 15. Open points in the paper

Each point is a place where the paper says two things. This spec follows section 3.3 of this file, which
matches the placement ledger. The owner must confirm.

1. Proposal, not settled: People, Rules manage floor. The paper section 3.2 and the ledger say Lead
   Moderator. The paper section 5.4 says the Editor only reads Rules. The ladder puts Editor above Lead
   Moderator, so the Editor can edit.
2. Proposal, not settled: People, Roles. The paper section 3.2 says "Broadcaster (assign Moderator and VIP:
   Lead Moderator)". The paper section 5.4 says the Editor only reads Roles.
3. Proposal, not settled: Discord. The paper section 3.2 says manage floor Lead Moderator. The Editor
   column in section 5.4 lists no Discord access.
4. Proposal, not settled: Connect for a Moderator. The paper section 3.2 gives a Moderator read on
   Discord, Custom Events and Supporters. The paper section 5.4 lists only "Discord read".
5. Proposal, not settled: the Rules floor moves. The paper section 2 says Rules keep their floors. The
   paper section 1.1 shows the Rules manage floor today is Editor. Section 3.2 sets it to Lead Moderator.

## 16. Decisions (resolved)

- The sidebar has eight groups: Home, Live, Build, People, Loyalty, Music & Voice, Connect, Setup. Setup
  is pinned at the bottom and starts collapsed.
- Admin and My data sit in the profile menu, not in the sidebar.
- Feature pages carry no wire-up controls. Setup, Integrations owns every credential.
- One shell, three rungs: participant (3a), management, Admin. Role-gated, never role-forked.
- Stream live operations are Deck buttons and Live, Stream. Home Live shows the first 8 Deck buttons.
- The `Route` sealed interface is owned by `frontend.md` §5. This spec maps pages and tabs to groups,
  floors and purpose.
