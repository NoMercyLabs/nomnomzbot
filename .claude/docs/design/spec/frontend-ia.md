# Frontend — Information Architecture & Shell

**Status:** Implementable. Build the dashboard shell from this directly.
**Subsystem:** The navigation *semantics* of the KMP + Compose dashboard — the page inventory, the
grouped sidebar, the profile menu, the Settings surface, and the unlockable platform **Admin** area.
This is the fourth frontend sibling: `frontend.md` (the typed client + nav mechanics), `frontend-structure.md`
(file placement), `frontend-design-system.md` (style). **Single-home rule:** the `Route` sealed interface
itself lives in `frontend.md` §5; this spec maps every route to its **sidebar group**, its **role gate**,
and its **purpose**. It invents no new route name that §5 does not declare.

## Grounding & locked decisions (binding)

- **One app, two planes of navigation.** The same KMP client serves both the per-channel **management
  dashboard** (Plane B) and — only when the connected principal holds a platform-IAM role — the
  cross-tenant **Admin** area (Plane C). One codebase, one design system, one query engine; the Admin
  area is a gated graph, not a second app. (Owner decision.)
- **Feature/Setup split, ~21 first-class pages.** Every backend-rich feature gets its own page; the sidebar
  groups them into labelled sections split into **daily-driver FEATURE workspaces** (the top, scrolling) and a
  pinned **SETUP** owner area (the bottom — configure-once). No feature is buried as an unlabeled tab inside
  another; **feature pages carry NO wire-up controls** — provider/credential setup lives in SETUP. (Owner decision.)
- **One shell, three rungs — role-gated, never role-forked.** The streamer, a delegated Mod/SuperMod/Editor,
  and a role-less viewer all use the **same** shell; the resolved rung just selects the page set and which
  controls are live. A non-null `ManagementRole` (Plane B) gets the management rungs (nav items + actions
  show/hide/disable per role, §7); a **null** management role is the **PARTICIPANT rung (Rung 0)** — a real,
  non-empty viewer surface gated by Plane-A community standing (§3a), never a dead-end. There is no separate
  "managing someone else's channel" UI and no separate viewer app.
- **Sidebar = what you manage; profile menu = who you are + which backend + app prefs.** The sidebar is
  channel-scoped feature workspaces; the profile block holds identity, the connection switcher, theme,
  language, and the Admin switch.
- **Plane A standing gates the participant rung; it never renders a management dashboard.** Community standing
  (`Everyone`…`Broadcaster`) decides the participant page set and viewer-facing actions only. The roles
  vocabulary is `roles-permissions.md` (canonical).

---

## 1. The three planes → three nav surfaces

| Plane | Vocabulary (`roles-permissions.md`) | Nav surface | Who |
|---|---|---|---|
| **A — Community** | `CommunityStanding`: `Everyone` `Subscriber` `Vip` `Artist` `Moderator` (sub tier = separate `SubTier` column, not enum values) | **none** (gates only) | viewers — read-only standing inferred from Twitch |
| **B — Management** | `ManagementRole`: `Moderator`(10) `SuperMod`(20) `Editor`(30) `Broadcaster`(40) | **Main shell** (§2–§5) | the streamer + anyone they delegate a `ManagementRole` (or an active `PermitGrant`, or a Plane-C `tenant:access`) |
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
│  (grouped,   │           HubState dot · global search (later) │
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
  and a search affordance (reserved, no route yet).
- **Profile block** — pinned bottom-of-sidebar (`NavUser` pattern): avatar + name + role badge; opens the
  profile menu (§4).
- **Web vs native:** identical layout; the connection switcher and Admin-switch obey the same `frontend.md` §6
  rules (web is single-origin → the channel chip is static, no switcher).

---

## 3. Sidebar groups & page inventory (management rungs)

Twenty-one content pages in the approved **Feature/Setup** IA: eight daily-driver FEATURE groups (top,
scrolling) and one pinned **SETUP** owner group (bottom, divider-separated, labelled). Each row: the §5
`Route`, the **default read floor** (the built-in minimum standing to see/open the page) and the **default
manage floor** (the built-in minimum to mutate within it), and the owning backend spec. "—" manage floor =
read-only page. The single source is `ShellNav.pages`; this table is its ratified mirror.

**Floors are DEFAULTS, not the effective gate (see §7).** The read/manage floors below are the actions'
*built-in* required levels. A broadcaster may **lower** a safe action's required level (per-action override,
`roles-permissions.md` §4: effective = `clamp(override ?? default, floor, Broadcaster)`), so the level that
actually gates a page for a given caller is the **effective** one. The shell therefore gates on the caller's
resolved **held-capability set** — the action keys they clear on this channel after overrides — surfaced by
the unfloored `/effective/me` self-introspection (§7), never on the raw default columns alone. Out of the box
(no override) these defaults ARE the effective gate, so a plain viewer/VIP still clears no management page.

### FEATURE — daily-driver workspaces (top)

#### Home
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Dashboard** | `Dashboard` | Moderator | — | `community-dashboard.md` (live chat feed, stream status, stat tiles, alerts) |

#### Chat
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Chat** | `Chat` | Moderator | Moderator | `chat-client.md` — live chat console; send as **you** (the operator), bot optional; emote composer + autocomplete; cross-channel moderation |
| **Commands** | `Commands` (+ `PipelineEditor`) | Moderator | Editor | `commands-pipelines.md` — list + T1/T2/T3 editor; built-in toggle list; T2 opens the pipeline editor, T3 the widget/code editor |
| **Pipelines** | `Pipelines` | Moderator | Editor | `commands-pipelines.md` — the visual pipeline builder (folded in from the dropped single-item Automation group) |
| **Timers** | `Timers` | Moderator | Editor | `commands-pipelines.md` §3.7 |
| **Quotes** | `Quotes` | Moderator | Editor | `commands-pipelines.md` — quote book CRUD + recall command |

#### Moderation
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Moderation** | `Moderation` | Moderator | Moderator | `moderation.md` — bans/timeouts/automod/filters (its own first-class group) |

#### Loyalty
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Channel Points** | `Rewards` | Moderator | Editor (create/delete reward: Broadcaster) | `rewards.md` — custom rewards + redemption queue |
| **Economy** | `Economy` | Moderator | Editor (payout/earn rules: Broadcaster) | `economy.md` — currency, store/redeemables, earn rules, leaderboards |
| **Games** | `Games` | Moderator | Editor | `economy.md` (chat games) + `live-games.md` (interactive overlay games) — per-game `GameConfig` |

#### Music
Music is a **first-class area**: `Music` is the **area home** (remote/transport/library/now-playing),
`SongRequests` is a **SIBLING lane** beneath it (not folded into SR), and `Tts` sits alongside.
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Music** | `Music` | Moderator | Editor | `music-sr.md` — playback remote, transport, library, now-playing (area home) |
| **Song Requests** | `SongRequests` | Moderator | Editor (queue moderation: Moderator) | `music-sr.md` — queue, blocklist, trust/fair-queue config, public SR-page token (sibling lane) |
| **TTS** | `Tts` | Moderator | Editor | `tts.md` — voices, approval queue, filters, provider keys |

#### Stream
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Overlays** | `Widgets` (+ `WidgetEditor`) | Moderator | Editor | `widgets-overlays.md` — first-party catalogue, install, settings, clone-to-edit, the code editor |
| **Alerts & Events** | `Alerts` | Moderator | Editor | `commands-pipelines.md` + `twitch-eventsub.md` — follow/sub/raid/cheer/gift event responses (event-triggered pipelines) |
| **Analytics** | `Analytics` | Moderator | — | `analytics.md` — the projection dashboards |

#### Community
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Viewers** | `Community` | Moderator | Moderator | `community-dashboard.md` + `roles-permissions.md` — viewer list/standings/leaderboards (real Twitch data only) |

#### Connect
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Discord** | `Discord` | Moderator | SuperMod | `discord.md` — guild link, role/optin sync, dispatch. Read floor lowered Broadcaster→Moderator and manage to SuperMod (was over-gated): seed `discord:*:read` = Moderator(10), `discord:*:write` = SuperMod(20). |

### SETUP — pinned, configure-once owner area (bottom)

The owner's wire-up area, divider-separated and pinned beneath the feature groups but **labelled** (not a
headerless rail). Feature pages must NOT carry wire-up controls — all provider/credential setup lives here.
| Page | Route | Read | Manage | Backend spec |
|---|---|---|---|---|
| **Integrations** | `Integrations` | Broadcaster | Broadcaster | `integrations-oauth.md` + `discord.md` — Spotify/Discord/YouTube/TTS OAuth + **BYOC Twitch client setup** (token custody = owner-level) |
| **Roles & Permits** | `Roles` | Broadcaster | Broadcaster | `roles-permissions.md` — Plane-B memberships + the action-permission matrix + permit grants. Re-homed from Community into the configure-once owner area. |
| **Settings** | `Settings` | Moderator | per-tab (§5) | `identity-auth.md`, `onboarding-setup.md`, `monetization-billing.md` (incl. **Bot Account** tab) |

> **Bot Account** is configured from the Settings/Integrations Setup pages (the dedicated bot identity OAuth);
> there is no standalone `BotAccount` route today — the capability lives in the existing Setup screens.

> **Stream Admin / Live-Ops** (`stream-admin.md`, `broadcaster-liveops.md` — title/game/tags, polls,
> predictions, raids) surfaces as **quick-action panels on the Dashboard** (the live home), not a separate
> sidebar page — these are run-while-live controls, used in the moment, not a workspace you navigate to.

---

## 3a. The participant rung (Rung 0)

A **null** `ManagementRole` is not a dead-end: the same shell renders the **participant surface**, gated by
Plane-A `CommunityStanding` rather than a management role (`ParticipantNav`). The base surface floors at
`Everyone` (every signed-in viewer sees it); a sub/VIP unlocks MORE within it. The page set:

| Participant page | Floor | Purpose |
|---|---|---|
| **My Channel** | Everyone | the caller's own profile/standing/activity + the channel's public summary |
| **Now Playing** | Everyone | live now-playing + queue; submit a song request (`music:request:submit`) |
| **Leaderboards** | Everyone | the channel's leaderboards (read) + the caller's own opt-in/opt-out toggle |
| **Points & Store** | Everyone | the caller's balance, the catalog (read + purchase), community jars, points transfers |
| **Games** | Everyone | the channel's games: read, play, and the caller's own play history |
| **Me** | Everyone | the caller's own data: pronouns, activity summary, participation footprint |

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

- **Identity** — avatar, display name, the active channel + the caller's `ManagementRole` badge.
- **Connection switcher** — *native only*; the saved-connections list (mDNS-discovered + manual), switching
  via `ConnectionStore.switchTo` (`frontend.md` §6). **Hidden on web** (single-origin).
- **Switch to Admin** — *only if the principal holds any Plane-C `IamRole`*; routes to the Admin graph (§6).
- **Theme** — Light / Dark / System (the dynamic chat-color accent still derives per `frontend-design-system.md`
  §8; this toggles only the light/dark base).
- **Language** — `en` / `nl` (runtime switch, `frontend.md` §7).
- **Account** — opens Settings → Account.
- **Log out** — clears the active-connection token (`TokenVault.clear`) and returns to the Connect/Setup gate.

---

## 5. Settings

`Settings` is a tabbed page; tabs render per the caller's floor.

| Tab | Floor | Contents |
|---|---|---|
| **Bot basics** | Editor | command prefix, default language, timezone (`onboarding-setup.md`) |
| **Bot Account** | Broadcaster | the dedicated bot identity OAuth (connect/disconnect the bot account) (`identity-auth.md`) |
| **Appearance** | Moderator | sidebar collapse default, theme, accent behavior (per-subject vs pinned) |
| **Account** | Moderator | the signed-in identity, session/logout |
| **Billing** | Broadcaster | tier/subscription, usage vs limits, invoices (`monetization-billing.md`) — SaaS only |
| **Danger zone** | Broadcaster | disconnect bot, GDPR export / erase request (`gdpr-crypto.md`) |

**Roles & Permits** is its own pinned **Setup** page (§3), not a Settings tab — the Plane-B membership CRUD,
the action-permission matrix, and permit grants. **Integrations** is likewise its own Setup page — both are
first-class configure-once owner surfaces, not preferences. Settings holds app prefs + the bot-account and
danger-zone owner toggles.

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
  Roles & Permits) and the Broadcaster-only Settings tabs; a `Moderator` SEES Discord (read floor Moderator)
  but cannot mutate it (manage floor SuperMod). A role-less VIP normally holds **no** management page (→
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

## 8. Decisions (resolved)

All settled and binding:
- **Feature/Setup split — eight feature groups + one pinned Setup group, 21 content pages.** FEATURE: Home ·
  Chat · Moderation · Loyalty · Music · Stream · Community · Connect. SETUP (pinned, configure-once owner
  area): Integrations · Roles & Permits · Settings. Economy, Games, Song Requests, TTS, Analytics, Alerts &
  Events, Pipelines, and Quotes are first-class pages (not nested tabs).
- **Music is a first-class area**, not a sub-tab of Song Requests: `Music` is the area home,
  `SongRequests` is a sibling lane beneath it, `Tts` sits alongside.
- **Discord re-homed to Connect**, read floor lowered Broadcaster→Moderator and manage floor SuperMod (seed
  `discord:*:read` = Moderator, `discord:*:write` = SuperMod — it was over-gated at Broadcaster).
- **Roles & Permits re-homed into Setup** (configure-once owner ownership), surfacing memberships + the
  action-permission matrix + permits.
- **The single-item Automation group is dropped**; Pipelines folds into the Chat workspace.
- **Feature pages carry no wire-up controls** — provider/credential setup lives in the Setup group.
- **One shell, three rungs** — participant (Rung 0, §3a) / Mod / Broadcaster; the streamer and delegated
  managers share the management rungs, gated on the caller's **effective held-capabilities** (§7) — hide a
  page the caller does not hold, disable-with-reason for actions the caller cannot manage. Defaults floor at
  `ManagementRole`, but a broadcaster-lowered action surfaces its page to the eligible standing; a caller who
  holds no management page routes to the participant surface, never a dead-end.
- **One app, gated Admin graph** for Plane-C — same client, same design system, reached via the profile menu,
  never shown to self-host or sub-Plane-C principals.
- **Stream live-ops are Dashboard quick-actions**, not a standalone page.
- The `Route` sealed interface is owned by `frontend.md` §5; this spec only maps routes to group/gate/purpose.
