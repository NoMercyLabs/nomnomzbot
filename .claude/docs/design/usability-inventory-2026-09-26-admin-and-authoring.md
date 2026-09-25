# Admin + authoring audit — 2026-09-26

Owner ask: the ADMIN side and the AUTHORING side always get too little attention. This audit checks
two things: (1) writing the bot's own behavior (code scripts, widgets, pipelines) and the help a user
gets while doing it, and (2) platform-admin control over every user-facing feature, globally.

Method: file:line evidence only, from direct reads and two Explore sub-agents (editor deep-dive,
admin-matrix deep-dive) whose reports are folded in below and marked as such. Builds on, and does not
repeat, findings already in `usability-inventory-2026-09-24-findings-ledger.md` (L5, L9, L10, L17) and
its companion plan doc.

**Headline correction versus assumption going in:** both areas are in noticeably better shape than the
owner's "always gets too little attention" framing implied. The code/widget editor is a real Monaco
instance with live TS diagnostics and real generated types, not a textarea. Feature flags and the
platform-content plane (commands/widgets/pipelines authored centrally) have a genuine global-layer +
admin-API + admin-UI + publish-with-blast-radius chain. The real gaps are narrower and listed below.

---

## Part 1 — Area 1: authoring, what exists today

The editor is real Monaco (`server/src/NomNomzBot.Api/Assets/editor/editor.js:45-47` loads
`monaco-editor@0.52.2` from CDN), not a textarea. `editor.js:397-428` turns on inlay hints, parameter
hints, bracket-pair colorization, sticky scroll, and quick suggestions. `editor.js:372-395` wires the
real TypeScript language service (`noSemanticValidation:false`) so diagnostics are real type errors,
not lint-only. Multi-file works: `editor.js:164-178` gives each project file a real `file:///` Monaco
model so cross-file `./helper` imports resolve. There's a command palette, fuzzy file-open, a
problems panel with click-to-jump, cross-file search, and 8 themes (`editor.js:444-842`). The Kotlin
side (`ProjectEditor.wasmJs.kt`) just mounts this page in an iframe and pumps files in/out over
`postMessage` — the editor itself is a real hosted web app, not a Compose widget. (The desktop/JVM
target uses a plain Swing textarea dialog per `ProjectEditor.kt:28-29` — not independently verified
this pass, and a real gap if desktop parity matters: desktop users get a materially worse editor than
web users.)

Types are real and can't drift by construction: `SdkController.cs:43-53` (`GET /sdk/types.d.ts`) is
generated live by `SdkTypeEmitter.EmitTypeScript` via reflection over the actual running server's C#
event types — there is no checked-in `.d.ts` to go stale. The client fetches it per-editor-open
(`CodeScriptsController.kt:264`, `WidgetsController.kt:398`) and feeds it into Monaco's language
service as an extra lib (`editor.js:393`, `addExtraLib`), giving real `nnz.` autocomplete and
hover types. Widgets and scripts use the SAME editor component — the only difference is the
`sdkTypes` context passed in (`"widget"` = narrower Public-tier surface, `"script"` = fuller
Broadcaster-tier surface, per `SdkController.cs:26-27`) and widgets additionally get an
`eventSubscriptions`-driven fire-bar/live-preview panel that scripts don't.

Sample payloads are genuinely real, independently verified: `EventSamplePayloads.cs:42-52`'s
`community.follow` sample (`user_id`, `user_login`, `user_name`, `followed_at`) matches field-for-field
what `ChannelFollowTranslator.cs:39-42` actually reads off the live Twitch EventSub payload. The file's
own doc comment states every external-event fixture is copied verbatim from that event's translator
test — confirmed against the entry's own traceability comment. Internal (non-EventSub) events fall back
to reflecting the real C# type (`ReflectionSampleGenerator.cs:19-28`), so even the fallback can't
diverge from the real contract, though field *values* in that fallback are heuristic placeholders
("sample-fieldname" etc.).

**What's actually missing:**
- **No published npm SDK package.** The types are correct and drift-proof but only reachable by
  fetching `/api/v1/sdk/types.d.ts` from a running server — no `package.json`/npm artifact exists
  anywhere in the repo for this surface. An external tool, CI script, or a user's own local project has
  no way to get these types outside the in-browser editor.
- **The SDK event catalog (`GET /sdk/event-catalog`, real per-event JSON Schema) is not wired into the
  editor at all.** Grep across `app/` for "event-catalog"/"EventCatalog" only hits the unrelated
  Webhooks feature's outbound-event catalogue UI. The endpoint that would answer "what can the SDK do"
  exists and is correct; nothing in the authoring UI calls it. This is the owner's "discoverability of
  what the SDK can do" ask, unmet.
- **No snippets, scaffolded templates, or doc-hover beyond bare TS types.** The editor's guidance is:
  autocomplete from the SDK types, and a problems panel with click-to-jump diagnostics. No "insert
  example handler," no starter-script gallery, no lint/format beyond Monaco's built-in
  `editor.action.formatDocument` (no project ESLint/Prettier). Error explanation is the raw TS
  diagnostic string or the raw backend compile-error string — no plain-language wrapper.
- **Pipelines remain the weak authoring surface** — already logged in the 2026-09-24 ledger's L5/B2:
  the pipeline editor drops backend-declared field kinds, 36+ actions fall to a raw key/value editor,
  `ResourceId` fields have no resource type so OBS/VTS/game/Discord pickers are free text, and typed
  values are sent as untyped strings and silently default. Not re-logged in full here — see that
  ledger. Net picture: two of three authoring surfaces (code scripts, widgets) are genuinely strong;
  the third (pipelines — the one most streamers touch first) carries nearly all the "raw text where a
  picker belongs" debt.

## Part 2 — Area 2: platform admin, what exists today

Coverage is real and, for two families, complete end-to-end — better than assumed. A
`PlatformContentController` plane exists that lets an admin author platform-owned **commands,
widgets, and pipeline definitions** centrally (`kind="command"|"widget"|"pipeline"`,
`app/.../feature/admin/ui/AdminContentTab.kt` → `AdminContentWidgetAuthoring.kt` /
`AdminContentPipelineAuthoring.kt`), with a publish-preview → confirm flow that shows a counted blast
radius before it goes live (`AdminContentTab.kt:16` doc comment). A separate `WidgetGalleryController`
gives a genuine platform-wide widget marketplace ("verified items... community widget",
`WidgetGalleryController.cs:22-24`), gated on a real `gallery:review` IAM permission. Feature flags are
fully wired: a global `FeatureFlag` definition + ramp, `FeatureFlagOverride` for explicit per-tenant
overrides, a real admin API (`FeatureFlagAdminController.cs:32`, IAM-gated), and an admin UI tab
(`AdminScreen.kt:1415`, `FeatureFlagsTab`) — this is the one family with a complete a→e chain and no
truth gap found.

Four families, however, are seed-only with **no runtime admin path at all**, meaning changing them
requires a code change and a redeploy:
- **Event-response defaults** — `EventResponsePresetCatalog.cs:29` is a hardcoded static C# class; no
  DB table, no admin controller, no admin UI tab.
- **TTS voices** — a real DB table exists (`TtsVoice` + `TtsVoiceSeeder.cs:24`, "global reference
  data") but it is seed-populated only; `TtsVoiceCatalogSync.cs` syncs from the Edge-TTS source, not
  from any admin edit path; no admin controller or UI tab found.
- **Action/permission defaults** — `ActionDefinition` is a real DB table, upserted by
  `ActionDefinitionSeeder.cs:36-89` and explicitly documented as "GLOBAL reference data" — but no admin
  controller references `ActionDefinition` at all (grep of `Controllers/V1` returns zero hits), so it's
  a global table with a deploy-time writer and no runtime one.
- **Builtins** (`BuiltinResponseComposer`, `BuiltinCommandCatalog`) — explicitly code-only per its own
  doc comment ("Catalog membership is defined by code; no DB seed rows"); no admin surface.

One more nuance: the **pipeline *action-type* catalogue** (which action kinds exist: send_message,
timeout, etc.) is distinct from platform-authored *pipeline definitions* (which the admin plane above
does cover). The action-type catalogue itself was not confirmed DB-editable — no controller was found
exposing action-type CRUD, which is consistent with the project's own documented convention (CLAUDE.md
"Adding a New Pipeline Action" = write a new `ICommandAction` C# class, scanned by DI). That is very
likely an intentional dependency-minimal design choice, not a bug — flagged as a design question below,
not a slice.

No TRUTH-tag (UI showing a setting as enforced when it isn't) was found among these 8 families for the
admin surfaces themselves — the gap is consistently *absence* of an admin path, not a misleading one.

### Area 2 matrix

Legend: (a) global/default backend layer · (b) admin API · (c) admin UI · (d) push/override mechanism ·
(e) any UI showing unenforced state. All cells below are from the two sub-agents' direct reads.

| Feature family | a | b | c | d | e |
|---|---|---|---|---|---|
| Commands (built-in catalogue) | missing — `BuiltinCommandCatalog.cs:18`: "membership defined by code, no DB seed rows" | n/a for the builtin catalogue; `PlatformContentController` (kind=command) covers *authored* platform commands, a different thing | yes for authored commands (`AdminContentTab.kt`) | n/a for the builtin catalogue | none found |
| Widgets/overlays (marketplace) | yes — `WidgetGalleryController.cs:22-24` real platform-wide gallery, distinct from per-channel Bundles | yes, `gallery:review` IAM-gated; plus `PlatformContentController` kind=widget | yes `AdminContentWidgetAuthoring.kt` | yes — publish-preview + blast-radius confirm | none found |
| Pipelines (definitions) | yes — `PlatformContentController` kind=pipeline, reuses the tenant ChainEditor | yes | yes `AdminContentPipelineAuthoring.kt` | yes, same blast-radius flow | none found |
| Pipeline *action-type* catalogue (distinct from above) | missing / hardcoded C# (inferred from absence of a matching controller, not directly read at the registration source) | missing | missing | n/a | none found |
| Feature flags | yes — `FeatureFlag` + `FeatureFlagOverride` | yes `FeatureFlagAdminController.cs:32` | yes `AdminScreen.kt:1415` | yes explicit tenant-override-of-global (`FeatureFlagAdminController.cs:59`) | none found — fully wired |
| Event response defaults | missing — `EventResponsePresetCatalog.cs:29` hardcoded C# | missing | missing | n/a | none found |
| TTS voices | yes as data, seed-only — `TtsVoice` + `TtsVoiceSeeder.cs:24` | missing | missing | missing (sync is from Edge-TTS, not admin) | none found |
| Action/permission defaults | yes as data, seed-only — `ActionDefinition` + `ActionDefinitionSeeder.cs:36-89` | missing (zero admin controller references) | missing | n/a (seed-only overwrite on deploy) | none found |
| Builtins (chat replies) | missing — code-only per its own doc comment | missing | missing | n/a | none found |

---

## Findings

- [ ] `RAW` server/src/NomNomzBot.Api/Controllers/V1/SdkController.cs:59 — `GET /sdk/event-catalog` (real per-event JSON Schema, `EmitEventCatalog`) has no caller anywhere in `app/` for the authoring editor; wire it into `/editor/index.html` as a browsable events/docs side panel with "insert handler" per event. **VERIFIED** by sub-agent grep + direct confirmation — already flagged `DEAD` in the 2026-09-24 ledger L17; re-flagged here as an authoring-discoverability gap specifically, since the fix is UX, not just "call the endpoint."
- [ ] `RESULT` no npm-publishable SDK package exists anywhere in the repo — types are correct and generated live but not distributable outside the in-app editor. Publish a versioned `@nomnomzbot/sdk-types` package from the same `SdkTypeEmitter` output as a CI step. **VERIFIED** (repo-wide `package.json` search found none outside `tools/streamdeck` and build-artifact vendor files).
- [ ] `PAGE` desktop/JVM code-script editor is a plain Swing textarea dialog (`ProjectEditor.kt:28-29`) versus the web build's full Monaco — desktop authors get materially less capability (no autocomplete, no diagnostics, no multi-file) for the same feature. Not independently verified beyond the doc comment this pass — flagged for follow-up, not filed as fully VERIFIED.
- [ ] `DEAD` no admin runtime path exists for event-response defaults (`EventResponsePresetCatalog.cs:29`, hardcoded C#), TTS voices (`TtsVoiceSeeder.cs:24`, DB table with no admin controller), action/permission defaults (`ActionDefinitionSeeder.cs:36-89`, DB table with zero admin controller references), or builtins (code-only) — four of eight platform-content families require a code change + redeploy to change a single default for the whole deployment. **VERIFIED** by direct sub-agent reads of each seeder/entity and a grep confirming no matching admin controller.
- [ ] `RAW` app/composeApp/.../feature/admin/ui/AdminScreen.kt:1533-1554 — per-tenant feature-flag override writes with no confirmation and no read-back of the resulting override state (already logged in the 2026-09-24 ledger L10b:479-481; re-cited here as the one weak spot in an otherwise fully-wired family).

---

## Proposed remediation order

1. **Wire the event catalog into the editor** — `SdkController.cs:59` already returns real per-event
   JSON Schema. Add a docs/events panel to `server/src/NomNomzBot.Api/Assets/editor/index.html` /
   `editor.js` that lists every event for the current context, its wire name, tier, and schema, with an
   "insert `nnz.on(...)` stub" action per row. Done-when: opening the editor shows a real, browsable
   event list, and inserting one produces a typed handler.
2. **Publish the SDK types as a real npm package** — add a small package under `tools/` (or
   `server/sdk-types`) with a build step that runs `SdkTypeEmitter.EmitTypeScript` (already exists,
   already correct) at CI time and version-bumps/publishes `@nomnomzbot/sdk-types` on release, matching
   the deployed server's version. Keep the live `/sdk/types.d.ts` endpoint unchanged (still the
   in-editor source). Done-when: `npm view @nomnomzbot/sdk-types` shows a version matching the deployed
   server, generated by CI, never hand-copied.
3. **Fix the feature-flag per-tenant override truth gap** — `AdminScreen.kt:1533-1554` needs a confirm
   step and a read-back of current overrides (list, not write-only). Done-when: setting an override and
   reopening the drawer shows the override that was just set, and every existing override for a flag is
   listable and clearable.
4. **Decide, then build, a DB-backed global layer for event-response defaults** — this is the
   highest-value of the four seed-only families, because it's the one platform-admin-relevant default a
   streamer actually experiences differently per-channel today (`EventResponsePresetCatalog` already
   has per-channel bindings; making the *catalogue itself* DB-seeded + admin-editable, with new channels
   seeded from it, closes the gap with the least new architecture). Follow the shape already proven by
   `FeatureFlag`/`FeatureFlagOverride` (global row + optional per-tenant override). Scope as its own
   spec slice before building — it changes seed architecture, not a one-file fix.
5. **Same decision for action/permission defaults** — `ActionDefinition` is already a DB table with the
   right shape; it only needs an admin controller + UI (no schema change). This is the cheapest of the
   four to close because the data layer already exists.
6. **Investigate desktop editor parity** — confirm whether the Swing textarea dialog
   (`ProjectEditor.kt:28-29`) is actually shipped as the desktop code-editing experience, and if so,
   scope bringing desktop up to the same Monaco-in-iframe pattern the web build uses (or an embedded
   WebView hosting the same `/editor/index.html`), since desktop is a first-class target per this
   project's own stack description.

## Owed / not checked

- TTS voice catalogue and action/permission defaults were confirmed seed-only by reading the seeder and
  grepping for a matching admin controller (zero hits) — not by reading every line of every
  `TtsConfigController.cs`/`TtsController.cs`/other controller that might touch them tangentially.
- The pipeline action-type catalogue's "hardcoded, no DB row" status is inferred from the absence of a
  matching admin controller and the project's own documented action-authoring convention — not from
  directly reading the `ICommandAction` DI-scan registration source. Flagged as inferred, not
  independently confirmed, in both the sub-agent report and here.
- Whether Monaco in `editor.js` truly offers go-to-definition (not just hover types + diagnostics) was
  not read at the exact call site — `createEditor()`'s config list (inlayHints, parameterHints,
  bracketPairColorization, stickyScroll, quickSuggestions) is strong circumstantial evidence but
  go-to-definition specifically wasn't grepped/confirmed.
- Snippets/templates gallery: confirmed absent by grep ("snippet"/"template" only hit unrelated Vue SFC
  preview code) — not exhaustively verified there is no separate mechanism elsewhere in the codebase.
- Whether `PlatformContentController`'s command/widget/pipeline authoring actually *pushes* a change to
  already-existing tenants who previously copied the platform content, versus only affecting new
  installs — the "blast radius" confirm step implies live push, but the propagation mechanism itself
  (does it edit tenant rows, or just the source template tenants copy from going forward) was not read
  line-by-line.
