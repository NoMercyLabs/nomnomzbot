# Vue dashboard: the foundation plan

Owner words, 2026-10-13 (verbatim in `.scratch/layout-mockups/inventory/owner-words.md`):
"ervoor zorgen dat we dus echt alles goed vooruit plannen, zodat we dus niet weer eens 100 API calls
moeten gaan herschrijven" and "Het moet wel performant zijn, het moet snappy zijn. Dingen moeten meteen
reageren". Next message: "Vue".

The go, same day: "Kunnen we niet beter nu meteen naar view gaan en alles wat we nu hebben bepaald
overnemen en de goede basis leggen vanaf het begin?" and "Dus we gaan pagina voor pagina gaan we langs
en dan gaan we gewoon kijken of wat, wat fijn werkt en wat niet fijn werkt. En totdat we elke pagina en
elke feature hebben uitgewerkt. Terwijl jij dus van tevoren weet waar alles terecht komt." And why:
"Twee van mijn grootste ergernissen met Kotlin is de lange beeldtijden. En dat er gewoon geen hot
reload is, zodat we gewoon snel kunnen doorsparren".

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
| V14 | Page layout rule | Overview with summary rows; a click opens the detail; advanced goes deeper. One view = one screen. The page never scrolls. | Stoney 2026-10-13: "Te veel op één pagina". |
| V15 | Routing | Vue Router, history mode, the same 33 page slugs and 56 routes. The server sends `index.html` for unknown dashboard paths. | Same links as today. The OAuth `#access_token` fragment no longer fights a hash router. |
| V16 | Auth | Same backend as today: access token in memory only; refresh token in the httpOnly cookie; `client=web`; OAuth returns in the fragment. | No backend change. No token in localStorage. |
| V17 | Translations | vue-i18n. A script converts the 6911 keys per language from the Compose XML to JSON, once, and counts them in and out. One language file loads at a time. | No text in code. Nothing lost in the move. |
| V18 | State outside server data | Pinia, only for UI state (open panels, chosen channel, readability). | Server data has one home (V6). |
| V19 | Desktop | A native webview window around the same web app (no CEF). The choice of shell (Tauri or Photino.NET) is made when the web app is complete. | The app does not depend on the shell. |
| V20 | Viewer pages | Stay plain server HTML, as today. | Out of this plan. |
| V21 | Type roles | The shadcn new-york-v4 source sets every size. Card title 16 semibold. Section label, form label and item title 14 medium. Description 14 normal, muted. Page title 20. 12 only for badges, counts, captions and table headers. A title and its description differ by weight and colour, not by size. | Stoney 2026-10-13: "UI-regels ... die standaard zijn voor het hele internet". Sources: `card.tsx`, `item.tsx`, `field.tsx`. |
| V22 | Grouped lists | Items in one group sit on the muted surface, with a 1 px divider (foreground at 10 %) between them. The group is part of its card, never a floating block. Two inputs that set one thing (for example Limits) are one field with one label. | Stoney 2026-10-13: "overal netjes een divider en de hiërarchie". One component (V13) carries it. |
| V23 | Page map first | Before any page is built, a script-built map puts every feature of `FEATURES-BY-SIDEBAR.md` on one page, one view and one level (overview, detail, advanced). The map is the checklist for the page slices. | "Terwijl jij dus van tevoren weet waar alles terecht komt." |

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
3. Shell: sign-in, the refresh cookie, the sidebar from the page list, the channel switch, both hubs connected.
4. Translations converted (V17), with a count check.
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
