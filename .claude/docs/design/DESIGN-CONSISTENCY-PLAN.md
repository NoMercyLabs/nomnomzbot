# Design consistency plan

Filed 2026-11-08. Lists remaining work only. A finished row is deleted, never marked done.

Owner request, Stoney, 2026-11-08, verbatim: "the issue is that he is not working much so things are not getting done when i need them to be done, so we do the work. i like you to file a plan for bringing in his desging items form figma and work through the consistency of every screen and tackling his points."

Scope: bring the designer's Figma kit items into the dashboard, make every screen consistent, and handle aaoa's points from the stream of 2026-11-04 (VOD v2919194023).

Sources (all read this run unless marked):

- Critique inventory: scratchpad `aaoa-critique-inventory.md` (15 points, 4 brand notes). Items 13 and 14 came from short transcript snippets only; each quote is a lead, not a full reading.
- Figma ledger: scratchpad `lane-a-figma-ledger.md`. Kit file `Y3uHgP9au3Gm9piJ68fR0s`, page `363:1721` (Components). M = 269 kit items (51 control variants and 218 icons). Compared by design values: 40 of 269.
- Routes: scratchpad `route-ledger.json`. 102 route records, 90 of them under `dashboard/src/views/`.
- Navigation: `dashboard/src/layouts/dashboard/navigation.ts`, `adminNavigation.ts`, `settingsNavigation.ts` (`SidebarEntries.vue` only renders the groups).
- Product decisions: `.claude/docs/design/PRODUCT-ALIGNMENT.md` (D7 one lane for backend and frontend; D8 stabilize before adding).

Rules for every row (CLAUDE.md, Critical Rules):

- Load the `sleak` skill before any UI edit: one primary action per group, concentric radius, scarce accent.
- shadcn new-york and the kit tokens (`dashboard/src/styles/tokens.css:57-77`, `--kit-*`) are the source. Never hardcode a color. A size difference alone is not a mismatch (compact shadcn sizes stay).
- An icon-only action button is at least shadcn `size="icon"` (36px) with a 20px icon (`size-5`).
- Shared files (components, lib, layouts, i18n, router) change under `lane-lock.sh take shared <lane>` and commit alone. The dashboard commit goes through `scripts/dashboard-commit.ps1`.
- Figma exports are poor (dotted edges do not render). Rebuild from the design values. Never paste exported code.

Lanes (from the manager brief):

| Lane | Work | Order |
| --- | --- | --- |
| A | Figma inventory and kit gaps (section "Figma items") | first, parallel with B and E |
| B | Shared primitives: spacing, icon placement, tags, dividers, scrollbar gutter, search stroke (points 11, 12, 13) | parallel with A |
| C | Screens, in chunks (section "Screens"); sidebar and home proposals first (points 3, 4, 5, 6, 14) | after B lands |
| D | Wording and entry paths (points 1, 7) | parallel with C |
| E | Check only: aaoa's role on the live box (point 2) | first |

Acceptance legend:

- GATE = the dashboard gates pass (`scripts/dashboard-commit.ps1`: vue-tsc, oxlint, eslint, cspell, the whole vitest run).
- SS = screenshot of the rendered Vue component beside the Figma node, region by region.
- CSS = computed style of the rendered element.
- SHOT = a rendered screenshot at 1280px and at 360px, checked against one primary action per group, concentric radius and scarce accent.
- A screen or primitive is done only by its acceptance check. A self-rating never counts (point 10).

---

## Architecture

N of M: 90 of 90 route records under `views/` listed. Data sources were found by grep of each view file's `@/api/...` imports (composables shown only when no api module is imported). Paths are relative to their frame: dashboard routes under `/app/`, settings under `/app/settings/`, admin under `/app/admin/`. The full URL of the site and viewer routes is not checked.

Navigation groups (from `navigation.ts`, `settingsNavigation.ts`, `adminNavigation.ts`):

- Dashboard sidebar, 7 groups: home, people, live, musicVoice, build, loyalty, setup.
- Settings sidebar, 7 groups: back, settings.you, settings.bot, settings.connections, settings.developers, settings.access, settings.account.
- Admin sidebar, 6 groups: back, admin.activity, admin.people, admin.billing, admin.safety, admin.configuration.

Not checked: the page `dashboard-catalog` and `dashboard-events` have no sidebar entry in `navigation.ts` (grep for their route names found none); other entry points are not checked.

| # | Route (name) | Group | Data source (api module) |
| --- | --- | --- | --- |
| 1 | admin/tenants (dashboard-admin-tenants) | admin.people | admin, errors |
| 2 | admin/overview (dashboard-admin-overview) | admin.activity | adminOverview |
| 3 | admin/eventsub-health (dashboard-admin-eventsub-health) | admin.activity | adminActivity, errors |
| 4 | admin/channels/:channelId/:tab? (dashboard-admin-channels-console) | not in sidebar (child of tenants; not checked) | admin, adminDiagnostics |
| 5 | admin/webhook-deliveries (dashboard-admin-webhook-deliveries) | admin.activity | adminActivity, errors |
| 6 | admin/spam-defaults (dashboard-admin-spam-defaults) | admin.safety | adminSpamDefaults, errors |
| 7 | admin/providers (dashboard-admin-providers) | admin.configuration | adminProviders, errors |
| 8 | admin/feature-flags (dashboard-admin-feature-flags) | admin.configuration | adminFeatureFlags, errors |
| 9 | admin/scheduled-jobs (dashboard-admin-scheduled-jobs) | admin.activity | adminActivity, errors |
| 10 | admin/support (dashboard-admin-support) | admin.people | adminSupport, errors |
| 11 | admin/data-requests (dashboard-admin-data-requests) | admin.safety | adminDataRequests |
| 12 | admin/event-replay (dashboard-admin-event-replay) | admin.activity | admin, adminEventReplay |
| 13 | admin/trust-safety (dashboard-admin-trust-safety) | admin.safety | adminSupport, adminTrustSafety |
| 14 | admin/tenant-usage (dashboard-admin-tenant-usage) | admin.activity | adminActivity, adminUsage, errors |
| 15 | admin/error-budget (dashboard-admin-error-budget) | admin.activity | adminActivity, adminErrorBudget, errors |
| 16 | admin/content (dashboard-admin-content) | admin.configuration | adminContent |
| 17 | admin/billing (dashboard-admin-billing) | admin.billing | not checked (the view imports no api module directly; sibling files import several admin* modules) |
| 18 | admin/audit-log (dashboard-admin-audit-log) | admin.activity | adminAudit, errors |
| 19 | admin/platform-bot (dashboard-admin-platform-bot) | admin.configuration | adminPlatformBot, whiteLabelBot |
| 20 | admin/access (dashboard-admin-access) | admin.configuration | adminAccess, auth |
| 21 | admin/system (dashboard-admin-system) | admin.configuration | adminSystem |
| 22 | admin/platform-defaults (dashboard-admin-platform-defaults) | admin.configuration | not checked (same as row 17) |
| 23 | analytics (dashboard-analytics) | live | analytics |
| 24 | catalog (dashboard-catalog) | not in sidebar | codeScripts, community |
| 25 | channel-points (dashboard-channel-points) | loyalty | rewards |
| 26 | chat (dashboard-chat) | live | chat, access, auth (ChatView.vue) |
| 27 | viewer-chat (dashboard-viewer-chat) | live | same view as row 26 |
| 28 | :channel/chat (viewer-chat) | viewer frame | same view as row 26 |
| 29 | :channel/twitch (viewer-external-chat) | viewer frame | auth, chat, chatIdentity, signIn |
| 30 | chat (popout-chat) | popout frame | same view as row 26 |
| 31 | commands (dashboard-commands) | build | commands |
| 32 | settings/discord (dashboard-settings-discord) | settings.connections | discord |
| 33 | economy (dashboard-economy) | loyalty | economy, permissions |
| 34 | event-responses (dashboard-event-responses) | build | eventResponses |
| 35 | events (dashboard-events) | not in sidebar | home |
| 36 | giveaways (dashboard-giveaways) | loyalty | giveaways |
| 37 | (empty path) (dashboard-home) | home | home, stream |
| 38 | media-share (dashboard-media-share) | musicVoice | mediaShare, widgets |
| 39 | music (dashboard-music) | musicVoice | music, home, access, playerButtons |
| 40 | obs (dashboard-obs) | live | obs, obsOutputs, access, widgets |
| 41 | overlays (dashboard-overlays) | build | widgets |
| 42 | people (dashboard-people) | people | people, community |
| 43 | pick-lists (dashboard-pick-lists) | build | pickLists |
| 44 | pipelines (dashboard-pipelines) | build | pipelines, access |
| 45 | pipelines/:id (dashboard-pipeline) | child of pipelines | pipelines, access, errors |
| 46 | polls (dashboard-polls) | live | liveOps |
| 47 | quotes (dashboard-quotes) | loyalty | quotes |
| 48 | scripts (dashboard-scripts) | build | codeScripts |
| 49 | settings/api-devices (dashboard-settings-api-devices) | settings.developers | automationTokens |
| 50 | settings/automod (dashboard-settings-automod) | settings.access | automod |
| 51 | settings/banned (dashboard-settings-banned) | settings.access | bans |
| 52 | settings/billing (dashboard-settings-billing) | settings.account | billing |
| 53 | settings/blocked-terms (dashboard-settings-blocked-terms) | settings.access | blockedTerms |
| 54 | settings/channel-management (dashboard-settings-channel-management) | settings.account | channelManagement |
| 55 | settings/chat-filters (dashboard-settings-chat-filters) | settings.access | chatFilters |
| 56 | settings/custom-event-feeds (dashboard-settings-custom-event-feeds) | settings.developers | customEventFeeds |
| 57 | settings/escalation (dashboard-settings-escalation) | settings.access | escalationPolicy |
| 58 | settings/event-journal (dashboard-settings-event-journal) | settings.access | eventJournal, people |
| 59 | settings/features (dashboard-settings-features) | settings.bot | features |
| 60 | settings/federation (dashboard-settings-federation) | settings.connections | federation |
| 61 | settings/integrations (dashboard-settings-integrations) | settings.connections | integrations, home |
| 62 | settings/permissions (dashboard-settings-permissions) | settings.access | access, permissions |
| 63 | settings/reports (dashboard-settings-reports) | settings.access | reports |
| 64 | settings/resource-limits (dashboard-settings-resource-limits) | settings.account | resourceLimits, billing |
| 65 | settings (dashboard-settings) | settings.you | none found (ReadabilityView.vue imports no api module) |
| 66 | settings/bot-basics (dashboard-settings-bot-basics) | settings.bot | settings, errors (BasicsView.vue) |
| 67 | settings/bot-personality (dashboard-settings-bot-personality) | settings.bot | personalitySettings (PersonalityView.vue) |
| 68 | settings/engagement-triggers (dashboard-settings-engagement-triggers) | settings.bot | engagement (EngagementView.vue) |
| 69 | settings/supporter-sources (dashboard-settings-supporter-sources) | settings.connections | not checked (SupporterSourcesView.vue under views/dashboard/supporters, not read) |
| 70 | settings/shoutouts (dashboard-settings-shoutouts) | settings.bot | shoutoutSettings |
| 71 | settings/spam-defense (dashboard-settings-spam-defense) | settings.access | spamDefensePolicy |
| 72 | settings/trust-policy (dashboard-settings-trust-policy) | settings.access | trustPolicy |
| 73 | settings/webhooks (dashboard-settings-webhooks) | settings.developers | webhooks, pipelines |
| 74 | settings/white-label-bot (dashboard-settings-white-label-bot) | settings.bot | whiteLabelBot, home, errors |
| 75 | settings/your-data (dashboard-settings-your-data) | settings.account | yourData |
| 76 | sounds (dashboard-sounds) | musicVoice | soundClips |
| 77 | stream (dashboard-stream) | live | stream |
| 78 | supporters (dashboard-supporters) | loyalty | supporters, home |
| 79 | triggers-timers (dashboard-triggers-timers) | build | timers, liveOps |
| 80 | tts (dashboard-tts) | musicVoice | tts |
| 81 | voice-triggers (dashboard-voice-triggers) | build | voiceTriggers |
| 82 | vtube-studio (dashboard-vtube-studio) | live | vts |
| 83 | (empty path) (home, site) | site frame | auth (a sibling file; not checked in the view itself) |
| 84 | invite/:code (invite) | site frame | auth, billing, signIn |
| 85 | login (login) | site frame | activeBot, signIn |
| 86 | /:pathMatch(.*)* (not-found) | site frame | none (static page; not checked beyond zero imports) |
| 87 | setup (setup) | site frame | setup |
| 88 | :channel/me (viewer-account) | viewer frame | viewer, viewerAccount |
| 89 | (empty path) (viewer-channels) | viewer frame | channels, viewer |
| 90 | :channel (viewer-me) | viewer frame | viewer |

Data-source gaps to close in lane C before the page is touched: rows 4, 17, 22, 69, 83, 86 (labelled "not checked" above).

---

## Figma items

N of M: 29 control rows (18 partial and 11 not checked, of 51 control variants) and 218 icon rows. Matching controls (22 of 51) have no row because this plan lists remaining work only. Icons: 0 of 218 were compared by glyph.

### Controls: partial (18)

| Node id | Item | Difference | Acceptance check |
| --- | --- | --- | --- |
| 308:1606 | Primary button, Default | Letter-spacing: kit 0.51px, code none. Icon chips: kit 24px circle rgba(0,0,0,0.1) behind each icon, code has none. Border: kit rgba(26,46,34,0.2), code border-black/20 | CSS letter-spacing 0.03em; SS vs 308:1606 |
| 308:1626 | Primary button, Loading | Button has no loading state. Kit shows a 24px designer Loader; `Spinner.vue:4` imports Loader2Icon from `@lucide/vue` at size-4 | grep `Spinner.vue` for `@lucide` returns none; SS vs 308:1626 |
| 391:1788 | Primary destructive, Default | Missing tracking 0.51px and icon chips. Border kit rgba(27,27,27,0.2) vs black/20 | SS vs 391:1788 |
| 310:1519 | Secondary button, Default | Missing tracking 0.51px and icon chips rgba(237,235,255,0.1) | SS vs 310:1519 |
| 310:1533 | Secondary button, Focused | Kit border rgba(237,235,255,0.64) plus ring rgba(255,255,255,0.64); code uses `--kit-focus-soft` for both, so border color differs | CSS border-color on focus |
| 310:1545 | Secondary button, Loading | Same as 308:1626 | Same as 308:1626 |
| 310:1580 | Action button, Loading | Same as 308:1626 | Same as 308:1626 |
| 235:1455 | Switch, off | Kit border 1.5px white, code 1px in `--kit-pill-from` | CSS border-width 1.5px |
| 235:1464 | Radio, off | Kit border 2px #484848, code 1px `--kit-outline` | CSS border-width 2px |
| 235:1469 | Checkbox, off | Kit border 2px #484848, code 1px `--kit-outline` | CSS border-width 2px |
| 235:1472 | Tooltip, tail right | Radius kit 18 vs code `rounded-2xl` 16. Tail: kit 26px rounded blob with 8px visible; code 10px square rotated 45deg (`TooltipContent.vue:31`) | SS tail region vs 235:1472 |
| 235:1479 | Tooltip, tail left | Same tail difference; tail alignment at call sites not checked | SS vs 235:1479 |
| 235:1486 | Tooltip, tail middle | Same tail difference | SS vs 235:1486 |
| 308:1581 | Input field, Default | Kit h54, radius 16, bg #191919, border #1f1f1f, Geist 16, placeholder white 0.32. Code: `rounded-md`, `bg-transparent dark:bg-input/30`, `border-input`; no kit field tokens. Label and helper (`field/`) not compared | CSS bg rgb(25,25,25), border rgb(31,31,31), radius 16 |
| 308:1583 | Input field, Hover | Kit bg #222, border #383838; code has no hover rule for bg or border | CSS on hover |
| 308:1590 | Input field, Focused | Kit border #62626a plus 2px ring #62626a; code `border-ring` and `ring-3 ring/50`, `--ring` is oklch lilac (`tokens.css:98`) | CSS focus ring rgb(98,98,106) |
| 308:1597 | Input field, Filled | Kit text white; code foreground token; bg and border as Default | SS vs 308:1597 |
| 363:2202 | Segmented controls | Kit track bg #191919, border #1f1f1f, pill 53, pad 6; sliding selector gradient #2a2a2a to #242424, border white 0.08, radius 32; inactive text #606060 Medium, active white SemiBold. Code: `TabsList.vue` `bg-muted rounded-lg`, trigger `rounded-md`, no sliding selector, no kit tokens | SS vs 363:2202; grep `--kit` tokens in `components/ui/tabs/` |

Shared fix behind rows 308:1606 to 310:1580: one change in `components/ui/button/index.ts` (tracking, icon chips, loading state) and one in `components/ui/spinner/Spinner.vue`, not one edit per call site. Same for the badge (tracking 0.24px and 14px icon chips are minor and absent).

### Controls: not checked (11)

| Node id | Item | Difference | Acceptance check |
| --- | --- | --- | --- |
| 391:1893 | Secondary destructive, state 1 of 5 (frame 391:1892) | not checked (no design read) | read the node with get_design_context, then SS vs 391:1893 |
| 391:1897 | Secondary destructive, state 2 of 5 | not checked | same, vs 391:1897 |
| 391:1901 | Secondary destructive, state 3 of 5 | not checked | same, vs 391:1901 |
| 391:1905 | Secondary destructive, state 4 of 5 | not checked | same, vs 391:1905 |
| 391:1912 | Secondary destructive, state 5 of 5 | not checked | same, vs 391:1912 |
| 391:1810 | Action destructive, state 1 of 5 (frame 391:1809) | not checked | read the node, then SS vs 391:1810 |
| 391:1815 | Action destructive, state 2 of 5 | not checked | same, vs 391:1815 |
| 391:1820 | Action destructive, state 3 of 5 | not checked | same, vs 391:1820 |
| 391:1825 | Action destructive, state 4 of 5 | not checked | same, vs 391:1825 |
| 391:1833 | Action destructive, state 5 of 5 | not checked | same, vs 391:1833 |
| 1053:2933 | Switch slider-line (frame 1053:2932) | not checked | read the node, then SS vs 1053:2933 |

### Icons (218)

Source pack: `dashboard/src/assets/icons/line` (1549 svg files, imported as `~icons/nnz/<name>`). Brand logos come from `components/layout/PlatformLogo.vue` (simple-icons: twitch, kick, youtube, x at lines 20-23).

Codes used in the table:

- G = a same-name pack file exists; the glyph is not compared. Check: render both at 24px and SS-diff the path data.
- M = no same-name pack file (a missing file does not prove a missing icon). Check: write the kit-name to pack-name map row first, then render both at 24px and SS-diff. Until the map exists the row stays "not checked".
- S = social logo with no same-name pack file. Check: the map row, or a documented decision that the logo is not used by the dashboard. Only twitch, kick, youtube and x are drawn today.

The map file (kit name to pack name) is part of lane A and is written once, then every M row is checked against it.

| Node id | Item (group) | Code | Acceptance check |
| --- | --- | --- | --- |
| 363:1839 | Ticket (system) | G | 24px SS vs ticket.svg |
| 363:1817 | Location (system) | G | 24px SS vs location.svg |
| 363:1819 | Lock (system) | M | map row, then 24px SS |
| 363:1835 | Unlock (system) | M | map row, then 24px SS |
| 363:1791 | Bag (system) | M | map row, then 24px SS |
| 363:1793 | Bag 2 (system) | M | map row, then 24px SS |
| 363:1803 | Filter (system) | M | map row, then 24px SS |
| 363:1795 | Category (system) | M | map row, then 24px SS |
| 363:1823 | Logout (system) | M | map row, then 24px SS |
| 363:1821 | Login (system) | M | map row, then 24px SS |
| 363:1813 | Home (system) | G | 24px SS vs home.svg |
| 363:1799 | Danger (system) | M | map row, then 24px SS |
| 363:1801 | Info Square (system) | M | map row, then 24px SS |
| 363:1815 | Danger Circle (system) | M | map row, then 24px SS |
| 363:1797 | Close Square (system) | M | map row, then 24px SS |
| 363:1887 | Shield (system) | G | 24px SS vs shield.svg |
| 516:169 | Shield profile (system) | M | map row, then 24px SS |
| 516:170 | Shield Done (system) | M | map row, then 24px SS |
| 363:1827 | Scan (system) | M | map row, then 24px SS |
| 363:1831 | Volume Off (system) | M | map row, then 24px SS |
| 363:1829 | Volume Down (system) | M | map row, then 24px SS |
| 363:1833 | Volume Up (system) | M | map row, then 24px SS |
| 363:1837 | Show (system) | M | map row, then 24px SS |
| 363:1805 | Hide (system) | M | map row, then 24px SS |
| 363:1787 | Activity (system) | M | map row, then 24px SS |
| 363:1789 | Add User (system) | M | map row, then 24px SS |
| 363:1825 | Profile (system) | M | map row, then 24px SS |
| 363:1843 | Translate (system) | G | 24px SS vs translate.svg |
| 363:1841 | History (system) | M | map row, then 24px SS |
| 363:1845 | Search (system) | M | map row, then 24px SS; also point 12 stroke |
| 363:1847 | Notification (system) | M | map row, then 24px SS |
| 363:1851 | Notification Mute (system) | M | map row, then 24px SS |
| 363:1853 | Notification Sleep (system) | M | map row, then 24px SS |
| 363:1849 | Notification Badge (system) | M | map row, then 24px SS |
| 363:1857 | Inbox (system) | G | 24px SS vs inbox.svg |
| 363:1861 | Forward-60 (system) | M | map row, then 24px SS |
| 363:1859 | Clock arrow-right (system) | M | map row, then 24px SS |
| 363:1863 | Calendar arrow-right (system) | M | map row, then 24px SS |
| 363:1855 | Letter (system) | M | map row, then 24px SS |
| 363:1865 | Email (system) | G | 24px SS vs email.svg |
| 363:1867 | Email open (system) | G | 24px SS vs email-open.svg |
| 363:1753 | Comments (system) | M | map row, then 24px SS |
| 363:1764 | Message Filter (system) | M | map row, then 24px SS |
| 363:1774 | Message Notification (system) | M | map row, then 24px SS |
| 363:1770 | Message Settings (system) | M | map row, then 24px SS |
| 363:1784 | Mima Review (system) | M | map row, then 24px SS |
| 363:1772 | Messages 2 (system) | M | map row, then 24px SS |
| 363:1778 | Message Notification Square (system) | M | map row, then 24px SS |
| 363:1780 | Messages (system) | M | map row, then 24px SS |
| 363:1756 | Send 2 (system) | M | map row, then 24px SS |
| 363:1760 | Send (system) | M | map row, then 24px SS |
| 363:1875 | Link 2 (system) | M | map row, then 24px SS |
| 363:1891 | Link (system) | G | 24px SS vs link.svg |
| 363:1899 | Unlink (system) | M | map row, then 24px SS |
| 363:1913 | Plus (system) | M | map row, then 24px SS |
| 513:112 | Minus (system) | M | map row, then 24px SS |
| 363:1877 | Close remove (system) | M | map row, then 24px SS |
| 363:1871 | Sidebar (system) | M | map row, then 24px SS |
| 363:1873 | Grid interface Header (system) | M | map row, then 24px SS |
| 363:1869 | Split (system) | M | map row, then 24px SS |
| 404:1989 | SidebarExpand (system) | M | map row, then 24px SS |
| 404:1988 | SidebarColapse (system) | M | map row, then 24px SS |
| 363:1879 | Sun Happy (system) | M | map row, then 24px SS |
| 363:1883 | Sun (system) | G | 24px SS vs sun.svg |
| 363:1881 | Moon Big (system) | M | map row, then 24px SS |
| 363:1885 | Moon (system) | G | 24px SS vs moon.svg |
| 363:1889 | Remove (system) | G | 24px SS vs remove.svg |
| 363:1901 | Keys (system) | M | map row, then 24px SS |
| 363:1893 | Key (system) | M | map row, then 24px SS |
| 363:1897 | Scan security (system) | M | map row, then 24px SS |
| 363:1909 | Plus Circle (system) | M | map row, then 24px SS |
| 363:1905 | Coins (system) | G | 24px SS vs coins.svg |
| 513:88 | Money Bag Dollar (system) | M | map row, then 24px SS |
| 363:1911 | More (system) | M | map row, then 24px SS |
| 513:118 | More Vertical (system) | M | map row, then 24px SS |
| 363:1915 | Copy (system) | M | map row, then 24px SS |
| 363:1723 | Camera (system) | G | 24px SS vs camera.svg |
| 363:1725 | Edit Square (system) | M | map row, then 24px SS |
| 363:1727 | Edit (system) | G | 24px SS vs edit.svg |
| 363:1729 | Delete (system) | M | map row, then 24px SS |
| 363:1731 | Image (system) | M | map row, then 24px SS |
| 363:1733 | HeartFilled (system) | M | map row, then 24px SS |
| 363:1735 | Heart (system) | G | 24px SS vs heart.svg |
| 363:1737 | Bookmark (system) | G | 24px SS vs bookmark.svg |
| 363:1739 | Bookmark Filled (system) | M | map row, then 24px SS |
| 363:1745 | Paper (system) | M | map row, then 24px SS |
| 363:1747 | Paper Download (system) | M | map row, then 24px SS |
| 363:1749 | Document (system) | M | map row, then 24px SS |
| 363:1741 | Calendar (system) | M | map row, then 24px SS |
| 363:1743 | Time Square (system) | M | map row, then 24px SS |
| 363:1762 | Share 2 (system) | M | map row, then 24px SS |
| 363:1758 | Share (system) | M | map row, then 24px SS |
| 363:1776 | Megaphone (system) | G | 24px SS vs megaphone.svg |
| 363:1751 | Checkmark (system) | M | map row, then 24px SS |
| 531:962 | Checkmarks (system) | M | map row, then 24px SS |
| 513:109 | Check circle (system) | G | 24px SS vs check-circle.svg |
| 391:1733 | Loader (system) | M | map row, then 24px SS; also Spinner.vue row 308:1626 |
| 577:4137 | Loader Fast (system) | M | map row, then 24px SS |
| 391:1747 | Loader-fade (system) | G | 24px SS vs loader-fade.svg |
| 363:1782 | Pin (system) | G | 24px SS vs pin.svg |
| 506:2382 | Headphone (system) | G | 24px SS vs headphone.svg |
| 506:2359 | Terminal Box (system) | G | 24px SS vs terminal-box.svg |
| 506:2381 | Terminal (system) | G | 24px SS vs terminal.svg |
| 506:2370 | Quote Block Open (system) | M | map row, then 24px SS |
| 506:2366 | Quote Block Close (system) | M | map row, then 24px SS |
| 506:2378 | Creative Quote Open (system) | M | map row, then 24px SS |
| 506:2376 | Creative Quote Close (system) | M | map row, then 24px SS |
| 506:2372 | Timer clock (system) | M | map row, then 24px SS |
| 506:2377 | Gift Card (system) | G | 24px SS vs gift-card.svg |
| 506:2373 | Gift (system) | M | map row, then 24px SS |
| 506:2369 | Songs Library (system) | M | map row, then 24px SS |
| 506:2375 | Songs Library Note (system) | M | map row, then 24px SS |
| 506:2368 | Speaker (system) | G | 24px SS vs speaker.svg |
| 506:2365 | Speaker megaphone 4 (system) | M | map row, then 24px SS |
| 506:2363 | Speaker megaphone 5 (system) | M | map row, then 24px SS |
| 506:2362 | Speaker megaphone 2 (system) | M | map row, then 24px SS |
| 506:2361 | Game Controller Dual (system) | M | map row, then 24px SS |
| 506:2360 | Game (system) | M | map row, then 24px SS |
| 506:2371 | Layers (system) | G | 24px SS vs layers.svg |
| 506:2367 | Chart (system) | M | map row, then 24px SS |
| 506:2364 | users group (system) | M | map row, then 24px SS |
| 506:2380 | 3 users (system) | M | map row, then 24px SS |
| 506:2379 | Source (system) | M | map row, then 24px SS |
| 506:2374 | monitor display stand (system) | M | map row, then 24px SS |
| 506:2358 | Heart rate (system) | M | map row, then 24px SS |
| 511:2433 | Bot flow (system) | M | map row, then 24px SS |
| 511:2432 | Flow chart (system) | M | map row, then 24px SS |
| 511:2442 | List Box (system) | M | map row, then 24px SS |
| 513:80 | Clips (system) | M | map row, then 24px SS |
| 513:99 | Play (system) | G | 24px SS vs play.svg |
| 513:98 | Face (system) | M | map row, then 24px SS |
| 516:168 | Delivery Box 1 (system) | M | map row, then 24px SS |
| 516:171 | Connecting Cable (system) | M | map row, then 24px SS |
| 516:172 | Category Square (system) | M | map row, then 24px SS |
| 516:167 | Setting (system) | M | map row, then 24px SS |
| 532:974 | Network world (system) | M | map row, then 24px SS |
| 534:991 | Circle message question (system) | M | map row, then 24px SS |
| 534:990 | Vote2 (system) | M | map row, then 24px SS |
| 534:1023 | Cup (system) | M | map row, then 24px SS |
| 363:1918 | Arrow - Down (arrows) | G | 24px SS vs arrow-down.svg |
| 363:1920 | Chevron - Down (arrows) | G | 24px SS vs chevron-down.svg |
| 363:1922 | Chevron - Left (arrows) | G | 24px SS vs chevron-left.svg |
| 363:1924 | Arrow - Left Circle (arrows) | M | map row, then 24px SS |
| 363:1926 | Arrow - Right (arrows) | G | 24px SS vs arrow-right.svg |
| 363:1928 | Chevron - Right (arrows) | G | 24px SS vs chevron-right.svg |
| 363:1930 | Arrow - Up (arrows) | G | 24px SS vs arrow-up.svg |
| 363:1932 | Chevron - Up (arrows) | G | 24px SS vs chevron-up.svg |
| 363:1934 | Swap (arrows) | M | map row, then 24px SS |
| 363:1936 | Reload left (arrows) | M | map row, then 24px SS |
| 363:1938 | Reload right (arrows) | M | map row, then 24px SS |
| 363:1940 | Refresh (arrows) | M | map row, then 24px SS |
| 363:1942 | Refresh left (arrows) | M | map row, then 24px SS |
| 534:1016 | Arrow Jump Left (arrows) | M | map row, then 24px SS |
| 534:1015 | Arrow Jump Right (arrows) | M | map row, then 24px SS |
| 363:1944 | Update left (arrows) | M | map row, then 24px SS |
| 363:1946 | Update left big (arrows) | M | map row, then 24px SS |
| 363:1948 | Arrows bent arrow left up (arrows) | M | map row, then 24px SS |
| 363:1950 | Expand (arrows) | M | map row, then 24px SS |
| 363:1952 | Colapse (arrows) | M | map row, then 24px SS |
| 363:1954 | Download (arrows) | G | 24px SS vs download.svg |
| 363:1957 | badoo (socials) | S | map row or not-used decision |
| 363:1959 | Behance (socials) | S | map row or not-used decision |
| 363:1961 | Discord (socials) | S | map row or not-used decision; call sites not checked |
| 363:1963 | digg (socials) | S | map row or not-used decision |
| 363:1965 | Dribbble (socials) | S | map row or not-used decision |
| 363:1967 | ello circle (socials) | S | map row or not-used decision |
| 363:1969 | Facebook (socials) | S | map row or not-used decision |
| 363:1971 | facebook old (socials) | S | map row or not-used decision |
| 363:1973 | Figma (socials) | S | map row or not-used decision |
| 363:1975 | Flickr Circle (socials) | S | map row or not-used decision |
| 363:1977 | Github (socials) | S | map row or not-used decision |
| 363:1979 | Github Circle (socials) | S | map row or not-used decision |
| 363:1981 | Google podcast (socials) | S | map row or not-used decision |
| 363:1983 | grooveshark Circle (socials) | S | map row or not-used decision |
| 363:1985 | hangout (socials) | S | map row or not-used decision |
| 363:1987 | hipchat (socials) | S | map row or not-used decision |
| 363:1989 | imo (socials) | S | map row or not-used decision |
| 363:1991 | Instagram (socials) | S | map row or not-used decision |
| 363:1993 | line (socials) | S | map row or not-used decision |
| 363:1995 | Medium (socials) | S | map row or not-used decision |
| 363:1997 | Meta Messenger (socials) | S | map row or not-used decision |
| 363:1999 | mymind (socials) | S | map row or not-used decision |
| 363:2001 | Nintendo (socials) | S | map row or not-used decision |
| 363:2003 | Onlyfans (socials) | S | map row or not-used decision |
| 363:2005 | Patreon (socials) | S | map row or not-used decision |
| 363:2007 | Periscope (socials) | S | map row or not-used decision |
| 363:2009 | Pinterest (socials) | S | map row or not-used decision |
| 363:2011 | Pocket (socials) | S | map row or not-used decision |
| 363:2013 | quora (socials) | S | map row or not-used decision |
| 363:2015 | slack-2 (socials) | S | map row or not-used decision |
| 363:2017 | Soundcloud (socials) | S | map row or not-used decision |
| 363:2019 | Spotify (socials) | S | map row or not-used decision; call sites not checked |
| 363:2021 | swarm (socials) | S | map row or not-used decision |
| 363:2023 | tidal (socials) | S | map row or not-used decision |
| 363:2025 | Twitch (socials) | S | compare with PlatformLogo.vue twitch (simple-icons) at 24px |
| 363:2027 | upwork (socials) | S | map row or not-used decision |
| 363:2029 | vimeo (socials) | S | map row or not-used decision |
| 363:2031 | wattpad (socials) | S | map row or not-used decision |
| 363:2033 | wechat (socials) | S | map row or not-used decision |
| 363:2035 | Whatsapp (socials) | S | map row or not-used decision |
| 363:2037 | Xbox (socials) | S | map row or not-used decision |
| 363:2039 | Xiaomi Square (socials) | S | map row or not-used decision |
| 363:2041 | Yahoo (socials) | S | map row or not-used decision |
| 363:2043 | yelp (socials) | S | map row or not-used decision |
| 363:2045 | Youtube (socials) | S | compare with PlatformLogo.vue youtube at 24px |
| 363:2047 | Meta (socials) | S | map row or not-used decision |
| 363:2049 | Threads (socials) | S | map row or not-used decision |
| 391:1780 | Kick (socials) | S | compare with PlatformLogo.vue kick at 24px |
| 363:2051 | Tiktok (socials) | S | map row or not-used decision |
| 363:2053 | Twitter (socials) | S | map row or not-used decision |
| 363:2055 | Tumblr (socials) | S | map row or not-used decision |
| 363:2057 | X.com (socials) | S | compare with PlatformLogo.vue x at 24px |
| 363:2059 | Linkedin (socials) | S | map row or not-used decision |
| 363:2061 | Mastodon (socials) | S | map row or not-used decision |
| 363:2063 | Reddit (socials) | S | map row or not-used decision |
| 363:2065 | Snapchat (socials) | S | map row or not-used decision |
| 363:2067 | Vk (socials) | S | map row or not-used decision |
| 363:2069 | Bluesky (socials) | S | map row or not-used decision |

Not checked anywhere in this section: the light theme (the kit is dark only) and the real call sites of the components.

---

## Screens

N of M: 90 of 90 route records, one row each. Lane C starts after lane B lands. Rows that share a view file (26-28 and 30) are one screen checked in each frame.

Codes used in the table:

- PRIM = apply the shared primitives from lane B: side insets (the same space on both sides of an icon and its label), icon spacing, tag placement (icon placed inside the tag), dividers and their placement, search icon stroke, scrollbar gutter reserved.
- CHECK = GATE, then SHOT (1280px and 360px; one primary action per group, concentric radius, scarce accent).
- First chunk (points 3, 4, 5, 6, 14): rows 37 (home), the sidebar frame, and the page-level help text. These land as a rendered proposal first (see "Decisions for Stoney").

| # | Route | View file (under `dashboard/src/views/`) | What to apply | Acceptance check |
| --- | --- | --- | --- | --- |
| 1 | admin/tenants | dashboard/admin/AdminTenantsView.vue | PRIM | CHECK |
| 2 | admin/overview | dashboard/admin/AdminOverviewView.vue | PRIM | CHECK |
| 3 | admin/eventsub-health | dashboard/admin/AdminEventSubHealthView.vue | PRIM | CHECK |
| 4 | admin/channels/:channelId/:tab? | dashboard/admin/AdminChannelConsoleView.vue | PRIM | CHECK |
| 5 | admin/webhook-deliveries | dashboard/admin/AdminWebhookDeliveriesView.vue | PRIM | CHECK |
| 6 | admin/spam-defaults | dashboard/admin/AdminSpamDefaultsView.vue | PRIM | CHECK |
| 7 | admin/providers | dashboard/admin/AdminProvidersView.vue | PRIM | CHECK |
| 8 | admin/feature-flags | dashboard/admin/AdminFeatureFlagsView.vue | PRIM | CHECK |
| 9 | admin/scheduled-jobs | dashboard/admin/AdminScheduledJobsView.vue | PRIM | CHECK |
| 10 | admin/support | dashboard/admin/AdminSupportView.vue | PRIM | CHECK |
| 11 | admin/data-requests | dashboard/admin/AdminDataRequestsView.vue | PRIM | CHECK |
| 12 | admin/event-replay | dashboard/admin/AdminEventReplayView.vue | PRIM | CHECK |
| 13 | admin/trust-safety | dashboard/admin/AdminTrustSafetyView.vue | PRIM | CHECK |
| 14 | admin/tenant-usage | dashboard/admin/AdminTenantUsageView.vue | PRIM | CHECK |
| 15 | admin/error-budget | dashboard/admin/AdminErrorBudgetView.vue | PRIM | CHECK |
| 16 | admin/content | dashboard/admin/AdminContentView.vue | PRIM | CHECK |
| 17 | admin/billing | dashboard/admin/AdminBillingView.vue | PRIM | CHECK |
| 18 | admin/audit-log | dashboard/admin/AdminAuditView.vue | PRIM | CHECK |
| 19 | admin/platform-bot | dashboard/admin/AdminPlatformBotView.vue | PRIM | CHECK |
| 20 | admin/access | dashboard/admin/AdminAccessView.vue | PRIM | CHECK |
| 21 | admin/system | dashboard/admin/AdminSystemView.vue | PRIM | CHECK |
| 22 | admin/platform-defaults | dashboard/admin/AdminPlatformDefaultsView.vue | PRIM | CHECK |
| 23 | analytics | dashboard/analytics/AnalyticsView.vue | PRIM | CHECK |
| 24 | catalog | dashboard/catalog/CatalogView.vue | PRIM | CHECK |
| 25 | channel-points | dashboard/channel-points/ChannelPointsView.vue | PRIM | CHECK |
| 26 | chat | dashboard/chat/ChatView.vue | PRIM; chat action tray keeps 36px buttons with 20px icons | CHECK |
| 27 | viewer-chat | dashboard/chat/ChatView.vue | same view as 26 | CHECK |
| 28 | :channel/chat | dashboard/chat/ChatView.vue | same view as 26 | CHECK |
| 29 | :channel/twitch | dashboard/chat/ExternalChatView.vue | PRIM | CHECK |
| 30 | chat (popout) | dashboard/chat/ChatView.vue | same view as 26 | CHECK |
| 31 | commands | dashboard/commands/CommandsView.vue | PRIM | CHECK |
| 32 | settings/discord | dashboard/discord/DiscordView.vue | PRIM | CHECK |
| 33 | economy | dashboard/economy/EconomyView.vue | PRIM | CHECK |
| 34 | event-responses | dashboard/event-responses/EventResponsesView.vue | PRIM | CHECK |
| 35 | events | dashboard/events/EventsView.vue | PRIM | CHECK |
| 36 | giveaways | dashboard/giveaways/GiveawaysView.vue | PRIM | CHECK |
| 37 | home (dashboard) | dashboard/home/HomeView.vue | PRIM; home layout proposal (point 14) | CHECK |
| 38 | media-share | dashboard/media-share/MediaShareView.vue | PRIM | CHECK |
| 39 | music | dashboard/music/MusicView.vue | PRIM | CHECK |
| 40 | obs | dashboard/obs/ObsView.vue | PRIM | CHECK |
| 41 | overlays | dashboard/overlays/OverlaysView.vue | PRIM | CHECK |
| 42 | people | dashboard/people/PeopleView.vue | PRIM; search stroke (point 12) | CHECK |
| 43 | pick-lists | dashboard/picklists/PickListsView.vue | PRIM | CHECK |
| 44 | pipelines | dashboard/pipelines/PipelinesView.vue | PRIM | CHECK |
| 45 | pipelines/:id | dashboard/pipelines/WorkbenchView.vue | PRIM | CHECK |
| 46 | polls | dashboard/polls/PollsView.vue | PRIM | CHECK |
| 47 | quotes | dashboard/quotes/QuotesView.vue | PRIM | CHECK |
| 48 | scripts | dashboard/scripts/ScriptsView.vue | PRIM | CHECK |
| 49 | settings/api-devices | dashboard/settings/api-devices/ApiDevicesView.vue | PRIM | CHECK |
| 50 | settings/automod | dashboard/settings/automod/AutomodView.vue | PRIM | CHECK |
| 51 | settings/banned | dashboard/settings/banned/BannedView.vue | PRIM | CHECK |
| 52 | settings/billing | dashboard/settings/billing/BillingView.vue | PRIM | CHECK |
| 53 | settings/blocked-terms | dashboard/settings/blocked-terms/BlockedTermsView.vue | PRIM | CHECK |
| 54 | settings/channel-management | dashboard/settings/channel-management/ChannelManagementView.vue | PRIM | CHECK |
| 55 | settings/chat-filters | dashboard/settings/chat-filters/ChatFiltersView.vue | PRIM | CHECK |
| 56 | settings/custom-event-feeds | dashboard/settings/custom-event-feeds/CustomEventFeedsView.vue | PRIM | CHECK |
| 57 | settings/escalation | dashboard/settings/escalation/EscalationView.vue | PRIM | CHECK |
| 58 | settings/event-journal | dashboard/settings/event-journal/EventJournalView.vue | PRIM | CHECK |
| 59 | settings/features | dashboard/settings/features/FeaturesView.vue | PRIM | CHECK |
| 60 | settings/federation | dashboard/settings/federation/FederationView.vue | PRIM | CHECK |
| 61 | settings/integrations | dashboard/settings/integrations/IntegrationsView.vue | PRIM | CHECK |
| 62 | settings/permissions | dashboard/settings/permissions/PermissionsView.vue | PRIM | CHECK |
| 63 | settings/reports | dashboard/settings/reports/ReportsView.vue | PRIM | CHECK |
| 64 | settings/resource-limits | dashboard/settings/resource-limits/ResourceLimitsView.vue | PRIM | CHECK |
| 65 | settings | dashboard/settings/SettingsView.vue (view prop readability; ReadabilityView.vue) | PRIM | CHECK |
| 66 | settings/bot-basics | dashboard/settings/SettingsView.vue (view prop basics; BasicsView.vue) | PRIM | CHECK |
| 67 | settings/bot-personality | dashboard/settings/SettingsView.vue (view prop personality; PersonalityView.vue) | PRIM | CHECK |
| 68 | settings/engagement-triggers | dashboard/settings/SettingsView.vue (view prop engagement; EngagementView.vue) | PRIM | CHECK |
| 69 | settings/supporter-sources | dashboard/supporters/SupporterSourcesView.vue | PRIM | CHECK |
| 70 | settings/shoutouts | dashboard/settings/shoutouts/ShoutoutsView.vue | PRIM | CHECK |
| 71 | settings/spam-defense | dashboard/settings/spam-defense/SpamDefenseView.vue | PRIM | CHECK |
| 72 | settings/trust-policy | dashboard/settings/trust-policy/TrustPolicyView.vue | PRIM | CHECK |
| 73 | settings/webhooks | dashboard/settings/webhooks/WebhooksView.vue | PRIM | CHECK |
| 74 | settings/white-label-bot | dashboard/settings/white-label-bot/WhiteLabelBotView.vue | PRIM | CHECK |
| 75 | settings/your-data | dashboard/settings/your-data/YourDataView.vue | PRIM | CHECK |
| 76 | sounds | dashboard/sounds/SoundsView.vue | PRIM | CHECK |
| 77 | stream | dashboard/stream/StreamView.vue | PRIM | CHECK |
| 78 | supporters | dashboard/supporters/SupportersView.vue | PRIM | CHECK |
| 79 | triggers-timers | dashboard/timers/TimersView.vue | PRIM | CHECK |
| 80 | tts | dashboard/tts/TtsView.vue | PRIM | CHECK |
| 81 | voice-triggers | dashboard/voice-triggers/VoiceTriggersView.vue | PRIM | CHECK |
| 82 | vtube-studio | dashboard/vts/VtsView.vue | PRIM | CHECK |
| 83 | home (site) | site/home/HomeView.vue | PRIM | CHECK |
| 84 | invite/:code | site/invite/InvitePage.vue | PRIM | CHECK |
| 85 | login | site/login/LoginGate.vue | PRIM | CHECK |
| 86 | not-found | site/not-found/NotFoundView.vue | PRIM | CHECK |
| 87 | setup | site/setup/SetupView.vue | PRIM | CHECK |
| 88 | :channel/me | viewer/account/AccountView.vue | PRIM | CHECK |
| 89 | viewer-channels | viewer/channels/MyChannelsView.vue | PRIM | CHECK |
| 90 | :channel | viewer/me/MeView.vue | PRIM | CHECK |

Notes on the view paths: rows 1-90 come from the `component` field of `route-ledger.json` and the folder of each `route.ts`. Rows 65-68 resolve to `SettingsView.vue` by `props: { view: ... }` in `views/dashboard/settings/route.ts`; the per-view files `ReadabilityView.vue`, `BasicsView.vue`, `PersonalityView.vue` and `EngagementView.vue` sit beside it. The folder of row 69 is `dashboard/supporters` (its route lives there), not read for imports.

---

## aaoa points

N of M: 15 of 15 points and 4 of 4 brand notes, one row each. Stream times are from the VOD of 2026-11-04. A quote is a lead from a Whisper transcript; the surrounding lines were not re-read for this plan. A row stays until its acceptance check passes.

| # | Stream time | His words (quote) | Lane | Acceptance check |
| --- | --- | --- | --- | --- |
| 1 | 34:42 | Wrong first-time chatter: the bot said a viewer came in "for the first time ever". Arc's reading: Twitch sends no first-message flag, so the wording should be "first time the bot sees you". | D | Grep of server and dashboard words for "first time ever" returns none; a test on the greeting text; GATE |
| 2 | 1:47:54 | "I still just have the view ... on nums bot which is wild." Fact, read from the box on 2026-11-08 by the manager: user aaoa_ has one membership, Moderator on channel mrbenburns (source TwitchBadge), and no ChannelModerators or IamRoleAssignments rows, so he holds no role on Stoney's channel. Granting one is Stoney's call (see "Decisions for Stoney"). | E | Read-only query repeated after any grant, showing the role row; no grant is made by this plan |
| 3 | 1:59:31, 2:10:31 | "the amount of function is overwhelming"; fixing it is more work than if "built with a system". Arc's reading: a smaller first view and common patterns, not removing functions (CLAUDE.md: never drop features). | C | Proposal screenshot approved by Stoney, then SHOT of the built result; no function removed (route and nav-entry counts unchanged unless Stoney decides) |
| 4 | 2:12:44 | UI explanation text "is like too much". No time to understand every page. | C | Per-page help text word count before and after, listed in the handoff; SHOT; GATE |
| 5 | 2:15:21 | "overwhelming for most people", "so much shit on the side" (the sidebar). | C | Sidebar proposal screenshot approved by Stoney; then SHOT at 1280px and 360px of the built sidebar; nav tests pass |
| 6 | 2:22:09, 2:38:09 | "we need to componentize content"; all screens need common patterns. | B, C | One shared component or helper per repeated pattern, with a guard test that blocks the hand-written form (pattern: `dashboard/src/__tests__/structure.spec.ts` ownFormatter); GATE |
| 7 | 2:30:59 | Pages assume a streamer-bot mindset (redemptions, triggers). Arc's reading: goal-first entry points. | D | Entry-point list per goal shown as a rendered proposal; SHOT of the built entry points |
| 8 | 2:35:30 | Pipeline docs: "way too much text". Belongs to the `nomnomzbot-docs` repo. Listed only; no work in this repo. | docs repo (owner: not assigned here) | Not checked here; the docs repo owns the check |
| 9 | 1:57:39 | No personality: "a ton of clodism design", "un-fun things". Proposal for Stoney (see "Brand notes" and "Decisions for Stoney"); nothing changes until Stoney picks. | F (proposal) | Concepts delivered as images, each checked at 32px; no code change |
| 10 | 2:19:09 | AI UI work distrusted: self-rating to 10/10 did not fix it. | all | A screen is done only by the acceptance script (GATE) and a rendered screenshot (SHOT), never by a self-rating; no handoff may carry a score |
| 11 | 2:43:38, 2:47:29 | Spacing: uneven space around icons; "the same amount of space on both sides". Dividers and placements. | B, then C | CSS check that left and right inset around an icon are equal in the shared component; SHOT per screen; dividers placed as the kit shows |
| 12 | 2:50:38, 2:56:19 | "that tag feels off, the icon is not placed properly"; the search icon stroke is wrong. Also at stream 2:56, aaoa: "the fact that the p is cropped makes no sense" (a clipped descender in a field). | B | Tag SS vs kit; search icon stroke equals the pack stroke (CSS); a rendered field with a descender (a "p", "g", "y") shows no clipping at 1280px and 360px |
| 13 | 2:52:09 | Scrollbar sits over the content gutter. aaoa himself called the options debatable ("Debatable removing the scroll bars"). The build keeps the native-style scrollbar and reserves its gutter, so content never sits under it. | B | CSS `scrollbar-gutter: stable` (or the equal reserved width) on scroll containers; SHOT shows no overlap |
| 14 | 2:53:11, 2:55:42 | Home page "way too much". He likes Happening now, quick actions and resume queues; he questions the connections block. | C | Home proposal screenshot approved by Stoney; then SHOT of the built home; the three liked blocks kept |
| 15 | 2:48:49 | "I'm not touching the code because you've been working on it too much." Settled: we do the work. | all | This plan exists and its rows are worked top to bottom; no further decision needed |
| N1 | brand note | Simple four-letter NOMS mark, readable at favicon size. The pulse detail stays. | F (proposal) | Concepts checked at 32px; no change in code |
| N2 | brand note | Fun versions can become alternative app icons. | F (proposal) | Same concepts offered as app-icon variants, checked at 32px |
| N3 | brand note | Mascot small only: tabs, app icon, empty states. No mascot alert faces. | F (proposal) | Mascot concepts checked at 32px; none used in alerts |
| N4 | brand note | Figma exports are poor (dotted edges do not render): rebuild from the design, do not paste exported code. | A, B | No exported Figma code in any diff; each control rebuilt from design values |

Brand proposals for Stoney (distinct concepts, each to be checked at 32px; nothing is changed in the repo):

- Wordmark: four heavy letters N O M S in one row, the pulse line running through the middle as the crossbar of the letters.
- Tile: the four letters in a 2 by 2 grid of rounded tiles, the pulse as a single bright tile.
- Bite: a round shape with a bite cut out, the pulse drawn along the edge of the bite, with no letters at favicon size.

Mascot concepts (small only, 32px check):

- Noodle: a single curled noodle with two dots for eyes.
- Pulse creature: the pulse line folded into a small body with a face.
- Bowl: a round bowl with a steam curl; the face appears only at sizes above 32px.

---

## Decisions for Stoney

1. Sidebar and home layout (points 3, 4, 5, 14). The changes are built as a rendered proposal (screenshots at 1280px and 360px) and shown to Stoney before they land. Until then the sidebar groups in `navigation.ts` stay as they are.
2. Role for aaoa_ (point 2). The box shows no role for aaoa_ on Stoney's channel. Granting one is Stoney's call. This plan makes no grant.
3. Brand concepts (points 9, N1 to N3). Pick, change or drop the wordmark, tile, bite and mascot concepts above. Nothing changes in the repo before that.
4. Light theme. The kit is dark only. Not checked whether the dashboard light theme should follow the kit; Stoney decides if the kit gets a light variant.

---

## Order of work

1. Lane E reads the role (point 2). Lane A writes the icon map file and reads the 11 not-checked controls. Lane B starts the shared primitives. All three in parallel.
2. Lane B lands: button (tracking, icon chips, loading state), spinner, input tokens, segmented control, tooltip tail, 1.5px and 2px borders, icon and tag spacing, divider, search stroke, scrollbar gutter.
3. Lane D lands the wording (point 1) and goal-first entry points (point 7) in parallel with step 2.
4. Lane C works sidebar and home proposals first, then the Screens table in chunks, each chunk gated by GATE and SHOT.
5. A finished row is deleted from this file in the same commit that finishes it.
