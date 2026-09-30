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

### Area 2 — open items (2026-09-30)

The per-family matrix and the findings checkboxes were removed after verification; closed rows and findings
are gone. What is still open:

- **Event-catalog panel in the editor.** `GET /sdk/event-catalog` (`SdkController.cs`) has no caller in the
  authoring editor. Add a browsable events panel with an "insert handler" action per event.
- **npm SDK-types package.** The types are generated live by `SdkTypeEmitter` but are not distributable. Publish a
  versioned `@nomnomzbot/sdk-types` package from the same output in CI.
- **Announcements to tenants.** No admin controller or UI sends platform announcements to channels.
- **OBS / VTS admin presets.** `ObsController` / `VtsController` are channel-scoped; there is no platform preset
  layer.
- **Automation / IPC keys admin tab.** The key-issuing APIs exist (`AutomationTokensController`,
  `AutomationPairingController`, `IpcDevModeController`); no admin UI tab is confirmed.
- **Sound-clip platform asset store.** `SoundClipsController` is per-channel; there is no platform clip library.

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

---

## Second pass — 2026-09-26

Answers the "owed" item above, adds the desktop-editor finding as VERIFIED, and completes the family
matrix for Task 1 (economy, moderation, quotes/timers/rewards/sound clips, TTS, music, EventSub,
billing, trust/safety, GDPR, automation/IPC, feature content kinds).

### Desktop editor gap — now VERIFIED

`app/composeApp/src/jvmMain/kotlin/bot/nomnomz/dashboard/core/editor/ProjectEditor.jvm.kt:42-50` opens
a plain Swing `JTextArea` in a dialog for desktop code/widget editing — no TypeScript language
service, no live preview, no fire bar. Web (`ProjectEditor.wasmJs.kt`, per Part 1 above) mounts the
real Monaco-in-iframe editor. The owner's binding requirement is that desktop and web are the same
universal client (CLAUDE.md "Frontend Architecture") — this is a direct violation, not a nice-to-have.
Desktop authors get a strictly worse tool for the exact same feature.

### Platform-content propagation — answered

Platform content is NOT `ITenantScoped` (`PlatformContentDefinition.cs`). A tenant's own row is a copy of the
platform version it was installed from. It carries a nullable provenance pointer (`PlatformSourceVersion` on
the tenant entity), never a live FK to the platform row.

- **Publish** updates every untouched copy, after a preview that shows a counted blast radius
  (`PlatformContentService.cs`, publish modes).
- **Edited copies are protected.** A copy whose live hash no longer matches the hash recorded at install or last
  sync is counted as skipped, never overwritten, unless the admin publishes in Force mode (needs the
  `ContentPublishForce` permission and a publish note).
- **Kind set** (`PlatformContentKinds`): `command`, `widget`, `pipeline`, `code_script`, `event_response`,
  `timer`, `reward`, `pick_list`.

### Family matrix and findings

The per-family matrix and findings checkboxes were replaced on 2026-09-30 by the open list under
"Area 2 — open items" in Part 2 above; every closed row was removed.

### Revised remediation order (small vertical slices, most-unblocking first)

1. **Desktop editor parity** — swap the Swing `JTextArea` dialog for the same Monaco-in-iframe/WebView
   pattern web uses. Where: `ProjectEditor.jvm.kt`. Done-when: a desktop user editing a code script or
   widget gets autocomplete, live TS diagnostics, and multi-file — same as web, same SDK types endpoint.
2. **Wire the event catalog into the editor** (unchanged from Part 1, item 1) — `SdkController.cs:59`
   already returns the data; add a docs/events side panel to `editor.js`/`index.html`.
3. **GDPR admin console** — a read-only-first admin tab listing open `GdprController.cs` requests
   (`GET requests`) with drill-down (`GET requests/{id}`), reusing the existing API. Where: new
   `AdminGdprTab.kt` beside `AdminTrustSafetyTab.kt`. Done-when: an admin can see and act on every open
   erasure/export request without a DB query.
4. **Channel-point reward presets** — the cheapest of the missing day-to-day families to close because
   `RewardsController.cs` already has the per-channel CRUD shape to copy from; add a
   `RewardPresetController` (admin-authored, channel-installable) following the `PlatformContentDefinition`
   pattern but as a new kind, or a lighter parallel table if extending the closed enum is unwanted.
   Done-when: an admin can publish a reward preset and a streamer can install it into their channel.
5. **Decide the fate of the four Part-2 seed-only families** (event-response defaults, TTS voices,
   `ActionDefinition`, builtins) — unchanged from Part 1's items 4–5; still the next-cheapest wins
   because the DB shape already exists for two of them.
6. **Publish the SDK types as an npm package** (unchanged from Part 1, item 2) — lower urgency than the
   above since the in-editor path already works; matters for external tooling, not for today's authors.

### Owed / not checked (this pass)

- Billing tier / spam-defense truth (e): API + UI both confirmed to exist, but the save→read-back round
  trip was not traced hop-by-hop this pass.
- `AdminOpsToolsTab.kt` was not opened — automation/IPC key admin UI coverage is inferred, not confirmed.
- Announcements/notifications-to-tenants: only one grep pattern tried; needs a second pass with
  "broadcast"/"banner"/"message-tenants" before calling it confirmed-absent.
- `ObsController.cs`/`VtsController.cs` scoping was inferred from filename convention (every sibling
  controller in the same folder is channel-scoped), not read line-by-line.
