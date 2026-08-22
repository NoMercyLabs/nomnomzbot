# Usability + stability shortcomings audit — scope and plan

Third plan, sitting on top of `stability-audit-scope-and-plan.md` (F1–F19) and
`widget-quality-audit-scope-and-plan.md` (§1–§8). Two parts:

- **Part A — owner-reported issues (2026-08-22)**, each grounded to file:line with the change
  needed, in words.
- **Part B — grounded rundown of the rest of the system**, by area, same format.

Nothing here is fixed. Every entry says what is wrong and what must change; none contain code.
Where an item belongs to the other two plans' remediation order, the slot is named.

---

## Part A — owner-reported issues

### A1. `!raid <user>` — only half the raid process runs

There is no `!raid` builtin (`Infrastructure/DependencyInjection.cs:883-910` registers every
`IBuiltinCommand`; raid is absent). `!raid hillforgames` is a user-authored command whose pipeline
chains `shoutout` + `start_raid` + announce steps, so "half worked" is per-step behaviour. Where it
silently stops, most likely first:

- `Stream/PipelineActions/ShoutoutAction.cs:145-164` — the global shoutout cooldown (2 min default)
  and per-user cooldown (60 min, `:129`) return **Success("skipped")** with only a debug log; the
  shoutout and its templated announcement (`:184+`) never happen and the pipeline carries on. A second
  `!raid` to the same target within the hour silently drops that half.
- `Platform/Pipeline/PipelineEngine.cs:319-325` — a failed step breaks the loop unless
  `ContinueOnError`; `:329-345` then still reports `Outcome = Completed`; `Chat/EventHandlers/ChatMessageHandler.cs:372`
  treats Completed as success. A half-run pipeline is recorded as fully successful; chat sees nothing.
  Same for an unhandled action exception (`PipelineEngine.cs:291-293`).
- `Platform/Transport/Helix/SubClients/TwitchRaidsApi.cs:38,100` — missing `channel:manage:raids`
  short-circuits with `missing_scope`; `StartRaidAction.cs:111` turns it into a generic step failure
  written only to step logs — no chat reply, no scope-repair prompt. `Identity/FeatureScopeMap.cs:29`
  maps feature `raids` → that scope, so if the raids feature was not enabled at consent time the scope
  was never granted.
- Two tokens: the Helix raid POST runs on the broadcaster token (`TwitchRaidsApi.cs:46-57`), the
  shoutout on the bot/moderator token. One can be valid while the other is expired/unscoped — exactly
  half works.
- `StartRaidAction.cs:81-91` — a failed Get Users lookup is collapsed into "not found"; the lookup
  lowercases so only logins match (display names that differ from login resolve to nothing).
- `StartRaidAction.cs:101-108` — `delay_seconds` waits **before** the Helix call (legacy fired first,
  then counted down), and `CancelAllForChannelAsync` (`PipelineEngine.cs:76-90`) can cancel mid-delay
  after the announce already went out. No "already raiding" tolerance (`:104`); no target-is-live
  pre-check (`:59`).
- `ChatMessageHandler.cs:280-314` — permission floor and cooldown rejections return with a debug log
  and zero chat feedback.
- `ChatMessageHandler.cs:926,942-943` — `{target}` strips `@`, `{args.0}` does not; steps using
  `{args.0}` raw keep the `@`.
- Outgoing-raid event: `channel.raid.out` exists (`Platform/Eventing/Translators/ChannelModerateTranslator.cs:65-77`,
  `Stream/EventHandlers/OutgoingRaidAlertHandler.cs:29`) but depends on the `channel.moderate` v2
  subscription + six `moderator:read:*` scopes (`Identity/AuthService.cs:91-101`); the preset exists
  (`EventResponsePresetCatalog.cs:103`) but is never seeded on onboarding
  (`Commands/EventHandlers/EventResponseSeedOnOnboardingHandler.cs:43` seeds incoming only).
  `Domain/Stream/Events/RaidSentEvent.cs:15` is declared and never published — `StartRaidAction` raises
  no domain event, so a chat-initiated raid gives no overlay alert, no Discord, no journal row.
- Legacy parity gaps (`nomercy-bot/.../commands/Raid.cs`): no-arg `!raid` lists ranked candidates
  (`:33-37`); live-check with a chat reply (`:64-73`); raid fired first then announce (`:81-82`);
  "already raiding" tolerated (`:201-205`); OBS scene switch to "Ending" (`:177-192`); hype announce
  + 45/30/15/10/5/3/2/1 countdown + "RAID LIVE!" (`:208-259`); OBS StopStreaming + Spotify pause
  (`:266,283`); catch-all chat reply on error (`:99-107`); broadcaster-only floor (`:24`).

What must change:

1. Pipeline engine: a broken-out run must report **PartiallyFailed**, not Completed, and the chat
   handler must tell the invoker which step failed (one short reply). Cooldown/permission rejections
   get a short reply too (configurable, default on for the invoker only).
2. Shoutout cooldown skip must not be a silent Success — either surface it as a skipped step in the
   reply, or let `start_raid` bypass the shoutout cooldown when invoked from a raid.
3. `start_raid`: fire the Helix raid **first**, then announce/countdown; tolerate "already raiding";
   pre-check target is live and reply if not; distinguish lookup failure from not-found; on
   `missing_scope` trigger the action-required scope re-grant flow; publish `RaidSentEvent` so
   overlay/Discord/journal see it.
4. Ship a first-party **raid preset** (one click in Commands → "Raid helper") reproducing the legacy
   flow: shoutout → Helix raid → countdown messages → optional OBS scene/stop + Spotify pause, each
   toggleable. Seed `channel.raid.out` on onboarding alongside `channel.raid`.
5. Strip `@` from `{args.N}` the same way `{target}` is stripped.

Slot: stability plan item 5 (F4 chat-send outcome threading) — item 1 here is the same "tell the
truth about execution" fix and should ship with it.

### A2. Spotify connected by a non-owner streamer on SaaS — nothing works

Ruled out after tracing: OAuth state carries the right broadcaster
(`IntegrationOAuthController.cs:86-100`, `IntegrationOAuthService.cs:240-292`); polling is
per-channel (`BackgroundServices/MusicStatePollingService.cs:173-187`); provider resolution is
per-tenant; no feature/billing gate disables music; the tenant query filter is evaluated per
DbContext instance (EF replaces the context constant — `ModelBuilderExtensions.cs:69-73` +
`AppDbContext.cs:377` are correct). Remaining causes, most likely first — all need checking on the
hosted box:

- **Spotify app in Development Mode** — one shared client id for every tenant
  (`appsettings.json:32-35`, `Platform/Configuration/SystemCredentialsProvider.cs:45-59`; no per-channel
  BYOC for SaaS — `Integrations/OAuthProviderRegistry.cs:205` only has a deployment-level flag). A dev-mode
  app serves only 25 allow-listed Spotify accounts; everyone else gets 403 on every API call after a
  successful connect. Nothing in `DEPLOY.md`/docs mentions this. **Check:** is qtkitte's Spotify
  account on the app's user list / does the app have extended quota.
- **403/401 is swallowed as "nothing playing"** — `Music/SpotifyMusicProvider.cs:627,1019,1073,1118,1144,1187`
  all return null on Unauthorized/Forbidden; only a `PREMIUM_REQUIRED` body is classified
  (`:1475-1480`). The integration card keeps saying "connected" with no diagnostic — which is why the
  report is "none of it works" with no error.
- **Music stack reads only the legacy `Services` mirror** — vault is canonical
  (`Integrations/IntegrationOAuthService.cs:275-292`) but `SpotifyMusicProvider.cs:1209` /
  `MusicService.cs:574` read `Services`, mirrored only in the OAuth callback
  (`Music/MusicProviderTokenMirror.cs:56`). If her `Services` row (`Name='spotify'`, her BroadcasterId)
  is missing, status says connected and everything is dead. **Check the row.**
- **Refresh needs client id/secret sealed into her own `Service` row** (`SpotifyMusicProvider.cs:1271-1293`);
  rows created before the mirror wrote those fields, or after a key rotation, refresh to null and go
  silently dead after one hour.
- **Redirect URI is the request origin** (`IntegrationOAuthService.cs:134,424`,
  `Api/Extensions/PublicOriginExtensions.cs:44`) — a second hostname on the hosted box not registered
  in the Spotify app breaks connect with a redirect mismatch.

What must change:

1. Make a Spotify 401/403 a **visible state**: integration status `needs_reauth` / `forbidden` with the
   Spotify error reason, shown on the Integrations card and Music page, and a chat reply to `!sr`
   ("Spotify is not available right now") instead of silence.
2. Drop the `Services`-mirror dependency — music reads tokens from the vault like every other
   integration (one source of truth), or the mirror is written on every vault write, not only in the
   callback.
3. Document the Spotify dev-mode / extended-quota requirement on the SaaS deploy surface, and add a
   per-channel BYOC option for Spotify (same as Twitch BYOC) so a tenant can use their own app.
4. Add a "Test connection" button on the Music/Integrations page that calls `/v1/me` and shows the raw
   Spotify outcome.

Slot: Part B music lane (B4) — ship together.

### A3. TTS widget must be system-level, owned by the TTS page

Current state:

- `Content/Widgets/Assets/tts_caption.vue:32-62` is the only TTS widget; it renders a caption and
  **ignores `audioUrl` entirely** — no audio element, no queue (the header comment `:8-10` still says
  audio rides the host sound bus). The "widget's own queue" from commit deabc759 is not in the shipped
  asset.
- `Api/Hubs/Broadcasters/TtsSpeakBroadcastHandler.cs:38-46` pushes `tts_speak` only to widget
  instances subscribed to it; `Tts/TtsDispatchService.cs:581-633` puts the mp3 as an inline data URI
  on the event and no longer calls the sound bus; `Api/Controllers/OverlaySdkController.cs:129-175`'s
  SDK audio bus handles `PlaySound`/`StopSound`/`TtsSpeak` (browser `speechSynthesis` only) and never
  plays `audioUrl`. **Net: server-synthesized TTS is silent in OBS unless someone hand-writes a widget.**
- `tts_caption` is a gallery entry the user must install (`Content/Widgets/FirstPartyWidgetCatalogue.cs:204-216`);
  `Domain/Widgets/Entities/Widget.cs:25-66` has no `IsSystem`/undeletable flag (precedent:
  `Domain/Identity/Entities/IamRole.cs:24`); nothing auto-provisions per channel.
- Overlay auth is channel-wide (`Api/Hubs/OverlayHub.cs:45-80`, `Channel.OverlayToken`
  `Channel.cs:102`, get/rotate `ChannelsController.cs:475,485`), so a channel-level TTS surface needs no
  widget row.
- `feature/tts/ui/TtsScreen.kt` has zero overlay/widget references; only `TestSpeakSection`
  (`:1894-1933`) calling `POST /tts/test` and playing in the dashboard.

What must change:

1. Introduce a **system surface** concept: a non-deletable, auto-provisioned per-channel TTS player
   (either a `Widget` with `IsSystem = true`, hidden from the gallery, created on channel creation /
   TTS enable, delete-protected in `Widgets/WidgetService.cs` + repository; or a channel-level overlay
   route `/overlay/tts?token=` that needs no widget row).
2. That surface owns an **ordered audio queue** playing `audioUrl` back-to-back (one utterance at a
   time, optional caption), and is the prerequisite for A5's segments.
3. The TTS page shows: the OBS browser-source URL (copy), connected/last-seen state, a "test through
   the overlay" button, queue controls (skip/clear), caption style settings. `tts_caption` stays as an
   optional caption-only widget or is folded into the system surface.
4. Fix the stale header comment in `tts_caption.vue:8-10` when it is touched.

Slot: widget plan item 1 (the systemic field-name fix) — same "widgets receive what the server sends"
class; A3 + A5 + A4 ship as one TTS slice.

### A4. TTS ignores the streamer's chosen voice; `!voice` finds no voice by id or name

(a) chosen voice ignored:

- **Root cause on the owner's `client_edge` mode:** `Api/Controllers/OverlaySdkController.cs:152-161` —
  `speakTts` builds a `SpeechSynthesisUtterance` and sets rate/pitch/volume only; it **never sets
  `utter.voice` or `utter.lang`** and never reads `payload.voiceId` (which the hub payload does carry —
  `Api/Hubs/Dtos/HubResponseDtos.cs:194-201`). The browser default voice always wins. `client_edge`
  synthesises nothing server-side (`Tts/TtsDispatchService.cs:398-424`), so the Edge-provider fixes in
  4bb56dfb/e8021bb8 do not affect what the owner hears.
- `TtsDispatchService.cs:646-674` — precedence is override → per-user → channel default → first; the
  XML comment says per-user first. Per-user key/tenant is consistent with what `!voice` writes.
- `Tts/PipelineActions/PlayTtsAction.cs:57-68` — any `voice` value authored on a `play_tts` step becomes
  the override and silently beats every viewer's personal voice (second independent cause on
  server-synth planes).
- `Tts/TtsService.cs:133-160` — `ResolveProvider` returns Azure for any non-GUID voice id whenever an
  Azure instance is registered (always, keyless included — `DependencyInjection.cs:919-931`);
  `AzureTtsProvider.cs:52-56` returns empty on a missing key → silence, not fallback.
- `TtsService.cs:84` — the Edge fallback hardcodes `en-US-AriaNeural`, discarding the resolved voice:
  any transient provider failure silently downgrades to Aria.

(b) `!voice` / picker finds nothing:

- **Root cause:** `Tts/TtsConfigService.cs:291-304` — the `q` filter matches Name, DisplayName,
  Gender, Accent, Description, Tags but **not `Id` and not `Locale`**. `!voice en-US-AriaNeural`
  returns zero rows at any capitalisation; the command's own help text ("try a language like en-US",
  `Tts/Builtins/VoiceBuiltin.cs:104`) advertises a filter that cannot match. Same omission in the
  pre-sync fallback (`TtsConfigService.cs:364-375`).
- `VoiceBuiltin.cs:98-116` — `SetAsync` gives up on zero search results, so `BestMatch` (`:130-143`,
  which does rank by id/name) never runs for an id query.
- `TtsConfigService.cs:429-439` — `VoiceExistsAsync` is case-sensitive on `Id` (Postgres ordinal).
- `Content/Tts/TtsVoiceSeeder.cs:34-145` seeds 10 Edge voices correctly; `Tts/TtsVoiceCatalogSync.cs:46-108`
  then overwrites `Name` "AriaNeural" with "Aria", so typing Microsoft's ShortName misses `Name` too.
- Dashboard picker `GET …/voices` (`TtsConfigController.cs:131-148`) inherits the same search.

What must change:

1. Overlay SDK: resolve `utter.voice` from `speechSynthesis.getVoices()` by voiceURI/name/lang against
   `payload.voiceId` (wait for `voiceschanged`), set `utter.lang`.
2. Search: add case-insensitive `Id` and `Locale` predicates to both search paths; make
   `VoiceExistsAsync` case-insensitive; let `!voice` fall through to an exact-id lookup before
   giving up.
3. Remove the hardcoded-Aria fallback and the keyless-Azure preference in `TtsService`; a failed
   provider must fail visibly (dashboard notice), not downgrade silently.
4. Fix precedence doc vs code; `play_tts` voice override only when explicitly chosen (A5's voice mode).

Slot: with A3/A5 as the TTS slice.

### A5. TTS as a pipeline action with multi-voice segments merged into one utterance

Current state:

- `Tts/PipelineActions/PlayTtsAction.cs:35-79` (`play_tts`): config is one `text` + one `voice`
  override; no "use the triggering user's voice" flag (it only happens implicitly when `voice` is
  empty), no segments, no `bypassQueue` (spec §6 lists it, never implemented). No `Category`/
  `Description` declared, so the palette shows it as `general` / "play_tts".
- `Tts/PipelineActions/TtsSynthesizeAction.cs:55-120` (`tts_synthesize`): one text + one voice →
  stored mp3 + `{{tts.audioUrl}}`; each call is an independent clip, nothing joins them.
- `Application/Contracts/Tts/ITtsDispatchService.cs:34-37,88-99` — `TtsSpeakRequest` is single
  `Text` + single `VoiceIdOverride`; `TtsDispatchService.cs:520-633` synthesises one voice per call,
  one ledger row, one event. `ResolveVoiceAsync` (`:647`) already does per-viewer → override → channel
  default → first available, so "user's own voice" per segment is reusable.
- No audio concatenation utility exists anywhere in `Infrastructure/Tts`.
- `Api/Controllers/V1/PipelinesController.cs:89-108` — the action catalogue returns only
  (Type, Category, Description); **no field schema**, so a new segment-shaped action cannot describe
  its form. The `play_tts` form is hand-written in Kotlin (`core/network/PipelineCatalogue.kt:241-253`);
  `BlockField`/`FieldKind` has no repeatable/list kind.
- `spec/tts.md:431-444` — §6 specifies only `play_tts(Text, VoiceId, BypassQueue)`; zero mentions of
  segments or multi-voice. Spec must be amended.

What must change:

1. Spec first (`spec/tts.md` §6): `play_tts` becomes a **segment list**; each segment = text template +
   voice mode (channel default / triggering user's voice / explicit voice) ; whole list dispatched as
   ONE utterance.
2. Dispatch: a segment-aware request (list of segments) that synthesises each segment with its
   resolved voice and emits ONE `tts_speak` payload carrying an ordered array of
   `{text, voice, audioUrl, durationMs}`; the system TTS surface (A3) plays them back-to-back. No
   server-side mp3 splicing needed. One ledger row per utterance, one queue slot, censor applied per
   segment.
3. Catalogue: add the field-schema to the backend action descriptor (fields, kinds, options) and a
   repeatable "segment" field kind in the palette so the builder renders "+ add segment" rows with a
   voice-mode dropdown per row. (This is the same catalogue work as A6 item 5 — do once.)
4. Ship the owner's example as a preset: "Sub streak redeem → random pick item + 'they also said:' +
   user's message in the user's voice".
5. Implement `BypassQueue` or remove it from the spec.

Slot: with A3/A4 as the TTS slice.

### A6. Discord "go live → message to channel + roles" is primitive and undiscoverable

What is wrong, grounded:

- The notification-rule dialog asks for a raw Discord channel snowflake
  (`app/.../feature/discord/ui/DiscordScreen.kt:803-812`) and a raw trigger-type string
  (`:796-801`, disabled on edit, so a typo means delete-and-recreate). The resolved-name dropdown
  `GuildPickerField` already exists in the same file (`:1357`) and is used for the role dialog
  (`:1149-1160`) and the opt-in-button channel dialog (`:1320-1335`) — just not for the rule editor.
- The saved rule list shows the channel as the raw id (`DiscordScreen.kt:687`), never `#announcements`.
- The rule dialog exposes only trigger + channel + template; the backend rule also carries a ping
  role and an embed (`Domain/Discord/Entities/DiscordNotificationConfig.cs:39`) which the create
  call never sends (`feature/discord/DiscordController.kt:324-337`). The template field has no helper
  list and no preview, although a preview endpoint exists (`Api/Controllers/V1/DiscordController.cs:214`).
- Backend already serves guild, roles and channels for pickers
  (`DiscordController.cs:134,144,156`; contract `IDiscordGuildDirectoryService.cs:22`) — built for
  exactly this, unconsumed by the rule editor.
- Trigger types are a closed set of four: `go_live`, `new_clip`, `schedule`, `milestone`
  (`Infrastructure/Discord/DiscordNotificationConfigService.cs:33-36`). There is **no**
  `hype_train` and **no** `go_offline`. Only `ChannelOnlineEvent` has a handler
  (`Discord/EventHandlers/DiscordGoLiveNotificationHandler.cs:26`); no offline counterpart.
- The pipeline action `send_discord_notification`
  (`Discord/PipelineActions/SendDiscordNotificationAction.cs:26-45`) takes only `trigger_type` +
  `dedupe_key` and re-uses the stored rule — it cannot post its own message to its own channel.
  The spec promised the action carries ChannelId + MessageTemplate + Embed
  (`spec/discord.md:427-429`). This divergence is why "hype train → channel Y with template" is
  impossible today.
- "Assign roles A,B,C to me on live / remove on offline" does not exist anywhere: the gateway has
  `AddMemberRoleAsync`/`RemoveMemberRoleAsync` (`IDiscordBotGateway.cs:55,64`,
  `DiscordRestBotGateway.cs:175,191`) but the only callers are viewer self-serve notify roles
  (`DiscordNotificationRoleService.cs:270,317`). No entity, no handler, no action, no spec text.
- The catalogue that drives the pipeline step form has field kinds Text/Number/Bool only
  (`core/network/PipelineCatalogue.kt:31-35`); `options` are static literals (`:43-46`); the
  renderer only shows a dropdown when `options` is non-empty (`feature/pipelines/ui/PipelinesScreen.kt:887`).
  The Discord action's fields have neither (`PipelineCatalogue.kt:312-321`) so `trigger_type` is a
  free text box for a closed enum. There is no "resource picker" field kind anywhere.
- Event Responses has zero Discord surface (`feature/eventresponses/` — no references; preset
  catalogue `EventResponsePresetCatalog.cs:108` has stream.online chat-only). Nothing on the go-live
  surface hints Discord exists.

What must change:

1. Rule editor: replace the channel text field with `GuildPickerField` (channels), the trigger text
   field with a dropdown of the four trigger values, add the ping-role picker (roles) and embed
   toggle, add a template-helper link + preview button using the existing preview endpoint. List
   rows show the channel name.
2. Add `go_offline` and `hype_train` (begin/end) triggers to the closed set, with handlers on
   `ChannelOfflineEvent` and the hype-train domain events, and spec text in `spec/discord.md`.
3. Make the pipeline action match the spec: optional own channel + template + embed + ping role,
   falling back to the stored rule only when omitted.
4. New "live role sync" feature: per guild connection, a list of role ids to add to the broadcaster's
   member on `ChannelOnlineEvent` and remove on `ChannelOfflineEvent`; dialog uses the role picker;
   surfaced on the Discord page next to notification rules. Spec it in `spec/discord.md` first.
5. Catalogue: add a resource-picker field kind (e.g. `discord_channel`, `discord_role`, later
   `twitch_user`, `reward`, `widget`) that the step form renders as a server-fed dropdown. This is the
   generic fix the "pickers not text boxes" complaint needs everywhere, not just Discord.
6. Event Responses: add a Discord preset for stream.online / stream.offline / hype train that deep-links
   to the Discord rule (or composes the richer action from item 3).

Slot: after widget-plan item 2 (test-run wiring) and before widget-plan item 7 (variable picker);
item 5 here is the same generic-form work as the variable picker and should ship together.

### A7. Template helper list in form modals → "all helpers" popup

Current state:

- There is **no template-variable catalogue endpoint**. `Platform/Templating/TemplateResolver.cs:33`
  (1252 lines, "90+ variables" across 12 namespaces + pick-lists + custom data + pronoun grammar +
  set_variable/HTTP keys) matches keys by inline string comparison — nothing can enumerate them.
  `ITemplateResolver.cs:17` exposes resolution only. (`CatalogController.cs:30` is the economy store,
  a name trap.)
- The only list the frontend gets is `Commands/Services/EventResponsePresetCatalog.cs:36` — per event
  a hand-written `string[]` of **seeded** variables: 29 presets, 2–7 each (median 3). ~3 shown vs 90+
  supported, and it answers "what does this event seed", not "what is valid here" (global
  namespaces `channel.*`, `stream.*`, `time.*`, `random.*`, `count.*`, `viewer.*`, `list.pick.*` resolve
  everywhere and are never listed).
- Frontend: the scrolling list is `VariableChips` — private to
  `feature/eventresponses/ui/EventResponsesScreen.kt:500` (`horizontalScroll` at `:509`), fed by the
  preset DTO (`:405-408`). Commands (`CommandsScreen.kt:671`), Timers (`TimersScreen.kt:504`) and
  event responses (`:410`) have only `PickListInsertMenu` (`feature/picklists/ui/PickListInsertMenu.kt:49`,
  one helper family). Rewards, pipelines, chat triggers, code scripts: **nothing**. Coverage: 1 of 5
  entry types has a variable list.
- Safe popup primitive: `core/designsystem/component/Dialog.kt:59` (window Dialog — not
  `androidx.compose.ui.window.Popup`, so the Wasm Popup deadlock does not apply); `Sheet.kt:76` is
  built on it. Not `DropdownMenu` (menu-shaped, wrong for ~90 grouped rows).
- i18n: only `event_responses_variables_label` exists (`strings.xml:2628`, `values-nl/:2615`); no
  per-helper descriptions in either language; backend has none either (only C# XML doc comments).

What must change:

1. Backend: a machine-readable helper registry (id, namespace, argument grammar, example, context
   applicability) that `TemplateResolver` is built from, and `GET /templates/helpers?context=<trigger>`
   returning the full valid set for that entry type (global namespaces + the trigger's seeded
   variables + channel-specific pick-lists/custom data/counters). Same registry drives save-time
   validation (stability plan F6).
2. Frontend: one shared `TemplateHelpersLink` ("All helpers…") opening a `Dialog` with search +
   namespace groups + click-to-insert, used in **every** template text field: commands, event
   responses, timers, rewards response, pipelines (send_message/reply/tts/discord), chat triggers,
   giveaways messages, Discord rule template. Remove the horizontal chip scroller.
3. Descriptions in `strings.xml` keyed by helper id (en + nl), matching the existing pattern.

Slot: widget plan item 7 (variable picker) — this **is** that item, made concrete; ships with A6 item 5.

---

## Part B — grounded rundown by area

Seven lanes. Each item: where — what is wrong — what must change. Paths are relative to `server/src/NomNomzBot.*`
or `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/`.

### B1. Commands · event responses · timers · chat triggers · pipelines

Dead config (saved, never read):
- `Platform/ChannelRegistry.cs:399-419` + `IChannelRegistry.cs:250-273` — `PrefixMode`, `CustomPrefix`,
  `MatchMode`, `MatchPattern` are never loaded; `ChatMessageHandler.cs:132-214` matches on channel
  prefix + exact lowercase name only. The Commands dialog's Prefix/Match modes
  (`feature/commands/ui/CommandsScreen.kt:695-746`) change nothing. Wire them into registry + matcher,
  or remove from the form.
- `Platform/Eventing/EventResponseExecutor.cs:78-97` — handles `chat_message` and `pipeline`; the
  `overlay` response type offered by the dashboard (`EventResponsesScreen.kt:292,418-431`) hits the
  no-op default. Implement the overlay leg or drop the type.
- `ChannelRegistry.cs:412`, `Commands/Jobs/TimerService.cs:240-245`, `EventResponseExecutor.cs:138-145`
  — none check `Pipeline.IsEnabled` (`Pipeline.cs:39`; list toggle `PipelinesScreen.kt:464`). Disabling a
  pipeline stops nothing. Filter in all three.

Stale cache after write:
- `Commands/PipelineService.cs:152-156` — `UpdateAsync` never invalidates the command / chat-trigger
  caches that embed the graph snapshot (`IChannelRegistry.cs:270,226`); the only
  `ChannelConfigChangedEvent` consumer invalidates `features` only (`ChatDecorationRulesCacheInvalidator.cs:28`).
  Editing a pipeline leaves bound commands running the old graph until reconnect. Invalidate on
  create/update/delete. (Stability plan F3 is the timer flavour of this.)

Timer runtime:
- `TimerService.cs:139-143` — null `LastFiredAt` + interval ⇒ a new or re-enabled timer fires within
  30 s. Stamp at create/enable (ties to F8).
- `TimerService.cs:52,152-156` — `_messageCountAtLastFire` starts empty so `MinChatActivity` passes on
  first fire and after every restart; dictionary never pruned on delete/disable.
- `TimerManagementService.cs:184-185` — rename has no duplicate-name check (create does).

Feedback:
- `feature/timers/state/TimersController.kt`, `chattriggers/…`, `eventresponses/…` take no `Feedback`
  — saving closes the dialog with no "saved/live" confirmation (Commands/Pipelines do). Inject and emit.
- `EventResponsesController.kt:196-200` + `Commands/EventResponseService.cs:59-77` — Delete removes the
  row and the next list call re-seeds it disabled: Delete appears to do nothing. Make it
  "reset to default" or stop re-seeding removed rows. Also that GET writes (seed inside `ListAsync`) —
  a race under two dashboards; move the seed to onboarding.

Authoring:
- `feature/pipelines/ui/PipelinesScreen.kt:873-968` — `FieldKind.Number` never gets a numeric field;
  every numeric param (`PipelineCatalogue.kt:131,158,177,236,261,292`) is free text with no range check.
- `ChatTriggersScreen.kt:507-527` — regex pattern has no client-side compile check; `:521-527` response
  has no helper insert; `:499-506` "use pipeline" is a dead toggle when no pipelines exist (event
  responses have create-and-bind, `EventResponsesController.kt:127-147` — offer it everywhere).
- `TimersScreen.kt:518-529` pipeline picker hidden when none exist; `:531-537` interval is raw minutes,
  out-of-range silently disables Save; `TimerDto.LastFiredAt/NextMessageIndex` never shown (`:311-373`).
- `CommandsScreen.kt:597-603` — command name locked on edit: rename = delete + recreate (loses usage
  count). `:642-646` — the `code` tier dead-ends (no link to Code Scripts, no bound-script indicator).
- `Commands/CommandService.cs:113` + `ChannelRegistry.cs:430-436` — aliases have no collision check;
  a new alias silently hijacks an existing command.

Runtime ordering:
- `ChatMessageHandler.cs:685-715` — chat triggers iterate a `ConcurrentDictionary` (undefined order),
  first match returns; no priority field anywhere. `:692-694` — a cooling-down trigger `return`s and
  blocks all others on that line. Add an order column, sort, `continue` on cooldown.

### B2. Rewards · economy · games · giveaways

- `Hubs/Broadcasters/RewardLifecycleBroadcastHandler.cs:35,47,59` vs `core/realtime/HubEvent.kt:25-48`
  — reward created/updated/removed and redemption-status pushes have no `HubEvent` case; dropped as
  Unknown. `RewardsController.kt:286-304` only *adds* redemptions — the pending queue grows until
  reload. Add the cases, remove on fulfil/refund.
- `Rewards/Dtos/RewardDtos.cs:76` — `CreateRewardRequest.Response` (chat template on redeem, consumed
  at `RewardRedeemedHandler.cs:146`) has no form field (`RewardsScreen.kt:904-986`) and is missing from
  `UpdateRewardRequest` (`:93-113`). `ActionType`/`ActionSettings` (`:83-84,105-106`) accepted, stored,
  no UI. Expose or delete.
- `EconomyScreen.kt:2062-2135` — catalog item dialog collects name/description/cost; the request
  (`EconomyRequests.cs:98-112`) supports SinkType, IconUrl, Permission, PipelineId, cooldowns,
  StockLimit, MaxPerViewerPerStream, SortOrder. No edit at all (`EconomyController.kt:311-333`; PATCH
  exists `CatalogController.cs:87`). The store is unusable as configured.
- `EconomyLeaderboardsController.cs:41,55,84,98` — upsert/delete config, opt-in/out exist;
  `EconomyApi.kt:166-183` lists and ranks the first config; no screen creates/picks/deletes one.
  `Economy/EconomyLeaderboardService.cs:198` stores the numeric Twitch id as `DisplayNameSnapshot` —
  leaderboards render ids.
- `Games/LiveGameEngine.cs:462-484` — settlement failure is logged, session marked Settled, stakes
  **not refunded** (refund only on cancel `:186/526`). Must refund or park in a retryable failed state.
  `:319-321` — a joiner who can't pay is dropped silently. `Games/LiveGameRunner.cs:63-75` — a
  throwing tick loops forever, session stays active, blocks new rounds (Start button dead with no
  reason, `GamesScreen.kt:503-506`). `GamesScreen.kt:766-790` — per-game tuning is blind key/value
  text; needs typed fields from the game manifest.
- `Domain/Giveaways/Entities/Giveaway.cs:71` — `ClosesAt` persisted, never assigned; no auto-close.
  `GiveawayDtos.cs:47-48,53` — `EligibilityJson`, `WeightingJson`, `PrizePipelineId` absent from the
  dialog (`GiveawaysScreen.kt:886-983`). `GiveawaysController.kt:232` drops per-code labels. No endpoint
  lists entries (only winners, `GiveawaysController.cs:167`). `:961-983` pool picker shown with zero pools.
- `EconomyScreen.kt:2500-2504` — jar invite role is free text for an enum. `SavingsJarsController.cs:36-150`
  — no update/delete for jars.
- `EconomyController.kt:348-356,399-407,429-441`, `RewardsController.kt:76-95,112` — failures degrade
  to null/empty with no error; `RewardsScreen.kt:179-186` 3 s poll loop with no backoff.

### B3. Moderation · chat · community

Built, unreachable:
- `core/network/ModerationApi.kt:235-238,752,767` — chat-filter client + DTOs exist; **no screen uses
  them**. The whole J.6 filter feature (`ChatFiltersController.cs`, `ChatFilterService.cs`) is invisible.
- `ITwitchModerationApi.cs:377,383` — AutoMod settings get/update implemented, no consumer.
  `:362,369` — AutoMod held-message check/manage implemented, no consumer: **no held-message queue
  anywhere**. `:232` — clear chat, no consumer. `ITwitchModeratorsApi.cs:33,40` — add/remove moderator
  only used by onboarding; VIP has endpoints (`CommunityController.cs:914,929`), mod does not.
- `Moderation/AutoModerationEngine.cs:46-92` vs `ModerationDtos.cs:172-181` — engine supports per-filter
  action/duration/min-length/regex/exempt roles; config DTO carries only enabled flags + thresholds.

Raw text / wrong defaults:
- `ModerationScreen.kt:1128-1134,1153` — timeout duration is free text; an unparseable value becomes
  `null` → the request goes out as a **permanent ban**. `:2380-2387,2976-2982,2946-2951,3011` — rule
  and escalation durations/windows unvalidated; zero-hour window silently disables the ladder.
- `ModerationController.kt:483-496` — nuke hardcodes reason/matchTerm null; dialog shows raw user id.
- `QuotesScreen.kt:451,469-475` — quoted speaker is free text, never linked to a viewer; `QuotedAt` never set.
- `CommunityScreen.kt:490,500` + `strings.xml:941` — Ban sends a canned reason into Twitch's permanent
  record; no reason field, no timeout option (`CommunityController.cs:815`).

Feedback:
- `ModerationController.kt:756-767,1031` — announcement success silent, dialog closes before result.
  `:414-427,643` — approve-unban fires with no confirm (deny confirms). `:567-572,793-803` —
  `afterWrite` drops errors when state ≠ Ready. `ModerationScreen.kt:1513-1524` — Warn clears the reason
  before the call resolves. `ModerationController.kt:226-244` — failed reads of escalation/shared-ban/
  nuke collapse to null so those cards vanish with no "needs permission" notice.
  `CommunityController.kt:240-283` — four per-viewer reads map failure to null → blank sections.

Live chat:
- `MultiChatScreen.kt` — multi-channel watch has no moderation actions and no composer.
  `DashboardHubClient.kt:120-130` — `connect()` reseeds `joinedChannels` with the primary only; watched
  panes go dead silently on channel switch. `MultiChatController.kt:59-63` — watch list not persisted.
- `ModerationController.kt:779-787` — hub-pushed mod-log rows have empty timestamp and raw ids;
  `:774-790` ignores shield-mode pushes (`ShieldModeBroadcastHandlers.cs`).
- `ChatScreen.kt:1289-1314` — four boolean toggles; slow-mode delay/followers duration not editable;
  unique-chat and non-mod delay not modelled despite Helix support (`TwitchChatDtos.cs:61-71`).
- `ModerationController.kt:607-623,674-679` — every AutoMod sub-edit re-POSTs the whole config; two mods
  clobber each other. `ModerationScreen.kt:2904-2911` — ladder dialog seeds stale state.

Backend:
- `Domain/Moderation/Events/WarningEvents.cs:16` — `WarningAcknowledgedEvent` raised, no handler.
- `AutoModerationEngine.cs:243-265` — empty allow-list + link filter blocks every link with no UI warning;
  `:267-286` — invalid regex silently degrades to literal substring; no regex tester.
- `ModerationController.cs:1084-1091` — stats count by `Contains("ban")` so unban/automod double-count.

### B4. Music · song requests

- **`Infrastructure/DependencyInjection.cs:670` + `Music/MusicService.cs:43` — `IMusicService` is
  registered Scoped by convention but holds the fair queue in an instance field.** Every request/
  chat message gets a fresh empty queue: `!sr` enqueues into an object that is disposed; `!queue`,
  `GET /queue`, remove all read a different instance. The queue is fictional. Needs a singleton
  store (or persistence), like `ITtsService` already is.
- `MusicService.cs:415,170` — provider `AddToQueueAsync` bool discarded: viewer told "Added" when
  Spotify queued nothing (no active device, expired token); skip dequeues before push, a failed push
  loses the request. `SpotifyMusicProvider.cs:1486-1515` — NO_ACTIVE_DEVICE only retried when a
  device is remembered, else swallowed. `:271-294` — search returns `[]` for auth failures → viewer
  told "No tracks found".
- `MusicService.cs:369` — admission checks only the blocklist: `MaxQueueSize`, `MaxRequestsPerUser`,
  `MinTrustLevel`, `AllowYouTube/Spotify`, `PreferredProvider` persisted (`MusicConfigService.cs:64-75`)
  and never read. `:546` `CheckTrustPermission` has zero call sites. `:292` + `SongRequestBuiltin.cs:51`
  — `IsEnabled` never consulted for chat: the off toggle lies. `:568-583` — provider picked
  alphabetically, ignores `PreferredProvider`.
- `MusicController.kt:121` + `MusicScreen.kt:1030-1039` — dashboard offers `{origin}/sr/@{login}` to
  copy but **no route serves `/sr/`** (only `now_playing.vue`/`sr_queue.vue` widgets; SPA fallback
  `Program.cs:878`). `SongRequestsScreen.kt:378-393` shows a bare token with no URL. Build the public
  page or remove the affordance.
- `SongRequestsScreen.kt:303-320` vs `MusicScreen.kt:858-1010` — two divergent editors for one config.
  `:501-509` — queue moderation is remove-only (no promote, ban-track, refund). No cost/max-duration/
  per-user-cooldown fields in `MusicConfigDtos.cs:16-24`.
- `MusicScreen.kt:918-935,998-999,872-873` — free-text numbers with hardcoded `isError=false`; blank
  field silently means "leave unchanged"; trust levels/providers duplicated as Kotlin literals.
  `MusicController.kt:82-115,361-376` — broken Spotify renders as a normal empty page (see A2).
- `MusicController.kt:335-347,305` — every control and every track change triggers a six-call reload.
  `MusicStatePollingService.cs:64,141` — 1 s serial tick over all channels.
- `PublicSongRequestController.cs:30` shared `api` rate-limit policy (spec says `public-sr`); `:96`
  `RequestedBy` is free text and is the fair-queue owner key — trivially spoofable.

### B5. Overlay / widget setup · OBS

- `Widgets/WidgetService.cs:1224` — every browser-source URL carries the **channel-wide**
  `OverlayToken`; no per-instance token. `WidgetsApi.kt:123-127` + `WidgetsScreen.kt:306-319` —
  rotation kills every OBS source at once with no grace window (`OverlayHub.cs:55`) and no post-rotate
  "re-copy these URLs" list. (= BUILD-TODO "individual tokens per widget / rotatable tokens".)
- `WidgetTestEventController.cs:45` — full test-fire endpoint (~30 event types), zero callers in
  `app/`. No "Test" button next to any widget URL.
- `WidgetsScreen.kt:655-706` — no in-dashboard preview (editor has one, `ProjectEditor.wasmJs.kt:505-511`).
  `lastRuntimeError`/`lastRanAt` fetched (`WidgetsApi.kt:314-315`) and never rendered.
- No connected/last-seen: `OverlayHub.cs:66,114` only log; `WidgetConnectedEvent` never raised.
  `OverlaySdkController.cs:105-110,125` — rejected handshake = blank overlay, silent infinite retry.
  `:116` — any re-join does `location.reload()`.
- `WidgetsScreen.kt:518` — settings form only for `first_party` (gallery/cloned widgets with a schema
  get none). `WidgetSettingsForms.kt:309-318` hex text instead of a colour picker; `:358-365` unknown
  types fall to text silently; no asset/sound/font field types (`WidgetSettingsSchemaDtos.cs:38-49`)
  although asset + sound-clip libraries exist; `:532-548` bad numbers silently become defaults.
  `:444-462` event subscriptions read-only though the backend accepts updates.
- Gallery: no version/changelog/preview/update state (`WidgetsScreen.kt:1144-1217`); browse is a
  dialog with framework filter only (`:1095-1100`).
- Sound upload: 8 MB / 64 MB limits (`ChannelAssetService.cs:23-24`) never shown; hardcoded volume 80.
- `ProjectEditor.jvm.kt:46-48` — desktop editor has no preview and no test-fire bar.
  `ProjectEditor.wasmJs.kt:515-519` — fire-bar events regexed from source, not from subscriptions
  (widget plan §3).
- `OverlayHub.cs:26,72-73` — one widget per connection map (second join orphans the first);
  `:48,55` token in query string, full-table scan, no throttle on bad tokens.

### B6. Onboarding · settings · integrations · home · shell

- `SettingsScreen.kt:1031` + `Identity/ChannelService.cs:306` — the "auto-join" toggle writes
  `Channel.Enabled`, the master kill-switch that also stops role/standing sync and bot-mod grants
  (`ChannelRepository.cs:32`, `*ReconcileService.cs:81-82`, `BotModGrantOnBotAuthorizedHandler.cs:53`).
  Rename or give auto-join its own column.
- `ChannelBotController.kt:81-90` + `SettingsScreen.kt:1441-1445` — white-label bot connect opens a
  **redirect** authorize URL (authorizes whoever is logged in — the streamer) and never polls/refreshes.
  Needs device-code + poll like the platform-bot connect.
- `IntegrationsController.kt:119-123,144-148` — failed status read renders as "nothing connected".
- `ChannelService.cs:308` — `User.Timezone` written, never read. `:304` — `Channel.Language` written,
  never consumed, and `ChannelInfoSeedOnOnboardingHandler.cs:85-88` overwrites it from Twitch during
  onboarding. `SetupController.kt:130` sends `botUsername` the wizard contract doesn't have
  (`SetupWizardDtos.cs:98-114`). `:186-207` — `applyBasics()` runs after complete, ignores failures.
- `ShellScreen.kt:1233` — no hub-state indicator; `DashboardHubClient.kt` exposes no connection state
  (failures swallowed `:142`; `AdminHubClient.kt:103`). `frontend-ia.md:62` requires it.
- `ShellAccessController.kt:73-85` — transient `effectiveMe` failure silently demotes the broadcaster
  to the viewer surface. `ConnectController.kt:477-511` — unreachable backend on boot looks like
  "logged out". `ReconnectBanner.kt:106` discards the error detail.
- **No action-required / notification centre exists** (no `actionRequired` anywhere in `feature/`,
  `core/`); `needsReauth` and missing scopes surface only on Integrations (`IntegrationsScreen.kt:240,483`),
  a Broadcaster-floored Setup page. Home (`HomeController.kt:83-138`) has no first-run / next-steps state.
- `SettingsScreen.kt:1458-1494` — raw scope strings, no feature mapping, no re-grant here
  (`IntegrationsController.kt:371` has it). `:404-407,507` — regrant/reconcile failures swallowed.
- IA vs `frontend-ia.md`: Admin in the channel sidebar (`ShellScreen.kt:723-725`, spec `:209,242`
  says profile menu + chrome swap); profile menu lacks theme + Account (`:1008-1121`, spec `:204-214`);
  Settings is one nine-card scroll, spec says tabs with floors (`:326-355`, spec `:220-229`); `MyData`
  at Moderator floor in Setup (`ShellNav.kt:180`) though it is the caller's own GDPR data; 42 routes
  shipped vs 21 in spec `:302` — reconcile.
- Copy: `strings.xml:106` tells users to generate a client secret the backend made optional
  (`SetupWizardDtos.cs:88-91`); `SetupCopy.kt:100-108` maps 3 of 4 wizard lines (Dutch shows one
  English line).

### B7. Runtime stability (hosted services · EventSub · hubs · tokens · DB)

- `Platform/Eventing/WebSocketEventSubTransport.cs:451-453,546-547` — clean-close path reconnects with
  **zero delay**; Twitch 4003/4004 (no subs / grace expired) becomes a tight spin. Backoff on every
  re-entry; reset only on welcome.
- `TwitchEventSubHostedService.cs:914-919` — `ReconnectAsync` stops all sessions and restarts only the
  bot session; per-broadcaster sessions stay dead until an unrelated subscribe.
- `:320-367` — `EventSubRevokedEvent` published, **no handler** (same for `EventSubConnectedEvent`,
  `EventSubSubscriptionStatusChangedEvent`); revocation never becomes `needs_reauth` or a dashboard
  notice. `EventSubDisconnectedEvent` declared, never published (`WebSocketEventSubTransport.cs:461-475`).
  `:236` — `_activeSubscriptionCount` assigned per owner, health reports one session's slice.
  `:391` — `Task.Delay(Infinite, ct)` leaks when ct is None; use `WaitAsync`.
- `Program.cs:152-170,774-777` — SignalR has no backplane, `WithStatefulReconnect()` never called so the
  configured buffer is inert. `Platform/ChannelRegistry.cs:29,118-210` — process-local cache, no Redis
  pub/sub anywhere; `IEventBus` in-process only (`DependencyInjection.cs:787-790`). Multi-replica =
  stale config + missing pushes.
- OAuth refresh has no per-connection lock (`Platform/Auth/TwitchAuthService.cs:251-262`,
  `Kick/KickAccessTokenProvider.cs:124-134`, `YouTube/YouTubeAccessTokenProvider.cs:78-83`); Twitch rotates
  refresh tokens, so concurrent refresh → `invalid_grant` → spurious `needs_reauth` after 3
  (`IntegrationTokenVault.cs:230-244`). `Platform/Scheduling/TokenRefreshService.cs:35-44` — Twitch-only,
  first tick after 30 min (expired tokens at boot), no proactive refresh for Spotify/YouTube/Kick/Discord.
- Four workers spin without backoff when the tick throws (delay inside the try):
  `Chat/YouTube/YouTubeLiveChatPollWorker.cs:83-96`, `Commands/Jobs/ScheduledPipelineExpiryService.cs:50-58`,
  `Commands/Jobs/TimerService.cs:75-83`, `Rewards/Jobs/RedemptionTimerExpiryService.cs:49-60`.
- `DependencyInjection.cs:806-807` — sync `ConnectionMultiplexer.Connect` with `abortConnect` default: Redis
  down at first resolve = rate limiter permanently unconstructable. `Program.cs:487-495` — Redis health
  check creates + disposes a multiplexer per probe.
- `DependencyInjection.cs:145` — SQLite opened without WAL / busy timeout (`LegacyImportCli.cs:28` assumes
  WAL) → "database is locked" under ~25 hosted services. `:141-166` — no `EnableRetryOnFailure` on
  either provider.
- `Platform/Persistence/UnitOfWork.cs:25-26` — nested `BeginTransactionAsync` orphans the outer
  transaction; class not disposable.
- `Api/HealthChecks/DatabaseHealthCheck.cs` — dead, Npgsql-hardcoded; delete or make provider-aware.

---

## Remediation order (on top of the two existing plans)

Severity first, then "unblocks the most":

1. **B4 music queue lifetime** (scoped service holding the queue) + A2 Spotify visibility — song
   requests are currently fictional; one-line DI fix + error surfacing.
2. **B7 runtime**: EventSub zero-delay reconnect, reconnect-drops-broadcaster-sessions, revocation
   handler, four no-backoff workers, SQLite WAL, refresh lock. All small, all silent-failure class.
3. **A1 + stability F4**: pipeline truthfulness (PartiallyFailed + invoker reply) and the raid preset.
4. **TTS slice = A3 + A4 + A5**: system TTS surface with audio queue; voice lookup/override fixes;
   segment action + spec amendment.
5. **Form infrastructure = A6 item 5 + A7 + widget-plan item 7**: backend action field-schema +
   resource-picker field kinds + helper registry/endpoint + shared "All helpers" dialog. Everything in
   B1/B2/B3 marked "raw text where a picker belongs" rides on this.
6. **A6 Discord**: rule editor pickers, offline/hype-train triggers, richer action, live-role sync.
7. **B1 dead config + stale cache** (prefix/match modes, overlay response type, Pipeline.IsEnabled,
   pipeline-update invalidation) with stability F3.
8. **B3 moderation reach**: chat-filters screen, AutoMod settings + held queue, mod add/remove, clear
   chat, timeout-duration parsing (the accidental permanent ban), reason fields.
9. **B2**: reward lifecycle hub events + Response field; catalog item full form + edit; game
   settlement refund; giveaway eligibility/weighting/auto-close.
10. **B5**: per-widget tokens + staged rotation; test button; preview; last-seen; settings form by
    schema-availability; picker field types.
11. **B6**: auto-join semantics, channel-bot device flow, hub-state indicator, notification centre +
    Home first-run, IA reconciliation, copy fixes.
12. Everything else in B1–B7 opportunistically, batched by file.
