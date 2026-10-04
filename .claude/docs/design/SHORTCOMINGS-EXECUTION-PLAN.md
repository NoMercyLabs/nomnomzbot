# Execution plan — the slice index (one ordered queue, top to bottom)

Binding inputs: `PRODUCT-ALIGNMENT.md` (decisions D1–D12) · findings in
`stability-audit-scope-and-plan.md` (**S**·F1–F19), `widget-quality-audit-scope-and-plan.md`
(**W**·§1–§8), `usability-shortcomings-audit-scope-and-plan.md` (**U**·A1–A7, B1–B7, C0–C7),
`usability-inventory-2026-09-24-audit-scope-and-plan.md` (**V**·A1–A6, B1–B8, Part C),
`sleak-review-2026-08-22.md` (**K**), `user-flow-behavioral-spec-2026-10-03.md` (**UF**·F1–F8, N1–N5,
H1–H5, A1–A8, O1–O5, M1–M8, I1–I7, V1–V7, T1–T11, X1–X8). This file only orders them.

**How to execute:** one slice at a time, in this order; each slice is the smallest testable vertical
cut (contract → service → data → UI where it applies → test). Test-first, locally (`tdd-local-no-ci`);
commit when the **Done-when** is proven; then **delete the slice from this file** (tracker = remaining
work only). A slice may be split further while executing, never merged. 🔒 = needs the owner first;
skip it and continue. Persona priority (owner): streamer → moderator of many → viewer.

**Ordering rule (owner, 2026-08-22): stabilize the CURRENT feature set first; merge new code only
where a fix requires it; add the new stuff after.** Phases: 0 truth-and-safety · 1 runtime stability ·
2 existing platforms made to work (minimal spine) · 3 form infrastructure · 4 existing-feature truth/
reach · 5 new model (one channel, many platforms; any login) · 6 new features + personas · 7 polish.
Slice IDs are stable; the order is the queue.

---

## OWNER REQUEST 2026-10-02 — code editor 100% type safe, SDK reliable and easy (in progress, top of queue)

Owner: "get the code editor to be 100% type safe and ensure the SDK is reliable and easy to use and
understand" + "a ton of tests to fire so scripts can be tested on their logic and output — every OBS and
chat exposable visible from the preview window". The draft user docs live in `docs/sdk/` (one topic per
page); `docs/sdk/help/known-problems.md` is the defect list for the slices below. Fix order: the
defects first, then S-SDK-DOCS-ATLAS documents the fixed code.

- **S-SCRIPT-MISSING-EXPORT** Found by the docs fact-check 2026-10-04, reproduced with the real esbuild and flags
  (`EsbuildScriptBundler.cs:76-91`). A direct named import of a missing export already fails the build ("No matching export",
  checked by the builder 2026-10-04). Two cases still save: a namespace member the file does not export
  (`import * as h`, then `h.missing()`) and a named import from a file with no exports. For those esbuild only warns,
  exits 0 and emits `(void 0)(...)`, `CompileAsync` only parses (`JintScriptExecutor.cs:260-261`),
  `CodeScriptService.cs:540-547` marks the version valid, and the first run faults with `TypeError: (void 0) is
  not a function`. The editor marks it red (TS2305/TS2306), but a red mark does not block Save
  (`editor.js:1146-1153`). Done-when: the save is refused with the file, line and column of the import (for
  example esbuild's `import-is-undefined` raised to an error), in the same `data.errors` shape as other build
  problems; a test failed first.
- **S-SCRIPT-CHAT-SEND-FAILURE** Found by the docs writer 2026-10-04. `IChatProvider.SendMessageAsync` returns
  `false` when the message could not be sent and says callers MUST honour it (`IChatProvider.cs:41-47`), but the
  script bridge drops that value in `chat.send` and in the `chat.reply` fallback (`ScriptHostBridge.cs:375`, `:405`),
  so a script whose message never reached chat sees success and `nnz.lastError` stays null. Done-when: a failed send
  sets `nnz.lastError` with a host error code and the call reports failure as its declared type allows; a test with a
  provider that returns `false` failed first. Check every other bridge call that drops a `bool` or `Result` from a
  provider and report N of M.
- **S-WIDGET-SETTINGS-DEFAULTS** Found by the docs writer 2026-10-04. The `default` of each field in a widget's
  `settings.json` never reaches the widget page until the streamer saves the settings form once: the overlay
  manifest sends only the saved bag (`WidgetService.cs:1267`), the SDK seeds from it (`OverlaySdkController.cs:57`,
  `:180`), and nothing merges the declared defaults. The editor types say every setting always has a value
  (`WidgetSettingsTypeWriter.cs`), so typed widget code reads `undefined` at run time. Done-when: a widget page
  receives every declared default for a field the streamer never saved (manifest, join `initialState` and
  `WidgetSettingsChanged` alike), so the generated type holds; a test failed first.
- **S-TESTRUN-SKIPS-VALIDATION** Found by the docs fact-check 2026-10-04. An editor test run captures every write
  (`chat.send`, `chat.reply`, `music.queue`, `storage.set`/`delete`, `tts.speak`, `tts.voice.set`, `widget.emit`,
  `reward.update`, `schedule.pipeline`, `actions.invoke:*`) and returns a canned success before the live bridge's
  argument checks run (`CaptureScriptHostBridge.cs:35-50`, `:83-94`). So an empty chat message, a bad delay or a bad
  storage key passes a test run with `nnz.lastError` null, while the same call fails live
  (`ScriptHostBridge.cs:368-372`). Done-when: each captured write runs the same argument validation as the live
  call (one shared validator per capability, no copy), so a test run reports the same `nnz.lastError` the live run
  would; a test per capability failed first; N of 11 reported. For `widget.emit` this includes the widget lookup: a test
  run now returns true for a widget that does not exist or is turned off (`CaptureScriptHostBridge.cs:47`). Starts after S-SCRIPT-CHAT-SEND-FAILURE lands
  (same file).
- **S-TTS-NAN-RATE** Found by the docs fact-check 2026-10-04. A script's speed or pitch of `NaN` or `Infinity`
  parses as a double (`ScriptHostBridge.cs:902-911`) and reaches the provider as `NaN%`, where the comment says a bad
  value keeps the default. Done-when: a non-finite value is treated as absent; a test failed first. Starts after
  S-SCRIPT-CHAT-SEND-FAILURE lands (same file).
- **S-WIDGET-EVENT-DATA** Found by the docs writer 2026-10-04. The pipeline step `widget_event` marks no field
  `Templated` (`WidgetEventAction.cs:39-58`), so the engine never fills `{user}`-style templates in it
  (`PipelineEngine.cs:849-866` resolves only `Templated` fields). Its `data` field is Text, so the widget gets
  the raw text, not a JSON object (`WidgetEventAction.cs:122`, `PipelinesApi.kt:382-404`). Done when `data` and
  `event_type` are `Templated`, a step with data `{"user":"{user}"}` reaches the widget as an object with the name
  filled in (the engine's `ResolvedStringToElement` already keeps object/array roots), and a test proves both.
- **S-WIDGET-EVENT-PRESENCE** Found by the docs writer 2026-10-04. `widget_event` returns success when no browser
  source has the widget open (`WidgetEventAction.cs:92-100`), so the run log says it worked while nothing showed.
  `IOverlayPresenceRegistry.IsWidgetAttached` already answers this for TTS. Done when the step fails with a plain
  reason when the widget is not attached, and a test proves both cases.
  The script call `widget.emit` has the same gap: it returns true with no page open (`ScriptHostBridge.cs:556-571`);
  done-when covers both paths (found by the send-to-a-widget writer 2026-10-04).
- **S-WIDGET-EVENT-LABEL** Found by the docs writer 2026-10-04. The editor labels `event_type` "Event type
  (optional)" (`strings.xml:3025`) for every step, but `widget_event` requires it (`WidgetEventAction.cs:50`);
  only `send_webhook` treats it as optional (`SendWebhookAction.cs:76`). Done when the label is right for each step.
- **S-YOUTUBE-NOW-PLAYING** Found by the docs fact-check 2026-10-04. `YouTubeMusicProvider` declares
  `MusicProviderCapabilities.NowPlaying` (`YouTubeMusicProvider.cs:92`) but `GetCurrentTrackAsync` always
  returns null (`:187-195`); its comment says the browser-source player relays now-playing over the
  OverlayHub, but that relay does not exist: OverlayHub has no player-report method, and `now_playing.vue`
  (:110-119, :340-342) only embeds a video from the bot's own snapshot (bot -> overlay, never back). So
  `nnz.api.music.nowPlaying()` and all 4 callers of `GetCurrentTrackAsync` (`MusicService.cs:1033` read,
  `:1807` poller publish, `:2012` lost-at-provider release, `:2100` duplicate gate) see null for YouTube, no
  `PlaybackStateChangedEvent` fires, and `SongRequestQueueReconciler.cs:56` never advances a YouTube request.
  Root cause: the playback driver for YouTube is the `ISongRequestSequencer` of `spec/music-sr.md` §3.5.2
  ("target, not built"; YouTube plays through the browser-source IFrame, track-end via `onStateChange(ENDED)`).
  Order: first a scout proves end to end on the dev box whether a YouTube song request ever plays today;
  then the §3.5.2 sequencer is built in slices (player report hub method + per-channel state holder that
  `GetCurrentTrackAsync` reads; overlay player plays the head and reports state; ENDED advances the queue).
  Done-when: a YouTube request plays in the overlay, `GetCurrentTrackAsync` returns it while it plays and null
  with no player attached, and it leaves the queue when it ends; each slice has a test that failed first.
- **S-ROTATED-TOKEN-OPEN-PAGE** Found by the docs writer 2026-10-04. The overlay hub checks a widget token only
  when a page connects (`OverlayHub.cs:67-77`), so after a rotate (and after the 15-minute grace,
  `WidgetService.cs:1118`) a page that is already open with the old address keeps receiving events until it
  reloads. A rotate after a leak does not cut the leaked page off, and the result dialog (`strings.xml:1734`)
  says the overlay goes blank. Done-when: when the old token's grace ends (or at once on a second rotate),
  connections that joined with it are dropped; the dialog text matches; a test failed first.
- **S-SDK-RELIABILITY** Found by the SDK docs research and checked against the code (2026-10-03). A script
  that leaves out an argument sends the text "undefined": `chat.send()` posts it in chat, `tts.voice.get()`
  looks up a viewer named "undefined" instead of the triggering viewer (`JintScriptExecutor.cs:165-201`, the
  `String(x)` bindings). `ScriptResourceBudget.MaxEgressBytes` (256 KB) is declared and never read. A script
  cannot stop its pipeline: `StopPipeline` is always false (`JintScriptExecutor.cs:482`, `ScriptRunner.cs`)
  although `RunCodeAction.cs:77` honours it. A compile or runtime error reaches the editor with no line or
  column (`CodeScriptService.cs:536-568`). `ScriptTestRunService.cs:150` and `ScriptRunner.cs:131` read
  `Result.Value` unchecked. Done-when: each has a test that failed first; a left-out argument means the
  documented default or a clear script error, never the text "undefined"; egress over the cap is refused with a
  clear error; a script can stop its pipeline through a typed SDK call; the editor underlines the error line.
- **S-WIDGETS-SCREEN-HIERARCHY** Render check on dev 2026-10-03 (0.1.0+d40fc9685), checked against the code. The
  overlay rows give every action the full accent: 8 `TextButton`s with `tokens.primary` text
  (`WidgetsScreen.kt:943, 957, 1046, 1059, 1077, 1137, 1150, 1640`), so a row reads as six equal-weight
  accent actions (Sleak: one primary per group, scarce accent). The gallery's first-party trust badge is
  `BadgeVariant.Default` (`WidgetsScreen.kt:1895`); every gallery item today is first-party, so every card
  carries a full-accent pill. (The closed gallery dialog's 43 buttons staying in the accessibility tree is
  S-CMP-A11Y-DIALOG: upstream CMP-9368, `ComposeWebSemanticsListener.kt:120-152` on 1.9.0.) Done-when: each
  row has at most one accent action, the badge is quiet when every item has it; each with a test that failed
  first, and a screenshot on dev.
- **S-UF-AUDIO-ONE-SOURCE** (stream-facing) Follow-up to the one-audio-source rule (owner 2026-10-02, shipped
  as S-SDK-WIDGET-DELIVERY). Done-when: each UF line below has a test that failed first.
  UF·T9: On the one audio page, sound is one at a time: a TTS line that arrives during a sound clip waits
  for the clip's `ended`, and a clip that arrives during a TTS line waits for the line (today `playSound`
  and the TTS queue run independently, `OverlaySdkController.cs:199-242`). An SDK test proves both
  orders. The no-Audio-Source inbox item is Critical while the channel is live and reads "TTS has nowhere
  to play. Add the TTS source in OBS." It replaces the transient `tts_no_output` alert, which checks
  `tts_speak` subscribers rather than the audio page (`TtsSpeakBroadcastHandler.cs:93-127`).
  UF·T11: The TTS page hands out the Audio Source page as **TTS source for OBS** (today `GET
  /tts/overlay` ensures and returns the `tts_caption` URL, `TtsConfigController.cs:66-90`, labelled
  "Browser-source URL", `strings.xml:804`). A controller test asserts the returned URL is the `tts_audio`
  widget's.
- **S-SDK-DOCS-ATLAS** Owner 2026-10-02: the SDK docs are written with the **atlas** skill (map, scanned
  source, two reviews per page, `check_docs.py status` = DELIVERED), for streamers who know no
  programming, one topic per page. The generic drafts now in `docs/sdk/` are existing documentation to
  audit (atlas Phase 4), not the delivery. Runs after the SDK fixes above so no page documents a bug.
- **S-EDITOR-I18N** The web code editor (`server/src/NomNomzBot.Api/Assets/editor/editor.js` and
  `index.html`) writes every label in English inside the bundle ("Run test", "Running…", panel titles,
  status text), so a Dutch dashboard shows an English editor. The gallery widget names and
  descriptions in `FirstPartyWidgetCatalogue.cs` are English literals in the same way. Done-when: the
  editor takes its labels from the app's resource strings (en and nl) in the open message, the gallery
  names and descriptions resolve through resource keys, and a test fails when a bundle label has no key.
---

## OWNER PUNCH LIST 2026-09-10 — remaining follow-ups

- **S-STREAMDECK-OBS-ICONS** Stream Deck OBS plugin icon art — Done-when: every OBS action ships its
  own key icon in the obs manifest
- **S-MOD-BAN-UNSEEN** (owner todo 2026-10-01, not now) The Moderation page's "Moderate viewer" dialog
  only finds viewers the bot has already seen (`searchViewers`). For a Twitch login that never chatted
  (`twrtlebeach`) it shows "No matching viewers" plus "Enter a Twitch user ID", so the streamer cannot
  pre-ban a known troll. Done-when: when no known viewer matches, the dialog looks up the typed login on
  the platform (Twitch Helix Get Users by login), shows that account (name, avatar, created date) as a
  pickable result, and ban / timeout applies to its platform id; a login that does not exist on Twitch
  says so.
- **S-SR-ROLE-LIMIT** (owner idea 2026-10-01: "per role song queue limit", filed for later) The song
  queue has one cap for everyone: `MusicConfig.MaxRequestsPerUser` (default 5), enforced as
  `PER_USER_LIMIT`. Done-when: the streamer sets a separate cap per role (viewer, follower, subscriber,
  VIP, moderator, broadcaster) on the music settings page; a requester gets the highest cap of the roles
  they hold; the refusal reply names their cap; a role without its own value falls back to the channel
  cap.
- **IDEA-REACT-RUNTIME** (owner idea 2026-10-04, an idea for later, not a slice) Real React widget support:
  a vendored React runtime the overlay page loads like `/overlay/vue.js`, `react` on the dependency allowlist,
  an esbuild JSX setup that resolves `react/jsx-runtime`, and the app offering `react` again. Until then
  S-REACT-WIDGET-HONEST refuses React with `WIDGET_FRAMEWORK_UNSUPPORTED`.
---

## OWNER BUG 2026-09-04 (b) — `!sr` answers with the PREVIOUS request's track (parked)

- [ ] **S-SR-STALE** (parked) Owner saw `!sr 9 to 5` answered with the track for the preceding
  `!sr joliene`; four layers are eliminated and it does not reproduce. Done-when: on a fresh
  sighting the exact outgoing Spotify query is logged beside the message id that triggered it, and
  the cause is fixed.

## OWNER REQUEST 2026-09-04 (b) — the admin plane a SaaS owner actually operates from

Owner, verbatim: "add a slice that completely overhauls the admin dashboard and gives me every tool i could
ever need managing the saas, including the editing of system commands, widgets, pipelines and code scripts.
and i mean everything i could ever need to configure as a saas owner/admin".

> **`saas` mode is a RESTRICTED option** — operating NomNomzBot as a hosted service for third parties is
> reserved to NoMercy Labs under the licence. Self-hosting your own bot is always free. Everything below is
> the operator surface for that restricted mode and must keep carrying this marker.

**Measured today (2026-09-04), not assumed.** The admin plane is **11 tabs** — Overview, Channels, Users,
System, Billing, IAM, Tenants, Audit, Spam Defaults, Providers — across 2,732 lines. Backing it: 10 admin
controllers (`AdminController`, `PlatformAdminController` with 9 routes, `PlatformIamController`,
`AdminBillingController`, `FeatureFlagAdminController`, `PlatformAnalyticsController`,
`AdminSpamDefenseController`, …).

**The structural gap, and it is the owner's exact ask.** Every piece of content the platform ships to every
tenant is seeded from **C# code**: `DefaultCommandsSeeder`, `FirstPartyWidgetCatalogueSeeder`,
`RaidFlowSeeder` / `RaidStartFlowSeeder` / `RaidCommitFlowSeeder`, `EventResponseDefaultsSeeder`,
`TtsVoiceSeeder`, `BillingTierSeeder`, `IamCatalogSeeder`, `ActionDefinitionSeeder`, `PronounSeeder`,
`ConfigSeeder` — 17 seeders in all. Per-tenant editors exist for all of it (`CommandsController`,
`WidgetsController`, `PipelinesController`, `CodeScriptsController`, `BuiltinsController`,
`TemplatesController`, `BundlesController`, `MarketplaceController`, `CatalogController`), but **nothing
edits the platform-level originals**. Changing one default command today means editing C#, building, and
deploying — the SaaS owner cannot author the product they are selling.

**Settle the spec before writing code** (house rule). Two questions decide the whole shape and neither has an
obvious answer, so they get decided FIRST, in the spec, with the reasoning written down:

1. **Propagation.** The owner edits a system command that 400 tenants already have. What happens to a tenant
   who renamed it? Who edited its response? Who deleted it? The existing seeder principle — *adopt an empty
   stub, never overwrite built content, never match on name alone* — is the starting point, but "the owner
   fixed a broken default and wants it to actually reach people" is a real need that principle refuses. The
   answer is probably per-edit (`publish as new` / `update in place where untouched` / `force`), and it must
   be visible in the UI before the owner commits, per the standing rule that consequences are shown.
2. **Seed vs template vs live.** Is platform content a row the seeder writes, a template tenants instantiate,
   or a live reference tenants point at? This decides whether an owner edit is a migration, a fan-out job, or
   nothing at all. Pick one and say why the other two lose.

Spec settled in `spec/platform-admin.md` (`93289b10`): propagation = per-publish mode with a counted
blast-radius preview; platform content is a TEMPLATE tenants instantiate. S-ADMIN-2 splits by content
kind on that spine: **2a** spine + system commands CLOSED (`78f816eb`), **2b** the admin surface for
them CLOSED (`6b342371`), **2c** widget kind on the spine CLOSED (`debe2e42`). Remaining: **2c-b** make
the widget publish actually reach a viewer, **2d** system pipelines, **2e** code scripts — each reusing
the real tenant-side editor, never a second worse one.

**S-ADMIN-2c-c CLOSED (`b2111281`).** The Content tab now authors the widget kind — editable Vue
source, settings schema and event subscriptions — and a publish job with failed tenant rebuilds
renders a visible failure line naming the count instead of leaving `RebuildFailedWidgetIds` unread.
No backend change was needed: `payloadJson` already carries source/settings/subscriptions end to end
and `WidgetContentPayload.TryParse` validates it server-side at publish. Verified independently, and
the failure-surface test asserts the rendered count string, so it fails if the field is dropped.

2a shipped the entities, BOTH migration sets (proven on a POPULATED database via `migration-check.ps1`,
not an empty one), the publish engine and `PlatformContentController`'s 9 routes. Two guards it added
beyond the brief and worth keeping: a force publish without a justification is REJECTED, and a publish
carrying a stale preview count fails closed rather than fanning out against numbers the owner never saw.

**S-ADMIN-2 CLOSED — the owner's explicit ask is delivered.** All four content kinds are authored at
platform level on one spine, each reusing the REAL tenant editor rather than a second worse one:
system commands (`78f816eb`/`6b342371`), first-party widgets (`debe2e42`/`b2111281`), system pipelines
through the shared tree editor (`84e6c9be`/`8010cda2`), and sandboxed code scripts through the shared
project editor (`ddbf75bf`). Every kind: a counted blast-radius preview, a stale count failing closed,
a force publish rejected without a justification, and propagation proven as per-tenant STATE —
untouched receives, customised is never overwritten, deleted is never resurrected.
`Publish_PlatformPublishedScript_RunsUnderTheSameSandboxLimit_AsATenantAuthoredScript` hits the real
Jint statement-limit wall identically for a platform-published and a tenant-authored script, so
publishing from the platform grants no wider powers. The Content tab's kind header now names each
row's actual kind instead of always saying "Command".

**CLOSED (`f04bf82c`).** The widget kind now opens its Vue source through the same `ProjectEditorIO`
overlay (entry `index.vue`, language `vue`), so all four content kinds author through the real tenant
editor. Settings schema and event subscriptions were asserted to still round-trip beside it — a silent
regression there would have broken widget publishing.

**S-ADMIN-4a CLOSED (`d191bc3e`, label + contract follow-up `365392eb`).** Tiers and prices are
authored from the admin plane: the edit persists and writes an audit entry, the blast-radius preview
returns the real counted number of tenants on the tier, and an apply carrying a stale confirmed count
fails closed rather than fanning out against numbers the owner never saw — three Api.Tests, plus
`AdminBillingTierEditorRenderTest` green on the module.

**S-ADMIN-4b CLOSED (`b36f42b3`), verified.** Comps and per-tenant entitlement grants, with both
migration sets and the snapshot regenerated through the script. The grant is proven to be read by the
REAL gate, not a parallel path: `RequireTierAction` calls `IBillingTierService.IsTierAtLeastAsync`,
which is the exact method the test drives on a real `BillingTierService` — false before the grant,
true after — and the expiry test asserts the capability is DENIED once expired, not merely that a
field was read. The admin surface renders reason and expiry and shows the counted blast radius before
the issue button enables.
**S-ADMIN-4c CLOSED (`e0e766be`).** Invoices list with their real shape and ordering, refunds record
the refunded amount and audit the actor with a second refund of the same invoice REJECTED rather than
double-paying, and dunning is derived through one shared `Invoice.ResolveDunningStatus` — the same
path the rest of the system reads, not a decorative badge. Both migration sets; snapshot regenerated
through the script; the tab shows the counted amount before the destructive button commits.

**Format-string trap found and fixed (`18c514de`).** `admin_tier_price` spelled the minor units
`%2$02d`, which reads correct and is not: compose-resources ignores width and flag specifiers on a
POSITIONAL argument, so a price of 10.05 rendered as 10.5 and had shipped that way since the tier
editor landed. Nothing rendered that string in a test. Padding now happens in Kotlin, and a test
asserts the rendered text — reverting the fix makes it fail. Both string tables swept: the only other
positional flag is `%4$+d` in `games_history_row`, which no call site uses.
**S-ADMIN-5 CLOSED (`2bfad340`).** Per-tenant and per-cohort flags, a staged percentage rollout and
per-integration kill switches — all proven by enforcement, not persistence: a kill switch disables
only the killed tenant, a tenant lands on the same side of a partial rollout every time, a 30%
rollout admits 25–35% of 5000 tenants, and an evaluation failure degrades to disabled instead of
throwing. The tab renders cohort percentage and per-tenant override state, shows the counted blast
radius before a kill switch commits, and a cancelled confirm leaves the flag untouched.
**S-ADMIN-6a CLOSED (`e8edf91a`, follow-ups `5691cb4c`).** Per-tenant EventSub subscription health read
from the REAL registry — a test with two tenants, one revoked, distinguishes them and the output moves
when the underlying subscription state moves — and an outbound webhook delivery log whose replay
performs a genuine new send, appends a NEW attempt row leaving the original intact, audits the actor,
and is REJECTED when the endpoint has since been deleted or disabled.

Two defects it exposed, both fixed rather than filed: the tenant-facing `RetryDeliveryAsync` still did
`delivery.Attempt++` and re-sent the same row, overwriting the failed status and response code the
operator was looking at when they pressed retry — it now delegates to the same append-only replay, so
there is one rule and not two; and `EntitlementGrant` (added in `b36f42b3`) was never classified into a
blast-radius category, which left `ChannelBlastRadiusSourcesCompletenessTests` red on master and would
have silently dropped that table from the count shown before a channel delete.

**S-ADMIN-6b CLOSED (`ceb9b7c4`), verified.** Background job queue with retry, and per-tenant usage.
Built on `ScheduledPipelineTask` — the real persisted dispatch primitive already swept by
`ScheduledPipelineExpiryService` — rather than inventing a parallel queue, because the codebase has no
generic background-job entity. Retry appends a new attempt and leaves the original failed one readable
(the bug shape fixed for webhooks in `5691cb4c`, proven catchable here), and is REJECTED when the job
already succeeded or its target pipeline is gone.

**It deliberately shows no cost figure, and that is the correct answer, not a gap in the slice.**
Per-tenant usage reports real measured quantities from `UsageRecord` + `TtsUsageRecord` with the period
stated. There is no per-unit price table anywhere in the codebase, so any currency number would have
been fabricated — which the never-show-unmeasured-state law forbids.

**S-ADMIN-4d CLOSED (`97a25e60`) — S-ADMIN is now complete, 2 through 9.** The owner authors a
priced-unit catalogue (integer minor units + explicit currency + a batch size, so a real
sub-minor-unit rate prices exactly in integers). It ships EMPTY on purpose: a unit with no row is
UNPRICED, and unpriced is not zero — usage still reports the measured quantity, reports cost only for
priced units, and NAMES the unpriced ones. The render test asserts BOTH unpriced lines say so, so
neither can quietly fall back to a zero. Repricing is deliberately not retroactive: cost is computed
live against the current rate, because this is an operator cost view, not an invoice ledger — chosen
and asserted rather than left implicit. Both migration sets; snapshot regenerated from a running API
(604 paths); Infrastructure 5170, Api 935, jvmTest and wasm green.

**S-ADMIN-6c CLOSED (`a785f8ce`) — S-ADMIN-6 is complete.** Error budget computed from real delivery
outcomes (it MOVES when the underlying records move) and scoped per-tenant, matching the
`GetTenantUsageAsync` precedent: a 2am tool exists to find WHICH tenant's integration is broken, so one
blended platform number would defeat its purpose. Event-store replay re-applies matched `EventJournal`
rows through each projection's own real `ApplyAsync` — the live driver's per-projection fold, not a
second engine — with a counted preview scoped to the projection's subscribed types, a stale count
failing closed and applying nothing, and an audit row naming operator, scope and count. Replay is
**idempotent** rather than append-only: `IProjection.ApplyAsync` is contractually an upsert keyed on
`EventId`, so a second identical run re-applies the same upserts instead of doubling state.
**S-ADMIN-7 CLOSED — 7a `0c6d017f`, 7b `922073d7`, both verified.** Cross-tenant person search and a
person view of real state (roles, standings, trust, entitlements, connections, each labelled with the
tenant it belongs to), plus history replayed from the real event journal. Gated on the new
`user:support:view` key threaded through all five places a key lives; every lookup requires a
justification and is audited naming operator and subject — denials audited too, and proven to read no
subject data.

The tests were checked for teeth, not just for green: the two-tenant history test asserts
EventType/BroadcasterId/ChannelName per index in a fixed newest-first order (it fails on a swap or a
reversed order) and seeds a THIRD person's event to prove no cross-person leak; the refusal test
asserts `FORBIDDEN` plus a real Denied audit row, so it would not pass against an endpoint that merely
returned an empty list; a fact the system does not hold renders absent, never as a zero row. The
shared `AuthTestContext` change was inspected separately because a loosened harness can manufacture
green: it only maps the previously-ignored `EventJournal` DbSet to its real production configuration —
strictly additive — and the full 5119-test suite across ~150 files sharing that context stayed green.
**S-ADMIN-8a CLOSED (`d1a9b897`).** Cross-tenant abuse signals computed from real recorded detections
(the aggregate names the right actor, tenants and detections, and an unrelated actor is excluded), and
a review queue for the platform's automatic calls where an overturn calls the real unban FIRST and
only then marks and audits — with a failed reversal leaving the detection untouched, so "marked
overturned but still banned" is impossible.

**It exposed a defect class with three members, all now fixed.** A reversal that stamps a row without
performing the undo is the never-show-unenforced-state law broken on a user-facing path:
1. platform review queue — correct by construction (`d1a9b897`);
2. per-channel `SpamDefenseService.OverturnDetectionAsync` — stamped `OverturnedAt` and never unbanned,
   so a moderator's overturn left the viewer banned. Fixed `5e2d68e0`, proven red-first;
3. `SpamCorrelationService` campaign de-qualification — stamped `ReversedAt` while its only consumer
   merely LOGGED `AccountsToRestore`, so a whole false-positive cohort stayed actioned. Fixed
   `20694769`: every account is attempted, but `ReversedAt` is written only when all were restored; a
   partial restore keeps `ReversedAt` null and persists `RestoredAccountCount` +
   `RestorationFailedAccountIds` so the shortfall is data, never silence.
The rest of the class was traced, not assumed: `AgeConsentService`, `LiveGameEngine`, `LiveGameRunner`,
`ModerationQueueService`, `ViewerReportService` and `TrustSafetyReviewService` all stamp only after
their real action. Search: `ReversedAt =|OverturnedAt =|ConfirmedAt =|ResolvedAt =` then trace consumers.

**S-ADMIN-8b CLOSED (`5e227dea`), verified against its safety properties, not just its tests.**
Network-wide blocks ship with: a counted preview naming the real tenants; an apply carrying a STALE
count failing closed with zero bans issued and zero rows written (verified — a refusal that had
already fanned out half the bans is the dangerous case); enforcement in `RoleResolver.HasCapabilityAsync`
itself, so the block is consulted on the real production path in a tenant the actor was never
individually blocked in, not merely visible to the admin surface; a lift that unbans every tenant the
apply touched and, on a failing leg, leaves `Status` non-Lifted with `LiftedAt` null and the failed
tenant named, still enforced; a required justification; and ownership proven in BOTH directions —
platform-super-admin is explicitly excluded, only the narrow named role grants it.
The two blast-radius edits were checked separately: the `RoleResolver` diff is a strict early
deny-return that cannot widen any permission, and all 52 fake-DbContext edits are additive
`NetworkBlocks` stubs, not assertions bent to pass.

Cross-INSTANCE signature sharing stays blocked (needs a NoMercy service that does not exist).

**S-MOD-REVERSAL-VISIBLE CLOSED (`b165fe50`).** A campaign's reversal outcome is now legible: three
states render distinguishably — "Reversal incomplete - N accounts still actioned", "Undone: <reason>",
and nothing when never reversed — so a partially-restored cohort can no longer look clean.
**S-ADMIN-9 CLOSED (`cdca15cf`).** The plane had grown to 18 surfaces; it is now five job groups —
**Live** (Overview, EventSub health, Webhook deliveries, Job queue, Usage, Audit) · **Who** (Channels,
Users, Tenants, Support) · **Money** (Billing) · **Risk** (Spam defence defaults, Trust & safety) ·
**Setup** (System, Feature flags, Providers, Content, IAM). Rejected: keeping the flat strip on
`TabsList`'s horizontal-scroll fallback (that hides overflow rather than organising it), and grouping
by backend module (mirrors implementation boundaries, not the operator's question).

Proven structurally rather than by inspection: `every_admin_tab_is_reachable_through_its_own_group`
walks `AdminTab.entries` by reflection and fails BY NAME if a surface has no reachability marker, so a
regrouping cannot quietly drop a tab; `the_job_group_strip_fits_a_390dp_phone_width_without_scrolling`
measures real rendered node positions; `support_and_trust_safety_stay_hidden_when_their_clients_are_not
_wired` proves the availability gate survived the move.

It also removed a latent bug: the old flat strip appended Support and Trust & Safety at fixed indices
16/17, which would have MISDISPATCHED had only one of the two clients ever been wired. The per-tab
enum identity makes that unrepresentable.

**Bar for the whole slice, non-negotiable:** every control does the real thing against the real backend and
is verified on the rendered client; no read-only panels pretending to be tools; no fabricated numbers; every
destructive or cross-tenant action shows its blast radius before it runs and lands in the audit log.

---

## AT A GLANCE — what is open, in one screen

Read this block first. It is the only summary; everything below is detail.

### S-SPAM — Sery-parity spam defence (owner 2026-09-03: "get the bot to behave like Sery bot")

Spam defence (`spec/spam-defense.md`) is built for L0–L5, the signature store and the dashboard surfaces. Open:

- [ ] **S-SPAM-NETWORK** Signature subscribe/contribute across instances. **BLOCKED, not deferred:**
      it needs a signature service at NoMercy that does not exist, and nothing in this repo can start
      it. The local corpus — store, quarantine, corroboration, curated-skips-quarantine, withdrawal,
      contribution eligibility — is built and is what makes corpus-match and near-duplicate able to
      fire at all.
- [ ] **S-SPAM-FOLLOWBOT-WIRE** follow events → ChannelBaseline → FollowBotTrack → per-account block
  + FollowBotBlock rows. Done-when: a simulated follow spike creates blocks and the dashboard
  restore works.
- [ ] **S-SPAM-LOCKDOWN-WIRE** raid/burst trigger → LockdownWindow → platform chat controls →
  restore on expiry. Done-when: a test hate-raid tightens and later restores the platform rules.
  Also wires JoinBurstFactor (it triggers the lockdown).
- [ ] **S-SPAM-TRUST-WIRE** the live tier resolution (`SpamDefenseService`) gathers watch time and
  calls `AccountRisk.Assess` with the channel's SemiTrustedWatchHoursHere/Instance (today it uses
  hard-coded constants and no caller passes watch time), and the engine checks capability floors via
  `TrustTierLadder.Allows`, with NonLatinScriptGate moving the NonLatinScript floor (today nothing
  calls `Allows`). Done-when: a lurker past the watch-hour setting is Semi-Trusted, and with the gate
  on a newcomer's Cyrillic message is held while a Regular's is not. The spam form marks these three
  settings "not yet active" until then (`SpamSettingCatalogue.PendingSlices`).
- [ ] **S-SPAM-SEED-CORPUS** idempotent seeder that loads `spec/data/spam-seed-corpus.md` as
  SpamSignature rows with Source=Curated. Done-when: a fresh install matches a corpus skeleton.

**Your asks, and where each one is:**

| Your words | Slice | State |
|---|---|---|
| make effects and repercussions visible | S-CONSEQ | law recorded, applies to every slice |
| budget system for payment tiers by resource usage | S-BUDGETS | queued - intent recorded: recover real cost, not upsell |
| old-bot behaviour only from generic blocks | (standing rule) | verified against the spec |
| stream-facing first (commands + overlays) | (ordering) | in force |
| research the human psychology and optimize the user flow | S-UF-* (spec 2026-10-03) | queued in Phases 0–7 |

**2026-09-24 — the queue is re-based on `usability-inventory-2026-09-24-audit-scope-and-plan.md`
(V·A1–A6, B1–B8, Part C).** Owner: "I thought we were done with the todo's but there is so much
more." Eleven lanes, every "whole platform" claim re-read at the lines, live box checked. Part C of
that doc is the ORDER: Tier 1 runtime (blue/green tug-of-war, Polly noise, scopes required at ENABLE
time) → Tier 2 "silently never worked" (AutoMod key contract, unban-approve, store purchases,
watch-time earning, typed pipeline params, widget settings push, double alerts) → Tier 3 the owner's
flows (impersonation, moderator-of-many, viewer) → Tier 4 form infrastructure incl. the standing
**A6 no-hoops rule (every picker creates inline)** and A5 dead links. Work Part C top to bottom
before anything below this line except the Tier-0 items already here.

**Phase 0-S is EMPTY — closed.** The queue is: the V·Part C tiers above, then Phases 0 and 1 below,
then Phase 2 onward.

**2026-10-03 — the user-flow spec is planned as `S-UF-*` slices** (`user-flow-behavioral-spec-2026-10-03.md`,
owner: "apply this research into the planning of the bots pending todo list … so we can execute on this in
agentic workflow"). 177 slices, each re-checked against master `22072b7b3`, sit at the end of their phase:
Phase 0 (1), Phase 2 (1), Phase 3 (16, the shared X1–X5 primitives most others depend on),
Phase 4 (104), Phase 6 (33) and Phase 7 (22). Inside a phase they run stream-facing first, then in
the spec's own order (stop silent loss → first value → live mode → moderator speed → authoring → viewer
loops → consolidation), with every `Depends:` landing first. Each slice names its **Files**, **Depends** and a
behavioral **Done-when**, so one slice is one builder dispatch (`dispatch-a-builder`). `[fix]` makes an
existing feature work or tell the truth; `[new]` adds a capability and waits in Phase 6/7 under D8, except
S-UF-F3a/F3b (the "it works" proof), placed in Phase 4 because three `[fix]` slices need its state. Spec
behavior that an existing slice already owns was appended to that slice's Done-when as a `UF·<id>:` line
(23 folds); items master already does were dropped. These also discharge usability-inventory Part C
items: M8a → #23, V3a → half of #21, V1a/V2a/V5a/V5b → parts of #26 (V-A3), V4a → the store half of #15.
Rules 5–12 below come from the spec's cross-cutting rules (X1–X8) and bind every slice from now on.

**Rules that now bind every future slice** (learned the hard way, each cost rework):
1. A guard that checks only a hand-written list is not a guard — enumerate from the real source.
2. Every model change lands in BOTH migration sets (SQLite AND Postgres) or Postgres deploys break.
3. Never show state that is not actually enforced.
4. Every control says what it does and what changes; destructive saves show a counted blast radius.
5. **Feedback where the eye is (X1).** A control shows its pressed state within 100 ms and its result or a spinner
   inside itself within 1 s; a dialog or editor never closes before the server answers, and a failure keeps the
   user's input.
6. **The safety ladder (X2).** Put every action on exactly one step: none, Undo (10 s), delayed send (5 s countdown
   with Undo), confirm with real numbers, or type-to-confirm for data loss; confirms are only for the top two, and a
   confirm button names the act, never "OK", "Yes" or "Confirm".
7. **Three honest states (X3).** Every list or page has a first-use empty state (purpose, one primary action), a
   filtered-empty state with a clear-filter link, a content-shaped skeleton while loading, and a "Couldn't load …"
   with Retry on failure; a failed fetch never renders as empty, and a form is never editable before its data arrives.
8. **Errors in human words (X4).** Every error says what happened, why if known, and what to do next, through the
   shared error map; never raw server text, an HTTP status as content, or an empty 403.
9. **People by name, keys behind a fold (X5).** People appear as display name + platform icon and are chosen with
   the viewer picker, never a typed id; feature keys, action keys, event types and scopes appear only under
   "Technical details".
10. **Push, don't poll (X6).** Any number that can change while the page is open updates from a hub push; a new
    `delay`-and-reload loop needs an allowlist entry with a reason.
11. **One word per concept (X7).** Use the PRODUCT-ALIGNMENT glossary words (viewer, event response, alert, widget,
    overlay; the bot types in chat and speaks in TTS), start labels with the verb, and put every user-facing string
    in en and nl resources.
12. **Keyboard first, mouse always (X8).** Every action is reachable by keyboard and registered with its shortcut;
    anything revealed on hover also appears on focus and in the row's "…" menu.

---

## BLOCKED ON THE OWNER — cannot be solved from this side

These are not "not done"; they are done-as-far-as-code-can-go and need a real-world action or a call
only Stoney can make. Do not burn agent time trying to work around them.

1. **Discord live-role setup.** The owner's physical steps now live in `DEPLOY.md` ("Discord
   live-role setup checklist").
2. **End-to-end Discord verification is impossible from here.** Unit tests prove the add/remove-role
   call is made with the right arguments against a FAKE handler. They can never prove Discord
   accepted it, that the token is valid, that the hierarchy is right, or that the resolved member is
   really him.
3. 🔒 **S-BUDGETS** classifies *registering a command* as near-free -> abuse floor, not a paid
   ceiling, per his own stated reason (recover real cost, never manufacture upsell). He cited
   commands as an example of a tier limit, so he may want to overrule. Files/TTS/CPU/bandwidth are
   cost-driving either way.
4. 🔒 **S-OWN21** Spotify/YouTube `!sr` needs "message replacement for its og widget body" (owner's
   words). Code tracing found the chain wired end to end (`SongRequestBuiltin` → `MusicService` →
   `SongRequestQueueChangedEvent` → `SrQueueBroadcastHandler` → the `sr_queue` overlay event;
   `now_playing.vue` renders the live track from its own `now_playing` event) and no bug. Done-when:
   the owner names which widget/body was stale (a screenshot of the wrong content) and it is fixed
   or confirmed correct.

---

## OWNER OBSERVATIONS 2026-08-29 — fold in at the right stage, do not jump on them

Live observations from using the bot, given as a batch with the instruction to slot each into
its natural phase rather than working them immediately. Each gets its own slice id (`S-OBS-*`) so they
survive independently as the queue is worked top to bottom.

- **S-OBS-01** stale-then-fresh flash on every page other than Home — a page loads cached data
  first, then the real server response replaces it a moment later, showing wrong data briefly before
  the correct data appears. Done-when: either the cache is never shown when a fresher fetch is
  already in flight, or the UI clearly marks cached data as loading/stale until the real response
  lands — no silent wrong-then-right flash.
- **S-OBS-03** no single place for server errors — errors need to surface both (a) in one consistent
  place (top-of-page banner or snackbar) AND (b) inline at the exact control/location that caused them.
  Done-when: every server error the dashboard receives does both, consistently, everywhere.
  UF·X1: The one consistent place is the FeedbackHost toast: an error toast stays until dismissed and
  carries Retry when a retry makes sense (S-UF-X1a); the toast text and the inline text come from the
  same S-UF-X4b error map; progress shows only in the control that was used, never a full-page spinner
  for a single action.
- **S-OBS-07** media page's `!media <url>` command works but the resulting media has no click-to-open
  popup, no on-page player, and no overlay-widget playback — it's captured but never actually watchable
  from the dashboard. Done-when: a `!media` result can be opened/played from the dashboard or an overlay.

---

## FUTURE INITIATIVE — global "system widget" architecture (NOT NOW, needs a dedicated co-working session)

Owner, 2026-08-29, explicit: "this is not now, this is when the time is right and the system is stable
to start working on this." Do not dispatch any slice against this until the owner opens that session.
Recorded here so it isn't lost, and so a future session picks the shape up as design context, not a
cold start.

**The shape of the ask:** some widgets are not per-consumer clone-and-customize instances (the current
model — code editor + clone feature per widget) — they are **system widgets**: one global thing the
admin (NoMercy Labs) authors and maintains centrally, and every consumer just picks CONFIGURATION for
it (a/b/c/d-style choices — e.g. corner position: top-left/top-right/bottom-left/bottom-right; a theme
choice) rather than getting their own editable copy. Two named examples:
- **TTS audio widget** — currently presumably has its own widget-clone flow; should instead be a fixed
  system widget whose URL + "open in popup" button live directly on the TTS settings page (no separate
  widget-gallery entry to browse/clone).
- **YouTube widget** — same pattern, same reasoning.
- **Chat overlay widget** — instead of many separate system-provided chat-overlay widget variants
  (one clone per look), this should be ONE system widget with a proper config system (position, theme,
  and whatever else) so a consumer picks from options rather than the admin needing to publish N
  near-duplicate widgets for each visual variant.

**Where this plugs in:** the owner frames this as belonging with the admin side of the code-scripts/
widget-editor system (S-CODE-EDITOR family, closed this session) — an "admin manages/edits the GLOBAL
widgets every consumer uses; the consumer only gets exposed config options" split, as opposed to today's
model where every widget is a per-consumer editable clone. This is a genuine two-tier authoring model
(admin-authored system widgets vs consumer-authored custom widgets) that doesn't exist yet.

**Design continuity:** there's prior Claude Design work on this — a "Chat Widget" design file at
`https://claude.ai/design/p/12517363-1a76-423c-8b05-9ed80f3e353c?file=Chat+Widget.dc.html` — pick that
up as the starting point rather than designing from zero; the owner said it "needs to be improved
first," i.e. it's a draft, not a finished spec.

**How the owner wants this session to run, verbatim intent:** "I want to do a proper co-working session
with you at that point where you ask me the ears off my head like a kid so we can make this awesome."
This means: when this is picked up, load `superpowers:brainstorming` first and interview the owner
thoroughly (scope, the exact config taxonomy per widget type, how admin-authored vs consumer-authored
widgets coexist in the data model, migration path for existing per-consumer widget clones) BEFORE
writing any spec or code — do not jump straight to a design doc or implementation plan.

**Business context**: this would give the owner's designer (aaoa-dev, who makes free stream-overlay
widgets) a real place to contribute polished system widgets that every consumer benefits from, rather
than each consumer needing their own clone-and-customize pass.

---

## Phase 0 — truth and safety

- **S-JWT-HANDLER** replace `JwtSecurityTokenHandler` with `JsonWebTokenHandler` in
  `JwtTokenService`, `ImpersonationSessionService` and `ImpersonationTokenMinter`. Done-when: no
  `JwtSecurityTokenHandler` remains under `server/src`, and the `JwtTokenServiceTests` (tampered,
  expired and wrong-algorithm tokens rejected) and the act-as identity-swap tests pass unchanged.
- **S-UF-F4a** [fix] The streamer sign-in forces account choice, and a bot sign-in that returns the streamer
  account is refused.
  Today `AuthService.cs:173` builds the streamer authorize URL with `force_verify` off. When a browser is
  logged into Twitch as the bot, Twitch silently signs the bot in as the "streamer" and creates a channel for
  it. Change: the streamer URL carries `force_verify=true`. `HandleTwitchChannelBotCallbackAsync`, the
  channel-bot device poll and the setup bot path refuse a returned account whose id equals the channel's
  `TwitchChannelId`, with code `BOT_IS_STREAMER` and the message "That's your streamer account. Sign in with
  the bot's account instead." Both the auth result and `BotStatusDto` carry the returned login and display
  name, so the client can confirm the account.
  Files: server `Identity/AuthService.cs`, `BotStatusDto`, `openapi/v1.json`.
  Depends: none.
  Done-when: Infrastructure tests assert the streamer URL contains `force_verify=true`. A bot callback that
  resolves to the owner's Twitch id returns `BOT_IS_STREAMER` and writes no `BotAccount`, connection or
  `ChannelBotAuthorization` row.

## Phase 1 — runtime stability

- **S-CONDUIT-LIVE** the dev box runs conduit mode (`EVENTSUB_CONDUITS_ENABLED=true`). Left: (1) a switchover
  with event traffic shows every event exactly once (event journal by EventSub message id); (2) make
  `EventSub:Conduits:Enabled` default-on or remove the flag. Done-when: both hold.
- **S-CMP-A11Y-DIALOG** after any Dialog/Popup closes, the web accessibility tree keeps only the closed
  layer's nodes until a reload (upstream CMP-9368, fixed in compose-multiplatform-core #3298, first shipped
  in 1.13.0-alpha01; we run CMP 1.9.0 on Kotlin 2.2.21). Blocked on CMP 1.13.0 stable. Then upgrade CMP and
  Kotlin (2.3.20+ is required for Wasm since CMP 1.11). Done-when: closing a ConfirmDialog, a Popover and a
  DropdownMenu leaves the page's full a11y tree in `body.shadowRoot`, checked live.

## Phase 2 — existing platforms made to work (Kick / YouTube are shipped features that are broken) — only the spine pieces these fixes REQUIRE

- **S-UF-M5d** [fix] Moderation calls carry the platform of the line they came from.
  Today chat moderation sends only a Twitch user id (`ChatApi.kt:52-67`, `BanUserRequest`), so acting on a
  Kick or YouTube line targets a Twitch account with that id. Build: ban, timeout, delete, warn and report
  send `provider` from the line (`ChatMessage.provider`). The server routes each action to that
  platform's moderation API, or refuses with a clear error when the platform does not support it, never
  falling back to Twitch.
  Files: `ChatApi.kt`, `MultiChatController.kt`, `ChatController.kt`, `BanUserRequest`,
  `PerformModerationActionRequest`, `ModerationService`. Depends: none.
  Done-when: an API test bans from a `provider=kick` line and asserts the Kick moderation client was called
  and the Twitch client was not. A platform with no ban API returns a named refusal.


## Phase 3 — form infrastructure (stabilizes existing authoring; every 'raw text box' finding rides on it)

- **S043** "All helpers" dialog — wired into every free-text template field that exists: commands,
  event responses, timers, chat triggers, Discord, pipelines, and the rewards `Response` field.
  Giveaways still has no announcement-text field to wire into — front or back — so that half is not
  a wiring task but the separate feature filed as W·§8 i7 (see S065-remaining). Done-when: the
  giveaway announcement field exists and opens the All-helpers dialog.
- **S-UF-A1a** [fix] Every authoring editor stays open until the server answers and keeps its input on failure.
  Today the seven editors (Commands, Event Responses, Alerts, Chat Triggers, Timers, Random Responses, Quotes)
  null their open state before the write runs (`CommandsScreen.kt:340-345` and the six siblings listed in
  the spec block), so a 4xx/5xx throws the typed form away. Build one shared editor-submit contract in
  `core/designsystem`: one filled primary bottom right reading "Create <noun>" / "Save changes"; while the
  write runs the button shows a spinner and the form is read-only; on success the dialog closes and a toast
  names the item ("Command !socials created"); on failure the dialog stays open, input intact, the error sits
  above the button with **Try again**; a disabled primary shows one muted line naming what is missing ("Add a
  name and a response to create this command."); field errors appear only after the field loses focus
  (`CommandsScreen.kt:859` today). Controllers return the write result instead of fire-and-forget. Alerts
  gains its missing success toast (`AlertsController.kt:182-188`).
  Files: `core/designsystem/component/` (editor footer), the seven `feature/*/ui/*Screen.kt` dialogs and their
  controllers, strings (en + nl).
  Depends: none.
  Done-when: a jvmTest per editor with a fake API that fails the save asserts the dialog is still composed,
  every field still holds the typed value and the error line is shown; the same test with a succeeding API
  asserts the dialog closed and one success feedback was emitted (Alerts included).
- **S-UF-A1b** [fix] Leaving a changed editor asks first; an unchanged one closes silently.
  Today a scrim click, Escape or Back dismisses every editor (`Dialog.kt:74,87` default true) and nothing
  tracks changes. Each editor compares its fields to the values it opened with; on a changed form, a scrim
  click, Escape, browser Back or a sidebar navigation opens "Discard your changes to !socials?" with **Keep
  editing** (primary) and **Discard**. The nav guard is a shell-level hook the router consults, so the
  pipeline editor (S-UF-A7a) reuses it.
  Files: `core/designsystem/component/Dialog.kt` (dirty-aware dismiss), shell navigation guard, the seven
  editors, strings.
  Depends: S-UF-A1a.
  Done-when: a jvmTest per editor: scrim click on a changed form shows the discard prompt and the form stays;
  **Discard** closes it; a scrim click on an unchanged form closes with no prompt; a sidebar click on a
  changed form keeps the route until **Discard**.
- **S-UF-X1a** [fix] The toast gets an action slot: Undo on success (10 s) and Retry on errors that can be retried.
  Today `FeedbackMessage(kind, label, formatArgs)` (`FeedbackMessage.kt:38-42`) cannot carry an action and every
  success dwells 4 s (`FeedbackHost.kt:49`). Build: `FeedbackMessage.action: FeedbackAction?` (label resource +
  suspend handler); a success with an action dwells 10 s, without 4 s; an error stays until dismissed and shows
  Retry when the emitter passes one; the action button runs once and the toast shows the action's own
  outcome. First adopters: Integrations connect/disconnect failures get Retry (`IntegrationsController.kt:658-661`);
  S-UF-X2a's Undo rides on it.
  Files: `app/core/feedback/FeedbackMessage.kt`, `FeedbackController.kt`, `FeedbackHost.kt`,
  `core/designsystem/component/Toast.kt`. Depends: none.
  Done-when: jvmTests with a fake clock assert a 10 s dwell with an action and 4 s without, that Retry re-invokes
  the failed call exactly once, and that a second click is ignored while it runs.
- **S-UF-X1b** [fix] A shared submit pattern keeps a dialog open until the server answers.
  Today Home's dialogs set `show…Dialog = false` before launching the call (`HomeScreen.kt:522-583`) and Roles
  clears its target first (`RolesScreen.kt:343-392`), so a failure loses the input. Build: one
  `rememberSubmission` helper: the confirm button shows its own spinner and disables, the dialog stays open, success
  closes it, failure keeps typed input and shows the error inline in the dialog (copy from S-UF-X4b). First adopters:
  Home title, prediction, raid, commercial, resolve dialogs and the Roles permit/revoke/override dialogs.
  Files: `core/designsystem/component/Dialog.kt` (helper), `HomeScreen.kt`, `RolesScreen.kt`. Depends: none.
  Done-when: a jvmTest per adopter fails the fake call and asserts the dialog is still open with the typed values
  and an inline error; a success closes it.
- **S-UF-X4a** [fix] A forbidden action returns which action, which role it needs and which role the caller holds.
  Today the 403 body is empty (`ActionAuthorizationHandler.cs:22`). Build: an `IAuthorizationMiddlewareResultHandler`
  writes ProblemDetails `code: FORBIDDEN_ACTION` with `action`, `requiredRole` and `heldRole` (role names, never level
  numbers) for a failed `RequireAction`; other 403s unchanged. Files: `Api/Authorization/*`, OpenAPI snapshot,
  `ApiResult.kt` (`ApiError` gains the three fields). Depends: none.
  Done-when: an Api test calls a timer write as a Moderator and asserts the three fields; `ApiContractTest` passes.
- **S-UF-X4b** [fix] One error-copy map turns every API failure into what happened, why, and what to do next.
  Today failures render the server text or English literals (`ApiClient.kt:104-113`) inside "Action failed: %1$s"
  (`strings.xml:20,307,407`). Build: `core/network/ErrorCopy.kt` maps `ApiError` (code, status, S-UF-X4a fields) to a
  resource sentence ("You need Editor to change timers.", "Twitch didn't answer. Try again in a minute.", "Your
  session expired. Sign in again."); the `ApiClient` literals move to resources (en+nl); a ratchet test counts
  `: %1$s` error templates and fails if it rises. First adopters: Home live-ops, Home attention and Participant
  action errors (`strings.xml:20,307,407`).
  Files: `ErrorCopy.kt` (new), `ApiClient.kt`, `strings.xml` (en, nl), the three controllers. Depends: S-UF-X4a.
  Done-when: table tests map 401, 403-with-fields, 404, 429, 5xx and NO_CONNECTION to their sentences in en and nl;
  a jvmTest asserts the Home live-ops failure shows "You need Editor …" for a FORBIDDEN_ACTION response.
- **S-UF-X2a** [fix] Undo for deleting a config item: the row disappears at once and an Undo toast restores it for
  10 s.
  Today a delete is a confirm dialog or immediate with no way back. Build: server `POST /channels/{id}/restore/
  {kind}/{itemId}` for soft-deleted channel config (commands, timers, chat triggers, quotes, pick lists, event
  responses) accepted within 60 s of `DeletedAt` and only by a caller holding that kind's write key; it clears
  `DeletedAt`/`DeletedBy` and re-publishes the kind's changed event. Client: delete runs without a confirm, emits an
  S-UF-X1a success with Undo. First adopters: Commands and Timers delete.
  Files: a `ConfigRestoreController` + service, `CommandsScreen.kt`, `TimersScreen.kt`, their controllers,
  `strings.xml`, OpenAPI snapshot. Depends: S-UF-X1a.
  Done-when: an Api test deletes then restores a command and asserts it answers in chat again (dispatch test) and
  that a restore after 60 s or by a caller without the write key is refused; a jvmTest asserts Undo re-lists the row.
- **S-UF-X2b** [fix] Delayed send: an external, irreversible, low-stakes action waits 5 s with a visible countdown
  and Undo, then sends.
  Today such actions fire at once (run ad: `HomeScreen.kt:566-573`). Build: a `DelayedSend` component (button turns
  into "Sending in 5… Undo"), the API call made only when the countdown ends, cancelled cleanly by Undo or by
  leaving the page (then not sent, and the toast says so). First adopters: Home "Run ad" and "End poll".
  Files: `core/designsystem/component/DelayedSend.kt` (new), `HomeScreen.kt`, `strings.xml`. Depends: S-UF-X1a.
  Done-when: a jvmTest with a fake clock asserts no call at 4.9 s, one call at 5 s, and zero calls after Undo.
- **S-UF-X2c** [fix] A type-to-confirm dialog for actions that destroy data.
  Today `ConfirmDialog` only offers a button (`ConfirmDialog.kt:23-35`). Build: `TypeToConfirmDialog(expected,
  blastRadius, actLabel)`: the counted blast radius, a field, the act button enabled only on an exact
  (case-sensitive, trimmed) match. First adopters: Delete channel (S-UF-I3b) and the nuke confirmation
  (`ModerationScreen.kt` `moderation_nuke_confirm*`), which shows the matched viewers by display name.
  Files: `core/designsystem/component/TypeToConfirmDialog.kt` (new), `ModerationScreen.kt`, `strings.xml`.
  Depends: none.
  Done-when: a jvmTest asserts the button is disabled for a near-match and fires once on the exact text.
- **S-UF-X2d** [fix] No confirm button reads "OK", "Yes", "Confirm" or "Continue".
  Today `admin_trust_safety_confirm` and `admin_confirm` read "Confirm" (`strings.xml:3891,4179`). Build: a jvmTest
  enumerates every resource key passed as a `confirmLabel` (scanned from source, not a hand list) and fails on
  those words in en or nl; the two offenders name their act.
  Files: `strings.xml` (en, nl), a guard test. Depends: none.
  Done-when: the guard passes, and fails when a confirm label is set to "OK".
- **S-UF-A2a** [fix] A "Chat will see" panel under every chat-producing field, rendered by the bot's own resolver.
  Today an unknown variable (`{usr}`) saves silently and reaches chat as raw text in every editor except
  pipeline fields. Add `POST /api/v1/channels/{channelId}/templates/preview` taking template text (or a list
  of random variants), the helper context and event type; it resolves through `TemplateResolver` with sample
  data (a real recent chatter of this channel, else "Ana"; sample args "!so Ana"; real uptime, game and
  follower count where they exist), applies the channel tone, the D5 `BotLinePrefix` rule and
  `IOutboundChatShaper` chunking, and returns: the sending account ("NomNomzBot:" or "Stoney_Eagle: *"), the
  rendered lines, a "Sends as N messages" count over 500 characters, and every unknown variable with its span
  and a nearest-match suggestion. The app shows the panel directly under each template field in Commands,
  Event Responses, Alerts, Chat Triggers, Timers (each message row) and Random Responses; random mode stacks
  every variant labelled "1 of 3"; an unknown variable is underlined red in field and preview with "Unknown
  variable {usr}. Did you mean {user}?" and a one-click fix. Debounced, never blocks typing.
  Files: `Api/Controllers/V1/TemplatesController.cs`, a preview service in `Infrastructure/Platform/Templating/`,
  `openapi/v1.json`, a shared `ChatPreviewPanel` in `core/designsystem`, the six editors.
  Depends: S-UF-A1a.
  Done-when: Api tests assert the preview of `Hi {user}` returns the recent chatter's name with the prefix
  applied when no bot account is connected and absent when one is, a 600-char template returns 2 chunks, and
  `{usr}` returns an unknown-variable entry suggesting `user`; a jvmTest per editor asserts saving a template
  with an unknown variable shows the warning before the write.
- **S-UF-A3a** [fix] Typing `{` opens a context-correct variable autocomplete, and **Insert variable** inserts at the cursor.
  Replace `TemplateHelpersLink` with a visible **Insert variable** button beside every template field
  (including each random-response row and each timer message row) that inserts at the focused field's
  cursor, adding a space only when the neighbour is not already a space or punctuation. Typing `{` in any
  template field opens an autocomplete at the cursor listing only the helpers valid for that context (event
  type for event responses); each row shows `{user}`, one line of meaning and a sample value. The helper
  registry gains a sample value per helper and a `ChatTrigger` context, and Chat Triggers stops borrowing
  the EventResponse context and drops the hard-coded label. One component serves Commands, Event Responses,
  Alerts, Chat Triggers, Timers, Random Responses, pipeline step fields, Discord, Webhooks and Rewards.
  Files: `core/designsystem/component/TemplateHelpersDialog.kt` (becomes the picker), every
  `TemplateHelpersLink(` call site (11), `Application/Abstractions/Templating/TemplateHelperRegistry.cs`,
  `TemplatesController.cs`, strings, `openapi/v1.json`.
  Depends: none.
  Done-when: a jvmTest places the cursor mid-text in a Commands response, picks `{user}` and asserts it lands
  at the cursor with one space; a timer test asserts the token lands in the focused (not last) row; an Api
  test asserts the ChatTrigger context returns `args` and not event-only helpers, each with a sample.
- **S-UF-I3d** [fix] Language is a dropdown of shipped locales and timezone a searchable list with the detected
  zone first.
  Today both are free text (`strings.xml:4320,4322`, `SettingsScreen.kt:1014`). Build: language picks from the
  shipped locales (en, nl); timezone uses the zone picker S-SCHEDULE-TZ builds, detected device zone first, invalid
  values impossible to submit.
  Files: `SettingsScreen.kt` (BasicsSection), `BasicsController.kt`, shared zone picker. Depends: S-SCHEDULE-TZ.
  Done-when: a jvmTest picks "Europe/Amsterdam" by typing "amst", saves, and asserts the PUT body; an unknown
  locale or zone cannot be sent.
- **S-UF-X3a** [fix] Shared empty, loading and failed-load states replace the per-screen copies.
  Today 39 private copies each decide their own look, and failed fetches render as empty
  (`DiscordScreen.kt:615,693`). Build: `core/designsystem/component/LoadStates.kt` with `EmptyFirstUse(purpose,
  primaryAction, templates?)`, `EmptyFiltered(query, onClearFilter)` ("No results for 'xyz'"), `QueueClear(lastChangeAt)`
  ("All clear"), `ContentSkeleton(shape)` and `LoadFailed(what, onRetry)` ("Couldn't load {what}" + Retry). A guard
  test counts private `ErrorContent`/`CenteredMessage` definitions and fails if the count rises (ratchet down). First
  adopters: Discord roles and dispatch log (failure becomes `LoadFailed`, not empty), Settings (skeleton instead of
  text), Roles list empty state.
  Files: `LoadStates.kt` (new), `DiscordScreen.kt`, `SettingsScreen.kt`, `RolesScreen.kt`, `strings.xml`, guard test.
  Depends: none.
  Done-when: a jvmTest fails the Discord roles fetch and asserts "Couldn't load roles" with Retry (and that Retry
  refetches); the ratchet test passes at today's count minus the three adopters.
- **S-UF-X5a** [fix] A shared viewer picker and viewer name chip replace typed ids.
  Today moderator add takes a typed Twitch user id (`strings.xml:570`, `ModerationScreen.kt:3279`). Build: a
  `ViewerPickerField` on `SearchPickerField` over the existing viewer search (display name + platform icon rows,
  returns the person's id) and a `ViewerName` chip for read-only display; a guard test fails when a string resource
  shown as a field label contains "user ID"/"user id" outside an allowlist of developer surfaces (pipeline fields,
  template helpers). First adopters: Moderation "Add moderator" and mod log rows (display names, not ids).
  Files: `core/designsystem/component/ViewerPickerField.kt`, `ViewerName.kt` (new), `ModerationScreen.kt`,
  `strings.xml`, guard test. Depends: none.
  Done-when: a jvmTest adds a moderator by typing a name and asserts the request carries the picked viewer's id;
  the guard passes with today's other id fields listed as known offenders that their own slices remove.
- **S-UF-I6f** [fix] The per-viewer TTS voice picks the viewer by name search.
  Today it asks "Viewer Twitch user ID" (`strings.xml:826`). Build: the S-UF-X5a viewer picker replaces the id
  field; rows show display name + platform icon. Files: `TtsScreen.kt`, `strings.xml`. Depends: S-UF-X5a.
  Done-when: a jvmTest types a name, picks a viewer, and asserts the assignment PUT carries that viewer's id.

## Phase 4 — existing features: truth, reach, completeness

- **S060-remaining** Editor fire-bar — desktop (JVM) parity 🔒: the desktop `ProjectEditor` is a
  Swing dialog with no live preview or DOM to fire into, so it needs a product decision on what
  "desktop gets the bar" means before it is buildable (owner call 2026-08-31, not a priority).
  Done-when: the owner names the desktop scope, then the bar fires real per-event samples there.
- **S062** Widget setup — open: inline preview; settings form by schema availability;
  asset/sound/font field types (no backend field type for these exists yet — needs a schema contract
  addition first); editable subscriptions (blocked on S085's `domain.action` naming realignment
  landing first — building this against today's ad-hoc event names would need re-doing). Done-when:
  add → copy → test → live from one row.
  UF·O1: After **Install** (gallery or system) an **Add to OBS** panel opens on the new widget and closes
  only when the user closes it: the URL with **Copy**, the size to enter from the widget's declared size
  (catalogue items gain a declared width/height, default 1920x1080), and three numbered steps in OBS's
  words ("In OBS, click + under Sources", "Choose Browser", "Paste the URL and set the size above"); a
  live line "Waiting for OBS to load this widget…" turns into "Loaded in OBS" from a hub push of the
  overlay presence signal, and **Send a test alert** (targeted at that widget, S-UF-O4b) then becomes the
  primary button. The Get started overlay step (S-UF-H2) completes on the first presence signal, not on
  Install. Tests: install leaves the panel open; a presence push flips the line and the primary.
  UF·O4: **Configure** opens a form generated from the widget's declared settings schema for every
  widget, gallery and custom widgets included, not only `isSystem` ones (`WidgetsScreen.kt:703`); a
  widget with no schema says so instead of hiding the action.
- **S065-remaining** announce-mode draw posts nothing and there is no giveaway overlay: add a
  `GiveawayDrawnEvent` consumer and a first-party giveaway widget — Done-when: an announce giveaway
  names the winner in chat and on the overlay. Note: an X winner cannot be DMed a code until S031
  lands an X client with `dm.write`; until then that delivery fails cleanly and the code stays
  `Assigned`.
  UF·V3: The draw line reads 'Winner: @Ana, picked from 54 entries. Ana, type anything in chat within 60
  s to claim.' (spec V3), using the real entry count and claim window.
- **S067** Song-request limits (U·B4) — owner answer recorded: free by default; the max-duration cap
  and the per-user cooldown are always on for everyone; a channel-currency cost is an opt-in
  per-channel setting, default OFF. `MusicConfig` has none of the three fields yet: add
  `MaxTrackDurationSeconds`, `PerUserCooldownSeconds` and `Cost` (entity,
  `MusicConfigDto`/`UpdateMusicConfigDto`, BOTH migration sets, the Song Requests editor), enforce
  them in `MusicService` admission, and charge the requester when `Cost` > 0 (the refund-on-remove
  mechanism already exists). Done-when: every SR toggle changes what `!sr` does (test per setting).
- **S068** Legacy builtins (U·C7) — open: `!songhistory`. No recently-played/song-history read path
  exists in the Music module (`IMusicService.GetQueueAsync` returns only the current track and the
  forward queue), so there is nothing truthful to answer with yet. Done-when: a fresh channel has
  every legacy command or a seed for it.
- **S069** Bot voice everywhere (U·C7, K copy) — open: the per-locale tone catalogue, which waits on
  the 🔒 owner call below (whether the bot ever replies in a non-English language, and what selects
  it). Tone applies only to the bot's own system voice (builtins, `send_message`'s default/fallback
  copy) and never rewrites what a streamer authored (owner call 2026-09-01). Done-when: the bot's
  own system-authored messages sound consistent across surfaces; user-authored content is explicitly
  exempt.
- **S085-remaining** Spec-led contract deltas — open: `IAutomationEventDescriptor` → attribute
  catalog (see S-AUTOMATION-EXPOSE-ALL); `FirstPartyWidgetCatalogue` `domain.action` subscription
  names; the `2026-06-16-database-schema.md` changelog entries; `economy.md` L.3
  `SubjectTwitchUserId`. (`BundleExports` and `CommandFlowSpec` keep integer levels on purpose: they
  are document formats, absent from `openapi/v1.json`.)
- **S-REPLAY-LIVE** click Replay on a real activity row with a real OBS browser source attached —
  Done-when: chat line, TTS and overlay alert all reappear and nothing touches
  currency/loyalty/reward state.
- **S-SR-PUBLIC-PAGE** the public `/sr/{token}` page has no client. The Song Requests screen builds
  that share link, but only the anonymous `PublicSongRequestController` API exists and nothing
  consumes it. Done-when: opening the share URL on the built bundle shows the live queue and lets a
  viewer request a track through the rate-limited public endpoint.
- **S-FAIRQUEUE-ENQUEUE** `FairQueue.EnqueueUnderLock` scans from the end and buries a fresh rank-1
  request; insert front-to-back before the first higher-rank item. Done-when: explicit-order tests
  incl. insert-after-dequeue.
- **S-I18N-NOCHANNELERROR** route the 18 `const val NoChannelError` literals through string
  resources (en+nl). They sit in the Alerts, Analytics, Automation, Bundles, Chat, ChatTriggers,
  Commands, Community, ViewerProfile, Discord, EventResponses, Games, Pipelines, Rewards, Roles,
  TtsQueue, VoiceTriggers and Widgets controllers (Widgets also wraps it as `NoChannelApiError`);
  the OBS and VTS controllers already use resource keys. Done-when: no `NoChannelError` string
  literal remains under `app/`, and a nl-locale test per controller asserts the Dutch string
  (pattern: `ObsControllerLocalizationTest`).
- **S-EGRESS-ALLOWLIST-CRUD** API + dashboard screen to manage `HttpEgressAllowlist` rows (host,
  enabled, methods, body/query/path clamps). Webhooks and custom data sources reject a host with no row
  (`CustomDataEgressFetcher.cs:62`), yet nothing lets an owner list, add, edit or remove one. Sandboxed
  scripts do NOT consult the list today: `http.fetch` takes any public https host through the pinned client
  (`ScriptHostBridge.cs:274`, `DependencyInjection.cs:697-701`); whether it should is the open owner question
  "fetch allowlist" (`spec/code-execution-sandbox.md:40`). Done-when:
  adding a host in the dashboard lets a webhook to it save, disabling or deleting it makes the same
  save fail, and a counted blast radius shows before delete.
- **S-EGRESS-GUARD-MULTICAST** `EgressAddressGuard` blocks private, loopback, link-local, CGNAT, ULA
  and unspecified ranges but not IPv4 multicast (224.0.0.0/4), the reserved 240.0.0.0/4 block or
  IPv6 multicast (ff00::/8). Done-when: table tests block 224.0.0.1, 239.255.255.250, 240.0.0.1 and
  ff02::1 (plain and IPv4-mapped) while a public address still passes.
- **S-CUSTOMDATA-PUSH** CustomData inbound webhook adapter, or hide the push option until it exists.
  `CustomDataSource.InboundWebhookEndpointId` is defined but nothing ever sets it, so a push source
  cannot receive anything. Done-when: a push source created in the dashboard gets an inbound
  endpoint and a POST to it ingests a `custom.<name>` event (or the option is hidden and tested as
  hidden).
- **S-CHATFILTER-HOLD** Hold = delete + moderation-queue entry, Flag = queue entry only; today
  `ChatFilterExecutionHandler` only logs both. Done-when: a Hold hit deletes the message and
  enqueues a pending `ModerationQueueItem`, a Flag hit enqueues without deleting, and the queue
  approve/deny works on both.
- **S-CHATFILTER-ENUM-WIRE** `ChatFilterDto.FilterType` and `.Action` serialize as raw ordinals
  because no `JsonStringEnumConverter` is registered; the dashboard only works by round-tripping
  opaque values. Done-when: GET returns the enum names as strings, and the Kotlin client,
  `openapi/v1.json` and `ApiContractTest` agree.
- **S-CHAT-SETTINGS-FIELDS** `ChatSettings` in `ChatApi.kt` has no `uniqueChatMode` (R9K),
  `nonModeratorChatDelay` or `nonModeratorChatDelayDuration`, which the backend `ChatSettingsDto`
  sends to Twitch. The whole-object PUT therefore resets them to off on every save. Done-when: the
  Chat screen toggles all three, and a test asserts that saving one setting round-trips the others
  unchanged.
- **S-MOD-PIPELINE-ACTIONS** add `warn`, `add_chat_filter_hit` and `apply_heat` pipeline actions,
  and route the Ban/Timeout actions through `IModerationService` (today `BanAction` and
  `TimeoutAction` call `IChatProvider` directly, so no mod-log row, escalation or audit is written).
  Done-when: the three actions are registered and appear in the block palette, and a pipeline
  Ban/Timeout writes the same mod-log row as the dashboard action.
- **S-FUNPRESET-BRACES** `FunCommandPresets` use `{{user.name}}` / `{{args.1}}`; verify what the
  single-brace resolver renders and fix the presets. Done-when: a test resolves every seeded
  fun-command preset through the real template resolver and no literal braces reach chat.
  UF·A3: No string resource in en or nl teaches the `{{...}}` form for a field resolved by
  `TemplateResolver` (today strings 2581, 2981, 3075, 5016-5028, 5206-5208, 5279-5283, 5295-5302,
  5316-5317, the Discord hint included); every such help string shows single braces, and a resource test
  fails on any `{{` in a template help string except `pipeline_send_webhook_event_type_help`, which says
  it is sent verbatim.
- **S-DASH-JAR-STALE** savings-jar numbers on the dashboard go stale. `JarContributedEvent`,
  `JarWithdrawnEvent` and the goal/membership events reach no hub push, and
  `EconomyController.subscribeToHub` reloads only on the `catalog` config change. Done-when: a
  contribution from a viewer or another member channel moves the open jar balance without a reload
  (hub push test + controller test).
- **S-SCHEDULE-TZ** the Schedule screen prints raw ISO strings (`startTime → endTime`), the segment
  read carries no timezone so the edit dialog cannot prefill it, and a new segment's timezone is a
  typed IANA string. Done-when: rows show start/end in the streamer's saved timezone, the timezone
  is picked from a zone list that defaults to it (S107's picker rides on this), the edit dialog
  seeds it, and an invalid zone cannot be submitted.
- **S-PINGDISCORD** `!pingdiscord` builtin, identity-link-gated (decided in
  `2026-06-16-discord-notifications.md`): it resolves the chatter's linked Discord identity
  (`UserIdentity`, provider `discord`) and toggles their opt-in for the channel's notify role; a
  chatter with no linked Discord identity gets a reply pointing at the link flow. Done-when: with a
  linked identity the command opts the member in and out (opt-in row and role call asserted),
  without one it replies with the link hint and changes nothing.
- **S-WALLET-UNIQUE** `CurrencyAccount` has only a plain index on `(BroadcasterId, ViewerUserId)`;
  "one wallet per viewer per channel" rests on the service's lazy create, so a race can mint two.
  Done-when: the index is UNIQUE in BOTH migration sets, the migration first merges existing
  duplicate wallets (balances and lifetime totals summed into the oldest row, ledger references
  repointed) and is proven on a populated database, and a concurrent-create test yields one wallet.
- **S-AGECONSENT-NULLABLE** `ViewerAgeConsent.ConsentRecordId` is a non-nullable `Guid`, so
  `AgeConsentService` writes `Guid.Empty` as a sentinel for an inferred adult (no consent record
  backs an inference; economy K.8 says `Null`). Done-when: the column is `Guid?` in BOTH migration
  sets (existing `Guid.Empty` rows become null), no `Guid.Empty` sentinel remains, and the
  inferred-consent tests assert null.
- **S-KEK-LINUX-MAC** `OsSecureStoreKeyVault` custodies the root KEK in the OS store only on Windows
  (DPAPI); macOS Keychain and Linux libsecret are still missing, so non-Windows self-host derives
  the KEK from `Encryption:Key` via HKDF. Done-when: on macOS and on Linux with a keystore the KEK
  is generated once and persisted in Keychain / libsecret (no plaintext key file), the
  encrypted-file/env fallback stays only for headless hosts, and each backend has a round-trip test.
- **S-AUTOMATION-EXPOSE-ALL** `AutomationEventRegistry` builds the public event catalog only from
  hand-written `IAutomationEventDescriptor`s, but `spec/automation-api.md` D6 exposes every
  `DomainEventBase` subtype under its wire name from attributes, with a PII projection. Done-when:
  an event type with no descriptor reaches a subscribed automation session under its default wire
  name, `[NotExposed]` members never leave the server, and `[Pii]` members are stripped for Public
  sessions (tests).
- **S-WATCHLIST-PERSIST** the multi-chat watch list is not persisted: `MultiChatController` takes a
  `WatchListStore` that defaults to `NoOp`, and no real store is wired in the app graph. Done-when:
  the watched channel set survives an app restart (an expect/actual store wired in `AppGraph`), with
  a test that a second controller instance restores it.
- **S-UF-V1a** [fix] Cooldown notices say when the command is ready, built-in cooldowns are per viewer, and each viewer gets one notice per window.
  Build: the notice reads "!sr is ready again in 8 s" from `GetRemainingCooldown`, with a
  `{cooldown.remaining}` variable on the re-wordable `SystemReplies.Cooldown` slot. Built-in cooldowns
  key per viewer by default, with a "channel-wide" option per built-in (carried in
  `ChannelBuiltinCommand.OverridesJson`, so no migration). A viewer gets at most one cooldown notice per
  command per cooldown window; later attempts in that window are ignored silently. Count notices for
  the "per 1,000 messages" measure.
  Files: `ChatMessageHandler.cs`, `CooldownManager.cs`, `BuiltinOverridesJson.cs`, the built-in editor (scope
  option). Depends: none.
  Done-when: a handler test has viewer A run `!sr` and viewer B run it 1 s later with a 10 s cooldown. B is
  admitted. A's second try within the window gets "ready again in 9 s", and A's third try gets no reply.
- **S-UF-V1b** [fix] A permission notice names the lowest role that can use the command.
  Build: "!so is for moderators." The lowest admitted role name fills a `{command.role}` variable on
  `SystemReplies.PermissionDenied`. Every tone keeps the role (the sassy line may decorate, never drop it).
  Files: `ChatMessageHandler.cs`, `ToneTemplateCatalog.Core.cs`. Depends: none.
  Done-when: a test resolves the denial for a moderator-floor command in every tone, and each line contains
  the role name.
- **S-UF-V1c** [fix] Bet-range and insufficient-funds replies state the numbers and the currency name.
  Build: "Bets go from 10 to 500 Nomz. You have 240." and "That costs 300 Nomz. You have 240." The
  refusal carries min, max, cost and balance as structured data. The builtin composes them with
  `CurrencyConfig.CurrencyName`/`CurrencyNamePlural`.
  Files: `GameService.cs`, `CurrencyAccountService.cs`, `GameBuiltins.cs`, `ToneTemplateCatalog.Core.cs`.
  Depends: none.
  Done-when: a test with a 10-500 range, a 240 balance and currency "Nomz" yields both exact lines, and a
  tone override keeps the numbers.
- **S-UF-V4a** [fix] A redemption that cannot run is cancelled on Twitch, refunded, answered in chat and raised to the streamer.
  Build: when a redemption cannot run (reward off locally, pipeline failure, missing integration), set the
  redemption to CANCELED via Helix, so the points return. Reply "Your points are back. That reward isn't
  working right now." (a re-wordable slot), and raise an attention item naming the reward
  (`IActionRequiredSource`). Twitch allows the cancel only for rewards created by the bot's client id
  and still UNFULFILLED. Otherwise, reply that the reward isn't working, without claiming a refund, and
  say so in the attention item.
  Files: `RewardRedeemedHandler.cs`, the pipeline-result path for reward runs, an attention source.
  Depends: none.
  Done-when: a test redeems a disabled managed reward and asserts the CANCELED status call, the chat
  line and an attention item with the reward's title. A non-cancellable redemption gets the no-refund
  wording.
  Store half (usability-inventory Part C #15, V-B3.1): catalog purchase runs the item pipeline (refund on
  failure)"), append: "A failed or unrunnable store purchase is refunded through `RefundPurchaseAsync`,
  the buyer gets 'Your points are back. That reward isn't working right now.' in chat, and the streamer
  gets an attention item naming the item (spec V4).
- **S-UF-I1a** [fix] Each integration card names its own provider in one of three states.
  Today every card that needs a reconnect says "Reconnect needed — Kick chat is paused"
  (`strings.xml:1359`, picked at `IntegrationsScreen.kt:678` for Spotify, YouTube, Discord and Kick alike),
  and the Bot account row passes `needsReauth = false` (`IntegrationsScreen.kt:552`), so a dead bot token
  never shows. Build: `IntegrationCard` shows exactly **Connected** / **Needs reconnecting** / **Not
  connected**, with a per-provider consequence line under "Needs reconnecting" (Spotify: "Song requests and
  now playing are paused"; Discord: "Go-live posts are paused"; YouTube; Kick; Kick bot; Bot account), all as
  en+nl resource keys; the Bot account row reads the bot connection's real reauth/decrypt state.
  Files: `app/feature/integrations/ui/IntegrationsScreen.kt`, `IntegrationsController.kt`, `strings.xml` (en,
  nl). Depends: none.
  Done-when: a jvmTest mounts the card per provider with `needsReauth = true` and asserts that provider's own
  consequence line (and that no card but Kick mentions Kick); a Bot-account connection in `needs_reauth` renders
  "Needs reconnecting" with a Reconnect action.
- **S-UF-I1b** [fix] A provider 401 or 403 shows "Needs reconnecting" at once and raises a severity-correct
  attention item.
  Today the card and `IntegrationStatusDto.NeedsReauth` read only the persisted status
  (`IntegrationOAuthService.cs:437-438`), which flips after 3 strikes (`IntegrationTokenVault.cs:38`); the live
  `auth.needs_reauth` / `forbidden` capability (`SpotifyMusicProvider.cs:69,1780,1800`) never reaches the card, and
  the attention item is always critical (`DeadIntegrationTokenSource.cs:76`). Build: `NeedsReauth` is also true
  while the provider's live capability store holds `auth.needs_reauth` or a non-premium `forbidden` (the vault keeps
  its 3-strike persistence and refresh backoff unchanged); the attention source emits an item for that live
  signal too; severity is `critical` when the channel is live (`Channel.IsLive`) and a live feature depends on the
  provider (song requests on the music provider, TTS on a BYOK TTS provider), `info` otherwise.
  Files: `Infrastructure/Integrations/IntegrationOAuthService.cs`, `Infrastructure/Notifications/Sources/
  DeadIntegrationTokenSource.cs` (or a sibling live-auth source), capability store. Depends: none.
  Done-when: an Infrastructure test drives one Spotify 401 through `ClassifyAuthAsync` and asserts the next
  integrations status read has `NeedsReauth = true` and the attention list holds one item, `critical` with the
  channel live and SR on, `info` with the channel offline; a following 2xx clears both.
- **S-UF-I2a** [fix] Chat lines for a broken provider or a disabled game answer the viewer, and owner text goes
  to the attention inbox.
  Today `!sr` with a dead Spotify token answers every viewer "Couldn't queue … the music connection needs to be
  reconnected." (`ToneTemplateCatalog.Music.cs:124`) and a disabled game says "turn it on under Economy → Games"
  (`ToneTemplateCatalog.Core.cs:288`). Build: extend the existing role split (`SongRequestBuiltin.cs:335-361`) to
  `AuthFailed`, `Forbidden`, `MissingScope` and `ProviderUnavailable`: a viewer sees "Song requests are paused right
  now." (new slot, all five tones), broadcaster and mods keep the owner line; `Game.NotEnabled` splits the same way,
  viewer line "!{game.name} isn't on in this channel.", owner setup copy only for broadcaster/mods. The owner side
  is the S-UF-I1b attention item, not a chat line.
  Files: `Application/Commands/Builtin/Personality/ToneTemplateCatalog.Music.cs`, `ToneTemplateCatalog.Core.cs`,
  `BuiltinResponseSlots.Music.cs`/`.Core.cs`, `Infrastructure/Commands/Builtins/SongRequestBuiltin.cs`,
  `GameBuiltins.cs`. Depends: none (S-UF-I1b supplies the inbox side).
  Done-when: builtin tests send `!sr` with a 401-ing provider as a viewer and as the broadcaster and assert the
  outgoing chat text for each (viewer text contains no "connect"/"dashboard"/"Economy"); a catalogue drift test fails
  when any viewer-facing slot's template names a dashboard page.
- **S-UF-T1a** [fix] An empty result from any TTS provider falls through to the next one before TTS gives up.
  Today Edge, Azure and ElevenLabs catch their own errors and return empty audio (`EdgeTtsProvider.cs:111-115`,
  `AzureTtsProvider.cs:98-102`, `ElevenLabsTtsProvider.cs:105-109`). `TtsService.cs:76-99` falls back to Edge
  only on an exception, and the BYOK leg (`TtsDispatchService.cs:588-617`) has no fallback. So one provider
  hiccup ends as "The TTS provider returned no audio." (`:636-644`). Build: zero-length audio counts as a
  failure. The chain is the channel's BYOK provider, then the free Edge voices (same voice id first, then the
  catalogue's first Edge voice, as `RetryThenFirstAvailableAsync` already does), then failure. Only after the
  whole chain fails does the request take the failure path (S-UF-T6a once that lands, today's
  `TtsUtteranceRejectedEvent` until then). The usage row records the provider that actually spoke.
  Files: `Infrastructure/Tts/TtsService.cs`, `TtsDispatchService.cs`, the three providers.
  Depends: none.
  Done-when: (1) a BYOK fake returning empty audio still produces a `TtsUtteranceDispatchedEvent` with
  `Provider = "edge"` and non-empty audio; (2) an Edge fake returning empty audio on the shared plane falls to
  the catalogue's first Edge voice; (3) all providers empty produces exactly one rejected event with reason
  `synthesis_failed` and no usage row.
- **S-UF-T1b** 🔒 [fix] New channels speak through server-synthesized free Edge voices, so the first TTS line
  is heard in OBS.
  Owner decision: reverse `spec/tts.md` §9 decision 3 (`:644`, "zero server cost", `Mode=client_edge`) and
  make the existing server plane (today's `self_host` value, Edge through the shared `ITtsService`) the
  new-channel default. Decide too whether existing channels still on `client_edge` are moved (a data migration
  in BOTH migration sets) or only told. The owner's 2026-10-02 decision in S-SDK-WIDGET-DELIVERY already
  settles the playback half: one Audio Source page, with volume set on the bot's side so balance stays the same
  across streaming PCs. `speechSynthesis` can meet neither part. OBS does not capture it, and voice and volume
  depend on each PC's OS voices (`OverlaySdkController.cs:331-334`). A browser page also cannot synthesize Edge
  audio itself, because the Edge endpoint needs a forged `Origin` header and cookie (`EdgeTtsProvider.cs:95-105`,
  inf.). So the only thing left to decide is cost: server CPU and bandwidth per line on SaaS. Note that tenants
  can already opt into the server plane free of charge (`TtsConfigDtos.cs:66`). If the owner refuses,
  S-UF-T2c's setup must say plainly "TTS is off-air until a provider is set", and the Test line must not report
  success on `client_edge`.
  Today: `TtsConfig.cs:40` defaults to `client_edge`. `GetConfigAsync` and reset materialize from
  `new TtsConfig()`, so every fresh channel is silent on stream while the dashboard says "Test sent to the
  overlay" (`strings.xml:809`).
  Build: the entity default and the defaults endpoint (`TtsConfigController.cs:218-232`) become the server
  plane. `client_edge` stays selectable only as an explicit choice. The tts.md decision text is rewritten to
  match.
  Files: `Domain/Tts/Entities/TtsConfig.cs`, `Infrastructure/Tts/TtsConfigService.cs`, `spec/tts.md` §9.3;
  migrations only if existing rows move.
  Depends: S-UF-T1a, S-SDK-WIDGET-DELIVERY.
  Done-when: a dispatch on a channel with no `TtsConfig` row emits one raw `TtsSpeak` with a non-null
  `audioUrl` to the audio page (an `OverlayAudioRoutingTests`-style assertion), and on the deployed site a
  fresh channel that adds the TTS source and presses **Send a test line** is heard in an OBS recording.
- **S-UF-T1c** [fix] The voice-source control says where voices come from and that the browser voice never
  reaches the stream.
  Today the control is "Dispatch mode" with "Client (Edge)", "Bring your own key" and "Self-host"
  (`strings.xml:838-841`, `TtsScreen.kt:315-317`). Nothing says `client_edge` is inaudible on stream, and
  "Self-host" is the free shared server plane, not a self-hosted server.
  Build: the label becomes **Where voices come from**. Options: "Free voices (recommended)" (the server plane),
  "My own provider key" (`byok`) and "Browser voice (you hear it, your stream doesn't)" (`client_edge`). Same
  strings in en and nl. The BYOK description (`strings.xml:867`) drops "Set the dispatch mode to BYOK". The
  control moves to the Provider tab in S-UF-T11b.
  Files: `feature/tts/ui/TtsScreen.kt`, `strings.xml` (en + nl).
  Depends: none.
  Done-when: a jvmTest renders the General tab and asserts the three option labels, and that picking the
  browser voice keeps its "your stream doesn't" wording visible beside the control after save.
- **S-UF-T6a** [fix] Every TTS request carries its source to the end, and a paid request that is not played is
  refunded.
  Build:
  - `TtsSpeakRequest` and the queue entry gain a source: redemption id, cheer, command (with any currency
    spend id), bot line, or test. Both migration sets.
  - Played: the redemption is marked FULFILLED.
  - Rejected by a moderator, expired, or not spoken (the whole provider chain failed, or no source; see
    S-UF-T9b): the redemption is CANCELED or the currency spend reversed.
  - Each failure raises an attention item through a new `IActionRequiredSource`.
  - Rewards bound to TTS must keep redemptions in the queue, because a skip-queue redemption cannot be
    refunded.
  Files: `ITtsDispatchService.cs`, `TtsDispatchService.cs`, `TtsApprovalQueueEntry` (+ config, both migration
  sets), `PlayTtsAction.cs`, `RewardRedeemedHandler.cs` (thread the redemption id), Notifications/Sources.
  Depends: none (S-UF-V4's refund work reuses the same `SetRedemptionStatusAsync` path; if V4 lands first,
  build on it).
  Done-when: a redemption-sourced line that is rejected calls CANCELED on that redemption id once (fake
  Helix); a played one calls FULFILLED; a failed one cancels and adds one inbox item; a second reject of the
  same entry refunds nothing.
- **S-UF-F1a** [fix] Signing in no longer installs the bot; only an explicit "run the bot" choice does.
  Today `AuthService.cs:375-397` creates and onboards a channel for any login without an owned channel.
  On SaaS that includes a D4 viewer signing in for their free account. Change: sign-in creates the `User`
  and the session only. A new `POST /api/v1/me/channel` ("Run the bot on my channel") creates the channel,
  onboards it and raises `ChannelOnboardedEvent`. `GET /api/v1/me/first-run` returns the recommended intent:
  `moderate` when the user moderates at least one channel that has the bot installed, else `run_bot`, plus
  that channel list. SelfHostLite keeps today's single-channel rule for a second login. An existing owner's
  re-login is unchanged.
  Files: server `Identity/AuthService.cs`, a new `MeController` action, the `ChannelOnboardingWriter`
  call site, `openapi/v1.json`, Kotlin `ChannelsApi`.
  Depends: none.
  Done-when: an Infrastructure test signs in a new SaaS user who owns no channel: no `Channel` row exists and
  no `ChannelOnboardedEvent` is published. After `POST /me/channel`, one onboarded channel exists and the
  event fired once. A second user who moderates an onboarded channel gets `recommended = moderate`.
- **S-UF-F1b** [fix] First sign-in shows "What do you want to do?" with three cards, one pre-selected.
  After the first sign-in, a user with no channel and no recorded choice sees one screen with three cards.
  **Run the bot on my channel** calls `POST /me/channel`. **Moderate a channel** lists the user's moderated
  channels that have the bot installed. **Manage my viewer profile** opens the viewer home (F8). The card
  from `GET /me/first-run` is pre-selected. The profile menu gains "Add the bot to my channel" for a user
  without a channel.
  Files: app `App.kt` (destination gate), new `feature/firstrun/`, `feature/shell/ui/ShellScreen.kt`
  `ProfileBlock`, strings (en+nl).
  Depends: S-UF-F1a.
  Done-when: a jvmTest drives the controller. With a moderated bot channel, Moderate is pre-selected.
  Choosing Viewer issues no `POST /me/channel` and lands on the participant shell. Choosing Run issues it
  exactly once and lands on the management shell.
- **S-UF-F5a** [fix] Music commands are seeded disabled, and the first music connection offers to turn them on.
  `DefaultCommandsSeeder.cs:90` seeds `!sr`, `!skip`, `!queue`, `!volume` and `!song` as `IsEnabled = true`
  with no provider connected. Seed them disabled for new channels. Leave existing channels' rows alone. When
  the channel's first music provider connects (`IntegrationConnectedEvent` for spotify or youtube), raise a
  one-time inbox item or prompt: "Turn on song requests? Viewers can use !sr in chat." with **Turn on**,
  which enables the five, and **Not now**.
  Files: server `Content/Commands/DefaultCommandsSeeder.cs`, a handler under Music or Notifications, a
  `turn-on-song-requests` action, app Integrations or Home prompt, strings.
  Depends: none.
  Done-when: an Infrastructure test seeds a new channel and sees all five disabled. A first
  `IntegrationConnectedEvent(spotify)` produces exactly one prompt item; a second connection produces none.
  **Turn on** enables exactly those five commands.
- **S-UF-F5c** [fix] Seeded fun presets carry a "Starter" tag and do not count as the user's own commands.
  Rows with a non-null `presetKey` show a "Starter" chip in the Commands list. The Get started "first custom
  command" step (H2) and every "has commands" test count only rows with `presetKey == null` and
  `isPlatform == false`.
  Files: app `feature/commands/ui/CommandsScreen.kt`, `feature/home/state/HomeController.kt`, strings.
  Depends: none.
  Done-when: a jvmTest shows a channel holding only seeded presets reports the custom-command step not done,
  and the list renders the Starter chip on exactly those rows.
- **S-UF-V6a** [fix] Every outgoing chat line has a priority and a deadline; stale and low-priority lines are dropped.
  Build: `EnqueueAsync` takes a priority and the trigger time. 1 = direct command replies (always sent).
  2 = thank-yous and alerts. 3 = notices (cooldown, permission, typo hint), dropped when the oldest
  queued line is more than 5 s old. 4 = timers and announcements, skipped for this cycle when the queue
  is backed up. The highest priority is served first. Any line unsent 30 s after its trigger is dropped
  and counted (the "replies sent more than 30 s late" measure must read 0).
  Files: `IOutboundChatShaper.cs` (`IChatSendQueue`), `TokenBucketChatSendQueue.cs`, `ChatPlatformRouter.cs`,
  each caller's priority tag. Depends: none.
  Done-when: a queue test with a drained bucket enqueues a timer line, a notice and a command reply. The
  reply is sent first, the notice is dropped once the head is 5 s old, the timer is skipped, and a
  line still queued at 30 s is dropped with a counter increment (fake time provider).
- **S-UF-V6b** 🔒 [fix] Thank-yous merge during a burst: "Thanks for the follows, Ana, Bo and 6 others!"
  Owner decision: may the bot replace the streamer's own authored follow/sub thank-you lines with one
  merged bot-composed line while the queue is backed up? The owner rule that tone never rewrites what a
  streamer authored (S069) says no as it stands. Build once decided: priority-2 lines of the same event
  type queued at once merge into one re-wordable burst slot per event type.
  Files: `TokenBucketChatSendQueue.cs` (coalesce by event type), event-response executor, tone catalogue.
  Depends: S-UF-V6a.
  Done-when: a test queues 8 follow thank-yous behind a full bucket and exactly one merged line naming 2
  viewers and "6 others" is sent.
- **S-UF-T6b** [fix] Each TTS outcome sends the viewer one chat line, and only one.
  Build, at priority 1 (V6):
  - Queued: "@Ana your message is waiting for a moderator (2 ahead)." Points are held.
  - Played: no line.
  - Rejected: "@Ana a moderator didn't approve your message. Your 500 points are back."
  - Expired: "@Ana your message wasn't reviewed in time. Your 500 points are back."
  - Couldn't be spoken: "@Ana TTS isn't working right now. Your 500 points are back."
  - Free `!tts` too long: "@Ana that's 240 characters; the limit is 200."

  The specific `play_tts` reason replaces the generic "hit a snag" notice for TTS commands
  (`ChatMessageHandler.cs:799-810`). Lines go through the builtin reply composer so they are rewordable and
  translated.
  Files: a TTS outcome notifier (event handlers on the TTS events), `ChatMessageHandler.cs`,
  `BuiltinResponseSlots`.
  Depends: S-UF-T6a (S-UF-V6 adds the priority-1 lane; until it lands the line uses the normal send).
  Done-when: per-outcome tests assert the exact outgoing chat text and the absence of a line on played; the
  queued line's position matches the entry's place among pending entries (oldest first).
- **S-UF-T4a** [fix] **Silence TTS** stops the current line, holds the queue and pauses new requests, and
  moderators can use it.
  Today pause lives only in the overlay page's memory (`OverlaySdkController.cs:222-275`), new requests keep
  dispatching, `speechSynthesis` is never cancelled, the app hides the buttons from moderators
  (`TtsScreen.kt:248,1954`), and turning TTS off leaves the queue playing.
  Build: a server-owned per-channel silence state. One press stops the line playing now, holds what is queued,
  and parks new requests in arrival order instead of dispatching them. The state lasts 10 minutes by default,
  or "Until I resume". The header control reads "TTS paused · 9:41 · Resume". Resume plays the held lines in
  order. Lines held longer than 10 minutes take S-UF-T6a's refund path. Skip, Pause, Resume and Clear also
  cancel or pause `speechSynthesis` on the browser voice. Turning TTS off clears what is queued. The TTS page
  header carries the control and the Skip/Pause/Clear buttons at the Moderator floor (`queueManage`). Held
  lines must survive a restart to be refunded, so they persist in BOTH migration sets.
  Files: `Infrastructure/Tts/TtsDispatchService.cs`, `TtsConfigController.cs` (playback routes),
  `OverlaySdkController.cs` (`ttsQueueControl`), `TtsScreen.kt`, `TtsController.kt`.
  Depends: S-UF-T6a (refund of lines held past 10 min).
  Done-when:
  - a dispatch during silence produces no `TtsSpeak` and a held row; resume emits the held lines in order;
  - a line held 10 min 1 s is refunded (clock-driven test);
  - an SDK test shows `pause` calls `speechSynthesis.pause()` and `clear` calls `cancel()`;
  - a jvmTest shows a Moderator with enabled Skip/Pause/Clear.
- **S-UF-T4b** [fix] **Clear queue** refunds every paid line in it, after a confirm that names the count.
  Today Clear drops the overlay's queue and refunds nothing (`OverlaySdkController.cs:254-258`).
  Build: the confirm reads "Clear 6 messages and refund 3,000 points?" (X2 step 3; the button names the act).
  The server counts the paid held and queued lines, refunds each through S-UF-T6a and tells each viewer.
  Files: `TtsConfigController.cs` (clear), `TtsScreen.kt`.
  Depends: S-UF-T4a, S-UF-T6a.
  Done-when: with three paid lines and one free line held, the preview count says 3 refunds; confirming cancels
  three redemptions (fake Helix asserted) and leaves an empty queue.
- **S-UF-T9a** [fix] The caption shows the speaker's display name and appears when the audio page actually
  starts the line.
  Build: the audio page reports cue started and cue ended to the hub (a new `OverlayHub` method; each `TtsSpeak`
  carries a cue id). The server relays `tts_speak` to caption widgets on start instead of on dispatch, and the
  caption hides on end (estimate fallback kept). The payload's `user` is the display name, with the platform id
  in a separate field. This signal also feeds S-UF-T2a.
  Files: `OverlayHub.cs`, `OverlaySdkController.cs`, `TtsSpeakBroadcastHandler.cs`, `WidgetEventPayloads.cs`,
  `tts_caption.vue`, regenerated SDK types.
  Depends: S-SDK-WIDGET-DELIVERY.
  Done-when: with two lines queued, the caption widget receives the second line's `tts_speak` only after the
  audio page reports the first ended (hub test), and the payload's `user` equals the requester's display name.
- **S-UF-T9b** [fix] With no TTS source connected while live, a request waits up to 2 minutes, then is
  refunded.
  Build: when `GetAudioTarget` is null and the channel is live, the line is parked, not marked dispatched. It
  plays the moment an audio page connects, and after 2 minutes it takes S-UF-T6a's refund with the "TTS isn't
  working right now" line. While offline, a request with no page fails at once through the same path (the spec
  is silent on offline; see Notes).
  Files: `TtsDispatchService.cs`, `WidgetNotifier.cs` (return whether a page received it),
  `OverlayPresenceRegistry.cs` (connect hook).
  Depends: S-UF-T6a, S-SDK-WIDGET-DELIVERY.
  Done-when: a live channel with no page holds the line, and a page joining at 90 s receives it; at 2 min 1 s
  the redemption is CANCELED; no `TtsUtteranceDispatchedEvent` is emitted for an undelivered line.
- **S-UF-T9c** [fix] A vanished saved voice falls back to the channel voice with one notice; roulette stays in
  the channel's language; `!voice clear` always works.
  Build:
  - A viewer voice no longer in the catalogue resolves to the channel voice. The viewer is told once: "@Ana
    your voice Ana (Neural) isn't available anymore, so I used the channel voice. Pick a new one with !voice."
    The stale row is cleared after the notice.
  - `!voice roulette` picks only voices in the channel's language (the channel voice's locale when
    `Channel.Language` is unset).
  - `!voice clear` bypasses the self-service gate.
  Files: `TtsDispatchService.cs` (`ResolveVoiceAsync`), `VoiceBuiltin.cs`, `TtsConfigService.cs`.
  Depends: none.
  Done-when: a dispatch for a viewer whose voice id was removed speaks in the channel voice and sends the
  notice once across two dispatches; roulette on an `nl` channel never returns an `en-*` voice (seeded
  picker); clear succeeds with self-service off.
- **S-UF-V2a** [fix] `!sr` reports position and wait, `!queue` leads with yours, and the lost-at-provider line is a reply slot.
  Build: Added reads "Added Song by Artist. You're #4, about 12 min." The wait is the sum of the
  remaining track lengths ahead (the `WaitBefore` already used by `!queue`), exposed as
  `{queue.position}` and `{queue.wait}` on `SongRequest.Added`. `!queue` from a requester shows "You're
  #4 with Song", then the next 3 songs. The lost-at-provider notice becomes a toned, re-wordable slot
  sent on the request's own platform.
  Files: `SongRequestBuiltin.cs`, `MusicBuiltins.cs`, `SongRequestLostAtProviderChatNotice.cs`, tone catalogue.
  Depends: none.
  Done-when: with 3 queued tracks of 4 min and a playing track with 0:30 left, a new request replies "#4,
  about 13 min". `!queue` returns own position plus exactly 3 next entries. An overridden lost-at-provider
  line is what reaches chat.
- **S-UF-V3a** [fix] Giveaway open, batched entries and rejections are announced in chat.
  Build: on open: "Giveaway open: type !join to enter. Followers only, ends in 5 min." (eligibility and
  end time from the giveaway). Every 15 s, while new entries arrive: "Ana, Bo and 12 others are in. 54
  entries so far." A rejected entry gets one reply per viewer per giveaway naming the reason ("You need
  to follow to enter."). Each line is a re-wordable slot. This discharges the "open announced" half of
  usability-inventory Part C #21 (V-B3).
  Files: a giveaway chat announcer (open, entry batch, rejection), `GiveawayKeywordListener.cs`, tone catalogue.
  Depends: S-UF-V6a.
  Done-when: a test opens a giveaway (one open line), feeds 14 entries in 15 s (one batch line with the right
  count and names), and has the same ineligible viewer try twice (one rejection line).
- **S-UF-V3b** [fix] A giveaway claim and a missed claim are announced, and a missed claim redraws.
  Build: "Ana claimed the prize. Congrats!" No claim in time gives "No claim from Ana. Drawing again…",
  then the next draw.
  Files: `GiveawayKeywordListener.cs`, `GiveawayClaimSweepWorker.cs`, announcer.
  Depends: S065-remaining (draw announcement).
  Done-when: a test claims within the window (one claim line, status Claimed). With the window expired, the
  sweep posts the no-claim line and records a new drawn winner.
- **S-UF-V3c** [fix] Live games report joins, a full round and the result in chat as well as on the overlay.
  Build: joins use the same 15 s batch line. A full round says "The heist is full (12/12)" once. The
  result gets one summary line in chat. `LiveGameTransition` gains a chat line.
  Files: `LiveGameTypes.cs`, `LiveGameEngine.cs`, each live game's resolve step, tone catalogue.
  Depends: S-UF-V6a.
  Done-when: a heist test with `MaxPlayers=12` sends exactly one "full (12/12)" line when the 13th joins,
  one batch join line per 15 s, and one result line on resolve.
- **S-UF-V5a** [fix] `!leaderboard` shows display names, and every amount line names the currency.
  Build: the ranking resolves each account's viewer display name (falls back to the login, never an
  id). Game won/lost/balance lines and other amount lines use `CurrencyName`/`CurrencyNamePlural`: "You
  won 120 Nomz on slots. Balance: 1,360 Nomz." Amounts use thousands separators.
  Files: `EconomyLeaderboardService.cs`, `LeaderboardBuiltin.cs`, `GameBuiltins.cs`, tone catalogue.
  Depends: none.
  Done-when: a test with seeded viewers asserts the leaderboard line contains their display names and no
  numeric id. A slots win line ends "Balance: 1,360 Nomz" with currency "Nomz".
- **S-UF-V5c** [fix] `!commands` lists the 8 most-used commands the viewer can run.
  Build: rank by recorded usage (command usage counts), filtered by the caller's permission, top 8, in
  one line.
  Files: `CommandsBuiltin.cs`, command-usage read. Depends: none.
  Done-when: a test with 12 runnable commands and usage counts returns exactly the top 8 in usage order,
  excluding a moderator-only command for a viewer.
- **S-UF-V7a** [fix] A viewer is greeted at most once per stream.
  Build: one per-stream greeting gate shared by first-time chatter, returning chatter and session-first
  message (cleared on `stream.online`, like `SessionChatters`). The first greeting that fires wins.
  Watch-streak milestones are celebrations, not greetings, and are not gated.
  Files: `EngagementTriggerSources.cs`, `ChatMessageHandler.cs` (session-first), a shared greeting gate.
  Depends: none.
  Done-when: a test with all three greetings enabled has a first-time viewer chat twice in one stream.
  Exactly one greeting line is sent; after a new `stream.online`, the returning greeting fires once.
- **S-UF-V7b** [fix] Every recognition line has toned default copy and can be re-worded per tone.
  Build: `EventResponseToneCatalog` entries for `engagement.first_time_chatter`, `returning_chatter`,
  `session_first_message`, `watch_streak` and `modiversary`, in every tone, with their variables. An
  enabled recognition response with no authored text then types a line (F5's pre-checked defaults need
  this).
  Files: `EventResponseToneCatalog.cs`. Depends: none.
  Done-when: a test enables each engagement type with no message and asserts the executor sends the toned
  default with the viewer's name resolved, and an override per tone replaces it.
- **S-UF-O3a** [fix] The channel-wide rotate rotates every OBS source with the same 15-minute grace and says how many.
  "Rotate token" rotates each widget's own token through the existing grace path and the channel token with
  a new `PreviousOverlayToken` + expiry on `Channel` (BOTH migration sets), so every old URL keeps working
  for 15 minutes. The confirm states the real count: "Rotate the token for all 7 OBS sources?" with the
  button **Rotate token**; the response returns each source's old URL, new URL and the expiry.
  Files: `ChannelService.cs`, `WidgetService.cs`, `Channel` entity + both migration sets, overlay token
  resolution (`WidgetService.cs:1612-1655`, `LiveOpsController.cs:449`), `ChannelsController.cs`,
  `openapi/v1.json`, `WidgetsController.kt`, `WidgetsScreen.kt`, strings.
  Depends: none.
  Done-when: Infrastructure tests assert that after a rotate the old channel token and every old widget token
  still resolve until expiry and stop resolving after it (fake clock), and the new ones resolve; a jvmTest
  asserts the confirm names the enabled-source count.
- **S-UF-O4b** [fix] A row's **Test** fires only at that widget, never every overlay on stream.
  `WidgetTestEventRequest` gains an optional `WidgetId`; when set, the sample goes to that widget only and
  the reach line describes that widget; the row and the editor preview always send it. The untargeted form
  stays for the channel-level test.
  Files: `WidgetTestEventController.cs`, `WidgetAlertDispatch`, `openapi/v1.json`, `WidgetsApi.kt`,
  `WidgetsController.kt`.
  Depends: none.
  Done-when: a hub-capture Api test with two follow-subscribed widgets fires a targeted test and asserts one
  delivery to the target and none to the other; the untargeted call still reaches both.
- **S-UF-O4c** [fix] Test alerts use a real recent viewer's name.
  Today samples render "TestFollower", "TestRaider" on stream. Fill the sample's display name, login and
  avatar from the channel's most recent chatter (fallback "Ana"), keeping the typed sample shape.
  Files: `WidgetTestSamples.cs`, `WidgetTestEventController.cs`.
  Depends: S-UF-O4b.
  Done-when: an Api test with a seeded recent chatter asserts the follow sample carries that viewer's display
  name and avatar; with no chatters it carries "Ana".
- **S-UF-O5a** [fix] Stopping the stream confirms with the real reach; stopping a recording waits 5 s with Undo.
  **Stop streaming** opens "End the stream for 1,234 viewers?" (live viewer count) with the button **End
  stream** (X2 step 3). **Stop recording** uses the 5-second delayed send with a visible countdown and Undo
  (X2 step 2); nothing is sent to OBS until the countdown ends.
  Files: `ObsScreen.kt`, `feature/obs/state/ObsController.kt`, strings.
  Depends: S-UF-X1, S-UF-X2 (delayed-send component).
  Done-when: jvmTests: Stop streaming calls nothing until **End stream** is pressed and the dialog shows the
  viewer count; Stop recording followed by Undo within 5 s sends no request, and without Undo sends exactly one
  after 5 s (fake clock).
- **S-UF-O5c** [fix] Rotating the bridge token keeps the old one working for 15 minutes.
  `ObsConnection` gains `PreviousBridgeToken` + expiry (BOTH migration sets); the bridge accepts either until
  expiry; the button confirms (X2 step 3, "Rotate the bridge token? The bridge reconnects within 15
  minutes.") and the page shows the countdown until the bridge reconnects with the new token.
  Files: `ObsConnectionService.cs`, `ObsConnection` + both migration sets, bridge auth, `ObsScreen.kt`.
  Depends: none.
  Done-when: Infrastructure tests (fake clock) assert the old token authenticates the bridge before expiry and
  is refused after, and the new one works throughout.
- **S-UF-F7a** [fix] Sign-in and reconnect errors name the real cause and offer the action that fixes it.
  An unreachable server says "Can't reach your bot server at <host>." with Retry, plus **Use a different
  server** on desktop, and always Sign out. An encryption-key change (`decrypt_failed`) says "Your bot's saved
  sign-ins were reset because its encryption key changed." with Reconnect Twitch, then Reconnect bot account.
  A device-code expiry says "That code expired after 10 minutes." with **Get a new code**; a denial says
  "Twitch says access was declined." with **Try again**.
  Files: app `feature/connect/**`, `feature/shell/ui/ReauthDialog.kt`, strings (en+nl).
  Depends: none.
  Done-when: a jvmTest drives `ConnectController` with an unreachable status, a `decrypt_failed` health read,
  an expired poll and a denied poll, and asserts each `ConnectError` variant and its rendered copy and action.
  No path still renders `connect_error_auth` for an unreachable host.
- **S-UF-F7b** [fix] A returning user with a valid session sees the shell at once, with skeleton content.
  Remove the `SPLASH_HOLD_MS` minimum (`App.kt:59,111`). While the session restores and `/effective/me`
  resolves, render the shell frame with skeleton content in place of `SplashScreen`. The fail-closed rule
  stays: no role-gated page renders before access resolves.
  Files: app `App.kt`, `feature/shell/ui/ShellScreen.kt`.
  Depends: none.
  Done-when: a test with a fake clock shows a restored session reaches the shell destination without waiting
  1.2 s, and no management page content composes before `ShellAccess.Resolved`.
- **S-UF-F7c** [fix] The Backend URL field shows only in the desktop app.
  Gate the `AppTextField` at `ConnectScreen.kt:209-216` on the same `discoverySupported` flag as the saved
  connections.
  Files: app `feature/connect/ui/ConnectScreen.kt`.
  Depends: none.
  Done-when: a compose test renders ConnectScreen with `discoverySupported=false` and finds no Backend URL
  node; with true it is present.
- **S-UF-A1c** [fix] Deleting a command, timer, trigger, quote or random list runs at once with a 10 s Undo.
  Today each delete is a "can't be undone" `ConfirmDialog` (`CommandsScreen.kt:365-375`; strings 2194),
  although every one of these rows is soft-deleted server-side. Add an undelete route per entity
  (`POST .../{id}/restore`, clears `DeletedAt`, refuses a name now taken by another row), delete immediately,
  and show "Deleted !socials · Undo" for 10 s. When the item has dependents (pipeline, timer, reward), the
  existing blast-radius dialog lists them first and the delete still ends in the Undo toast.
  Files: `Api/Controllers/V1/{Commands,Timers,ChatTriggers,Quotes,PickLists}Controller.cs` + services,
  `openapi/v1.json`, the five controllers/screens in `app`.
  Depends: S-UF-X1 (toast action slot), S-UF-A1a.
  Done-when: per entity an Api test deletes then restores and asserts the row is back with its fields and
  is matched again by the chat handler (commands: `!socials` answers again); a jvmTest asserts delete
  emits a 10 s feedback carrying an Undo action that calls restore.
- **S-UF-M5b** [fix] Banning in every moderated channel confirms with the count and a required reason, and reports per-channel results.
  Build: the all-channels scope opens its own confirm, "Ban Ana in 14 channels?", with a required
  reason and a button that names the act. The result toast reads "Banned in 13 of 14 channels · 1
  failed", with details. This supersedes the `ChatController.ban` partial-failure item in
  S-AUDIT-REMAINDER (6).
  Files: `ChatScreen.kt` (`BanDialog`), `ChatController.kt`, the moderated-channel count read.
  Depends: S-UF-X1.
  Done-when: a test with a fake result of 13 ok and 1 failed renders "Banned in 13 of 14 channels · 1
  failed" and lists the failed channel. The confirm button stays disabled until a reason is entered.
- **S-UF-M5c** [fix] Clear chat and nuke report their result; nuke's toast carries Revert.
  Build: clear chat keeps its confirm ("Clear all messages for everyone in chat?"), and a success toast
  follows (silent today). The nuke result toast carries Revert. The History row keeps Revert, and the
  target in the confirm shows the display name, not the raw user id
  (`ModerationScreen.kt:2567` passes `userId` to `moderation_nuke_confirm_message`).
  Files: `ModerationController.kt`, `ModerationScreen.kt`. Depends: S-UF-X1.
  Done-when: a controller test asserts a success feedback after clear chat. A nuke result toast's Revert
  calls `revertNuke(batchId)`, and the confirm text contains the display name.
- **S-UF-T5a** [fix] Viewer text is cleaned before it is spoken, and a paid message over the limit is
  shortened instead of rejected.
  Build: before the lexicon, viewer text passes these rules in order:
  1. cheermotes and emote codes are removed (using the platform's message fragments);
  2. URLs become "link";
  3. any character repeated more than 3 times collapses to 3;
  4. an all-caps message is lower-cased.

  A paid request (redemption or cheer) over the effective cap is cut at the last whole word before the cap and
  ends with "…". A free command still gets the S-UF-T6b length reply. The approval queue and the caption show
  the cleaned text.
  Files: a new `TtsTextCleaner` (Infrastructure/Tts), `TtsDispatchService.cs`, `CheerEventHandler.cs` (pass
  fragments).
  Depends: S-UF-T6a (request source decides paid vs free).
  Done-when:
  - table tests: "Cheer500 heyyyyyy https://x.y" → "heyyy link";
  - "STOP SHOUTING" → "stop shouting";
  - a 260-char paid message at cap 200 dispatches ≤200 chars ending "…" at a word boundary;
  - the queue row's text equals the dispatched text.
- **S-UF-T5b** [fix] **Read usernames** and **Skip bot messages** do what they say.
  Today both are saved and never read (`TtsConfig.cs:72-74`, no dispatch reader).
  Build: Read usernames prefixes "Ana says:" to viewer-sourced lines when on. Skip bot messages drops lines
  whose author is a known bot (the channel's bot account, the platform's known-bot list) when on.
  Files: `TtsDispatchService.cs`.
  Depends: S-UF-T5a (rule order).
  Done-when: with Read usernames on, a viewer line dispatches as "Ana says: ..." and a bot-originated line does
  not; with Skip bot messages on, a request from the bot account emits a rejected event `bot_author` and no
  `TtsSpeak`.
- **S-UF-T8a** [fix] Every TTS request carries the triggering viewer's real standing and bits, and bot lines
  skip the who-can and bits gates.
  Build: reward redemptions set `user.role` (resolved standing) and `user.bits` = 0; cheers set `user.bits`;
  every other viewer source carries the same keys. Requests whose source is a bot line (S-UF-T6a's source:
  event response, shoutout, quote, script, test) bypass `MinPermission` and `MinBitsToTts`.
  Files: `RewardRedeemedHandler.cs`, `CheerEventHandler.cs`, `TtsDispatchService.cs`, `QuoteBuiltin.cs`,
  `ScriptHostBridge.cs`, `ShoutoutAction.cs`.
  Depends: S-UF-T6a (source kind).
  Done-when: on a "subscribers" channel a subscriber's redemption speaks and a stranger's is rejected
  `standing_gate`; on a 100-bit channel a 100-bit cheer speaks; with both gates set, `!quote` with TTS and a
  shoutout still speak.
- **S-UF-T8b** [fix] The builtin "Also read the reply out with TTS" switch appears only on builtins that honour
  it.
  Build: the builtin list DTO gains `supportsTts`, true where the builtin reads `context.SpeakWithTts`
  (`!quote` today). The dialog hides the switch elsewhere, and `SetSpeakWithTtsAsync` refuses an unsupported
  builtin. A new builtin that adds support flips the flag in the same commit.
  Files: `Infrastructure/Commands/BuiltinCommandService.cs`, the builtin DTO + snapshot,
  `BuiltinDetailDialog.kt`.
  Depends: none.
  Done-when: a jvmTest shows the switch on `!quote` and not on `!uptime`, and a server test proves enabling TTS
  on `!uptime` returns VALIDATION_FAILED.
- **S-UF-F2a** [fix] The self-host wizard is two server-tracked steps, and onboarding completes only on a
  streamer sign-in.
  The wizard contract shrinks to `twitch_app` and `streamer_sign_in`. In `twitch_app`, the shared app is the
  recommended action and BYOC is secondary. `platform_bot`, `spotify`, `discord` and `youtube` leave the
  wizard: their credential routes stay, and are reached from Integrations. The server saves the wizard
  position after every step, and the non-secret typed values with it (a saved secret comes back as "saved",
  never echoed). `OnboardingComplete` becomes true only once a streamer session has been established on this
  deployment, not on `HasTwitchDecision`.
  Files: server `SetupWizardDtos.cs`, `SystemController.cs` (status + a wizard-progress record), a migration
  in BOTH sets if progress is persisted as an entity, `openapi/v1.json`.
  Depends: none.
  Done-when: Api tests cover it. After the Twitch decision is saved, `GET /system/status` still says
  `onboardingComplete=false`, and `GET /system/setup` returns `currentStep=streamer_sign_in` with the
  client id restored and no secret value. After a streamer sign-in it says `true`. The wizard lists no
  `platform_bot` step.
- **S-UF-F2b** [fix] The wizard UI has two steps, resumes where it was, and asks no basics questions.
  Step 1: **Use the shared NomNomzBot app (recommended)** is the pre-selected primary button. "Use my own
  Twitch app" is collapsed, with the line "For full independence from NoMercy. Takes about 5 minutes on
  dev.twitch.tv." Expanding it shows numbered steps, the exact redirect URL and a Copy button. Step 2 is the
  streamer sign-in. The review step is removed. The prefix is `!`, the language comes from the browser
  locale and the timezone from the browser's zone, all sent at finish. In Settings, language becomes a
  dropdown and timezone a searchable zone picker.
  Files: app `feature/setup/**`, `App.kt:116-136`, `feature/settings/ui/SettingsScreen.kt:1081-1096`, strings.
  Depends: S-UF-F2a; S-SCHEDULE-TZ (the zone picker it introduces).
  Done-when: a jvmTest shows a fresh controller over a server reporting `currentStep=streamer_sign_in` opens
  on step 2 with the typed client id filled. finish() sends the browser's locale and zone. The Settings
  timezone field rejects a zone that is not in the list.
- **S-UF-F3a** [new] The server reports bot readiness live and observes the first `!ping` reply with its
  latency.
  Add a per-platform bot readiness read (`joining`, `in_chat` = the chat-read EventSub subscription is
  enabled, `moderator` / `not_moderator`) and push its changes on the dashboard hub, including the moment
  `channel.moderator.add` names the bot. Add `POST /channels/{id}/onboarding/say-hi`, which sends an
  editable line, defaulting to "NomNomzBot is here. Try !commands". When the broadcaster's `!ping` is
  answered, push `OnboardingPingObserved` with the latency from the inbound message timestamp to the send
  acknowledgement. Persist `FirstReplyConfirmedAt` on the channel. The H2 step 2 reads that field.
  Files: server Identity (bot status), Chat (the ping observation), `DashboardNotifier`, a `Channel` column
  in BOTH migration sets, `openapi/v1.json`, `HubEvent.kt`.
  Depends: S-UF-F1a.
  Done-when: Infrastructure tests prove three things. A `channel.moderator.add` event for the bot pushes
  `moderator`. say-hi sends exactly one chat line with the given text. A broadcaster `!ping` whose reply is
  acknowledged pushes `OnboardingPingObserved` with a positive latency and stamps `FirstReplyConfirmedAt`
  once; a second ping does not restamp it.
- **S-UF-F3b** [new] Onboarding ends on "Your bot is in your chat" with live rows, mod help, Say hi and a
  confirmed `!ping`.
  The last onboarding screen has one row per platform: "Joining chat…", then "In chat", then "Moderator"
  or "Not a moderator yet", updated from the hub with no polling. When the bot is not a moderator, the row
  shows `/mod <botlogin>` with Copy and "Moderators aren't slowed down by Twitch's chat limits."; it turns
  green on the push. **Say hi in chat** shows the line arrive in a small embedded chat. Then comes the prompt
  "Now type !ping in your chat.", then "It works. The bot answered in 0.4 s." with the real latency. Last,
  **Set up alerts** (primary, to S-UF-F5b) and **Go to dashboard**. "I'll do this later" skips the screen
  and leaves Get started step 2 open.
  Files: app new `feature/onboarding/`, `App.kt`, `ChatController` reuse for the embedded chat, strings.
  Depends: S-UF-F3a, S-UF-F1b, S-UF-F2b.
  Done-when: a jvmTest feeds hub events. A `moderator` push turns the row green. `OnboardingPingObserved(412ms)`
  renders "0.4 s" and enables both buttons. Skip leaves `FirstReplyConfirmedAt` null. On the deployed SaaS
  box, a fresh streamer reaches the confirmed `!ping` within 2 minutes of the first click, with a timed
  rendered-client check.
- **S-UF-F5b** [fix] A "Set up alerts" screen names every alert chat line that is on, with toggles and a
  rendered sample.
  After S-UF-F3b, and from Get started, one screen lists the core alert lines that are on by default (follow,
  sub, gift sub, resub, cheer, raid). Each row has its own toggle and the line it produces, rendered in the
  channel's tone with a sample name, for example "Welcome to the raid, Ana and her 12 viewers!". Below them,
  **Welcome first-time chatters** and **Celebrate watch streaks** are pre-checked; returning-chatter greetings
  stay unchecked. Confirming writes the channel rows (`FollowsPlatformDefault=false`). Overlay output and
  sounds for these events stay off.
  Files: server an `event-responses/first-run` read (rendered sample per type, through the real tone
  resolver) and a batch write; app new screen, strings.
  Depends: S-UF-F3b for the entry point (the screen also stands alone from Get started).
  Done-when: an Api test shows the sample for `channel.raid` comes from `EventResponseToneCatalog` in the
  channel's tone with the sample name substituted. Confirming the defaults leaves the six core types enabled,
  `engagement.first_time_chatter` and `engagement.watch_streak` enabled, `engagement.returning_chatter`
  disabled, and no overlay or sound output enabled.
- **S-UF-F6** [fix] Turning on a feature that lacks a Twitch scope opens an inline permission step, then the
  toggle completes.
  Today `FeatureService.cs:194-224` enables a feature whatever the grant, so it fails later, at use. Change:
  enabling a feature whose `RequiredScopes` are not all granted returns `SCOPES_REQUIRED` with the missing
  scopes and a plain-language reason. The row then shows "Channel points needs permission to read and manage
  your rewards on Twitch." with **Allow on Twitch**, which runs the widened re-auth. When the grant returns
  the toggle completes by itself; cancelling leaves it off. Scope strings move behind "Technical details".
  The Integrations banner, Settings White-label and Features link to the one Settings Permissions view
  instead of repeating it.
  Files: server `Platform/FeatureService.cs`, `FeaturesController`; app `feature/features/**`,
  `feature/settings/**`, `feature/integrations/**`, strings.
  Depends: none.
  Done-when: an Infrastructure test shows enabling `channel_points` without its scope leaves `IsEnabled`
  false and returns the missing set. With the scope it turns on. A jvmTest shows a re-auth that comes back
  with the scope completes the pending toggle without a second click, and a cancel leaves it off.
- **S-UF-H2** [fix] Get started shows five steps with a progress bar, each completed from real state.
  The steps:
  1. Signed in (always done, so the bar starts at 1 of 5).
  2. See the bot answer in your chat (`FirstReplyConfirmedAt`, F3).
  3. Add your alert overlay to OBS (heartbeat, O1).
  4. Turn on a follow alert (`channel.follow` response enabled, own or followed default).
  5. Make your first custom command (`presetKey == null`, F5c).
  Each step deep-links to its exact control and completes only from real state. "Build a pipeline" is
  removed. "Hide for now" hides the card for 7 days. At 5 of 5 the card shows "You're set up" once, then
  disappears for good.
  Files: server a `GET /channels/{id}/get-started` read computing the five, plus hide-until and completed-at
  fields (BOTH migration sets); app `HomeController.kt`, `HomeScreen.kt` `FirstRunChecklistCard`, strings.
  Depends: S-UF-F3a, S-UF-F5c, S062 (the overlay presence signal).
  Done-when: an Api test shows a fresh channel reads 1 of 5 and a channel holding only starter presets keeps
  step 5 open. A jvmTest shows "Hide for now" suppresses the card until a fake clock passes 7 days, and 5 of
  5 shows the message once and then no card.
- **S-UF-F4b** [fix] The bot account is an optional choice shown as two rendered chat lines, with an
  account confirmation step.
  New channels get `BotLinePrefix = "*"` while no dedicated bot is connected (`ChannelOnboardingWriter`). The
  bot account is offered as an optional Get started step and on Integrations. Both offer it as a choice
  between "<Streamer>: * Thanks for the follow, Ana!" (your account, with marker) and "NomNomzBot: Thanks for
  the follow, Ana!" (separate bot account), rendered with the real names. After each sign-in the screen shows
  "Signed in as <login>. Is this your bot account?" with **Yes** and **Use a different account**. The
  instructions say "Open twitch.tv/activate in a private window and log in as the bot there."
  Files: server `Identity/ChannelOnboardingWriter.cs`; app `feature/integrations/**`, `feature/home/**`
  (Get started), strings.
  Depends: S-UF-F4a, S-UF-F2a (the bot step leaves the wizard), S-UF-H2.
  Done-when: an Infrastructure test shows a freshly onboarded channel with no bot gets `*` on its first bot
  line. A jvmTest shows **Use a different account** disconnects the just-connected bot and restarts the
  sign-in, and `BOT_IS_STREAMER` renders the refusal copy.
- **S-UF-T2a** [fix] **Send a test line** reports what the OBS source did, not that a request was sent.
  Today the test reports "Test sent to the overlay" whether or not a page played it (`strings.xml:809`,
  `TtsController.kt:237-252`). After `e95e7417d`, audio goes to at most one page, and nothing comes back.
  Build: the test line carries a cue id. The result reads "Played on 1 OBS source" only when the audio page
  reports that cue started (the signal from S-UF-T9a). If no page can play audio, it reads "No OBS source
  played it. Add the TTS source in OBS." The button shows progress while waiting (X1); the wait times out after
  10 s (a number the spec does not set). On `client_edge` it reads "Played in the browser only; your stream
  didn't hear it."
  Files: `Api/Controllers/V1/TtsConfigController.cs` (overlay/test), `feature/tts/state/TtsController.kt`,
  `TtsScreen.kt` OverlayCard, strings.
  Depends: S-UF-T9a.
  Done-when: a controller test with a fake audio page acks the cue and the response says played on 1 source; a
  test with no page open says no source played it; and a jvmTest asserts each rendered sentence.
- **S-UF-T3a** [fix] Every TTS play button makes a sound in the dashboard, and **Use** sets the channel voice
  at once.
  Today Preview without a `previewUrl` and **Play** in Test voice both synthesize, then show text only
  (`TtsScreen.kt:2475-2480`, `:1064-1067`). Test voice hides on a blank voice (`:2421`). **Use** only fills a
  field (`:497`).
  Build:
  - Every play button plays the `previewUrl` if present, otherwise the returned `audioBase64` as a data URI
    through the existing `playSoundPreview` expect/actual (`core/io/SoundPreviewPlayer.kt:19`).
  - Test voice is always visible and names the effective voice. When `followsPlatformDefaultVoice` is true it
    reads "Platform default: Jenny (en-US)".
  - **Use** saves the channel voice immediately (`UpdateTtsConfigDto.DefaultVoiceId`) and shows "Channel voice
    is now Jenny · Undo". Undo restores the previous voice, or the platform-default follow flag.
  Files: `TtsScreen.kt`, `TtsController.kt`, `SoundPreviewPlayer` actuals, strings.
  Depends: S-UF-X1 (toast action slot for Undo).
  Done-when:
  - a jvmTest with a fake player asserts that Preview on a voice without `previewUrl` plays the returned data
    URI;
  - **Use** issues one config PUT, and Undo issues the inverse PUT (state asserted on the fake API);
  - a platform-default channel renders "Platform default: ...".
- **S-UF-T3b** [fix] Voices are picked from a searchable list with name, language, gender and a play button,
  never typed as ids.
  Today the channel voice is a free-text id field (`TtsScreen.kt:2200-2216`), and per-viewer assign also takes
  a typed voice id (`TtsScreen.kt:1490-1497`).
  Build: one `VoicePickerField` backed by `GET /tts/voices` search (paged). Each row shows name, language,
  gender and a play button (sound as in S-UF-T3a). It replaces both fields; S-UF-T10's viewer card reuses it.
  Files: `core/designsystem` (picker) or `feature/tts/ui`, `TtsScreen.kt`.
  Depends: S-UF-T3a.
  Done-when: a jvmTest types "jen", picks "Jenny (en-US, female)", and the saved config carries that voice's
  id. No `AppTextField` for a voice id remains in `feature/tts`.
- **S-UF-H1a** [fix] Home's live numbers keep moving: uptime ticks, viewers update at least every 60 s,
  and the layout swaps on status change.
  On `StreamStatusChanged`, Home re-derives live state with no reload: `startedAt`, the viewer count and the
  live platforms. Uptime ticks every second on the client from `startedAt`. The server pushes the viewer
  sample to the dashboard hub at most 60 s apart while live (the poll interval drops to 60 s, or the
  `StreamViewerCountSampledEvent` is pushed). The Prep and Live layouts swap within 2 s of the push, with a
  150 ms crossfade and no other animation.
  Files: server `Stream/Jobs/StreamStatusPollingService.cs`, a hub broadcaster, `openapi/v1.json`; app
  `HomeController.kt`, `HomeScreen.kt`, `HubEvent.kt`.
  Depends: none.
  Done-when: an Infrastructure test shows two live polls 60 s apart push two viewer samples. A jvmTest shows
  an online `StreamStatusChanged` sets `startedAt` and the live layout, uptime advances with a fake clock,
  and an offline push returns to Prep.
- **S-UF-H1c** [fix] The Prep layout shows a computed Ready-to-go-live checklist, the next stream editor
  and the last stream recap.
  **Ready to go live** rows are computed from real state, never ticked by hand, and each failing row has a
  **Fix** link to its exact control:
  - the bot is in chat on each connected platform;
  - every enabled overlay loaded in OBS in the last 24 h;
  - Spotify, Discord and TTS are healthy;
  - title and category are set;
  - the bot token is valid.
  **Next stream** edits title, category and tags in place. **Last stream recap** shows duration, peak
  viewers, new followers, new subs, clips, the top 5 chatters and the top command. **Get started** (H2) shows
  while unfinished.
  Files: server a `dashboard/readiness` read and a `dashboard/last-stream` read; app `feature/home/**`.
  Depends: S-UF-H1a, S-UF-F3a (bot-in-chat state), S062 (the overlay presence signal), S-UF-H2.
  Done-when: an Api test shows a channel with an enabled overlay unseen for 25 h gets a failing overlay row
  with its deep link, and that a fresh overlay load clears it. A compose test renders the recap numbers from
  the read.
- **S-UF-H3a** [fix] Home hides actions the role cannot run and shows live-only actions only while live.
  An action whose key the caller does not hold is not rendered. Raid, Run ad, Clip and Mark moment render
  only while live.
  Files: app `feature/home/ui/HomeScreen.kt` (`GatedQuickAction`, `QuickActionsCard`).
  Depends: none.
  Done-when: a compose test as a Moderator without `live-ops:ads:write` finds no Run ad node. Offline, a
  Broadcaster finds no Raid, Ad, Clip or Mark moment node; live, all four render.
- **S-UF-H3b** [fix] Each live action sits on its safety-ladder step, with real numbers, Undo windows and
  in-dialog results.
  - **Edit title**: the dialog stays open until the platforms confirm, then the toast "Title updated on
    Twitch, Kick". A failure shows inside the dialog with Retry.
  - **Mark moment, Clip**: run on press. The clip toast carries the link as a real link with Copy.
  - **Start poll or prediction**: sent on submit.
  - **End poll**: "Ending poll in 5 s · Undo".
  - **Cancel prediction**: confirms "Refund <sum of outcome channelPoints> points to <sum of users>
    viewers?" (from `LiveOpsPredictionOutcome`), and the button reads **Refund and cancel**.
  - **Run ad**: the length picker defaults to the channel's last used length (persisted server-side), then
    "Ad starts in 5 s · Undo".
  - **Raid**: starts at once. The live strip shows the countdown with **Cancel raid** until it fires.
  Files: app `feature/home/**`, `feature/liveops/state/LiveOpsController.kt`; server last-ad-length on the
  channel (BOTH migration sets).
  Depends: S-UF-X1, S-UF-X2.
  Done-when: jvmTests prove four things. End poll followed by Undo within 5 s sends no end call. Cancel
  prediction shows the summed numbers and sends nothing until confirmed. Run ad sends the last used length
  after 5 s. A failed title update keeps the dialog open with the error.
- **S-UF-H3c** [fix] Raid search finds any channel by name and, before typing, lists followed live channels
  by viewers.
  Add a raid-target search over Helix Search Channels, and a followed-live list over Get Followed Streams.
  The list needs `user:read:follows`, asked for just in time by the F6 flow when missing.
  Files: server `LiveOpsController` or `StreamController` routes over the existing `ITwitchSearchApi` and
  `ITwitchStreamsApi.GetFollowedStreamsAsync`, `openapi/v1.json`; app `HomeController.searchRaidTargets`,
  `RaidDialog`.
  Depends: S-UF-F6.
  Done-when: an Api test with a fake Helix returns a channel that never chatted in this channel, and returns
  the followed-live list sorted by viewer count. Without the scope it returns `SCOPES_REQUIRED`.
- **S-UF-H3d** [fix] Edit title tags are chips with autocomplete.
  Files: app `HomeScreen.kt` `ChangeTitleDialog`, a tag-suggest source.
  Depends: none.
  Done-when: a compose test adds two tags as chips, removes one, and saves exactly the remaining tag list.
- **S-UF-H4** [fix] Home and the sidebar read one attention store, and severity alone decides how loud an
  item is.
  Home's inbox reads `graph.attentionController.items`. Its own fetch and its `actionRequired` field go,
  and a dismiss updates both placements from the one store at once.
  - **Critical** (only while live): the bot is out of chat on any platform, an enabled overlay
    disconnected, or the bot token died. It raises a toast that stays until dismissed, with an optional
    sound, off by default.
  - **Action** (held AutoMod, queues past 60 s): badge and inbox row only.
  - **Info**: inbox row only, never a badge.
  While live, the Reauth modal becomes a Critical item with **Reconnect Twitch**. Offline it stays a modal.
  Files: app `feature/home/**`, `feature/attention/**`, `feature/shell/ui/ReauthDialog.kt`; server the
  source severities (`critical` only while live; an `action` level in place of `warning` where the spec
  says so).
  Depends: none.
  Done-when: a jvmTest dismisses on Home and the sidebar count drops in the same frame, with no second fetch.
  While live, a dead-token health read raises no modal and adds a Critical item plus a sticky toast; offline
  it raises the modal. An Info item adds no badge.
- **S-UF-H1b** [fix] The Live layout leads with a live strip, the attention inbox, live actions, the running
  poll and a collapsed feed.
  Top to bottom:
  1. **Live strip**: LIVE pill, ticking uptime, viewers in large type with the delta against 5 minutes ago,
     one chip per live platform, and the next ad as "Ad in 4:12" with inline **Snooze**.
  2. **Needs you now**: the H4 store, shown only when it holds items.
  3. **Live actions**: Mark moment, Clip, Poll, Prediction, Run ad, Raid, Edit title. Each shows its key
     letter (M, C, P, R, A, D, T), and the letters work as shortcuts.
  4. **Active poll or prediction**: live bars per option, time left and **End**. This replaces
     `HomeScreen.kt:1188-1205` and the separate Chat poll card.
  5. **Two columns**: on the left, the activity feed with identical consecutive events collapsed to one row
     ("<name> redeemed TTS ×12, last 21:14") and time of day, not date; on the right, Now playing (title,
     requester, **Skip**) and the top commands this stream.
  6. **This stream so far**, one quiet row: new followers, new subs, bits, chatters. It replaces the 8 tiles.
  Files: app `feature/home/**`; server a per-stream command-use read.
  Depends: S-UF-H1a, S-UF-H4, S106 (the poll, prediction and ad pushes).
  Done-when: a compose test of a live state renders the countdown from `nextAdAt` and **Snooze** calls
  `snoozeNextAd`. Twelve identical redemptions render one row with "×12". A poll vote push moves its bar. No
  tile row renders while live.
- **S-UF-M3** [fix] Chat and Multi-Chat stop jumping to the newest line while a moderator aims.
  Today both feeds snap to the newest line on every message (`ChatScreen.kt:324-326`,
  `MultiChatScreen.kt:384-386`), so during a raid the line being acted on moves away. Build: auto-scroll
  pauses while the pointer is over the feed, the user has scrolled up, a line menu or the user card is
  open, or keyboard mode has a selection. While paused, a bottom pill counts new lines ("27 new
  messages") and a click resumes. Auto-scroll resumes 3 s after the pointer leaves, unless the user
  scrolled up. One shared feed-follow state used by both screens.
  Files: `ChatScreen.kt`, `MultiChatScreen.kt`, a shared follow-state helper in `feature/chat/ui`.
  Depends: none.
  Done-when: a UI test with the pointer over the feed appends 27 lines. The first visible item does not
  change and the pill reads "27 new messages". Clicking the pill shows the last line. With the pointer
  gone for 3 s, following resumes. After a scroll-up, it does not.
- **S-UF-M4a** [fix] A moderated chat line changes in place, attributed, on every moderator's screen.
  Build: no feed reload after an action. A deleted message becomes "Message deleted by Ana", shown to
  moderators only, with the original text revealed on click (today it is filtered out:
  `ChatController.kt:258-264`). A timed-out or banned author's lines turn muted and carry "Timed out 10 m
  by Ana" or "Banned by Ana". Driven by the hub, so every moderator's dashboard shows the same tag.
  Decode `moderatorDisplayName` in `HubModAction`. Have Chat and Multi-Chat consume `ModAction`. Add the
  deleting moderator's display name to the `message_deleted` payload (it carries only `deletedByUserId`:
  `feature/chat/state/ChatModerationPayloads.kt:22-26`).
  Files: `HubEvent.kt`, `ChatController.kt`, `MultiChatController.kt`, `ChatScreen.kt`, `MultiChatScreen.kt`,
  the message-deleted hub broadcaster. Depends: none.
  Done-when: a controller test feeds a `ModAction{action=timeout,moderatorDisplayName=Ana}` push and
  asserts the author's lines are marked "Timed out 10 m by Ana" with no `messages()` refetch. A
  `message_deleted` push marks the line instead of removing it.
- **S-UF-M4b** [fix] Timeout and ban from chat carry a 10-second Undo that runs the real inverse.
  Build: the action's result toast carries Undo for 10 s. Undo calls remove-timeout or unban, and the
  tag updates to "Undone by Ana" on every dashboard (from the `UserUnbanned` push).
  Files: `FeedbackMessage.kt` (consumes the X1 action slot), `ChatController.kt`, `MultiChatController.kt`.
  Depends: S-UF-M4a, S-UF-X1 (toast action slot).
  Done-when: a test bans from chat, presses Undo within 10 s, asserts the unban call went to the same
  target and the line tag reads "Undone by Ana". Undo pressed after 10 s is not offered.
- **S-UF-M5a** [fix] Routine moderation runs at once: no confirm for delete, timeout or local ban; Undo instead.
  Build: apply X2 steps 0 and 1. Delete, timeout and warn run with no dialog and show their result in
  place (M4a). A ban in this channel, from chat, the card or a held message, runs at once with the
  10 s Undo (M4b). Block term from a held message runs at once with Undo, and the toast says "Also
  blocks 3 held messages with this term" (count from the held set).
  Files: `ChatScreen.kt`, `MultiChatScreen.kt`, `AttentionInbox.kt`, `HomeController.kt`.
  Depends: S-UF-M4b, S-UF-X2.
  Done-when: a UI test deletes and times out from a chat line with no dialog shown. The held-message Ban
  shows an Undo toast, and Undo issues the unban. Block term reports the real number of other held
  messages that contain the term.
- **S-UF-X6a** [fix] The chat poll card updates from a hub push, and a guard stops new pollers.
  Today `ChatPollsCard` reloads every 4 s while mounted (`ChatPollsCard.kt:75`). Build: the server pushes a
  `ChatPollChanged` hub event on open, vote tally change and close; the card applies it and loads once on mount; a
  guard test enumerates `while (true)` loops that call `delay` then a load/refresh under `feature/` and fails on any
  not allowlisted (the no-hub fallback at `ChatScreen.kt:209`, countdown clocks), with GamesScreen and RewardsScreen
  listed as known pollers to remove.
  Files: chat poll service + hub publisher (server), `HubEvent.kt`, `ChatPollsCard.kt`, `ChatPollsController`, guard
  test. Depends: none.
  Done-when: a hub test casts a vote and asserts one `ChatPollChanged` push with the new tally; a jvmTest feeds that
  event and asserts the bar moves with no API call; the guard fails if the 4 s loop is restored.
- **S-UF-M1a** [fix] One shared mod-notes store replaces the two that exist today.
  Today the moderation history keeps its notes in one store (`ModerationController.cs:560`, written by
  the Viewer profile's "add note") and the moderation desk keeps `UserNote` rows in another (`:1165-1230`),
  so a note written in one place is invisible in the other (ledger L193). Build: one store, with author,
  date and pin. Migrate the rows from the losing store into the surviving one (BOTH migration sets,
  proven on a populated database), and route both endpoints and both screens to it.
  Files: `Moderation` notes service + entities + migrations, `ModerationController.cs`, `ViewerProfileScreen.kt`,
  `ModerationScreen.kt`. Depends: none.
  Done-when: a note added on the Viewer profile is listed by `GET users/{userId}/notes` and on the
  moderation context panel with its author and date, and the migration test shows pre-existing notes from
  both stores present once each after upgrade.
- **S-UF-M1b** [fix] Clicking a name in Chat or Multi-Chat opens a user card side panel; the feed keeps running.
  Build: a side panel (not a dialog), opened from the name on any line in `ChatScreen` and `MultiChatScreen`.
  It shows, in order: display name, pronouns and the linked platform icons; account age, follow age,
  first seen here, messages here, watch streak; standing, trust and heat as named levels (never
  numbers); shared notes (M1a) with author and date; the viewer's last 10 messages in this channel; the
  full mod history (timeout, ban, warn, deletion, each with who, why and when). Server: resolve the card
  by `(provider, providerUserId)`, because a chat line carries the platform id, not `Users.Id`. Add a
  `userId` filter to `GET chat/messages`. Add account age (Helix Get Users `created_at`, the same source
  as `AccountAgeBuiltin`) and follow age (Get Channel Followers `followed_at`).
  Files: `ChatController.cs` (filter), the profile/context read service, `ChatScreen.kt`,
  `MultiChatScreen.kt`, a new `feature/chat/ui` user-card composable. Depends: S-UF-M1a.
  Done-when: a UI test clicks a name in Chat, the panel renders every field above from a seeded viewer,
  and new hub lines keep appending to the feed while it is open. An API test proves the `userId` filter
  returns only that viewer's last 10 lines, newest last.
- **S-UF-M1c** [fix] The user card acts: timeout presets, ban, warn, add note, unban or remove timeout.
  Build: actions in the card. Timeout offers 1 m, 10 m, 1 h, 1 d and a custom duration. Then Ban, Warn,
  Add note, and Unban or Remove timeout when one applies. Warn and notes work for any viewer, not only
  a banned one (`POST warn` already accepts any target: `ModerationController.cs:810`). The same card
  opens from a queue row, a mod-log row and an activity-feed row.
  Files: user-card composable, `ModerationScreen.kt` (log + queue rows), Home activity feed.
  Depends: S-UF-M1b.
  Done-when: a test warns a viewer who was never banned from the card, then asserts the mod-log row and the
  card's warn count increment. A 1 h preset sends `durationSeconds=3600`. Clicking a mod-log name opens the
  card.
- **S-UF-M2a** [fix] Chat lines show Delete, Timeout and Ban beside "…", with timeout presets and reason chips.
  Build: on hover or keyboard focus, three icons appear beside the existing "…", which stays for touch. A
  single click on Timeout uses the channel default. Its dropdown lists 1 m, 10 m, 1 h and 1 d. Reason
  chips (Spam, Bot, Harassment, Hate, Off-topic) fill the reason and typing stays possible. The reason is
  sent with the timeout (today `ModerationActionBody` carries none from chat). Add a channel
  `DefaultTimeoutSeconds` (default 600) to the moderation config, editable in Enforcement Rules (BOTH
  migration sets). Same in Multi-Chat.
  Files: moderation config entity + DTO + migrations, `ChatScreen.kt`, `MultiChatScreen.kt`, `ChatApi.kt`,
  Enforcement Rules section of `ModerationScreen.kt`. Depends: none.
  Done-when: with the default set to 300, one click on the Timeout icon posts `durationSeconds=300`. The
  1 h preset posts 3600 with the chosen chip's reason, and the mod-log row carries that reason. A UI test
  proves the icons appear on focus, not only on hover.
- **S-UF-M8a** [fix] Song-request Remove, Ban track and Promote target the request id; Remove uses Undo.
  Build: `QueueItemDto` carries the request id and the requester's user id. The three routes take the
  id, and a request that is gone returns 404 instead of acting on whatever slid into that position.
  Remove runs at once with the 10 s Undo, which restores the request to its old place. The requester's
  name opens the user card. This also discharges usability-inventory Part C #23 "address queue entries
  by code, not position".
  Files: `MusicQueueDtos.cs`, `MusicController.cs`, `MusicService`, `SongRequestsController.kt`,
  Song Requests screen. Depends: S-UF-X1, S-UF-M1b.
  Done-when: a test removes request R while another request ahead of it is dequeued in between, and R
  (not its neighbour) is removed. Undo puts R back at its old index.
- **S-UF-T7a** [fix] The TTS approval queue holds only viewer text, runs oldest first, expires on the server,
  and the first moderator wins.
  Build:
  - Bot-line, test and event-response sources skip approval (source from S-UF-T6a).
  - Pending is ordered oldest first with no 25-row cap, and is pushed to the dashboard hub on queue, review
    and expiry.
  - A server sweep removes items at 10 minutes and runs S-UF-T6a's refund.
  - Approve and reject are an atomic pending→claimed transition. The loser gets a conflict, and the row reads
    "Approved by Bo" with its buttons gone.
  - Every row shows its expiry countdown.
  - Approved items play in approval order after the current line.
  Files: `TtsDispatchService.cs`, `TtsQueueController.cs` (server), a hub push, an expiry sweeper,
  `TtsQueueController.kt`, `TtsScreen.kt` queue section.
  Depends: S-UF-T6a.
  Done-when:
  - two concurrent approves of one entry produce exactly one `TtsUtteranceDispatchedEvent`;
  - an entry 10 min 1 s old is gone and refunded;
  - an event-response line on an approval channel dispatches directly;
  - the jvmTest list re-orders from a pushed queue event without reload.
  M6 later hosts these rows (S-UF-M6).
- **S-UF-M6a** [fix] Queue is one oldest-first list of everything waiting on a moderator, with one row component.
  Build: Review Queue becomes **Queue**. A server read returns one list, oldest first: held messages,
  TTS awaiting approval, media-share submissions, unban requests and viewer reports. Filter chips per
  kind, each with its count. Every row: name (opens the user card), trust and heat, age ("held 2 m"),
  an expiry countdown when one exists ("expires in 6:12"), the content, inline actions. TTS rows show
  the original text with the censored parts highlighted. An empty queue reads "All clear" with the time
  of the last change. The Home held-message dialog renders the same row.
  Files: a queue aggregation service + endpoint, `ModerationScreen.kt` Queue section, `AttentionInbox.kt`,
  new shared row composable, `TtsApi.kt`. Depends: S-UF-T7a (TTS approval-queue rules), S-UF-M1c.
  Done-when: an API test with one item of each kind returns them oldest first with kind, age and expiry. A
  UI test renders the TTS row with the censored span highlighted. A filtered-empty list reads "All clear"
  with a timestamp, never an empty card.
- **S-UF-M6b** [fix] The Queue updates from hub pushes; nothing loads once.
  Build: every kind pushes add, resolve and expire to the Queue (and to the Home dialog). That includes
  a pushed `tts_queue_changed` and a `media_share_submitted` event, so a second moderator's approval
  removes the row everywhere.
  Files: TTS dispatch + media share services (broadcast), `DashboardNotifier`, queue controller.
  Depends: S-UF-M6a.
  Done-when: a controller test feeds a `tts_queue_changed` push and a `media_share_submitted` push and
  the list changes without a refetch. An API test shows resolving a held message emits the push the list
  consumes.
- **S-UF-A4a** [fix] The command form starts with name and reply, and the machinery moves under More options.
  New order: **Command** with the channel prefix as a fixed `!` adornment (users type `socials`); **Response**
  with the A2a preview and a segmented control "One reply" / "Pick one at random"; one row with **Who can
  use it** (default Everyone) and **Cooldown** (default 5 s per viewer, 0 s channel-wide); **More options**
  collapsed (aliases, prefix override, match mode and pattern, channel-wide cooldown, enabled on create,
  "Run a code script instead"). The type picker leaves the top of the form. Under Response a link "Needs
  logic, sounds or overlays? Turn this into a pipeline." creates a pipeline whose first step is
  `send_message` with the text already written, binds it to the command and opens it in the pipeline
  editor; nothing typed is lost. Editing an existing pipeline or code command opens with More options
  expanded on its binding.
  Files: `feature/commands/ui/CommandsScreen.kt`, `feature/commands/state/CommandsController.kt`, strings.
  Depends: S-UF-A1a, S-UF-A2a.
  Done-when: a jvmTest asserts a new command opens with userCooldown 5, channel cooldown 0 and More options
  collapsed; a test clicks "Turn this into a pipeline" on a typed reply and asserts one pipeline created with
  a single `send_message` step carrying that text, the command bound to it, and the editor opened on it.
- **S-UF-A4b** [fix] Aliases get the name's collision check, and an empty search offers to create the command.
  Run the shadow warning (`CommandsScreen.kt:788-791`) over every alias as well as the name, and add a
  collision warning when a name or alias matches another custom command. When the search finds nothing the
  card says "No command matches 'xyz'" with **Create !xyz** (opens the form with the name filled), not
  "No commands yet".
  Files: `CommandsScreen.kt`, strings.
  Depends: none.
  Done-when: jvmTests: an alias `quote` shows the built-in shadow warning and blocks save until acknowledged;
  an alias equal to another command's name shows the collision warning; searching `xyz` with commands present
  shows the no-match line and **Create !xyz** opens the form with name `xyz`.
- **S-UF-A5a** [fix] Each Event Responses row reads as one plain sentence with its outputs as chips.
  "When someone follows", "When someone gifts subs", "When a raid arrives", for every event type in the
  catalogue; no raw key is ever shown (an unmapped type gets a generated sentence from the catalogue display
  name, never the key; the key moves behind "Technical details"). The row's right side shows Chat, Overlay,
  Sound, TTS, Pipeline chips, filled when on and outline when off.
  Files: `feature/eventresponses/ui/EventResponsesScreen.kt`, `EventTypeLabels.kt`, strings (en + nl).
  Depends: none.
  Done-when: a resource test fails when any catalogue event type has no sentence key in en or nl; a jvmTest
  asserts a row with chat on and TTS off renders a filled Chat chip and an outline TTS chip, and no row text
  contains a `.`-separated event key.
- **S-UF-A5c** [fix] **Test** on an event-response row fires a sample through the real path, previewed, not broadcast.
  Add `POST .../event-responses/{eventType}/test` that runs the response with the event's typed sample
  payload; the chat line returns to the A2a preview only unless the user ticks **Also send to chat**; the
  overlay part goes only to the response's alert widget (via the targeted test of S-UF-O4b), never every
  subscribed widget; TTS and sound follow the same "targeted, opt-in" rule. Nothing touches currency,
  loyalty or reward state.
  Files: `EventResponsesController.cs` + service, `openapi/v1.json`, `EventResponsesScreen.kt`.
  Depends: S-UF-A2a, S-UF-O4b.
  Done-when: an Api test asserts a follow test returns the rendered line and sends zero chat messages by
  default and exactly one with **Also send to chat**; a hub-capture test asserts the overlay event reached
  only the bound widget.
- **S-UF-A6a** [fix] Trigger and timer forms load before they are editable, explain a bad interval, and always offer a pipeline.
  The timer edit form shows a skeleton until the detail loads and only then becomes editable (no re-seed over
  typed input). An out-of-range interval shows "Pick between 1 and 1,440 minutes" under the field. The
  pipeline option is always visible in both Chat Triggers and Timers; with zero pipelines it offers **Create a
  pipeline** inline through `PipelineBindPicker`, as Commands does.
  Files: `feature/timers/ui/TimersScreen.kt`, `feature/chattriggers/ui/ChatTriggersScreen.kt`, strings.
  Depends: none.
  Done-when: jvmTests: with a delayed detail fetch the fields are not editable until it lands and a value
  typed after it lands survives; interval 2000 shows the range line; on a channel with zero pipelines both
  forms show **Create a pipeline**, and creating one binds it.
- **S-UF-A6c** [fix] Each timer row shows its next fire live.
  "Next in 12 min", or "Waiting for 3 more chat messages" when the activity floor is what holds it. The timer
  list DTO carries `messagesSinceLastFire` (the count `TimerService.cs:167-179` already computes) and the hub
  pushes timer fires so the row re-anchors without a reload.
  Files: `Infrastructure/Commands/TimerManagementService.cs` (list projection), timer DTO, hub push,
  `openapi/v1.json`, `TimersScreen.kt`, `TimerSchedule.kt`.
  Depends: none.
  Done-when: an Infrastructure test asserts the list DTO's count equals the chat messages after
  `LastFiredAt`; a jvmTest renders "Waiting for 3 more chat messages" for floor 5 and count 2, and the row
  text changes after a hub timer-fired event.
- **S-UF-A7a** [fix] The pipeline editor has one save level and never loses a draft.
  Step dialogs **Apply** to the draft; the header shows an "Unsaved changes" chip and one filled **Save
  pipeline**; the draft is written to local storage after every change and a reload offers "Restore your
  unsaved draft from 21:14?"; **Back** or a sidebar click with unsaved changes uses the S-UF-A1b prompt.
  Files: `PipelinesController.kt`, `PipelinesScreen.kt`, a draft store (expect/actual), strings.
  Depends: S-UF-A1b.
  Done-when: jvmTests: an applied step marks the draft dirty and Back prompts; a new controller over the same
  draft store offers the restore with the saved time and restores the steps; Save clears the chip and the
  stored draft.
- **S-UF-A7b** [fix] A save by someone else never replaces the open editor.
  While `Editing`, a hub `pipelines` change for the open pipeline refetches into a side slot and shows "Ana
  saved a newer version of this pipeline 1 min ago" with **Load theirs** and **Keep mine**; changes to other
  pipelines refresh only the list behind. The pipeline gains `UpdatedBy` + `UpdatedAt` on the read DTO (BOTH
  migration sets) so the banner can name who.
  Files: `PipelinesController.kt`, `PipelinesScreen.kt`, `Pipeline.cs` + both migration sets, pipeline DTO,
  `openapi/v1.json`.
  Depends: S-UF-A7a.
  Done-when: a jvmTest emits a hub change while editing and asserts the state is still `Editing` with the
  local steps and the banner is shown; **Load theirs** swaps in the fetched graph; an Api test asserts
  `UpdatedBy` is the saving user.
- **S-UF-A7c** [fix] **Test** runs the draft on screen with a generated sample-input form.
  `PipelineTestRunRequest` takes an optional graph; the server validates and runs it in capture mode exactly
  like the saved one. Sample inputs become a form generated from the pipeline trigger's variables,
  pre-filled with realistic values the user can change (no `key=value` lines). The result shows chat lines
  in the A2a style, then each captured effect in order with its timing.
  Files: `IPipelineTestRunService.cs` + implementation, `PipelinesController.cs` (Api), `openapi/v1.json`,
  `PipelinesController.kt`, the test-run dialog.
  Depends: S-SDK-TEST-FIRE (its per-trigger typed samples), S-UF-A2a.
  Done-when: an Api test runs a draft graph that differs from the saved one and asserts the captured chat
  line comes from the draft; a jvmTest for a follow-triggered pipeline renders input fields for the follow
  variables pre-filled, with no free-text variables box.
- **S-UF-A7d** [fix] Removing a block happens at once with Undo, and Ctrl+Z / Ctrl+Shift+Z walk the draft history.
  "Removed If block and 3 steps inside it · Undo" for 10 s; the draft keeps an undo/redo stack of every chain
  edit.
  Files: `PipelinesController.kt` (history stack), `PipelinesScreen.kt` (key handling), strings.
  Depends: S-UF-X1, S-UF-A7a.
  Done-when: jvmTests: removing an If block with 3 children emits the Undo feedback with the count; Undo
  restores all four steps in their lanes and order; Ctrl+Z / Ctrl+Shift+Z step back and forward.
- **S-UF-A7e** [fix] **New pipeline** opens the editor on the new pipeline directly.
  Today create returns to the list (`PipelinesController.kt:179-188`); after a successful create, open the
  editor on the returned id (same no-extra-hop rule S-OBS-10 gave code scripts).
  Files: `PipelinesController.kt`.
  Depends: none.
  Done-when: a jvmTest asserts create ends in `Editing` for the new pipeline id.
- **S-UF-A8a** [fix] Code Scripts copy says JavaScript.
  The page subtitle and any other "Lua" copy say the scripts are JavaScript (they run on Jint), en and nl.
  Files: strings (en + nl).
  Depends: none.
  Done-when: a resource test fails on "Lua" in any en or nl string.
- **S-UF-A8c** [fix] The quote form picks the game from categories, defaulting to the current game, and previews the `!quote` line.
  Game becomes a category search picker (the same category search the stream-info editor uses) that
  defaults to the live game; the form shows the `!quote` line it will produce through the A2a panel.
  Files: `feature/quotes/ui/QuotesScreen.kt`, `QuotesController.kt`, strings.
  Depends: S-UF-A2a.
  Done-when: a jvmTest with a live game opens a new quote with that game selected, and the preview renders
  the same line the quote builtin produces for that quote.
- **S-UF-N1a** [fix] Sidebar groups open in one click, start collapsed except the active page's group, and
  remember their state.
  Fix the two-click Setup toggle (`ShellScreen.kt:882-885`, the default and the negation disagree). Every
  group starts collapsed except the group holding the active page. The expanded set is saved per user and
  restored on reload.
  Files: app `feature/shell/ui/ShellScreen.kt`, a small prefs store.
  Depends: none.
  Done-when: a compose test shows one click expands Setup. On first render only the active page's group is
  expanded. A toggle survives a recreated shell.
- **S-UF-A5b** [fix] The Alerts page is removed; its route opens Event Responses with the On-stream alerts filter.
  Today two pages edit the same rows with different powers and Alerts asks for raw keys
  (`AlertsScreen.kt:517-523`). Remove the Alerts screen and its controller; the `alerts` route and nav entry
  redirect to Event Responses with the **On-stream alerts** filter on; anything only Alerts could do (the
  alert queue view) moves into Event Responses first.
  Files: `feature/alerts/` (delete), `feature/eventresponses/`, shell routes/nav, strings.
  Depends: S-UF-A5a; the sidebar entry itself moves with S072 and S-UF-N1a.
  Done-when: a navigation test opening the alerts route lands on Event Responses with the filter active; the
  alert-queue panel renders there; no file under `feature/alerts/` remains.
- **S-UF-N5a** [fix] An unknown or forbidden route explains itself and offers Back to Dashboard, instead of
  silently opening Dashboard.
  A forbidden page renders its own title and "You need <readFloor role> or higher to open <Page>. Ask the
  broadcaster for access." with **Back to Dashboard**. An unknown slug renders "That page doesn't exist" with
  the same button. A live permission drop on the open page shows the same explanation.
  Files: app `feature/shell/ui/ShellScreen.kt`, `ShellRouteSlug.kt` (`find` in place of `parse` for the
  intent), strings.
  Depends: none.
  Done-when: a compose test as a Moderator, opening Pipelines with its floor raised, renders the explanation
  naming Editor, and the button selects Dashboard. A `PermissionChanged` that revokes the open page shows the
  explanation, not Dashboard.
- **S-UF-N5b** [fix] Browser Back on the first shell page warns before signing out.
  The first `disconnectRequests` emission shows "Press Back again to sign out" for 3 s and re-pushes the
  current page entry. A second Back within 3 s signs out.
  Files: app `App.kt`, `RouteStore` (web actual).
  Depends: none.
  Done-when: a test with a fake clock shows that one Back leaves the session connected and shows the notice,
  that two Backs within 3 s call logout, and that two Backs 4 s apart do not.
- **S-UF-I3a** [fix] The Settings page scrolls, so every card is reachable.
  Today the root `Column` does not scroll and the stream-info `Box` holds `weight(1f)` above nine unweighted cards
  (`SettingsScreen.kt:316-381`), so Billing and Event journal are cut off on a laptop-height window. Build: one
  scroll container for the page; the stream-info card sizes to content.
  Files: `app/feature/settings/ui/SettingsScreen.kt`. Depends: none.
  Done-when: a jvmTest at an 800 dp tall window scrolls to and clicks the Event journal export button; the
  stream-info form still saves.
- **S-UF-I3b** [fix] A red-bordered Danger zone holds Leave, Reset and Delete; Delete channel shows its counted
  preview and needs the channel name typed.
  Today Delete sits beside Join (`SettingsScreen.kt:740-805`) and confirms with static copy
  (`SettingsScreen.kt:408-421`); the delete preview (`ChannelsController.cs:516`) is never shown. Build: Join stays
  in General; a separate Danger zone card (destructive border token) holds Leave channel, Reset configuration and
  Delete channel; Delete opens the S-UF-X2c type-to-confirm dialog showing the delete-preview counts and the 30-day
  restore window, armed only when the typed text equals the channel name; the button reads "Delete channel".
  Files: `SettingsScreen.kt`, `SettingsController.kt`, `ChannelsApi.kt`, `strings.xml`. Depends: S-UF-X2c.
  Done-when: a jvmTest asserts the dialog renders the preview counts from a fake API, the button stays disabled
  for a wrong or partial name, and a correct name issues exactly one `DELETE /channels/{id}`.
- **S-UF-I3c** [fix] Reset configuration lists what it resets, with counts, before it runs.
  Today `POST /channels/{id}/reset` deletes every `Configuration` row for the channel with no preview and no
  destructive marker (`ChannelsController.cs:553-574`); the dialog says nothing countable. Build:
  `GET /channels/{id}/reset-preview` counts the `Configuration` rows reset deletes, grouped by area (settings, TTS,
  shield, blocked terms, …) with plain labels; the reset endpoint gains `[DestructiveAction(HasCountedBlastRadius =
  true)]` and refuses a stale confirmed count; the confirm lists the groups and names the act ("Reset
  configuration"). It lists only what reset really deletes (see Notes: the spec's example counts commands, which
  reset does not touch).
  Files: `Api/Controllers/V1/ChannelsController.cs`, a reset-preview service, `SettingsScreen.kt`, `strings.xml`.
  Depends: S-UF-I3b.
  Done-when: an Api test seeds known Configuration rows, asserts the preview counts per group, and asserts a reset
  carrying a stale count deletes nothing; a jvmTest asserts the dialog lists the groups.
- **S-UF-I4a** [fix] Discord consent comes from the OAuth identity of whoever connected the server; no id is typed.
  Today `InstalledByDiscordUserId` is hard-coded null (`DiscordOAuthController.cs:257`) and re-approving after a
  revoke needs a typed Discord user ID (`DiscordScreen.kt:434`, `strings.xml:2517`). Build: the install exchange
  reads the authorizing user (`identify`, `GET /users/@me`) and stores it as `ApprovedByDiscordUserId`; re-approval
  re-runs the install OAuth (or uses the dashboard user's linked Discord identity) instead of a text field; the
  typed-id dialog and `discord_consent_discord_user_id` are removed.
  Files: `Api/Controllers/V1/DiscordOAuthController.cs`, `Infrastructure/Discord/DiscordGuildService.cs`,
  `DiscordScreen.kt`, `DiscordController.kt`, `strings.xml`. Depends: none.
  Done-when: an Api test runs the callback against a fake Discord token + `/users/@me` handler and asserts the
  stored approver id; a jvmTest asserts no text field asks for an id and Re-approve starts the OAuth URL.
- **S-UF-I5a** [fix] Disconnecting the bot account confirms with the visible consequence, on both surfaces.
  Today Integrations disconnects at once (`IntegrationsScreen.kt:337`) and Settings' confirm copy names a shared
  bot that may not exist (`strings.xml:1433`). Build: a bot-disconnect blast radius from the server that resolves
  the real fallback sender (`HelixChatProvider.ResolveBotSenderAsync` order: shared platform bot, else the
  streamer's account); both surfaces use the counted dialog; with no shared bot the text reads "The bot will type as
  {streamer} with the {marker} marker until you connect another bot account." (marker from the D5 setting); button
  "Disconnect bot account".
  Files: `Api/Controllers/V1/IntegrationsController.cs` (or ChannelsController bot route), `IntegrationsScreen.kt`,
  `SettingsScreen.kt` (ChannelBotSection), `strings.xml`. Depends: none.
  Done-when: an Api test asserts the preview names the streamer as fallback with no shared bot and the shared bot
  when one is connected; a jvmTest asserts clicking Disconnect issues no request until the dialog's confirm.
- **S-UF-I5b** [fix] "Take control" states what changes on Twitch before it runs.
  Today one click recreates the reward (`RewardsScreen.kt:244,270`). Build: a confirm that says a new reward is
  created under the bot with the same title, cost and prompt; the old reward's Twitch redemption history stays with
  the old reward; if the title is taken the streamer must delete the original in the Twitch dashboard first
  (`RewardService.cs:890-898`); button "Take control on Twitch".
  Files: `RewardsScreen.kt`, `strings.xml`. Depends: none.
  Done-when: a jvmTest asserts no recreate call before confirm, and exactly one after.
- **S-UF-I5c** [fix] A Bundles import with Overwrite lists the items it will replace before running.
  Today Overwrite installs immediately (`BundlesScreen.kt:506,796`) and inspect returns no conflicts
  (`IBundleImportService.cs:79-83`). Build: `BundleInspection` gains `Conflicts` (kind, name, existing id) computed
  against the channel; choosing Overwrite with conflicts shows them in a confirm, button "Replace {n} items"; zero
  conflicts runs without a dialog.
  Files: `Application/Marketplace/Services/IBundleImportService.cs`, its implementation, `BundlesController.cs`,
  `BundlesApi.kt`, `BundlesScreen.kt`, `strings.xml`, OpenAPI snapshot. Depends: none.
  Done-when: an Infrastructure test inspects a bundle with two name clashes and asserts both conflicts; a jvmTest
  asserts Overwrite shows the two names and imports only after confirm.
- **S-UF-I6b** [fix] The public request page link is called "Public request page" and rotating keeps the old link
  working for 15 minutes.
  Today it is "Song Request Page Link" and rotation revokes at once (`strings.xml:1297-1301`,
  `SongRequestPageTokenService.cs:116`). Build: rename (en+nl); rotation stores the previous token with a 15-minute
  expiry (the widget `PreviousOverlayToken` pattern), the public endpoint accepts both until then; the rotate dialog
  says so.
  Files: `Infrastructure/Music/SongRequestPageTokenService.cs`, its entity (BOTH migration sets),
  `PublicSongRequestController`, `SongRequestsScreen.kt`, `strings.xml`. Depends: S-SR-PUBLIC-PAGE.
  Done-when: a test rotates, asserts the old token still resolves at +14 min and is rejected at +16 min (fake clock).
- **S-UF-T11d** [fix] The "Read aloud (TTS)" step names `{input}` "Viewer's message" and its help uses single
  braces.
  Build: the helper label `template_helper_input` becomes "Viewer's message". `pipeline_play_tts_text_help`,
  `pipeline_tts_synthesize_text_help` and the send-message helps (`strings.xml:5016,5018`) use `{user}`.
  Files: `strings.xml` (en + nl).
  Depends: none.
  Done-when: a test fails if any `pipeline_*_help` string contains `{{`, and the play_tts text field's helper
  picker lists "Viewer's message" for the Pipeline context.

## Phase 5 — new model (D1 one channel / D2 any login) — merged only after Phases 0–4; S023/S024 are the minimum the viewer-identity fixes need

- **S019-remaining** `PlatformConnection` model — `Channel` loses `Provider`; attaching a SECOND
  platform to an existing channel; data migration folds existing sibling channels into one (U·C0,
  spec `platform-identity.md`). Done-when: a Twitch+Kick streamer is ONE `Channel` with two
  connections; all tenant-scoped reads unchanged.
- **S023-remaining** Viewer identity key sweep — open: `BuiltinCommandContext` carries no
  `Provider`, so `PermitBuiltins` and `AccountAgeBuiltin` default to Twitch (plumb it through every
  builtin dispatch); `ChannelsController.EnterModeratedChannel` is genuinely Twitch-only today
  (Helix Get Moderated Channels has no Kick/YouTube equivalent yet); `*TwitchUserId` →
  `*ExternalUserId + *Provider` on the 18 entities; delete `PlatformType` (U·C0). Done-when: build +
  migration green; no call site defaults the provider.
- **S024** Viewer linking — `LinkAsync` absorption (§3.1a), `IViewerMergeParticipant` + the eight
  participants, `ViewerRowAbsorbedEvent` published (U·C0). Done-when: a viewer who chatted on Kick then
  links Twitch ends with ONE User row and ONE balance (test).
- **S025** Login any platform (D2) — `auth/providers`, `auth/{provider}/device`, `/poll`; first
  login creates the channel, others attach (spec `identity-auth.md`). Done-when: a Kick-only
  streamer signs in, onboards, and has a channel with one Kick connection.
- **S026** Onboarding "connect more platforms" stage (non-blocking) + channel-bot connect via device
  code with poll/refresh (U·B6, spec `onboarding-setup.md`). Done-when: wizard attaches a second
  platform; bot-account card updates without reload.
- **S032** Combined management fan-in/out — combined chat composer with target selector + per-target
  result; per-line platform badge incl. Twitch; render 7TV zero-width emote stacking; `provider` on
  `ChannelSummary`; (provider,id) dedupe + reorder window; timers/event responses/announcements
  platform target sets with per-platform rate limit + duplicate suppression; one go-live form with
  per-platform results; per-platform viewer breakdown + total + cross-platform stream session;
  owner-scoped "ban on all my platforms"; earning credits the linked person (U·C1). Done-when: live
  on three platforms, one timer posts once on each; one ban bans the human everywhere; Home shows
  per-platform viewers + total.
  UF·M5: The ban control carries a checkbox 'Ban across all linked platforms of this person', on by
  default, listing the platforms by name ('Twitch, Kick'), and one press bans every linked identity (spec
  M5).
- **S-VIEWER-RESTRICTION-D1** `ChannelModerationStanding` is per platform (`Provider` + raw `UserId`
  + `Standing`), so a Twitch mute does not mute the same human's Kick identity. `moderation.md` J.12
  (D1) is `ChannelViewerRestriction` keyed on `Users.Id` with
  `Restriction {muted|shadowbanned|blacklisted}`. Rename and rekey it, resolve the inbound identity
  through `IUserIdentityService.ResolveUserAsync` at the enforcement seams, and migrate existing
  rows in BOTH migration sets. Done-when: a mute set on a viewer's Twitch identity also ignores the
  same human's linked Kick identity, and a blacklist drops their chat at all three publishers.
- **S-COMMUNITY-MULTIPLATFORM** `CommunityController` is Twitch-only: the followers, VIP, subscriber
  and moderator tabs come from Helix, and viewer lists and pick options key on `User.TwitchUserId`,
  so a Kick- or YouTube-only viewer never appears and cannot be acted on. Done-when: the Community
  lists key on the person (`Users.Id`) and show each viewer's platform identities; a Kick-only
  chatter appears and can be looked up and moderated; platform-only tabs name their platform and
  read only from an API that really exposes them (never fabricated).

## Phase 6 — new features and personas (D3 X, D4 viewer, moderator-of-many, new capabilities)

- **S044** Helper expansion + presets — math/string/date namespaces, general `{{ns.key:arg}}` grammar,
  any-step output, `{{stream.viewers}}`; raid preset (shoutout → raid → countdown → optional OBS/
  Spotify) + `channel.raid.out` seeded on onboarding (W·§6, U·A1 i4). Done-when: `!raid <user>` from a
  fresh channel runs every step or names the failing one.
- **S054** TTS segments — `TtsSegment` list request, per-segment voice mode, ONE `tts_speak` payload
  with ordered segments; `BypassQueue`; sub-streak preset (U·A5, spec `tts.md` §6). Done-when: the
  owner's example plays as one utterance with two voices.
- **S056** Discord triggers + action — `go_offline` + `hype_train` with handlers; action carries own
  channel/template/embed/ping; Event Responses Discord preset (U·A6 i2/3/6).
- **S057** Discord live-role sync — REST endpoints to create/update/delete `DiscordLiveRoleConfig`
  plus the dashboard role picker and per-guild config UI (U·A6 i4). Done-when: go live → roles on;
  offline → roles off.
- **S059** Alert system surface — moderation retraction (`widgets-overlays.md` §2a) still has to be
  honoured on every surface; the domain event and hub plumbing are built. One slice each:
  - **S-RETRACT-b** Chat + Alert surfaces honour it (remove the message node / cancel an on-screen or
    queued `AlertQueueEntry` — this is where the original "queued-but-undelivered" note lives).
  - **S-RETRACT-c** TTS surface (`tts.md` §3.4a — stop mid-sentence, drop matching queued utterances).
  - **S-RETRACT-d** Sound clip + custom-widget SDK surfaces (`widget-sdk.md` — the SDK's chat/alert
    helpers retract automatically; a bespoke widget reading raw events must retain `SourceMessageId`
    to correlate).
- **S071** Notification centre + Home (U·B6, K) — open: the action-required inbox covers only dead
  integration tokens and held AutoMod messages. Missing scopes (every gap is progressive by design),
  failed timers (`Timer` has no run-failure tracking) and pending unbans (only live-fetchable under
  an operator token) have no honest backing signal yet. Done-when: timers record run failures and
  the inbox lists them, and each remaining category is either backed by a real signal or documented
  as not applicable.
- **S072** IA reconciliation (U·B6) — open: `MyData` is still a Setup page with a Moderator floor
  (it belongs on the participant rung, see S077); Settings is one scrolling column, not tabbed; the
  Commands manage floor is Moderator; `frontend-ia.md` drifts from the shipped nav for Event
  Responses/Alerts placement. 🔒 Admin via profile menu + chrome swap: `Beheer` renders as its own
  standalone sidebar link under CONFIGURATIE, not folded into the profile-menu dropdown alongside
  theme/Account as first specced — confirm the profile-menu destination is still the intended target
  before building it (the sidebar link may have been a deliberate later call). Done-when: `MyData`
  opens for a role-less viewer, Settings is tabbed, and `frontend-ia.md` matches the shipped nav.
  UF·N1: The non-pinned sidebar is grouped by job. Chat holds Chat, Multi-Chat, Commands, Chat Triggers,
  Voice Triggers, Timers, Quotes and Random Responses. Stream holds Overlays, Event Responses, Schedule,
  Analytics, OBS and VTube Studio. Automate holds Pipelines, Code Scripts, Custom Events, Webhooks,
  Automation and Federation. Moderation, Loyalty, Audio, Community and Setup keep their pages. "Alerts &
  Events" has no nav entry of its own: it is the **On-stream alerts** filter chip inside Event Responses
  (after S-UF-A5). `frontend-ia.md` and `ShellNavTest` match this grouping.
  UF·I3: Settings has six tabs (General · Bot voice · Bot account · Permissions · Billing · Data and
  danger) as the I3 table in `user-flow-behavioral-spec-2026-10-03.md` lists them; the Bot account tab
  absorbs the 'White-label bot' card and Integrations' 'Bot account' row so the bot account has one home;
  stream info (title, category, tags) leaves Settings for the Home Prep layout (S-UF-H1); Appearance
  (emoji style) moves to the profile menu under 'On this device'; a test asserts every former Settings
  card is reachable through exactly one tab.
- **S031** X Live as a platform connection (D3) — `IntegrationProvider.twitter`,
  `AuthEnums.Platform` gains `x` (D3), login + connection, chat read/send via X's API to the extent
  it exposes (document limits), events where available (U·C4, spec `platform-identity.md` §10).
  Done-when: X connection attaches; chat lines carry `x`.
- **S073** Moderated-channel discovery per platform; reconcile covers moderator-mode tenants +
  "roles last synced"; live dot bound to `isLive`; roster refresh on `StreamStatusChanged`; roster
  cached (U·C6).
  UF·N4: The channel header's dot is red while the channel is live and grey when offline, never hardcoded
  (`ShellScreen.kt:951-959`). It updates from `StreamStatusChanged` with no reload.
- **S074** Never act on the wrong channel — stale active-channel pin detected + cleared + explained;
  `primaryChannel()` hard-fails instead of substituting; switch splash with timeout/error; active role
  in the sidebar header (U·C6). Done-when: revoked access yields an explained state, never a 403 loop.
  UF·N4: Switching channels keeps the sidebar and the current page. Only the content area shows a
  skeleton while the new access resolves; during the switch the sidebar shows only the pages valid for
  both roles, never the old role's extra pages. If the target channel lacks the current page, its
  Dashboard opens with the toast "<Page> isn't available on <channel>".
- **S075** Cross-channel awareness — hub joins every roster channel for alert/mod classes; attributed
  notifications with click-through; `GET /me/moderation/queue` + "my channels" home; queues re-fetch
  on `ModAction` (U·C6). Done-when: a mod of 4 channels sees which is live and gets attributed alerts.
  UF·H5: A caller whose role on the active channel is below Editor gets **Mod home**, never the
  streamer's tiles. It shows, in order: **Live now**, one row per live moderated channel with viewers,
  the held AutoMod count, the review-queue count and **Open chat**; then the active channel's held
  messages and queues; then the caller's own last 10 mod actions across channels, each with Undo where
  the action allows it (S-UF-M-items). The queue counts stay live for every live moderated channel, not
  only the active one. A Moderator never sees Followers, Subscribers or Donations tiles.
  UF·M6: The Queue page has a Channel filter whose first option is 'All channels I moderate', listing
  every moderated channel's queue items in the one oldest-first list (spec M6).
- **S077** Viewer entry — switcher source "channels I appear in"; honest empty state; `MyData` floor
  to Everyone on the participant rung (D4); channel chip shows the channel; routes/deep links
  (U·C5). Done-when: a role-less viewer's first run lands on a usable Me page.
  UF·F8: The viewer home lists one card per channel on this instance where the viewer has history, each
  with the balance and its currency name, the watch streak, the leaderboard rank and the current song
  request if any. The cross-channel settings (S078) follow below the cards. A viewer with no history sees
  "Chat in any channel that uses NomNomzBot and it will show up here" above a list of channels that are
  live now. The participant surface never calls a Moderator-floored endpoint such as `dashboard/stats`
  (`ParticipantController.kt:129`), so no 403 text renders.
  UF·V5: `!mydata` replies with a link to the viewer's own data page, which a role-less viewer can open
  (spec V5).
- **S078** Me page — GDPR export/erase, linked platforms (identity API client), own TTS voice, standing,
  profile fields, leaderboard opt-in read, per-jar contributions, own SR requests + public page link,
  preview-as-viewer forces Everyone (U·C5).
  UF·T10: The Me page's **My TTS voice** card uses the S-UF-T3b searchable picker with play buttons,
  shows "Channel default" when nothing is set, offers **Reset to default**, and offers "Use everywhere"
  or "Only in this channel". "Use everywhere" is a cross-channel viewer voice that every channel's
  resolver reads after the per-channel pick, in both migration sets. A jvmTest proves the card calls
  `me/voice`, and a server test proves an everywhere-voice speaks in a second channel where the viewer
  set nothing.
- **S079** Viewer giveaway entry/my-entries endpoint + card (or drop from IA) (U·C5).
  UF·V3: The viewer's giveaway card states the draw method (uniform random, or weighted by tickets) (spec
  V3).
- **S-BOT-IMPORT** One-click import from other chat bots (owner 2026-09-30: "a one-click import from
  streamelements and the others for their enabled commands and quotes"). One importer per service
  (StreamElements, Nightbot, Streamlabs Cloudbot, Moobot, Fossabot, Wizebot, Streamer.bot — each
  service's export path (API, OAuth scope or file) researched and recorded first); enabled commands
  land as custom commands with their variables mapped to our helpers (an unmappable variable is
  named, never dropped silently); quotes land in Quotes with author and date kept. A preview lists
  what will be created, skipped and renamed before anything is written. Done-when: from a live
  StreamElements channel, one click imports every enabled command and every quote, and a re-run
  creates no duplicates. Research 2026-09-30 (verify each against a live channel before building):
  StreamElements = API, `bot:read` OAuth2 or JWT, commands `GET /kappa/v2/bot/commands/:channel`,
  quotes route unofficial; Nightbot = API `GET /1/commands`, OAuth2 scope `commands`, no quotes system;
  Fossabot = unauthenticated public-commands endpoint (unverified); Moobot and Streamer.bot = export
  file only; Streamlabs Cloudbot and Wizebot = no public read API found. An `Import` module with a
  StreamElements export DTO already exists (`Application/Import`, `ProviderImportService`) — extend it.
- **S-CONTRIB-GUIDE** Owed only the proof: file one issue through each form (bug, feature, platform
  API gap) as a person and one as a bot (`gh issue create` with the form headings and the yaml block), then
  close them. Done-when: an agent reproduces each defect from the issue alone, without asking a question.
- **S-UF-V1d** [new] A near-miss command typo gets "Did you mean !socials?", at most once per viewer per 10 minutes.
  Build: only when exactly one enabled command the viewer can run is 1 edit away. An unknown word with
  no near match stays silent.
  Files: `ChatMessageHandler.cs` (unknown-command branch), shared Levenshtein helper. Depends: S-UF-V6a
  (the hint is a priority-3 notice).
  Done-when: `!socails` gets one hint, a second `!socails` within 10 minutes gets none, and `!xyzzy`
  gets none. With two commands 1 edit away, no hint is sent.
- **S-UF-I1c** [new] Each connected card shows its last successful use in plain words.
  Today nothing records use (`IntegrationConnection` has only `LastRefreshedAt`/`LastErrorAt`). Build: a
  `LastSucceededAt` + `LastSucceededAction` on the connection (BOTH migration sets), stamped by each provider's
  success path (Spotify queue/play, Discord post with channel name, YouTube chat read, TTS synthesis), rendered as
  "Last played a song 3 min ago" / "Last posted to #live 2 days ago"; absent (not "never") when nothing was recorded.
  Files: `Domain/Integrations/Entities/IntegrationConnection.cs`, both migration sets, provider success paths,
  `ChannelIntegrationDto`, `IntegrationsScreen.kt`, `strings.xml`. Depends: S-UF-I1a.
  Done-when: a test queues a Spotify track and asserts the stamped action and time; the card renders the relative
  sentence from that row; `migration-check.ps1` passes on a populated database.
- **S-UF-I1d** [new] A Test button on each card does one real, harmless round trip and reports on the card.
  Today no integration has a Test action. Build: `POST /channels/{id}/integrations/{integrationId}/test`
  (write-cheap rate tier, D11) running per provider: Spotify reads player state ("Connected · Playing on Desktop");
  Discord posts "Test from NomNomzBot" to the chosen announcement channel and returns the message link; TTS
  synthesizes "This is a test" in the default voice and plays it on the Audio Source page (reuse the TTS page's
  existing overlay test, `strings.xml:807-810`); YouTube reads the channel name. A 401/403 from the test feeds the
  S-UF-I1b path. The result renders inline on the card, never as a page-level toast only.
  Files: `Api/Controllers/V1/IntegrationsController.cs`, a per-provider `IIntegrationProbe`, `IntegrationsApi.kt`,
  `IntegrationsScreen.kt`, `strings.xml`. Depends: S-UF-I1a, S-UF-I1b.
  Done-when: per provider, a test against a fake handler asserts the outbound request shape and the card text;
  the Discord probe asserts one real `create message` call and the returned link; a 401 probe flips the card to
  "Needs reconnecting".
- **S-UF-O1b** [new] With an OBS connection, **Add to my current scene** creates the browser source for the widget.
  The panel's primary becomes **Add to my current scene**: the bot sends obs-websocket `CreateInput`
  (`browser_source`, the widget URL, its declared width and height) into the current program scene, named
  "NomNomzBot · <widget name>"; a name clash or a failed request says why and falls back to the manual steps.
  Files: `Infrastructure/Obs/ObsControlService.cs` + its interface, `ObsController.cs` (Api), `openapi/v1.json`,
  the S062 panel.
  Depends: S062 (the panel), S-UF-O4b.
  Done-when: an Infrastructure test with a fake OBS transport asserts one `CreateInput` with kind
  `browser_source`, the widget URL, width/height and the scene name; a jvmTest asserts the button shows only
  when the OBS connection is up.
- **S-UF-T2b** [new] One server call sets TTS up from four answers: triggers, channel voice, safety preset, on.
  Today a reward that reads the viewer's message takes about 12 steps across Rewards and Pipelines. The
  presets' "approval only for viewers first seen in the last 7 days" has no backing field
  (`TtsConfig.ModApprovalRequired` is all-or-nothing, `TtsConfig.cs:57-58`).
  Build: new channels start with TTS **off** (`TtsConfig.IsEnabled` default false). A setup endpoint takes:
  - the checked triggers:
    - a "Text to speech" reward: 500 points, viewer text required, kept in the redemption queue so it can be
      refunded, bound to a pipeline that speaks "{user} says: {input}";
    - cheers of at least 100 bits;
    - sub and resub messages;
    - a `!tts` command for subscribers;
  - a voice id;
  - one preset: Relaxed (300 characters, never approve), Balanced (200, approve only viewers first seen in the
    last 7 days) or Strict (150, approve every message). All three keep the profanity filter and link and
    emote removal on (S-UF-T5a).

  It creates the reward, pipeline, event responses and command, applies the numbers, adds an approval-scope
  field (never / new viewers / every) in BOTH migration sets, and turns TTS on. A re-run creates no
  duplicates.
  Files: a new `TtsSetupService` + route, `TtsConfig` (approval scope), Rewards, Pipelines, Event Responses,
  Commands.
  Depends: S-UF-T5a, S-UF-T8a, S-UF-T6a.
  Done-when: one call on a fresh channel yields the reward (cost 500, input required, not auto-fulfilled), a
  pipeline whose `play_tts` text is `{user} says: {input}`, the chosen trigger rows and `IsEnabled = true`. A
  Balanced channel queues a 2-day-old viewer and speaks a 30-day-old viewer directly. A second call adds
  nothing.
- **S-UF-T2c** [new] While TTS is off, the TTS page shows **Set up text to speech** as four steps on one
  screen.
  Build:
  1. Trigger checkboxes, the reward pre-checked.
  2. Six recommended voices in the channel's language. Each plays "Thanks for the follow, Ana!" in the
     browser; one is pre-selected.
  3. The three presets with Balanced pre-selected. The numbers table is always shown under the choice.
  4. The O1 panel for the TTS source, ending on **Send a test line** with the S-UF-T2a acknowledgement.

  **Turn on TTS** calls S-UF-T2b. Every value stays editable afterwards on the Rules tab. If S-UF-T1b is
  refused, step 4 states "TTS is off-air until a provider is set".
  Files: `feature/tts/ui/` (a new setup composable), `TtsController.kt`, strings (en + nl).
  Depends: S-UF-T2a, S-UF-T2b, S-UF-T3a, S-UF-O1.
  Done-when: a jvmTest drives the four steps with defaults and asserts the setup call's body (reward checked,
  pre-selected voice, Balanced), then that the page switches to the Live tab once TTS is on.
- **S-UF-H1d** [new] Up to 5 saved stream presets (title, category, tags) apply with one click.
  Files: server a `StreamPreset` entity (BOTH migration sets), CRUD and an apply call through the existing
  stream update; app the Prep layout's Next stream card.
  Depends: S-UF-H1c.
  Done-when: an Api test shows a 6th preset is refused, and applying one sends exactly its title, category
  and tags to the stream update.
- **S-UF-T9d** [new] Optional "Lower Spotify volume while TTS plays", off by default.
  Build: a TTS setting. When on, cue start lowers Spotify through the existing music volume path and cue end
  restores it.
  Files: `TtsConfig` (both migration sets), the music volume service, the cue signal from S-UF-T9a.
  Depends: S-UF-T9a.
  Done-when: with the setting on, a cue start then end issues a lower and then a restore volume call (fake
  Spotify), and with it off issues none.
- **S-UF-N3a** [new] Song Requests shows a live count badge that turns accent when the oldest request
  waits over 60 s.
  Push a song-queue summary (count and oldest-request age) on the dashboard hub whenever
  `SongRequestQueueChangedEvent` fires. The Song Requests nav item shows the count: neutral grey while the
  oldest item is under 60 s, accent after. A collapsed group header shows the sum of its children's badges.
  Files: server Music hub broadcaster, `DashboardNotifier`, `openapi/v1.json`; app `HubEvent.kt`,
  `feature/shell/ui/ShellScreen.kt`.
  Depends: none.
  Done-when: an Api test shows a queued request pushes count 1 with its enqueue time. A compose test with a
  fake clock shows the badge grey at 59 s and accent at 61 s, and the collapsed Music header shows the sum.
- **S-UF-N4a** [new] The channel switcher sorts live channels first, searches past 5 channels, and jumps
  with Ctrl+1 to Ctrl+9.
  Order: live channels by viewer count, then the rest alphabetically. A search field appears when the user
  has more than 5 channels. Ctrl+1 to Ctrl+9 select the first nine in that order.
  Files: app `feature/shell/state/ChannelSwitcherController.kt`, `feature/shell/ui/ShellScreen.kt`.
  Depends: S073 (a live-accurate roster).
  Done-when: a unit test sorts a mixed roster into the stated order. A compose test presses Ctrl+2 and
  `activeChannelId` becomes the second channel.
- **S-UF-N4b** [new] The channel is in the URL, so a shared link opens the same channel and page.
  The route becomes `#/c/<channel-login>/<page>`. On load it selects that channel when the caller has
  access; when they do not, the N5 explanation shows. A switch rewrites the URL. Old `#/<page>` links still
  resolve.
  Files: app `core/navigation/RouteStore*`, `ShellRouteSlug.kt`, `ShellRouteMemory.kt`.
  Depends: S-UF-N5a.
  Done-when: a test parses `#/c/stoney_eagle/commands` to that channel and Commands, and an old `#/timers`
  still opens Timers.
- **S-UF-M2b** [new] Keyboard mode for chat moderation.
  Build: J and K select the next and previous line, and a selection pauses auto-scroll (M3). D deletes,
  T opens the timeout presets (1 to 4 pick one), B bans, W warns, U opens the user card. Escape returns to
  live scroll. ? lists every shortcut.
  Files: `ChatScreen.kt`, `MultiChatScreen.kt`. Depends: S-UF-M2a, S-UF-M3, S-UF-M1b.
  Done-when: a UI test presses K, K, T, 2 and the selected line's author gets a 10 m timeout call. Escape
  clears the selection and the feed follows the newest line again.
- **S-UF-M6c** [new] Keyboard actions in the Queue.
  Build: J and K move, A approves, D denies, U opens the user card.
  Files: queue composable. Depends: S-UF-M6a, S-UF-M1b.
  Done-when: a UI test presses J then A, and the second row's approve call fires.
- **S-UF-N3b** [new] The moderator Queue nav item shows the unified queue count with the same aging rule.
  The badge reads the M6 queue's count and oldest age from the same push that updates the Queue page.
  Files: app `feature/shell/ui/ShellScreen.kt`.
  Depends: S-UF-M6, S-UF-N3a.
  Done-when: a compose test shows that a held AutoMod message plus a TTS approval read "2", and resolving one
  drops it to "1" without a reload.
- **S-UF-M7a** [new] A "Chat is spiking" banner offers Shield, Followers-only 10 m, Slow 30 s and Dismiss.
  Build: when the message rate passes 3 times the stream's 10-minute average, or more than 20 first-time
  chatters arrive within 1 minute, the server pushes a spike signal. Chat shows a banner with **Shield
  mode**, **Followers-only 10 m**, **Slow 30 s**, **Dismiss**, and each button shows its on state once
  active ("Slow · 30 s"). The rate and first-chatter counts reuse `ChannelBaseline`.
  Files: a spike detector over chat events, hub push, `ChatScreen.kt`. Depends: none (shares
  `ChannelBaseline` with S-SPAM-LOCKDOWN-WIRE).
  Done-when: a detector test with a 10-minute baseline of 10/min fires at 31/min and not at 29/min, and
  fires on 21 first-time chatters in 60 s. A UI test presses Slow 30 s and the pill reads "Slow · 30 s".
- **S-UF-M7b** [new] A chat line's menu can delete all like it and time out everyone who sent it.
  Build: **Delete all like this** (same normalized text, last 2 minutes) and **Time out everyone who sent
  this**. Each shows the count first ("Time out 18 users for 10 m?"), then reports the result with Undo.
  Files: chat moderation service (normalized-text match over the last 2 min), `ChatScreen.kt`,
  `MultiChatScreen.kt`. Depends: S-UF-M4b.
  Done-when: a service test with 18 senders of one normalized text (case and whitespace differences
  included) times out exactly those 18. Undo lifts all 18 timeouts.
- **S-UF-M7c** [new] Ctrl+Shift+S toggles Shield mode from any page.
  Files: shell key handler, `ModerationApi.setShieldMode`. Depends: none.
  Done-when: a UI test on the Commands page presses Ctrl+Shift+S and the shield PATCH is sent with the
  toggled value. The toast names the new state.
- **S-UF-M8b** [new] A moderator can block one viewer from song requests, for this stream or permanently.
  Build: each row's menu adds **Block Ana from song requests** with "this stream" or "permanently".
  `!sr` from a blocked viewer gets a re-wordable refusal slot. New entity, BOTH migration sets.
  Files: Music entities + migrations, `MusicService` admission, `SongRequestBuiltin.cs`, Song Requests screen.
  Depends: S-UF-M8a.
  Done-when: after a stream-scoped block, that viewer's `!sr` is refused with the slot text, and after
  `stream.online` it is admitted again. A permanent block survives the next stream.
- **S-UF-T7b** [new] **Approve and trust Ana for this stream**: Ana's later messages that stream skip
  approval.
  Build: a second option on Approve. It records a per-stream trust for the viewer that ends with the stream.
  Files: a trust row (both migration sets) or stream-scoped cache, `TtsDispatchService.cs`, the queue row menu.
  Depends: S-UF-T7a.
  Done-when: after "approve and trust", Ana's next request in the same stream dispatches directly, and in the
  next stream it queues again.
- **S-UF-A2b** [new] **Send test to chat** sends the rendered line once, prefixed "[test]", without saving.
  Secondary button inside the A2a panel; the server sends the preview output through the normal chat router
  with a "[test]" prefix, rate-limited as write-cheap (D11) and audited as a test send.
  Files: `TemplatesController.cs`, preview service, `ChatPreviewPanel`.
  Depends: S-UF-A2a.
  Done-when: an Api test asserts one outbound chat send whose text starts "[test]" and equals the preview
  line, and that no command/timer/response row was written.
- **S-UF-A4c** [new] The empty Commands page shows six starter cards instead of "No commands yet".
  `!socials`, `!discord`, `!schedule`, `!lurk`, `!so`, `!rules`; each opens the A4a form pre-filled with its
  preview already rendered. Starter content comes from the platform template catalogue (the S-ADMIN-2 spine)
  so the owner can edit it, not from app literals.
  Files: `CommandsScreen.kt`, platform content templates for commands, strings.
  Depends: S-UF-A4a.
  Done-when: a jvmTest on a channel with zero custom commands renders six cards; clicking `!lurk` opens the
  form with name `lurk` and its reply filled, and saving creates exactly that command.
- **S-UF-A5d** [new] Each event-response row shows when it last fired ("2 h ago").
  Add `LastFiredAt` to the event-response entity (BOTH migration sets), stamped by the live dispatcher (not by
  tests), on the list DTO and pushed on the hub so the row updates live.
  Files: event-response entity + both migration sets, the event-response dispatcher, DTO, `openapi/v1.json`,
  `EventResponsesScreen.kt`.
  Depends: S-UF-A5a.
  Done-when: an Infrastructure test dispatches a follow and asserts `LastFiredAt` moved, a test fire leaves it
  unchanged, and a jvmTest renders the relative time from the DTO.
- **S-UF-A7f** [new] **New pipeline** offers six templates plus Blank.
  "Shoutout when a raid arrives", "Sound and chat line on a reward", "Welcome first-time chatters", "Song
  request with a cost", "Timed giveaway reminder", "Clip on a command", plus **Blank**; templates come from
  the platform pipeline content (S-ADMIN-2d spine) so the owner edits them; each creates the pipeline with
  its trigger and steps and opens the editor.
  Files: `PipelinesScreen.kt`, platform pipeline content seed, strings.
  Depends: S-UF-A7e.
  Done-when: a test per template creates it and asserts the resulting graph validates and its test run
  (S-UF-A7c) captures the template's chat line.
- **S-UF-V2b** [new] "@Ana your request Song is playing now," on by default; consecutive requesters merge into one line.
  Build: on track start, when the track came from a request, send the notice (a reply slot with an
  on/off setting, default on). Two requesters starting back to back within one notice window merge into
  one line.
  Files: the now-playing change handler, `MusicConfig` (toggle, BOTH migration sets), tone catalogue.
  Depends: S-UF-V6a.
  Done-when: a test advancing to a requested track emits exactly one notice naming the requester. With the
  toggle off, none. Two quick consecutive request starts produce one merged line.
- **S-UF-V5b** [new] Built-in `!points` (alias `!balance`) answers balance and rank.
  Build: "@Ana you have 1,240 Nomz, #12 in this channel." A re-wordable slot. A viewer with no wallet gets
  "you have 0 Nomz".
  Files: new `PointsBuiltin`, builtin registry + `DefaultCommandsSeeder`, tone catalogue. Depends: S-UF-V5a.
  Done-when: a test with a 1,240 balance ranked 12th returns the exact line, and `!balance` routes to the
  same builtin.
- **S-UF-V5d** [new] A public commands page, linked from `!commands`.
  Build: an anonymous, rate-limited route (D11 anonymous tier) plus a public page listing each enabled
  public command with its description, who can use it and its cooldown. `!commands` appends "All
  commands: <public base>/c/<channel-login>". The link uses the instance's public base URL, so
  self-host needs no NoMercy domain.
  Files: a public commands controller, the dashboard public route, `CommandsBuiltin.cs`.
  Depends: S-UF-V5c.
  Done-when: an anonymous API test returns the channel's enabled commands with description, role and
  cooldown, and omits disabled ones. The `!commands` line ends with that URL.
- **S-UF-N1d** [new] A Pinned group of at most 8 pages heads the sidebar, with role defaults and
  pin/unpin.
  A **Pinned** group sits at the top with at most 8 pages. The defaults depend on the role. Broadcaster:
  Dashboard, Chat, Commands, Event Responses, Overlays, Music, Moderation, Settings. Moderator: Dashboard,
  Chat, Moderation, Review Queue, Viewers, History, Song Requests, TTS. Editor: Dashboard, Commands, Event
  Responses, Timers, Overlays, Pipelines, Sound Clips, Chat. Every nav item has pin/unpin in its right-click
  menu and in a "…" overflow menu that keyboard focus also reveals. The pinned set is saved per user per
  channel on the server. A page the role cannot open is never pinned.
  Files: server a user-channel nav-prefs read/write (entity in BOTH migration sets), `openapi/v1.json`;
  app `feature/shell/**`.
  Depends: S-UF-N1a; S072 (the regroup), so pins point at final groups.
  Done-when: an Api test shows a pin persists per (user, channel) and a 9th pin is refused. On a 1080 px high
  window, a fresh broadcaster sees 8 pinned items plus collapsed group headers above the fold (a measured
  compose test).
- **S-UF-N2a** [new] Ctrl+K (Cmd+K on macOS) opens a palette that finds pages, commands, viewers and
  channels.
  The palette opens from any page and from a search field at the top of the sidebar. It searches the pages
  the caller can open, the channel's commands, viewers by name (`communityApi.searchViewers`) and the
  caller's channels. With an empty query it lists the last 5 used items first. Each result row shows its
  shortcut.
  Files: app new `feature/palette/`, `feature/shell/ui/ShellScreen.kt`, a recents store.
  Depends: none.
  Done-when: a compose test presses Ctrl+K, types "tim" and Enter, and the Timers page is selected. After 6
  picks, an empty query lists the 5 most recent first.
- **S-UF-N2b** [new] Palette actions take inline arguments, and actions the role lacks are shown greyed with
  the role they need.
  Actions: `timeout <name> 10m`, `ban <name>`, `title <text>`, `ad 60`, `clip`, `marker`, `skip song`,
  `raid <channel>`. Each runs through the same controller call and safety step as its button (H3, X2). An
  action whose action key the caller lacks is listed greyed with "Needs Editor" (the role from the
  server's 403 shape, X4), not hidden.
  Files: app `feature/palette/**`, the LiveOps, Moderation and Music controllers.
  Depends: S-UF-N2a, S-UF-H3b.
  Done-when: a keyboard-only compose test runs every H3 live action from the palette. `ad 60` starts the
  5-second delayed send. A Moderator sees `title` greyed with "Needs Editor".
- **S-UF-T4c** [new] **Silence TTS** also sits on Home's Live strip and in the command palette.
  Build: the same control and state as S-UF-T4a, on the H1 Live strip and as a palette action (N2), for
  Moderator and up.
  Files: `feature/home/ui`, the palette registry.
  Depends: S-UF-T4a, S-UF-H1, S-UF-N2.
  Done-when: pressing it on Home flips the TTS page header to "TTS paused" through the hub push, without a
  reload.
- **S-UF-I3e** [new] Changing the tone shows three sample lines in the old and new tone side by side.
  Today the tone confirm shows only follow/own counts (`ToneChangeDialog.kt:36-47`). Build: the dialog renders
  three representative builtin lines (e.g. song queued, cooldown, game won) through the real tone catalogue for both
  tones, with sample data; the existing confirm and counts stay.
  Files: `ToneChangeDialog.kt`, a tone-sample read (server renders through `ToneTemplateCatalog`), `strings.xml`.
  Depends: none.
  Done-when: a test asserts the three old/new pairs equal what the catalogue composes for those slots and tones.
- **S-UF-I4b** [new] "Send test post" posts the go-live message for real and links to it.
  Today preview only (`DiscordController.cs:270`). Build: `POST /discord/configs/{id}/test-post` renders the
  config with sample stream data and posts to the chosen channel through the bot gateway, prefixed "Test from
  NomNomzBot"; returns the message URL; the dashboard shows "Posted · Open in Discord" at the button; failures say
  why (missing permission in that channel, deleted channel).
  Files: `Api/Controllers/V1/DiscordController.cs`, Discord notification service, `DiscordApi.kt`,
  `DiscordScreen.kt`, `strings.xml`. Depends: none.
  Done-when: a test asserts one outbound create-message call with the rendered body and the returned link; a 403
  from Discord renders the permission sentence, not raw text.
- **S-UF-X8a** [new] A shortcut registry and a `?` sheet that lists every shortcut on the page.
  Today shortcuts cannot be discovered. Build: `ShortcutRegistry` in the shell; a screen registers (keys, action
  label resource, handler) while it is shown and is removed on leave; `?` (outside text fields) opens a sheet listing
  global then page shortcuts; the palette (S-UF-N2) and moderator keyboard mode (S-UF-M2) read the same registry.
  First adopters: Home live actions and the Chat composer send.
  Files: `app/core/navigation/ShortcutRegistry.kt` (new), shell, `HomeScreen.kt`, `ChatScreen.kt`, `strings.xml`.
  Depends: none.
  Done-when: a jvmTest presses `?` on Home and asserts the registered actions are listed; after navigating away
  Home's entries are gone; pressing a registered key runs its handler once; `?` typed in a text field does not open
  the sheet.

## Phase 4B — the surfaces round four found (U·Part E) — existing features, same stability-first rule

- **S100-remaining** Custom data sources truth — drop or wire `InboundWebhookEndpointId` (U·E3);
  tracked as S-CUSTOMDATA-PUSH in Phase 4.
- **S101** Supporters — provider list + capabilities from the backend (`GET /supporters/sources`),
  mode-correct connect forms (secret / socket token / OAuth connection), error state + reason, staleness-
  derived status, per-connection test; resolve `SupporterUserId` where payloads allow + amount-scaled
  earning; dedup unique-violation handled; event-type in Patreon/Treatstream dedup key; source filter;
  ingest failure counter surfaced (U·E4). Done-when: all 11 adapters connectable and truthful.
- **S102** Billing/usage truth — Usage panel reads real counts for count-capped keys, localized labels,
  unlimited rendered; `UsageQuotaExceededEvent` + `SubscriptionTierChangedEvent` consumers; `free` tier
  limits seeded; downgrade at-period-end + over-cap warning (U·E4).
- **S103** Bundles + pick lists — export all 12 types; type filter select; semver + tags chips; installed
  version compare/update; pick-list anti-repeat window, per-item weight/enable, ETag, bulk paste/import/
  reorder (U·E4).
- **S104** Media share + sound + assets — media player widget (system surface) consuming `GetNext`;
  moderation rows with thumbnail/link/name; submitted/playback events as trigger sources; paged queue;
  sound handle threaded end-to-end; upload dialog with volume/trigger/cooldown/floor; clip replace;
  asset picker wherever a media URL is configured; used-by guard; limits shown; paging (U·E2).
  UF·M6: Media-share moderation rows (thumbnail, link, name) are the shared Queue row from S-UF-M6a, not
  a separate list (spec M6).
- **S105** OBS + VTS truth — OBS page consumes OBS events; scene/source/input pickers in pipeline
  fields; source-visibility + replay-buffer on the control screen; bridge error vs offline;
  edit-reset fix; VTS probe + bridge status; inventory failure vs locked; parameter/tint control;
  endpoint prefilled + validated (U·E2).
- **S106** Stream / live-ops page — dedicated Stream destination (stream info incl. language per
  platform connection, polls/predictions with live results + hub refresh, ad countdown + snooze, raids,
  markers, clips, shield, hype train, goals, charity, guest star); errors not swallowed; raid-pending
  cleared; platform badge on every control (U·E1). Done-when: an operator runs a poll and sees votes
  move without reload.
- **S107** Schedule + journal — pickers for start/timezone/duration, edit seeds timezone, formatted rows,
  webcal subscribe URL surfaced; journal list/query endpoint + browse/filter/inspect UI; rebuild status
  polling; replay/import-legacy reachable or removed (U·E1).
- **S108** Analytics truth — failures visible, selectable window + metric, local-day boundary,
  platform-analytics client (U·E1).
- **S109** Code scripts developer experience — capability catalogue + per-script declared/granted/denied
  view with links to the toggles; all failing capabilities reported at save; SDK types failure visible;
  starter templates + capability chooser; used-by view; test-run with triggering user; execution history;
  bridge unwired capability throws; desktop editor parity decision stated in-app (U·E3).
  UF·A8: Creating a script opens a template picker ("Reply with a random fact", "Call a web API", "Keep a
  counter", plus Blank) instead of an empty source textarea (`CodeScriptsScreen.kt:190-196`); each
  template saves, compiles and passes its own test run.
- **S110** Automation + federation — Stream Deck run-pipeline/run-command action with picker; federation
  opt-in validated server-side, peer/capability pickers, Direction collected (U·E3).
- **S112** Self-host ops — version stamping (`/health/version` real); ready = migrations + EventSub,
  Degraded ≠ ready; update check + notice; pre-migration DB snapshot + documented rollback;
  backup/restore verb in deploy scripts; versioned image tags; firewall + log path documented, log
  size cap; tray parity on Linux/macOS or printed URL/PID; `.env` dev-password warning at boot;
  boot-time notice in saas mode (U·E5).
- **S113** Quality gates — in-process E2E host fixture so the suite runs by default; typed
  `ProducesResponseType<T>` on the 157 schema-less operations + a regenerate-and-diff contract test; Esc
  in the shared dialog; label the 17 icons; move the 47 literal labels to strings.xml; locale date/number
  formatter; hub reconnect jitter; `primaryChannel()` cached (S050 dependency); Wasm optimize step with a
  size budget; chat decoration + pronouns + engagement get a settings surface (U·E6, E4).

## Phase 6A — platform admin: reliable system-level management (U·Part D) — safety items first, then reach

- **S087** IAM mutation audit + guards — every assign/revoke/create/deactivate/reactivate writes an
  `IamAuditLog` row with target/role/scope; create transactional + validate-before-mutate; no duplicate
  or inactive-target assignment; last `iam:manage` holder protected; flag changes audited (U·D2).
- **S090** Support access that works — session grants scoped read-only Plane-B visibility (RoleResolver
  reads the session); list active grants; any `iam:manage` holder can end any grant; expiry reaper;
  "view as tenant" reuses preview-as-viewer (client downgrade) (U·D4). Done-when: support staff can read
  a tenant's console without impersonating.
- **S091** Platform-wide user controls — user detail endpoint (channels, identities, sessions,
  consent); platform disable/ban; `MergeIdentitiesAsync` exposed; compliance key for admin erasure
  (not `tenant:access`) (U·D3).
- **S092** Tenant ops — delete/purge + ownership transfer (writes `DeletionAuditLog`); per-tenant billing
  state + quota/limit view; re-run seeds for a tenant; rotate tenant tokens/secrets; `IgnoreQueryFilters`
  on admin lists; search by id/owner/GUID; Sort/Order honoured; real stats (no hardcoded "healthy"/0);
  rate limits + explicit target confirmation on destructive admin ops (U·D3).
- **S093** Ops visibility — EventSub session inventory per broadcaster; token health across tenants;
  worker status + queue depths; error-log surface; AdminHub connect snapshot + scoped pushes;
  break-glass/denial alerts from `IamAccessEvaluatedEvent` (U·D2/D3).
- **S094** Billing roles — `billing:write`/`billing:grant` keys on the four billing writes; `billing:refund`
  endpoint or key removed; `platform-billing` role usable (U·D2).
- **S095** Admin UI truth — render `state.error` + every slice's failure; writes route through
  `actionError`; paging on every list; refresh per tab; hub live state truthful + connect errors
  surfaced; Admin entry in profile menu gated on Plane-C roles with chrome swap + per-page
  routes/deep links (U·D6). Done-when: a 403 on any admin write is visible.
- **S096** Admin UI reach — flag editor (enable/rollout/tier/mode + per-tenant overrides); invite dialog
  (count/tier/expiry/founder); grant tier/founder actions; support-access begin/end + active list;
  impersonate confirm + justification; reasons + confirm on revoke/deactivate; Ban escalated behind
  name-echo confirm; audit filters as pickers + date range; role keys viewable + role CRUD; Channels tab
  merged into Tenants; timestamps formatted; one primary action per admin page (U·D6, K).
- **S097** System-level content — `SystemPreset` (kind command|pipeline|event-response|pick-list|tone|
  announcement, key, payload, version, enabled, origin seeded|operator) seeded from today's static
  catalogues; `/admin/presets` CRUD + `SystemPresetAdoption` (auto|optin|declined, version) with push-to-
  all and per-tenant opt-in; `Widget.IsSystem` + delete protection + restore; catalogue version stamp on
  gallery items + installed widgets ("update available"); admin kill-switch for a first-party widget and
  a builtin (`BuiltinCommandRegistry`); `PlatformNotice` (announcement/maintenance banner) read on
  bootstrap (U·D5). Done-when: the operator creates a custom command preset and every opted-in tenant gets
  it without a redeploy.

## Phase 7 — polish and structure

- **S080** Sleak pass (K) — toggles neutral + one accented CTA per screen; chat-colour clamp; accent
  derivation floor; form width cap; random-responses segmented control; chat-mode on/off state;
  concentric radius tokens; 13 px muted contrast; one identity block; re-render the six screens +
  Overlays/Economy/TTS/Integrations/Pipelines and re-run the checklist.
  UF·N4: The bottom-of-sidebar identity block is gone. The profile menu (language, reconnect, preview as
  viewer, sign out) lives in the channel header's dropdown.
- **S081** Widget component splits (W·§7/§8 i11) after S058; `WidgetGalleryItem` file-set storage first.
- **S082** Drop game redesign 🔒 mechanic; stacked-transition chat style 🔒 reference (W·§8 i5/i8).
- **S083** Render-manifest + per-page hub event-class subscriptions.
- **S084** Remaining per-widget nits (W·§8 i10), the 15 code scripts test-run on the live channel, S
  LOW/informational list.
- **S-GLYPHBUTTON-A11Y** found by S047-remaining (39314dd5): `GlyphButton.kt`'s `clearAndSetSemantics`
  wipes ancestor-contributed Disabled/stateDescription from its own semantics node, so a disabled
  `GlyphButton` only exposes its disabled state via a wrapping node (e.g. `ManageGate`'s `Box`) — affects
  every disabled icon-button app-wide, not just the pipelines Test action. Done-when: a disabled
  `GlyphButton` reports Disabled/stateDescription on its OWN semantics node (assistive tech reads it
  without depending on a specific wrapper), proven by a jvmTest on the component directly.
- **S-AUDIT-REMAINDER** Interactive-surface classes still open (moved from the retired 2026-07-20 audit
  ledgers). Re-verify each against the live client before fixing; drop any that no longer reproduce.
  (1) **C5 stale-after-write:** Discord roles card (`DiscordScreen.kt`, roles keyed on
  `connection.id`, role add/edit/delete/post never re-fetch); Economy `JarManageDialog` memberships
  (`LaunchedEffect(jar.id)`, invite/accept/remove reload the page, not the dialog; a `getJar` failure
  reads as "no memberships"). (2) **C1 can't-unbind:** Timers pipeline, ChatTriggers pipeline→text and
  Rewards limits/colour cannot be cleared (`explicitNulls=false` drops the null) — needs an explicit
  clear flag on the update DTOs. (3) **C6 empty-state:** Roles list still shows Empty instead of
  Ready. (4) **C4 capability without UI:** Admin FeatureFlags toggle, grantTier/grantFounder/
  beginTenantAccess, federation peer keys, settings validateInvite, schedule cancelSegment (one
  occurrence), games revokeConsent, chat slowModeDelay/followersOnlyDuration inputs. (5) **C9 pickers:**
  Economy jar contribute/withdraw/invite and Participant Points/Store transfer take raw ids — use
  `SearchPickerField`. (6) Single defects: `ScheduleScreen` `SegmentDialog` edit seeds timezone/
  duration blank while `canSave` requires them (title-only edit impossible); Economy `AccountsSection`
  reads the first page only (`EconomyApi.accounts`), so accounts past the first page are unreachable;
  analytics opt-out failure feedback;
  `ChatController.ban` partial-failure surface; `AnimatedImageDecode` Skia bitmap leak (`.close()` per
  frame); file-picker `<input>` leak; CustomEvents field-map serialization; `run_code` `code_script_id`
  picker; drop the unsupported "svelte" framework option. Done-when: each fix has a consequence test
  (state change visible after the write) and a rendered-client check.
  UF·X3: (3) the Roles empty state uses S-UF-X3a's `EmptyFirstUse`; (5) the raw-id pickers use S-UF-X5a's
  viewer picker.
- **S-READINESS-REMAINDER** Configuration gaps left after the 2026-07-19 readiness sweep: music
  playback-volume slider on the Music screen (`song_volume` action and `SetVolumeAsync` exist); direct
  VTube Studio model-move control (`vts_move_model` action exists, no screen control); Assets
  tags/folders; a click-through of economy, webhooks, sound and OBS-mixer screens on the rendered
  client; Helix `schedule/icalendar` and `clips/downloads` coverage checks (full external-API coverage
  rule — verify against live docs first).
- **S-APPTESTS-FLAKE** `NomNomzBot.Application.Tests` fails about 1 run in 20 and the failing test is
  not named. Done-when: the next red run's test name is captured and that test is fixed so it fails
  only for the right reason.
- **S-COVERAGE-REMAINDER** External-API coverage gaps found by the retired requirement-test pass
  (the `Requirements/` suites are not in the tree). Verify each against the live API docs and current
  code, then implement (full-coverage rule: a gap is added, never dropped): YouTube
  `liveChatModerators` (no code hit today); Discord webhook management (no code hit today) and the
  remaining Discord guild surface (edit/delete message, list members, create channel) beyond
  `IDiscordBotGateway`; Discord OAuth moved into the descriptor-driven `IOAuthProviderRegistry`
  instead of the bespoke `DiscordOAuthController`; Twitch Helix analytics/conduit/extensions/drops
  are org-gated or opt-in and stay under the ROADMAP owner-confirm list. Done-when: each capability
  has a service method, a route, and a test that asserts the outbound request shape.
- **S-LEADMOD-APP-RENAME** 🔒 pending owner answer on the LeadModerator display name. Rename Kotlin
  `ManagementRole.SuperMod` to `LeadModerator` plus its string keys (the server already says
  LeadModerator). Done-when: no `SuperMod` identifier or string key remains under `app/`, and
  `ShellNavTest` and the manage-gate tests pass.
- **S-RETIRE-LEGACY** delete `PermissionsController` + `IPermissionService` + `PermissionService`,
  the unused `commands:builtin:*` seed keys in `ActionDefinitionSeeder`, and the legacy
  `ChannelSubscription` entity (both migration sets). Done-when: a full caller trace (DI
  registrations, reflection scans, routes, openapi, app client) shows no caller before deletion, and
  build, both migration checks and `ApiContractTest` are green after it.
- **S-UF-O4a** 🔒 [fix] A widget row shows name, status chip, toggle, **Test** and **Configure**; the rest moves to an overflow menu.
  Owner decision: the spec reaches **Edit code** only through **Clone to edit** so an installed widget always
  stays updatable, but today editing a catalogue widget in place is deliberate (`isCustomized` + "Reset to
  default", `core/network/WidgetsApi.kt:396-399`, strings 1775-1779). Decide: keep in-place edit (behind the
  overflow) or require Clone. Otherwise: status chip "Loaded in OBS" / "Not in OBS"; Versions, Rename, Clone,
  Rotate URL, Update/Reset, Delete in the row's "…" menu (keyboard reachable); words follow the glossary:
  page and nav **Overlays**, each item a **widget**, header **Add widget**, dialog "Add a widget"; retire
  "Create Overlay" and "Create Overlay Widget" and the "overlay %1$s" control labels (en + nl).
  Files: `WidgetsScreen.kt`, strings (en + nl).
  Depends: S062 (Configure form).
  Done-when: a jvmTest asserts exactly four inline controls per row plus a "…" menu holding the rest; a
  resource test fails on "Create Overlay" and on "overlay %1$s" labels.
- **S-UF-O5b** [fix] The OBS page is three tabs: Connection, Live control, Advanced.
  **Connection** (default until connected; numbered steps with OBS's menu path, a **Paste** button on the
  password), **Live control** (scenes, stream and record, audio mixer, studio mode; default once connected),
  **Advanced** (filters, source actions, hotkeys, screenshot, raw and vendor requests).
  Files: `ObsScreen.kt`, strings.
  Depends: S-UF-O5a.
  Done-when: a jvmTest opens the page disconnected on Connection and connected on Live control, and every
  existing card is reachable under exactly one tab.
- **S-UF-A1d** [fix] Same words for the same thing across editors, and one blast-radius load type.
  Rename "Who can trigger it" (strings 2246, 4206) to "Who can use it", "Bound pipeline" / "Run pipeline
  (optional)" (1053, 2254, 3476) to "Pipeline", and retire "Add" as a create verb (2175, 2205, 2262) in en
  and nl. Merge `PipelineDeleteConfirmDialog.kt:46` `BlastRadiusLoadState` into
  `core/consequences/DeleteBlastRadiusDialog.kt:102`.
  Files: strings (en + nl), `feature/pipelines/ui/PipelineDeleteConfirmDialog.kt`, `core/consequences/`.
  Depends: S-UF-A1a.
  Done-when: a resource test fails if any of the retired labels exists in en or nl; one
  `BlastRadiusLoadState` type remains and the pipeline delete confirm test still renders its dependents.
- **S-UF-A6b** [fix] A live one-sentence summary under every trigger and timer form.
  "When anyone says *discord* (any case), the bot replies once every 30 s at most." / "Every 30 minutes, if
  at least 5 chat messages arrived since the last one, the bot types one of 3 messages in order." Built from
  string resources with plural forms (en + nl), recomputed on every field change.
  Files: `ChatTriggersScreen.kt`, `TimersScreen.kt`, strings.
  Depends: S-UF-A6a.
  Done-when: table-driven jvmTests assert the sentence for match type x case x cooldown, and for interval x
  min-activity x message count x fire-once, in en and nl.
- **S-UF-A7g** [fix] One **Add step** button opens a searchable palette grouped by job.
  Replace the six header buttons (`PipelinesScreen.kt:808-813`) and the lane menus with one **Add step** that
  opens a searchable palette grouped Chat, Media, Moderation, Economy, Integrations, Logic; If, Switch,
  Loop, Random and Try live under Logic.
  Files: `PipelinesScreen.kt`, palette grouping from the backend catalogue, strings.
  Depends: S-UF-A7a.
  Done-when: a jvmTest asserts the header has one add button, searching "if" lists If under Logic, and every
  catalogue action appears in exactly one group.
- **S-UF-A8b** [fix] "Pick list" is called Random Responses in every label, dialog and doc.
  Rename strings 2027-2029, 2192, 4007, 5359 (and nl) and the user docs; the token `{list.pick.<name>}` stays.
  Files: strings (en + nl), `docs/`.
  Depends: none.
  Done-when: a resource test fails on "pick list" (any case) in en or nl user-facing strings.
- **S-UF-N1b** [fix] No two sidebar pages share an icon.
  Give MultiChat, ChatTriggers and Schedule glyphs of their own (`ShellScreen.kt:1517-1565`).
  Files: app `feature/shell/ui/ShellScreen.kt`, `core/designsystem/icon/**`.
  Depends: none.
  Done-when: a unit test over `ShellRoute.entries` asserts `icon()` is injective.
- **S-UF-O2** [fix] The top of Overlays lists every browser source the channel needs, with status and Copy.
  An **OBS sources** panel: each enabled widget, the TTS overlay, the Audio Source page and the OBS bridge
  when bridge mode is on. Each row: a status ("Loaded in OBS 2 min ago", "Never loaded", "Last seen 3 days
  ago"), **Copy**, and with an OBS connection **Add to scene** (S-UF-O1b). Widgets gain a persisted
  `LastSeenAt` stamped on overlay detach and heartbeat (BOTH migration sets) so status survives a restart.
  The TTS and OBS pages link to this panel instead of showing their own URLs.
  Files: `WidgetsScreen.kt`, `TtsScreen`, `ObsScreen.kt`, `OverlayPresenceRegistry.cs` / `OverlayHub.cs`,
  widget entity + both migration sets, widget DTO, `openapi/v1.json`.
  Depends: S062 (presence push).
  Done-when: an Infrastructure test asserts `LastSeenAt` is written when the source disconnects; a jvmTest
  renders one row per enabled widget plus TTS overlay and Audio Source with their status strings; the TTS
  page no longer renders a URL.
- **S-UF-O3c** [fix] After a rotate the OBS sources panel opens in update mode with a countdown.
  "Old URLs stop working in 14:32"; each row is marked "Updated" when presence reports it loaded with the new
  token.
  Files: `WidgetsScreen.kt` (O2 panel), presence push.
  Depends: S-UF-O2, S-UF-O3a.
  Done-when: a jvmTest with a fake clock renders the countdown from the returned expiry and flips a row to
  "Updated" on a presence push carrying the new token.
- **S-UF-O3b** [new] With an OBS connection, **Update all sources in OBS** rewrites every source's URL.
  After a rotate, the bot lists OBS browser inputs whose URL matches an old token and sends
  `SetInputSettings` with the new URL for each; each row turns "Updated" as OBS reloads it.
  Files: `ObsControlService.cs`, `ObsController.cs` (Api), `openapi/v1.json`, the update-mode panel.
  Depends: S-UF-O3a, S-UF-O3c.
  Done-when: a fake-OBS test with three matching inputs and one unrelated asserts exactly three
  `SetInputSettings` calls carrying the new URLs.
- **S-UF-I3f** [fix] "Rebuild projections" becomes "Rebuild statistics from history" with one sentence on when
  to use it.
  Today `journal_rebuild*` reads "Rebuild projections … read models" (`strings.xml:1839-1841`). Build: label
  "Rebuild statistics from history", one-line when-to-use, confirm button "Rebuild statistics", en+nl.
  Files: `strings.xml` (en, nl). Depends: none.
  Done-when: a resource test asserts the en and nl keys and that no user-facing string contains "projection".
- **S-UF-I4c** [fix] The Discord page is one guided flow: connect, pick channel, write message, send test post,
  turn on.
  Today connect is on Integrations, consent and the master switch are separate gates (`DiscordScreen.kt:251,790-
  820`). Build: on the Discord page, five steps each unlocking the next: 1 Connect a server (OAuth start here);
  2 Pick the announcement channel (postable-channel picker, `DiscordController.cs:208`); 3 Write the go-live message
  with the A2 preview rendered as a Discord embed; 4 Send test post (S-UF-I4b); 5 Turn on announcements (the
  existing `streamer-enabled`). Steps already satisfied render as done.
  Files: `DiscordScreen.kt`, `DiscordController.kt`, `strings.xml`. Depends: S-UF-I4a, S-UF-I4b, S-UF-A2.
  Done-when: a jvmTest walks a fresh channel through the five steps against fake APIs and asserts each step stays
  locked until the previous one is satisfied, and step 5 issues the `streamer-enabled` PUT.
- **S-UF-I6a** [fix] The Channel Points page header says "Channel Points", matching the nav.
  Today `rewards_title` = "Rewards" (`strings.xml:1167`) vs `shell_nav_rewards` = "Channel Points" (177). Build:
  header uses the nav label (en+nl). Files: `strings.xml`, `RewardsScreen.kt`. Depends: none.
  Done-when: a test asserts header text equals the nav label in both locales.
- **S-UF-I6d** [fix] Features rows show a plain name and one line on what the feature does; keys and scopes sit
  behind "Technical details".
  Today the row's secondary text is `featureKey` and scopes print raw (`FeaturesScreen.kt:156,238`). Build: a shared
  collapsed `TechnicalDetails` disclosure (reused by S-UF-I6c and X5) holds key and scopes; rows show name and
  description from resources.
  Files: `FeaturesScreen.kt`, `core/designsystem/component/TechnicalDetails.kt` (new), `strings.xml`.
  Depends: none (F6's permission view may restyle it later).
  Done-when: a jvmTest asserts the key is not in the visible tree until Technical details expands.
- **S-UF-I6c** [fix] "Permits" becomes "Individual permissions" with grouped plain labels and one grant path.
  Today the section is "Permits" (`strings.xml:2611`), the picker shows `action.actionKey` (`RolesScreen.kt:898`), and
  a member-level "Grant" duplicates the permit grant (`strings.xml:2628-2632` vs 2659-2661). Build: rename with the
  subtitle "Give one person one extra ability without changing their role."; actions render as "Moderation · Ban
  users" from a label map (raw key behind Technical details); the member-level Grant is removed, the permit dialog is
  the single way.
  Files: `RolesScreen.kt`, an action-label resource map, `strings.xml`. Depends: S-UF-I6d (Technical details).
  Done-when: a jvmTest asserts no visible `:`-separated key in the picker, the grouped label for
  `moderation:ban`, and that a member row no longer offers Grant; a guard fails when an action key has no label.
- **S-UF-I6e** [fix] Saving a TTS BYOK key asks "Use this key for TTS now?" and sets dispatch mode and provider in
  one step.
  Today the key and the dispatch mode are two settings on two tabs (`strings.xml:838-840,866-873`). Build: after a
  key saves, a prompt "Use this key for TTS now?" with "Use for TTS" sets dispatch mode BYOK and the default provider
  to the key's provider in one config write; "Not now" leaves both.
  Files: `TtsScreen.kt`, its controller, `strings.xml`. Depends: none.
  Done-when: a jvmTest saves an ElevenLabs key, accepts, and asserts one config PUT with mode BYOK and provider
  ElevenLabs.
- **S-UF-I7a** [fix] While the economy is off the page is a three-step setup, and "Turn on economy" finishes it.
  Today the operational views render while the economy is off and the step that makes it do anything sits fifth;
  watch time is seeded off (`EarningRuleSeedOnOnboardingHandler.cs:94`). Build: economy off → only a three-step
  setup: 1 currency name singular + plural with a live sample ("1,240 Nomz"); 2 how viewers earn, watch time
  pre-selected at the seeded rate (10 per window), chat activity optional; 3 what they spend on (games, store, song
  requests; SR cost needs S067). "Turn on economy" saves config, enables the chosen earning rules and turns the
  economy on in one write; only then do leaderboards, accounts, jars and purchases appear. Economy on → today's page,
  Earning rules first after the config card.
  Files: `EconomyScreen.kt`, `EconomyController.kt`, `strings.xml`. Depends: S067 for the SR spend option (the step
  omits it until then). The currency name in chat lines is V5's slice.
  Done-when: a jvmTest on a disabled economy asserts no leaderboard/accounts render, completes the steps, and asserts
  the config write plus the WatchTime rule enabled; a test asserts the operational views appear after.
- **S-UF-X7a** [fix] A glossary guard keeps retired words out of user-facing strings.
  Today "chatter" appears in 16 user-facing strings ("Timeout this chatter?", `strings.xml:2080`). Build: a jvmTest
  reads en and nl `strings.xml` and fails on the PRODUCT-ALIGNMENT retired aliases (chatter, reaction chain, alert
  config, system widget, "browser source" as a noun, "tenant" outside `admin_*` keys, "speaks" for chat), plus an
  en/nl key-parity check; the current offenders are reworded (viewer, event response, widget, overlay).
  Files: guard test, `strings.xml` (en, nl). Depends: none.
  Done-when: the guard passes and fails when "chatter" is put back in any non-admin string.
- **S-UF-T11a** [fix] The TTS page has four tabs by job, opening on the one that fits the moment.
  Build:
  - **Live**: now playing with Skip, the queue, Silence TTS, Send a test line.
  - **Voice**: channel voice, per-viewer overrides picked by name with the picker above Assign, pronunciation
    rules.
  - **Rules**: from S-UF-T11c.
  - **Provider**: from S-UF-T11b.

  The page opens on Live when the channel is live, items are waiting, or the viewer is a moderator, and on
  Voice when offline for Editor and up.
  Files: `TtsScreen.kt`, strings.
  Depends: S-UF-T3b, S-UF-T4a, S-UF-T7a.
  Done-when: jvmTests assert the default tab for (live, Editor) → Live, (offline, Editor, empty queue) → Voice,
  and (offline, Moderator) → Live.
- **S-UF-T11b** [fix] One Provider tab: saving a key asks "Use this key for TTS now?", and removing one
  confirms the fallback.
  Build: Where voices come from (S-UF-T1c) and the BYOK keys sit together. A saved key asks "Use this key for
  TTS now?", and yes sets the source and provider in one write. Remove asks "Remove your Azure key? TTS falls
  back to free voices." with a button that names the act (X2 step 3).
  Files: `TtsScreen.kt`, `TtsController.kt`.
  Depends: S-UF-T1c.
  Done-when: a jvmTest saves an Azure key, answers yes, and the fake API receives one config write with
  `mode=byok`, `defaultProvider=azure`; cancelling Remove leaves the key.
- **S-UF-T1d** [new] "My own TTS server": a channel points TTS at its own synthesis endpoint.
  Today no such plane exists. `self_host` means the operator's configured providers
  (`TtsDispatchService.cs:618-621`).
  Build: a fourth source, a channel-supplied HTTPS endpoint behind the egress allowlist
  (S-EGRESS-ALLOWLIST-CRUD), with a **Test** that proves it returns audio before it can be saved. It falls
  through to the free voices as in S-UF-T1a.
  Files: `TtsConfig` (both migration sets), a provider adapter, the Provider tab.
  Depends: S-UF-T1a, S-UF-T11b, S-EGRESS-ALLOWLIST-CRUD.
  Done-when: a saved endpoint that returns audio is what a dispatch speaks (fake HTTP handler asserted), and
  one returning 500 falls through to Edge.
- **S-UF-T11c** [new] The Rules tab lists every source that can trigger TTS, each linked to its editor, and the
  safety preset with its numbers.
  Build: a read endpoint that enumerates rewards bound to pipelines with `play_tts`, the bits threshold, sub
  messages, commands whose pipeline speaks, and event responses with TTS on. Each row deep-links to its editor.
  The preset shows its numbers.
  Files: a TTS sources query (Infrastructure/Tts), `TtsScreen.kt`.
  Depends: S-UF-T2b (preset field), S-UF-T11a.
  Done-when: a server test seeds one of each source and the endpoint returns five rows with the right editor
  routes; deleting the reward removes its row.

## 🔒 Owner calls still open
- SignalR/Redis backplane for multi-replica (S035) — single-instance acceptable for now?
- Cooldown DB write-through (S040) — scaling investment, defer?
- Sidebar regroup vs `frontend-ia.md` update (S072).
- Per-locale tone catalogue and bot-reply language (S069) — does the bot ever reply in a non-English
  language, and what selects it? `Channel.Language` is saved but read nowhere; D9 needs a decision.
- Drop game mechanic; stacked chat transition reference (S082).
- Pre-existing from BUILD-TODO: authz key names (Plane-C + Gate-2), self-host owner = platform admin,
  user-scripting model (JS-first), YouTube non-BYOC client, Stripe, pipelines 6-surface unification,
  community reposition, data-sources push-bridge, federation transport, Streamer.bot import.
- UF D8: the spec marks some items **new** (S-UF-* tagged `[new]`, Phase 6/7). Ship each in its phase, or
  move it to the tracker as an idea? Also: is S-UF-F3a/F3b (setup ends on a confirmed `!ping`) a minimal
  honesty fix (kept in Phase 4) or a new feature?
- UF S-UF-T1b: reverse `tts.md` decision 3 ("zero server cost") so new channels default to server-synthesized
  free Edge voices, which OBS can capture (the browser voice is silent on stream)? Do existing `client_edge`
  channels move? The 2026-10-02 one-audio-source decision already rules out the browser voice on stream.
- UF S-UF-F1a: sign-in alone never creates a channel; "Run the bot on my channel" does (changes the D2 / S025
  wording). Confirm.
- UF S-UF-F2a: the shared Twitch app becomes the recommended wizard path over BYOC ("encouraged" today). Confirm.
- UF S072: does the spec's job-based sidebar regroup settle the open "Sidebar regroup vs `frontend-ia.md`" call?
- UF S-UF-O4a: keep in-place **Edit code** on installed catalogue widgets, or reach code only via **Clone to edit**?
- UF S-UF-O3a: after B5, should "Rotate token" rotate every widget's own token (planned) or only the legacy
  channel token, with honest copy?
- UF S-UF-M5: keep nuke Revert unlimited as today, or limit it to 24 h as the spec says?
- UF S-UF-M6: song requests "flagged by trust rules" do not exist as a hold in Music. Add them as a new
  capability, or drop them from the Queue?
- UF S-UF-V6b: may a merged burst thank-you replace streamer-authored follow/sub lines (S069 says tone never
  rewrites authored text)?
- UF S-UF-I3c: should Reset also reset commands, timers and event responses, or stay limited to settings rows?
- UF S-UF-X2a: the server accepts an Undo restore for 60 s while the toast shows 10 s. Confirm, or set both to 10 s.
- UF S-UF-A4a sets the new-command default to 5 s per viewer, and S-UF-T2a uses a 10 s OBS acknowledgement
  timeout. Confirm both.
