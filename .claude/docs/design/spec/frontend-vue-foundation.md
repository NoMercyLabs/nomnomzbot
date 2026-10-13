# Vue dashboard: the foundation plan

Owner words, 2026-10-13 (translated from Dutch; in `.scratch/layout-mockups/inventory/owner-words.md`):
"make sure we really plan everything well ahead, so that we do not have to rewrite 100 API calls
again" and "It has to be performant, it has to be snappy. Things have to react immediately".
Next message: "Vue".

The go, same day: "Can we not better go to Vue right now, take over everything we have decided so far,
and lay the good base from the start?" and "So we go through it page by page, and we just look at what
works well and what does not work well. Until we have worked out every page and every feature. While you
know up front where everything ends up." And why: "Two of my biggest annoyances with Kotlin are the
long build times. And that there is simply no hot reload, so that we can spar quickly".

This plan replaces the KMP verdict in `ui-restructure-research-2026-10-11.md` §9 (keep Kotlin). The
HTML mockups stop here: what they decided moves into the components (V13, V21, V22), and the rest is
decided page by page in the Vue app.

## 1. What we have today (counted 2026-10-13)

| Thing | Count | Source |
|---|---|---|
| API operations in `server/openapi/v1.json` | 883 (710 paths, 1129 schemas) | contract scout |
| Operations with an `operationId` | 0 | contract scout |
| Auth scheme declared in the spec | none (code uses JWT bearer, `Program.cs:317`) | contract scout |
| Hand-written Kotlin API functions | 743, in 70 `Rest*Api` classes | `core/network/` |
| Dashboard pages / routes / sidebar rows | 33 / 56 / 54 | `ShellPage.kt`, `ShellRoute.kt`, `ShellNav.kt` |
| Viewer pages | 6 | `ParticipantNav.kt:50-86` |
| SignalR hubs on the server | 4 (dashboard 15 events, overlay 10, obs 2, admin 3) | `Program.cs:798-801` |
| Hubs the dashboard uses | 2 (dashboard, admin) | `DashboardHubClient.kt`, `AdminHubClient.kt` |
| Translation keys | 6911 per language, 2 languages (en, nl) | `composeResources/values*/` |
| Design components (Compose) | 60 | `core/designsystem/component/` |
| Kotlin files | 958 | `app/composeApp/src` |
| Web first load today | 46 MB | research §9 |
| Vue already in the repo | 23 widget `.vue` files, Vue 3.5.39 pinned | `Content/Widgets/Vendor/README.md` |

## 2. The decisions

Each row is one decision. My recommendation is in the row. Stoney signs the whole table.

| # | Area | Decision | Why |
|---|---|---|---|
| V1 | Framework | Vue 3 + TypeScript (strict) + Vite. Same Vue version as the widgets. | One Vue in the repo. TypeScript catches contract breaks at build time. |
| V2 | Folder | New `dashboard/` folder at the repo root. `app/` stays until every page is moved, then it is deleted. | The live bot keeps a working dashboard during the move. No dual code after the move. |
| V3 | API calls | Types are generated from `server/openapi/v1.json` (`openapi-typescript`). Calls go through one typed fetch client (`openapi-fetch`). Nobody writes a URL or a DTO by hand. | A server change regenerates the types. Every call that breaks shows as a build error. That is the "never rewrite 100 calls" guard. |
| V4 | API calls, one place | Each area has one query file (`dashboard/src/api/<area>.ts`). Pages never call the client directly. A lint rule blocks it. | When an endpoint changes, one file changes. |
| V5 | Backend step before V3 | Declare JWT bearer in the OpenAPI document. Add a test that every operation has a tag. | The spec must say the truth about auth. |
| V6 | Speed engine | TanStack Query for Vue holds all server data. Cache first: a page shows the last known data at once, then refreshes in the background. | Pages open instantly on the second visit. |
| V7 | Instant clicks | Every save is optimistic: the screen changes at once; on an error it rolls back and shows the error. | A click reacts before the server answers. |
| V8 | Live updates | The official `@microsoft/signalr` client. One connection per hub. A hub event writes straight into the query cache. | Live data shows without a refetch. The hand-written Kotlin protocol is not needed in a browser. |
| V9 | Prefetch | Hovering a sidebar row or a summary row loads that page's code and data. | The next page is ready before the click. |
| V10 | Code split | One code chunk per page. The shell loads first. | The first load drops from 46 MB to a few hundred KB. |
| V11 | Long lists | Virtual lists (TanStack Virtual) for chat, people, history and logs. | 10,000 rows scroll as fast as 10. |
| V12 | Components | shadcn-vue (Reka UI primitives) + Tailwind 4. The tokens come from one CSS file, ported from the mockup `tokens.css`. | Same design source as before (shadcn new-york). Accessible primitives for free. |
| V13 | Our own components | Built once from the mockup patterns: page shell, board grid, panel, summary row, detail header with back, inline flow, profile dialog. | One change to a component changes every page. That ends the many-file edits. |
| V14 | Page layout rule | Overview with summary rows; a click opens the detail; advanced goes deeper. One view = one screen. The page never scrolls. | Stoney 2026-10-13 (translated from Dutch): "Too much on one page". |
| V15 | Routing | Vue Router, history mode, the same 33 page slugs and 56 routes. The server sends `index.html` for unknown dashboard paths. | Same links as today. The OAuth `#access_token` fragment no longer fights a hash router. |
| V16 | Auth | Same backend as today: access token in memory only; refresh token in the httpOnly cookie; `client=web`; OAuth returns in the fragment. | No backend change. No token in localStorage. |
| V17 | Translations | vue-i18n. A script converts the 6911 keys per language from the Compose XML to JSON, once, and counts them in and out. One language file loads at a time. | No text in code. Nothing lost in the move. |
| V18 | State outside server data | Pinia, only for UI state (open panels, chosen channel, readability). | Server data has one home (V6). |
| V19 | Desktop | A native webview window around the same web app (no CEF). The choice of shell (Tauri or Photino.NET) is made when the web app is complete. | The app does not depend on the shell. |
| V20 | Three areas, one app | One Vue app, three areas, each with its own layout and its own code chunks. **Site** (`/`): the public website, signed out, prerendered to plain HTML at build time so it loads at once and search engines read it; it replaces `server/.../Assets/landing`. **Viewer** (`/me/...`): the viewer's own view (D4), phone first: balance, TTS voice, pronouns, linked platforms, and per channel now playing, leaderboards, store, games. **Dashboard** (`/app/...`): streamers and moderators, desktop first, dense. Sign-in is shared; after it a streamer or moderator lands in `/app`, a viewer in `/me`. Areas share the API layer, auth, `components/ui`, tokens and translations, never each other's views. The server-HTML pages (voice listener, overlays, widgets) stay server pages. | Stoney 2026-10-13 (translated from Dutch): "we have to have a main website and a user optimized view". A visitor never downloads dashboard code. |
| V21 | Type roles | The shadcn new-york-v4 source sets every size. Card title 16 semibold. Section label, form label and item title 14 medium. Description 14 normal, muted. Page title 20. 12 only for badges, counts, captions and table headers. A title and its description differ by weight and colour, not by size. | Stoney 2026-10-13 (translated from Dutch): "UI rules ... that are standard across the whole internet". Sources: `card.tsx`, `item.tsx`, `field.tsx`. |
| V22 | Grouped lists | Items in one group sit on the muted surface, with a 1 px divider (foreground at 10 %) between them. The group is part of its card, never a floating block. Two inputs that set one thing (for example Limits) are one field with one label. | Stoney 2026-10-13 (translated from Dutch): "a neat divider everywhere and the hierarchy". One component (V13) carries it. |
| V23 | Page map first | Before any page is built, a script-built map puts every feature of `FEATURES-BY-SIDEBAR.md` on one page, one view and one level (overview, detail, advanced). The map is the checklist for the page slices. | Stoney 2026-10-13 (translated from Dutch): "While you know up front where everything ends up." |
| V25 | Connect to any bot | Every client connects to every bot it can reach: a local self-contained bot, a bot on the home network, or the SaaS. The client keeps a list of saved bots and one active bot; the API client takes its base address from the active bot. The bot that serves the page is the first entry. A typed address is cleaned up (trim, no trailing `/`, `http://` when no scheme) and checked with `GET /api/v1/system/status` before it is saved. Sign-in options come from `GET /api/v1/auth/providers`: the OAuth redirect, or a device code that works on any client. How the session is kept sits behind one seam in `api/session/`: the HttpOnly refresh cookie on the web, the OS keychain in the native shell (the server already returns both tokens as JSON to native clients). Finding bots on the home network (`_nomnomz._tcp` mDNS) is native only; a browser cannot do it. An https page cannot reach an http bot (mixed content); the client says so in plain words and offers the bot's own address or the native app. | Stoney 2026-10-13 (translated from Dutch): "I want every client to be able to connect to every server that it can reach. So the native app must be able to connect to a local self-contained bot, a local network, or the SaaS." |
| V24 | Folder layout | Fixed before the first file, in section 2a. The generators set the base (create-vue, shadcn-vue `components.json`); we add only what they lack. | Stoney 2026-10-13 (translated from Dutch): "think carefully about the folder structure ... so that we do not constantly start swapping and switching and renaming and moving". |

## 2a. Folder layout (V24)

One name runs through every layer: the area and the page slug. The dashboard TTS page is route
`/app/tts`, folder `views/dashboard/tts/`, query file `api/tts.ts`, translation keys `dashboard.tts.*`.
Find one, and you find all. The three areas (V20) are `site`, `viewer` and `dashboard`; sign-in lives
in `views/site/login/`.

```
dashboard/
  index.html  package.json  vite.config.ts  tsconfig*.json  eslint.config.ts   (create-vue)
  components.json                        (shadcn-vue init)
  e2e/                                   (create-vue, Playwright)
    gates/       affordance, work, hover, fit, axe (section 4), run on every route
    speed/       the budgets of section 3
  src/
    main.ts  App.vue                     (create-vue)
    router/      index.ts joins the areas; site.ts, viewer.ts, dashboard.ts hold each area's routes,
                 one lazy import per view (V10, V15); guard.ts holds the sign-in guard
    layouts/     SiteLayout.vue, ViewerLayout.vue, DashboardLayout.vue: one per area (V20)
    views/       one folder per area, then one folder per page slug
      dashboard/
        tts/
          TtsView.vue      the page
          route.ts         the page's route constant
          i18n/            en.json, nl.json: only the keys `dashboard.tts.*`, loaded with the page
          components/      parts used by this page only
          __tests__/       (create-vue)
      viewer/    the same shape
      site/      the same shape; login/ lives here
    components/
      ui/        shadcn-vue output; changed only through its CLI
      layout/    our own building blocks (V13, V21, V22): PageShell, BoardGrid, Panel,
                 SummaryRow, DetailHeader, InlineFlow
      shared/    a part used by two or more views (moved here on the second user, not before)
    api/
      client.ts            the one openapi-fetch client
      queryClient.ts       the one TanStack QueryClient and its cache defaults (V6)
      auth.ts              sign-in, refresh, sign-out; the access token lives here, in memory only
      session/             how the refresh token is kept: cookie (web), keychain (native shell) (V25)
      schema.d.ts          generated from server/openapi/v1.json, never edited
      <slug>.ts            the queries and mutations of one area (V4, V6, V7)
    realtime/    dashboardHub.ts, adminHub.ts: hub events write into the query cache (V8)
    stores/      Pinia, UI state only (V18): connections.ts (saved bots, the active bot, V25),
                 channel.ts, panels.ts                         (create-vue)
    composables/ shared use* functions (current channel, optimistic save)
    i18n/        index.ts (loadPage); locales/en.json, nl.json hold only the shared words (`common.*`) (V17)
    styles/      tokens.css (from the mockup tokens), main.css
    lib/         utils.ts holds cn() only, as shadcn-vue requires; a plain function shared by two or
                 more areas gets one file named after its topic (duration.ts, currency.ts)
```

Placement rules:

- A file lives with the one page that uses it. On a second user it moves to `components/shared/`. That
  is the only planned move.
- No `utils`, `helpers` or `misc` file or folder. `lib/utils.ts` is shadcn's fixed name for `cn()`.
  `__tests__/structure.spec.ts` fails on a new one, and on a second export in `lib/utils.ts`.
- Each page owns its route constant in `views/<area>/<slug>/route.ts`. The area file in `router/`
  only lists them. Code names a route through its constant (`loginRoute.name`), never a string.
- Each page owns its words in `views/<area>/<slug>/i18n/`. They load with the page code (`loadPage`),
  so no locale file grows with the app. A word used by two or more areas moves to `common.*`.
  The structure test fails on a key of another page; ESLint (`@intlify/vue-i18n`) fails on a missing
  key and on text written in a template.
- Generated folders (`components/ui/`, `api/schema.d.ts`) are never edited by hand.
- A new top-level folder in `src/` needs a row in this table first.

## 3. Speed budgets (a test fails when one is broken)

| Budget | Limit |
|---|---|
| First load, shell JavaScript (gzip) | 200 KB |
| One page chunk (gzip) | 100 KB |
| Click to visible change (optimistic) | 100 ms |
| Open a page already in the cache | 100 ms to full content |
| Open a new page, first paint | 300 ms; a skeleton shows only after 150 ms |
| Dev loop: save a file to see the change (Vite hot reload, page state kept) | 1 s |
| Dev server cold start | 5 s |

A Playwright test measures these on every page in a production build. A size check runs on every build.

## 4. Gates carried from the mockups

The mockup guards become Playwright tests that run on every route:

- `affordance-check.js` (everything pressable looks pressable);
- `work-check.js` (every control does something);
- the hover check and the fit check (no view scrolls);
- axe accessibility.

The scripts are reused as they are. They are injected into the page.

## 5. Order of work

Each step is one validated slice.

1. Backend: auth scheme in the OpenAPI document (V5). Refresh `v1.json`.
2. `dashboard/` scaffold: Vite, TypeScript, lint rules, tokens, the generated client, the gates and the speed test. No pages yet.
3. Shell. Every building block a page needs exists before the first page, so no page invents its own:
   - sign-in, the refresh cookie, and a client middleware that refreshes once on a 401 and retries (V16);
   - the connect page (V25): saved bots (the serving bot and the official bot `https://api.nomnomz.bot`
     are always listed and cannot be forgotten), a typed address with its status check, the sign-in options of the
     chosen bot, the device code, and the "cannot reach" states;
   - server, for a dashboard on another origin than its bot (V25): OAuth may return to an allow-listed client
     origin (`Cors:Origins`), and the refresh cookie is `SameSite=None; Secure` only for such an origin;
   - TanStack Query wired in `main.ts`; sign-out clears the cache (V6);
   - `composables/useOptimisticSave`: change the cache at once, roll back and report on an error (V7);
   - `realtime/`: both hubs connected after sign-in, events written into the query cache (V8);
   - prefetch on hover: the route chunk and the page's first query (V9);
   - translation keys checked: ESLint fails on a key missing from the locale files (V17);
   - Pinia stores for the chosen channel and the open panels (V18);
   - the role landing: a streamer or moderator lands in `/app`, a viewer in `/me` (V20);
   - `DashboardLayout` with the sidebar from the page list and the channel switch; `ViewerLayout`; the site home;
   - the error inbox in the frame: every actionable error shows outside the page and links to its fix;
   - loading: a skeleton only after 150 ms; empty and error states as shared components.
4. Translations converted (V17), with a count check. The script writes each key into its page's `i18n/` file by the page map, and into `common.*` when two or more areas use it.
5. Shared components (V13, V21, V22), shown on one catalogue page that runs the same gates.
6. The page map (V23).
7. Pages, one slice each, stream-facing first: Home, Chat, Stream, TTS, Music, People, Commands, then the rest of the 33. Stoney walks each page in the running app; what does not feel right is fixed in the component, so every page gets it.
8. The server serves `dashboard/` in place of the Wasm app. Delete `app/`.
9. Desktop shell (V19).

## 6. Docs that change when this is signed

- `CLAUDE.md`: 5 lines name Compose, KMP, `jvmTest` or `compileKotlinWasmJs`.
- `.claude/docs/reference/`: 28 lines in 8 files.
- `spec/frontend*.md`: 70 lines in 6 files. `frontend.md` is replaced by this file.
- `SHORTCOMINGS-EXECUTION-PLAN.md`: the Kotlin-only slices (S-KMP-FONTS, S-KMP-CMP112, S-KMP-FALLBACK, S-KMP-A11Y) close as replaced. The `ApiContractTest` lines in acceptance criteria become "the generated types build".
- `ui-restructure-research-2026-10-11.md` §9: marked as replaced by this plan.
