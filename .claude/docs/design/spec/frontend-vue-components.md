# Vue dashboard: the component set and the page plan

Stoney, 2026-10-13 (translated from Dutch): "Make sure everything is reusable, so that you do not keep making
new components. That it is well thought out. And that all the pages that are in the mockup now are finished."
And: "I hope that a tab view also simply becomes a component."

This file fixes the building blocks before any page is built (foundation plan V13, V21, V22, step 3 and 5).
Sources: the 17 mockup files in `.scratch/layout-mockups/` (their frame rules in `base/frame.css` comments and
`index.html:45-59`), the psychology check (`ui-page-drawings-2026-10-11.md` section 10), the IA
(`spec/frontend-ia.md`) and the research (`ui-restructure-research-2026-10-11.md`).

## 1. Rules every component carries

| # | Rule | Source |
|---|---|---|
| C1 | The window never scrolls. Only a panel body scrolls. | `frame.css:1-2`, V14 |
| C2 | One tab row per page, in the page header, never inside a panel. More than one screen of content becomes tabs. | `frame.css:78`, `index.html:45-48` |
| C3 | One primary action per group. Siblings are outline or ghost. A solid red button only for the final confirm in a delete dialog; other harm is red words on a plain edge. | `frame.css:101-127, 338`, sleak |
| C4 | Panel tiers: lead, default, quiet. Every tier keeps its card surface and edge. One lead panel per view. | `frame.css:326-337` |
| C5 | Status is a dot plus words, never a tinted pill. Pink is the one primary action; red is harm. | `frame.css:335`, memory visual-weight |
| C6 | Concentric radius: rows 12 in a panel of 20; popover rows 8. | `frame.css:99` |
| C7 | A panel keeps its size in the loading, empty and error states. A skeleton shows only after 150 ms. | `9-components.html:188`, V-budget |
| C8 | A risky action shows its consequences before it runs (blast list from the server), and an undoable action shows its result in place with Undo. | `9-components.html:135`, `inline-flow.js:14-26` |
| C9 | Done goes to a toast. Broken goes to the error inbox in the frame and links to its fix. | `9-components.html:254`, foundation step 3 |
| C10 | Every user name opens the profile dialog, on every page. | `profile.js:2` |
| C11 | Words come from the page's i18n file; every sentence maps to a stored field. A value with no source shows a dash with a reason, never a guess. | memory plain-words, `8-viewer.html:66` |
| C12 | Hit areas 24 / 32 / 44 px by density; the person's readability settings apply everywhere. | `frame.css:286-298`, `readability.js:1` |

## 2. Primitives (shadcn-vue, `components/ui/`, CLI output only)

alert-dialog, avatar, badge, breadcrumb, button, button-group, card, chart (Unovis), checkbox, collapsible,
command, dialog, dropdown-menu, empty, field, input, input-group, item, kbd, label, popover, progress,
radio-group, scroll-area, select, separator, sheet, sidebar, skeleton, slider, sonner, spinner, switch, table,
tabs, textarea, toggle, toggle-group, tooltip. Libraries beside them: TanStack Virtual (long lists), VueUse
(timers, storage, keys), Monaco (code editor, memory code-editor-vscode-web-real-sdk).

Some CLI parts carry English screen-reader labels. Our code never shows them (guard: `structure.spec.ts`):
`SheetContent` and `DialogContent` get `:show-close-button="false"` and a translated close button;
`SidebarTrigger`, `BreadcrumbEllipsis` and `DialogScrollContent` are not used. The shadcn `Sidebar` turns into
a sheet with an English title under 768 px, so the frame shows the sidebar from 840 px only; below that it
uses the icon rail (600 to 839) and the bottom bar of 4 plus More (under 600).

## 3. Our building blocks (`components/layout/`)

One file each. A page composes these; it never restyles a primitive.

| Component | Built on | Props and slots | Mockup source | Used by |
|---|---|---|---|---|
| `PageHeader` | none | `title`, `crumb?`; slots `status`, `actions` (one primary), `meta`, `tabs` | `frame.css:69-76` | every page |
| `PageTabs` | Tabs | `tabs: {value, label}[]`; the value lives in the route query `?view=`, so a link opens a tab and Back works | `frame.css:79-84`, `10-stream.html:63` | stream, analytics, tts, commands, people, overlays, scripts, workbench, settings |
| `BoardGrid` / `BoardCell` | CSS grid | 12 columns; cell `span`, `rows` | `1-board.html:9` | home, music, tts, stream, analytics, viewer |
| `ListDetail` | none | slots `list`, `detail`, `aside?`; widths from tokens; on a phone the detail opens over the list | `2-list-detail.html:9` | commands, overlays, scripts, people, settings, admin |
| `PanelCard` (exists) | Card | add `state: ready / loading / empty / error`, `emptyText`, `onRetry`; slots `actions`, `footer` | `frame.css:89-97, 332` | every page |
| `RowGroup` (exists) | Item group | one muted group with 1 px dividers (V22) | `frame.css:100` | every list |
| `SummaryRow` (exists) | Item | `title`, `detail`, `pick`, `selected`; slots `lead`, `trail` | `frame.css:100-105` | every list |
| `SettingRow` | Item + Switch | `title`, `description`, `modelValue`; the whole row toggles | `9-components.html:243` | settings, stream labels, tts, music rules |
| `StatusBadge` | Badge | `tone: ok / warn / bad / live / off`, label | `frame.css:144-148, 335` | home, stream, overlays, scripts, admin, settings |
| `StatTile` / `StatStrip` | none | `label`, `value`, `context?`; no icons | `9-components.html:202` | home, stream, analytics, viewer |
| `KeyValueList` | dl | `items: {label, value}[]` | `frame.css:223` | stream, admin, overlays, scripts |
| `FilterBar` | ToggleGroup / Select | 2-4 options become a segmented control, more become a select; `search?` | `9-components.html:217` | people, admin, overlays, scripts, analytics, commands |
| `SearchField` | InputGroup | `modelValue`, placeholder key; `/` focuses it | `12-overlays.html` `#q` | every list |
| `InlineFlow` + `useInlineFlow` | Button | kinds `confirm / apply / run / note`; shows the result in place with Undo; Esc backs out | `inline-flow.js:14-26` | home, stream, settings, music, workbench, scripts |
| `ConfirmDialog` + `BlastList` | AlertDialog | `title`, `consequences[]` (from the blast-radius endpoint), `action` label; Cancel outline left, red button names the action | `9-components.html:135`, `frame.css:234` | every delete, raid, ban, regenerate, view-as |
| `FormDialog` | Dialog | 5 fields or fewer; 6 or more become an editor page | `9-components.html:169` | new command, new script, pair device, poll |
| `ProfileDialog` + `UserName` | Dialog + Tabs | one instance in the frame; `UserName` opens it by user id; tabs Overview / Chat / Activity / Moderation; footer actions | `profile.js:99-275` | chat, people, home, analytics, music |
| `TemplateField` + `HelperPanel` | Textarea + Command | `{` suggests template helpers; the panel inserts at the cursor | `2-list-detail.html:196-199` | commands, timers, event responses, pipelines |
| `CodeEditor` | Monaco | files, problems, console, runs | `12-overlays.html:413`, `13-scripts.html:374` | scripts, overlays |
| `VirtualList` | TanStack Virtual | rows by key, sticky at the bottom for chat | V11 | chat, people, history |
| `Countdown` | VueUse `useNow` | target time; shows mm:ss; emits at zero | `10-stream.html:352` | home ads, stream ads, timers |
| `ChartPanel` | Chart (Unovis) | line, area, bar, grouped bar; a one-line takeaway above every chart | `11-analytics.html:150-197` | analytics, stream |

Frame parts (`layouts/` and `components/layout/`): `DashboardLayout` (Sidebar with groups that fold, the open
page's group stays open; `ChannelSwitch`; `ProfileMenu` holding Admin and My data; `ShieldToggle` in every
dashboard header; `ErrorInbox`), `ViewerLayout` (viewer sidebar, channel chip), `FocusLayout` (no sidebar, for
the setup wizard). Source: `shell.js:5-6, 60-62, 170-196`.

Composables (`composables/`): `useOptimisticSave` (V7), `useCurrentChannel`, `usePageView` (the `?view=` tab
state used by `PageTabs`), `useUndo`.

## 4. The pages

| Route | View folder | Layout | Mockup | Tabs |
|---|---|---|---|---|
| `/app` | `dashboard/home` | board | `1-board.html` | none; live and offline states |
| `/app/chat` | `dashboard/chat` | live split | `3-live-split.html` | One channel / All my channels |
| `/app/stream` | `dashboard/stream` | board | `10-stream.html` | Stream info / Run the stream / History |
| `/app/analytics` | `dashboard/analytics` | board | `11-analytics.html` | Overview / Cause and effect / What chat likes / One stream / Streams / Chat / Community |
| `/app/commands` | `dashboard/commands` | list-detail | `2-list-detail.html` | Custom / Built-in |
| `/app/pipelines/:id` | `dashboard/pipelines` | workbench | `5-workbench.html` | Steps / Runs |
| `/app/scripts` | `dashboard/scripts` | list-detail | `13-scripts.html` | All / On / Off / Failed |
| `/app/overlays` | `dashboard/overlays` | list-detail | `12-overlays.html` | All / On / Off / Has a problem |
| `/app/people` | `dashboard/people` | list-detail | `14-people.html` | All / Moderators / VIPs / Subscribers / Regulars / New |
| `/app/music` | `dashboard/music` | board | `15-music.html` | none |
| `/app/tts` | `dashboard/tts` | board | `16-tts.html` | Overview / Voices / Rules |
| `/app/settings` | `dashboard/settings` | settings | `4-settings.html` | Channel / This device |
| `/app/admin/channels` | `dashboard/admin` | table | `6-table.html` | none |
| `/setup` | `site/setup` | focus | `7-focus.html` | none; steps |
| `/me` | `viewer/home` | board | `8-viewer.html` | none |
| `/app/catalogue` | `dashboard/catalogue` | none | `9-components.html` | development builds only |

Each page is one slice: its view, its `api/<slug>.ts` queries and mutations from the generated client, its
`i18n/` words, its tests, and a live check signed in against the local bot. A mockup control with no endpoint
is not built as a fake: it shows the designed empty or "not recorded" state with its reason (C11).

## 5. Order

1. Primitives through the CLI, then remove the Google Fonts import the CLI adds (guard: `structure.spec.ts`).
2. The building blocks of section 3, each with a behaviour test, shown together on `/app/catalogue`.
3. `DashboardLayout` with the sidebar from the page list, the channel switch, sign-in guard and the role landing.
4. The pages, stream-facing first: home, chat, stream, tts, music, people, commands, then the rest.
