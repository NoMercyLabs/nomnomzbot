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

`PlatformContentDefinition.cs:17-20` doc comment: platform content is "NOT `ITenantScoped`... a
tenant's own row... carries a nullable provenance pointer back to the version it was installed from,
never a live FK to this row." This confirms install-time copy, not live push — publishing a new
platform-content version does **not** change any tenant who already installed an earlier version;
they stay on their copy until they re-install/update. `PlatformContentKinds.cs:61-69` also confirms
the closed kind set is exactly `command`/`widget`/`pipeline`/`code_script` — no `timer`, `quote`,
`reward`, `sound_clip`, or `event_response` kind exists, so those families can never get a
platform-authored template even once this system is fully built out.

### Completed family matrix

Legend unchanged: (a) global/default layer · (b) admin API · (c) admin UI · (d) push/override ·
(e) truthful state.

| Feature family | a | b | c | d | e |
|---|---|---|---|---|---|
| Billing tiers / plans | yes — `AdminBillingController.cs:47` tiers CRUD | yes, same file | yes `AdminBillingTierSection.kt` | yes, grant/revoke per channel (`:126-144`) | not traced this pass |
| Spam-defense defaults | yes — `AdminSpamDefenseController.cs:48` `GET defaults`, `:66` `PUT defaults` | yes | yes `AdminSpamDefaultsTab.kt` | global write, no visible per-tenant override endpoint in this controller | not traced this pass |
| Trust & safety (cross-tenant blocks) | yes — `AdminTrustSafetyController.cs:141-200` network-blocks preview/create/list/lift | yes | yes `AdminTrustSafetyTab.kt` | yes, blast-radius preview before block (`:141`) | not traced this pass |
| GDPR data ops | yes — `GdprController.cs:60-186` export/erasure-preview/erasure/opt-out/requests/consents | yes, same controller | **no admin UI tab found** (grep of `feature/admin/ui` file list, no `AdminGdpr*.kt`) — this is a caller-scoped (per-user) surface today, not an admin ops console | n/a | **`c` missing** — a real backend GDPR pipeline with no platform-admin console to run/monitor it |
| Automation/IPC keys | yes — `IpcDevModeController.cs:53-75`, `AutomationTokensController.cs`, `AutomationPairingController.cs` | yes | not confirmed — no matching `Admin*.kt` file found in the admin tab list (`AdminOpsToolsTab.kt` is the closest candidate, not opened this pass) | n/a | not traced |
| Economy (currency/games/catalog) | **missing** — `CatalogController.cs:31`, `CurrencyController.cs:29`, `GamesController.cs:31` are all `channels/{channelId}/economy/...`; only per-channel seed found is `EarningRuleSeedOnOnboardingHandler.cs` (fires at onboarding, per channel, not a global admin-editable table) | missing | missing | n/a | none found |
| Quotes | **missing** — `QuotesController.cs:30` is `api/v1/quotes` but tenant-scoped via the resolved channel, no platform-default quote pack found | missing | missing | n/a | none found |
| Timers | **missing** — `TimersController.cs:24` is `channels/{channelId}/timers`, no global timer-template layer found | missing | missing | n/a | none found |
| Channel-point reward presets | **missing** — `RewardsController.cs:29` is `channels/{channelId}/rewards`; no `RewardPreset`/`RewardTemplate` type found anywhere in the repo (searched both `Domain` and `Api` by name) | missing | missing | n/a | none found |
| Sound clips | **missing** — `SoundClipsController.cs:27` is `api/v1/sound-clips` but per-channel; no global sound-clip library/seed found | missing | missing | n/a | none found |
| TTS provider config (Azure/ElevenLabs) | yes as env-config only — `Azure:Tts:ApiKey`/`ElevenLabs:ApiKey` in `appsettings.json` (see CLAUDE.md env table); `TtsConfigController.cs` exists but is channel-scoped credential passthrough, not a platform default | missing (no controller writes the platform-level provider key at runtime) | missing | n/a | none found — this is deploy-time config, not a runtime admin surface, consistent with other secrets |
| Music/song-request defaults (limits, blocklists) | **missing** — `MusicController.cs:29` is `channels/{channelId}/music`; no platform-wide default limits/blocklist table found | missing | missing | n/a | none found |
| OBS/VTS control-plane presets | **missing** — `ObsController.cs`/`VtsController.cs` both channel-scoped by naming convention (not opened line-by-line this pass); no preset/template layer found by grep | missing | missing | n/a | none found |
| Announcements/notifications to tenants | not found — no controller name matched `Announce`/`Notif` + `Admin` in `Controllers/V1`; searched by `ls | grep -i -E "announce|notif"` against the full controller list, zero hits | missing | missing | n/a | none found |

Net picture for Task 1: the **billing / trust-safety / spam-defense / feature-flag / platform-content**
families are genuinely well-built admin surfaces with real API + UI + push mechanisms. Every
**channel-content family that a streamer authors day to day** — economy, quotes, timers, reward
presets, sound clips, music limits, control-plane presets — has **no global layer at all**: not
seed-only like TTS voices or `ActionDefinition` (Part 2 above), but **completely absent**, so the
owner cannot set a platform-wide default or template for any of them today. That is a bigger gap than
Part 1's "four seed-only families" framing suggested — it's not four gaps, it's most of the
day-to-day feature surface.

### New findings

- [ ] `PAGE` `app/composeApp/src/jvmMain/kotlin/bot/nomnomz/dashboard/core/editor/ProjectEditor.jvm.kt:42-50` — desktop code/widget editor is a plain Swing `JTextArea`, no language service, no live preview, no fire bar, versus web's full Monaco. Violates the "desktop + web same universal client" rule. **VERIFIED**.
- [ ] `RESULT` `server/src/NomNomzBot.Domain/PlatformContent/Entities/PlatformContentDefinition.cs:61-69` — `PlatformContentKinds` is closed to `command`/`widget`/`pipeline`/`code_script`; there is no platform-content kind for timers, quotes, reward presets, sound clips, or event responses, so those families cannot get a platform-authored template even after the seed-only gaps in Part 2 are fixed. **VERIFIED**.
- [ ] `RESULT` no global/admin layer exists for economy (`CatalogController.cs:31`, `CurrencyController.cs:29`, `GamesController.cs:31`), quotes (`QuotesController.cs:30`), timers (`TimersController.cs:24`), channel-point reward presets (`RewardsController.cs:29`, no `RewardPreset` type anywhere in repo), sound clips (`SoundClipsController.cs:27`), or music/song-request defaults (`MusicController.cs:29`) — all are per-channel-only routes with no matching admin controller. **VERIFIED** by route-prefix reads on each controller and a repo-wide grep for preset/template types.
- [ ] `RESULT` `server/src/NomNomzBot.Api/Controllers/V1/GdprController.cs:60-186` — a real, complete GDPR pipeline (export, erasure preview/execute, opt-out, requests, consents) exists as a per-caller self-service API, but no admin console tab was found to run or monitor GDPR requests platform-wide (grep of the admin UI file list found no `AdminGdpr*.kt`). **VERIFIED** absence by file-list grep; not independently confirmed there is no other UI entry point.
- [ ] `RESULT` `server/src/NomNomzBot.Api/Controllers/V1/AutomationTokensController.cs`, `AutomationPairingController.cs`, `IpcDevModeController.cs:53-75` exist with real key-issuing APIs, but no matching `Admin*.kt` UI file was found — `AdminOpsToolsTab.kt` is the closest candidate and was not opened this pass to confirm it covers these. Flagged as unconfirmed, not filed as a hard gap.
- [ ] `RAW` no controller name in `Controllers/V1` matches announcements/notifications-to-tenants (`ls Controllers/V1 | grep -i -E "announce|notif"` = zero hits). Either this feature doesn't exist yet or it's implemented under a name this grep missed — needs a second grep pass (e.g. "broadcast", "banner") before treating as confirmed-missing.

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
