# Usability + stability shortcomings audit — scope and plan

Third plan, sitting on top of `stability-audit-scope-and-plan.md` (F1–F19) and
`widget-quality-audit-scope-and-plan.md` (§1–§8). Two parts:

- **Part A — owner-reported issues (2026-08-22)**, each grounded to file:line with the change
  needed, in words.
- **Part B — grounded rundown of the rest of the system**, by area, same format.

Only open entries remain (fixed ones were deleted). Every entry says what is wrong and what must
change; none contain code.
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
- Two tokens: the Helix raid POST runs on the broadcaster token (`TwitchRaidsApi.cs:46-57`), the
  shoutout on the bot/moderator token. One can be valid while the other is expired/unscoped — exactly
  half works.
- `ChatMessageHandler.cs:926,942-943` — `{target}` strips `@`, `{args.0}` does not; steps using
  `{args.0}` raw keep the `@`.
- Legacy parity gaps (`nomercy-bot/.../commands/Raid.cs`): no-arg `!raid` lists ranked candidates
  (`:33-37`); live-check with a chat reply (`:64-73`); raid fired first then announce (`:81-82`);
  "already raiding" tolerated (`:201-205`); OBS scene switch to "Ending" (`:177-192`); hype announce
  + 45/30/15/10/5/3/2/1 countdown + "RAID LIVE!" (`:208-259`); OBS StopStreaming + Spotify pause
  (`:266,283`); catch-all chat reply on error (`:99-107`); broadcaster-only floor (`:24`).

What must change:

2. Shoutout cooldown skip must not be a silent Success — either surface it as a skipped step in the
   reply, or let `start_raid` bypass the shoutout cooldown when invoked from a raid.
4. Ship a first-party **raid preset** (one click in Commands → "Raid helper") reproducing the legacy
   flow: shoutout → Helix raid → countdown messages → optional OBS scene/stop + Spotify pause, each
   toggleable.
5. Strip `@` from `{args.N}` the same way `{target}` is stripped.

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
   `{text, voice, audioUrl, durationMs}`; the system TTS surface plays them back-to-back. No
   server-side mp3 splicing needed. One ledger row per utterance, one queue slot, censor applied per
   segment.
3. Catalogue: add the field-schema to the backend action descriptor (fields, kinds, options) and a
   repeatable "segment" field kind in the palette so the builder renders "+ add segment" rows with a
   voice-mode dropdown per row.
4. Ship the owner's example as a preset: "Sub streak redeem → random pick item + 'they also said:' +
   user's message in the user's voice".
5. Implement `BypassQueue` or remove it from the spec.

Slot: the TTS slice.

### A6. Discord "go live → message to channel + roles" is primitive and undiscoverable

What is wrong, grounded:

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
- Event Responses has zero Discord surface (`feature/eventresponses/` — no references; preset
  catalogue `EventResponsePresetCatalog.cs:108` has stream.online chat-only). Nothing on the go-live
  surface hints Discord exists.

What must change:

2. Add `go_offline` and `hype_train` (begin/end) triggers to the closed set, with handlers on
   `ChannelOfflineEvent` and the hype-train domain events, and spec text in `spec/discord.md`.
3. Make the pipeline action match the spec: optional own channel + template + embed + ping role,
   falling back to the stored rule only when omitted.
4. Live-role engine exists (`DiscordLiveRoleService`); missing the endpoint and dashboard UI to
   create and edit a config row (per guild connection: role ids added on `ChannelOnlineEvent`,
   removed on `ChannelOfflineEvent`), surfaced on the Discord page next to notification rules.
6. Event Responses: add a Discord preset for stream.online / stream.offline / hype train that deep-links
   to the Discord rule (or composes the richer action from item 3).

Slot: after widget-plan item 2 (test-run wiring) and before widget-plan item 7 (variable picker).

---

## Part B — grounded rundown by area

Seven lanes. Each item: where — what is wrong — what must change. Paths are relative to `server/src/NomNomzBot.*`
or `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/`.

Written 2026-08-22: re-verify each bullet against the current code before trusting it.

### B1. Commands · event responses · timers · chat triggers · pipelines

Dead config (saved, never read):
- `Platform/Eventing/EventResponseExecutor.cs:78-97` — handles `chat_message` and `pipeline`; the
  `overlay` response type offered by the dashboard (`EventResponsesScreen.kt:292,418-431`) hits the
  no-op default. Implement the overlay leg or drop the type.
- `ChannelRegistry.cs:412`, `Commands/Jobs/TimerService.cs:240-245`, `EventResponseExecutor.cs:138-145`
  — none check `Pipeline.IsEnabled` (`Pipeline.cs:39`; list toggle `PipelinesScreen.kt:464`). Disabling a
  pipeline stops nothing. Filter in all three.

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

- `MusicService.cs:415,170` — provider `AddToQueueAsync` bool discarded: viewer told "Added" when
  Spotify queued nothing (no active device, expired token); skip dequeues before push, a failed push
  loses the request. `SpotifyMusicProvider.cs:1486-1515` — NO_ACTIVE_DEVICE only retried when a
  device is remembered, else swallowed. `:271-294` — search returns `[]` for auth failures → viewer
  told "No tracks found".
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
- `Platform/Persistence/UnitOfWork.cs:25-26` — nested `BeginTransactionAsync` orphans the outer
  transaction; class not disposable.
- `Api/HealthChecks/DatabaseHealthCheck.cs` — dead, Npgsql-hardcoded; delete or make provider-aware.

---

## Part C — round two: multi-platform simultaneous streaming + personas (2026-08-22)

Owner's hard rule: streamers go live on Twitch + Kick + YouTube **at the same time** and must manage
all of it uniformly from one place. Eight lanes. Same format.

### C0. The structural root (every lane hit it)

- Viewer identity: `User + UserIdentity(Provider, ProviderUserId)` exists (`UserIdentity.cs`,
  `User.cs:27`) and per-viewer state keys on internal Guids — but linking is still **inert**:
  `UserIdentityService.cs:152-158` `LinkAsync` returns `IDENTITY_ALREADY_LINKED` for the only real case
  (already chatted on Kick before linking; spec §3.1a absorption unbuilt), `IViewerMergeParticipant` has
  zero occurrences, `ViewerRowAbsorbedEvent` (`UserIdentityEvents.cs:48`) has no publisher.
- Entities still carry `ViewerTwitchUserId` / `SubjectTwitchUserId` with no provider column
  (`CurrencyAccount.cs:25`, `UserTrustScore.cs:31`, `GiveawayEntry.cs:31`, `LeaderboardSnapshot.cs:27`, … —
  the correct shape already exists in `ChatPoll.cs:61-66`, `EventJournal.cs:72-76`,
  `ChannelChatterDay.cs:31`).
- `Domain/Platform/Enums/PlatformType.cs:13` is a dead second platform enum (Twitch, Discord) with no
  reference outside its own file, contradicting `AuthEnums.Platform`.

What must change: viewer link + absorption + merge participants built; `*TwitchUserId` →
`*ExternalUserId + *Provider`; delete `PlatformType`.

### C1. Combined management matrix (surface → today)

| Surface | Today | Evidence |
|---|---|---|
| Combined chat feed | per-channel merge, read-only, no composer; Twitch lines unbadged | `MultiChatController.kt:38-144`, `MultiChatScreen.kt:64`, `ChatScreen.kt:432-435`; `ChannelSummary` has no `provider` (`IntegrationDtos.kt:38-47`); merge dedupes by id not (provider,id), sorts by ISO time across push/poll (`MultiChatController.kt:131-139`) |
| Commands/builtins reply target | per-tenant, origin-blind; no per-platform restriction on a command | `ChatPlatformRouter.cs:119-129`, `ChatMessageHandler.cs:442-443` |
| Timers/announcements | per-channel, single platform; author thrice | `TimerService.cs:103-108,213-214` |
| Event responses/alerts | Twitch EventSub vocabulary only; no Kick-sub / YouTube-member translators | `SubscriptionTranslators.cs:19-26,85`; `Platform/Eventing/Translators/` 17 files all Twitch |
| Moderation | per-tenant; "ban everywhere" only via federation-gated shared-ban/network-nuke | `ModerationService.cs:1437-1498`, `moderation.md:963-967`, `OperatorNetworkBanService.cs:44` |
| Stream info / go-live | per-channel form; YouTube rejects category/tags, Kick has no platform API | `StreamController.cs:220-223,307,336,364`, `YouTubePlatformApi.cs:51,62`, `DependencyInjection.cs:1334-1335` |
| Home/analytics | `platformsLive` combined; viewer count single tenant (Twitch only sampled) | `DashboardController.cs:349-368`, `HomeScreen.kt:305,556`, `StreamStatusPollingService.cs:104-143,237` |
| Economy earning | per-message provider-aware identity, per-tenant balance | `ChatEarningHandler.cs:47-58` |
| Song requests/TTS | per-tenant queue | `SongRequestBuiltin`/`SongRequestAction` via tenant `IChatProvider` |
| Settings/bot identity/prefix/personality | per-channel; no owner-level surface | `ChannelBotController.kt` |

What must change: composer with target selector (all watched / one) + per-target result; badge every
line incl. Twitch; `provider` on `ChannelSummary`; timers/announcements/responses get a platform
target set with per-platform rate limiting + duplicate-send suppression; supporter events normalised
into the same domain events with a `Provider` discriminator (one response config, one alert queue);
one go-live form fanning out with per-platform applied/rejected; per-platform viewer breakdown +
summed total + cross-platform stream session; owner-scoped "apply to all my platforms" mod action
without federation; earning credits the linked person.

### C3. YouTube streamer

- `OAuthProviderRegistry.cs:194` — never requests `youtube.force-ssl` → every reply, ban, delete on
  YouTube **403s**. `YouTubeLiveChatClient.cs:61` — 403 mapped unconditionally to `MISSING_SCOPE`
  (quota/rate 403s misreported, 15-min backoff). `YouTubeAccessTokenProvider.cs:157` — failed refresh
  → null, no dashboard signal.
- `ChannelOnlineHandler.cs:115` + `TimerService.cs:96` + `ChatMessageHandler.cs:121` — `IsLive` only
  from Twitch `stream.online` → timers and first-message welcome never run for YouTube.
- No YouTube translators (17 files all Twitch): super chat/sticker, membership, member milestone,
  gifting never become events. `YouTubeLiveChatClient.cs:428` — `MapMessage` ignores `snippet.type`,
  non-text items published as blank chat lines. `:51` — `maxResults=1`, second simultaneous broadcast
  silently ignored. `:178` + `YouTubeChatPlatform.cs:75` — >200 chars fails closed, warning only.
  `YouTubeLiveChatPollWorker.cs:216,243` — own-channel fetched twice per tick (quota); `:50,150` flat
  1-min retry, no jitter/Retry-After; `:60` per-process poll state (double polling on two instances);
  `:386` `UserLogin` = lowercased display name (not unique/stable) used as key.
- `YouTubeChatPlatform.cs:127` — unban of a Studio-made ban is a log-only no-op reported as success.
  `ChatPlatformRouter.cs:127` fallback can execute a YouTube tenant's moderation against Twitch.
- UI/config: `strings.xml:57,955` connect copy says "music provider" (never mentions live chat);
  `IntegrationsScreen.kt:127` fixed `youtube.manage`, no re-grant path; `SystemDtos.kt:36` omits the
  `YouTube` system check the backend emits (`SystemController.cs:153`; the Kotlin comment claiming a
  backend gap is wrong); `SystemController.cs:159` calls the credentials "optional — music provider"
  though they power live-chat refresh; `YouTubeMusicProvider.cs:76,99` needs `YouTube:ApiKey` with no
  wizard step/check. `StreamStatusPollingService.cs:105` — `concurrentViewers` never read.
  `YouTubePlatformApi.cs:51,62` — combined stream-info update half-applies with one `Result`.
- Coverage: `liveBroadcasts.list/update`, `liveChatMessages.list/insert/delete`, `liveChatBans`,
  `channels.list(mine)` only. Missing: all event types, `liveChatModerators`, `liveStreams`/health/
  cuepoints, broadcast transition/bind/insert/delete, `concurrentViewers`, `type` discriminator.

### C4. X/Twitter and platform-neutrality

- X = login only: `Identity/Login/TwitterLoginProvider.cs:30` (PKCE, tokens vaulted, never read);
  `LoginProviderRegistry.cs:57` behind `use_twitter_login`; `AuthEnums.cs:81-94` `IntegrationProvider`
  has no twitter; scopes read-only (no `tweet.write`); `ProviderBrand.kt:79` only X UI.
  `spec/platform-identity.md:186-206` decides X is login-only (intentional) — there is no cross-post
  spec at all. Go-live announce is Discord-shaped (`DiscordGoLiveNotificationHandler`), not a
  registry of announcement targets.
- No Trovo/TikTok/Facebook/Rumble anywhere; platform set closed at three. 92 `"twitch"|"kick"|"youtube"`
  string literals across 68 files (mostly registration keys). `MultiChatScreen.kt:219-221` tags
  non-Twitch only; `ProviderBrand.kt:58-80` palettes used only on the connect modal.
- What must change before a 4th platform: C0 spine + badge every line + announcement-target registry;
  before X specifically: `IntegrationProvider.twitter`, `tweet.write`, announcement target. DECIDED
  (PRODUCT-ALIGNMENT D3): X Live is a sibling streaming platform connection now; tweets/clips are a
  separate announcement-target idea.

### C5. Viewer persona on SaaS

Built (6 participant screens) but stranded:
- `ChannelSwitcherController.kt:54` — switcher lists owned/modded channels only → a pure viewer's
  switcher is empty. `ShellAccessController.kt:58-68` — `primaryChannel()` fails → `channelId = ""`
  → all six screens show "No active channel — reconnect" with a Retry that can never succeed
  (`ParticipantController.kt:503,506`). This is a real viewer's first-run experience.
- `ShellNav.kt:180` — `MyData` (GDPR) floors at Moderator; `GdprController.cs:29-30` is plain
  `[Authorize]`, `GdprApi.kt` wired — zero viewer UI for export/erase. `MeScreen.kt:113-133` — no
  GDPR, no linked accounts, no notification prefs; `AuthController.cs:120,142,214,266,276` identity
  link/unlink/primary API has **no client at all**. `TtsConfigController.cs:362,381,403` `me/voice` +
  `TtsApi.kt:111,242,254,260` wired, never called (TTS page floors at Moderator `ShellNav.kt:144`).
  `CommunityController.cs:948` `me/standing` never called. No viewer giveaway entry/my-entries
  endpoint or page (`GiveawaysController.cs:46-167` all management). `PublicSongRequestController.cs:28-70`
  exists but ParticipantShell never links to it and never shows the viewer's own requests
  (`ParticipantController.kt:159`). `ShellNav.kt:126` — lowered `quotes:read` has no participant
  destination.
- Truthfulness: `ParticipantStates.kt:77` leaderboard `optedIn` defaults true, never read
  (`ParticipantController.kt:200-211`); `ShellScreen.kt:294-302` "preview as viewer" uses the manager's
  own access (previews a moderator); `ParticipantShell.kt:150,191` channel chip shows the *viewer's*
  name; `ParticipantController.kt:431,438` analytics failures → card vanishes; `:481-486` profile
  self-service passes `null,null` (pronoun only); `MeScreen.kt:220-252` "channels I appear in" is inert
  (it is the missing switcher source); `:291-301` no "my contributions" per jar; `ParticipantShell.kt:126`
  no route/deep link, refresh dumps to MyChannel.
- Multi-platform: `ShellAccessController.kt:107` keys everything on the platform GUID; Twitch forced
  primary sign-in (`AuthController.cs:166`); a Kick-only viewer likely cannot sign in at all; no link UI.

### C6. Moderator of many channels

- `ChannelsController.cs:166-212,228-314` — moderated-channel discovery is Twitch-only (Helix Get
  Moderated Channels, `Platform.Twitch` hardcoded); Kick/YouTube mods invisible.
  `ManagementRoleReconcileService.cs:80-83` walks `Enabled && IsOnboarded` only → moderator-mode
  tenants (un-onboarded by design) never re-reconciled: demotion never propagates. `:35`/`ChannelsController.cs:35`
  first tick after 10 min; reconcile runs on each broadcaster's token, dead token → skipped forever
  silently (`:93-94`); no "roles last synced" signal.
- `ShellScreen.kt:777-786` hardcoded green live dot; `:243` roster loaded once per session
  (`StreamStatusChanged` hub event never fed back); `:758` active channel dropped from roster → header
  silently renames to another channel while `SessionStore.activeChannelId` (`SessionStore.kt:85-87`
  never cleared) keeps sending the revoked id → every page 403s (`TenantResolutionMiddleware.cs:83-89`).
  `ChannelsApi.kt:83-98` `primaryChannel()` falls back to `channels.firstOrNull()` and controllers pass
  that as route `channelId` (route beats header, `TenantResolutionMiddleware.cs:111-119`) → **action
  can land on a different streamer's channel**. `ChannelsApi.kt:136-137` + 43 call sites re-fetch the
  whole list uncached, each a live Helix moderated-channels read + membership upserts
  (`ChannelsController.cs:84-91,100,116-163`).
- `ShellScreen.kt:251-263` + `DashboardHubClient.kt:120-130` — hub joins exactly one channel; nothing
  from other channels reaches the mod (hub supports multi-join `DashboardHub.cs:28-37,90-153`).
  `ShellScreen.kt:355-367` — the only hub reaction is an unattributed error toast. No "my channels"
  home / cross-channel queue (per-channel data exists: `ModerationApi.kt:39,141,166,191`).
  `ModerationController.kt:219-241` — queues never re-fetched on `ModAction` (two mods double-act).
  `ShellScreen.kt:271-278` — mid-switch blank splash with no timeout/error (stuck forever on probe
  failure); `:1190` role badge only inside the dropdown; `:294` preview keyed on stale `access.channelId`.
- `spec/moderation.md` / `stream-admin.md` — no multi-channel moderator section at all. 🔒 owner:
  spec the persona (aggregate queue endpoint shape, notification attribution) before building.

### C7. The bot as a chat bot

- There is no `!songhistory` builtin (checked against every `BuiltinKey` in `server/src`) and nothing
  seeds it as a custom command — a regression against the legacy bot's `commands/*.cs` set for
  migrating viewers. It needs a backend a custom command cannot give: no played-track history exists
  in the stack (findings ledger L8b).

What must change (C7): a `!songhistory` builtin backed by a played-track history.

---

## Part D — platform admin (Plane-C): system-level management for the SaaS operator and self-host owner (2026-08-22)

Four lanes: IAM/tenant-ops backend · admin dashboard UI · system-level content · impersonation/support access.

### D1. What exists (inventory)
Controllers: `AdminController` (stats/channels/users/system/health/events), `PlatformAdminController`
(tenants list/detail/suspend/reinstate, support access begin/end, impersonate, audit search),
`PlatformIamController` (roles/principals/assign/revoke), `AdminBillingController` (invites, grant tier/
founder), `FeatureFlagAdminController`, `ComplianceController` (erasure), `FederationController`,
`WidgetGalleryController` (`gallery:review`), `PlatformAnalyticsController`, `AdminHub`. 13 keys seeded /
12 enforced (`billing:refund` never enforced).

### D2. IAM and bootstrap (backend)
- `PlatformIamService.cs:171-236,324-371` — assign/revoke/create/deactivate/reactivate write **no**
  `IamAuditLog` row (only the permission check does, without target/role/scope). `:106-169` create is
  non-transactional and can flush an orphan principal on "Unknown user". `:163/:66` acting principal
  `Guid.Empty` on self-host/bootstrap → assignments attributed to nobody. `:200` duplicate/inactive-target
  assignments allowed. `:335` only self-deactivation guarded → last `iam:manage` holder can be removed.
  `PlatformAdminController.cs:186-191` + `PlatformIamController.cs:145-152` duplicated helper swallows a
  resolve failure into `Guid.Empty` (= allow on self-host).
- `Content/Identity/IamCatalogSeeder.cs:101` — `platform-billing` role can only read; all billing writes
  (`AdminBillingController.cs:55,63,69,86`) gate on `iam:manage`; `billing:refund` has no endpoint.
  `ComplianceController.cs:42` — admin GDPR erasure gated on `tenant:access` (a support-visit key).
- `PlatformIamService.cs:79` `IamAccessEvaluatedEvent` and `PlatformAdminService.cs:250`
  `TenantAccessGrantedEvent` have zero consumers (no owner notification, no break-glass alert, no
  expiry reaper); `FeatureFlagAdminService.cs:87,136,161` flag changes unaudited.

### D3. Tenant ops and user management (backend)
- Missing: platform-wide user disable/ban (only per-channel bans exist); `GET admin/users/{id}` detail
  (channels/identities/sessions/consent); tenant delete/purge + ownership transfer (`DeletionAuditLog`
  unused); quota/limit administration + per-tenant billing state; re-run onboarding seeds; tenant secret/
  token rotation; EventSub session inventory, token-health-across-tenants, worker status, queue depth,
  error-log surface. `UserIdentityService.cs:308` `MergeIdentitiesAsync` built + tested, zero call sites.
- `PlatformAdminService.cs:288-291` — an operator can end only their OWN support grant; no list of active
  grants. `:50,54,97` + `AdminService.cs:79` — tenant/channel lists never `IgnoreQueryFilters` (soft-deleted
  tenants invisible to the operator). `:56` search on normalized name only (no id/owner/GUID).
- `AdminService.cs:63` headline status hardcoded `"healthy"`; `:89,175` counts hardcoded `0`; `:71-143`
  ignore Sort/Order, no search. No `[EnableRateLimiting]` on any admin controller; destructive ops take a
  whitespace-checked string only. `AdminHub.cs:25` no connect snapshot; all pushes `Clients.All`.

### D4. Impersonation and support access
- Support access (`BeginTenantAccessAsync` `PlatformAdminService.cs:200-274`) creates a time-boxed
  scoped `IamRoleAssignment` with reason + expiry but grants **zero** channel-plane capability
  (`RoleResolver.cs:107-191` never reads IAM; `PlatformIamService.cs:415-425` scope honoured only for
  Plane-C) — the only door into a tenant's tools is full impersonation, and impersonation does not
  require an open support session. Spec (`stream-admin.md:231-263`) promises audited support access
  only; impersonation is code ahead of spec.
- Right shape to reuse: `ShellScreen.kt:290-305` preview-as-viewer (client-side downgrade, no token).

### D5. System-level content (what the operator defines once for every tenant)
Every content type is code-seeded (a change = redeploy): widget catalogue (`FirstPartyWidgetCatalogue.cs:49`,
seeder upserts gallery rows; installed tenant copies never updated, no version stamp on either side),
widget templates, TTS voices (+ boot sync), pronouns, IAM catalogue, action definitions, billing tiers,
config defaults, builtin catalogue (DI-derived, no DB row → cannot disable platform-wide), default
builtin enablement (top-up over all channels), event-response presets (`EventResponsePresetCatalog.cs:22`
static; onboarding + boot backfill), tones (`ToneTemplateCatalog.cs:27` static), custom-data presets.
**None** for: pick-list presets, permission presets, fun-command packs, template-helper catalogue,
platform announcement/maintenance banner, and **no system-level custom command or pipeline preset at all**
(`DefaultCommandsSeeder.cs:39` seeds five builtin keys, not command bodies). `Widget.cs:25-65` has no
`IsSystem` (tenant can delete a system surface; nothing recreates it). No admin kill-switch for a
first-party widget or builtin.

### D6. Admin dashboard UI
- `AdminController.kt:150-165,202-235` — health/events/flags/invites failures → empty; seven
  flag/billing writes ignore `ApiResult`.
- `AdminScreen.kt:541-579` flags read-only though set/override/delete are wired; `:605-616` invite
  creation hardcoded (1 redemption, no tier/expiry/founder); `:80-81` grant-tier/founder dead;
  `AdminTenantsTab.kt:157-189` support access built, never callable; no End-access, no active-grant list.
- `AdminScreen.kt:412-419` + `AdminApi.kt:240` — Impersonate with no confirm, constant justification.
  `AdminIamTab.kt:145,220-233` — revoke no confirm, reasons null. `AdminTenantsTab.kt:292-318` — Ban and
  Suspend equal-weight buttons, no name echo. `AdminAuditTab.kt:77-84` — raw permission text filter;
  principal/tenant/date filters unexposed. Lists never page (`AdminController.kt:334-349,395-410`; 25 cap).
- `ShellScreen.kt:316-318,723-725` — Admin in the sidebar on legacy `user.isAdmin`, no Plane-C role
  check, no chrome swap, one route (`:642`) vs spec's per-page routes; Channels tab duplicates Tenants with
  less; `AdminController.kt:172-198` hub "live" never goes false; `AdminHubClient.kt:103` connect failures
  swallowed; `AdminScreen.kt:174-176` stale-vs-empty load logic, no refresh; role keys never viewable, no
  role CRUD; raw ISO timestamps; status string untranslated on Overview; service-account key dialog
  mislabelled (`AdminIamTab.kt:435-437`). i18n en/nl parity clean (114 keys).

What must change (admin): see S087 and S090-S097 in `SHORTCOMINGS-EXECUTION-PLAN.md`.

---

## Part E — round four: every surface not yet walked (2026-08-22). Denominator now closed.

### E1. Stream / live-ops / schedule / analytics / journal
- **No Stream page**: `feature/liveops/` = schedule only; `LiveOpsController.kt:32` consumed solely by
  `HomeScreen.kt:236` — live-ops is a button grid on Home. Hype train, goals, charity, guest star fully
  built server-side (`HypeTrainEvents.cs`, `GoalEvents.cs`, `CharityEvents.cs`, `GuestStarEvents.cs`,
  Helix clients, `HypeTrainBroadcastHandlers.cs`) with **zero** dashboard consumers. Shield only on
  Moderation. `PlatformAnalyticsController.cs:32` has no Kotlin client.
- **No journal browse**: `EventStoreController.cs` = export/import/replay/import-legacy/rebuild only; no
  list/query; replay + import-legacy have zero callers; three buttons in Settings (`SettingsScreen.kt:1694`);
  rebuild shows a raw task id with no status (`:1696`).
- Webcal subscribe built (`LiveOpsController.cs:433`) but no client builds the URL; `ScheduleController.kt:169`
  downloads a dead snapshot. `ScheduleScreen.kt:455,462,389,468` start/timezone/duration free text; `:421`
  edit blanks timezone; `:305` raw ISO in rows; `HomeScreen.kt:1149` tags as comma text.
- `HomeScreen.kt:804-820` poll/prediction results never rendered (votes/outcomes fetched); no refresh of
  poll state; ad countdown never shown (`nextAdAt` loaded, unused); `LiveOpsController.kt:54-65` poll/
  prediction/ad failures → empty; `HomeScreen.kt:434` raid-pending never clears; `:218` errors dropped
  unless Ready; `ScheduleController.kt:88-148` writes `channelId ?: return` silently.
- Analytics: `AnalyticsController.kt:116-118,246-268` failures → empty; `:320` fixed 30-day window;
  `:92-97` metric hardcoded Messages; `:376` UTC day boundary; `:115` unchecked cast.
- Whole lane Twitch-only by construction (`StreamInfoUpdate` has no platform; language read-only).

### E2. Assets · media share · sound · OBS · VTS
- Media share: mods moderate blind (`MediaShareScreen.kt:302-325` — no thumbnail/link/name; DTO carries
  them); **no player widget exists** (`GetNext` `MediaShareController.cs:78` has no consumer — approved
  clips never play); `MediaShareSubmittedEvent`/`PlaybackChangedEvent` have no handler/trigger source;
  queue unpaged at 25 (`MediaShareApi.kt:73-84`).
- `PipelinesScreen.kt:873-965` OBS/VTS fields free text though `obs/scenes`, `obs/inputs`, `vts/inventory`
  exist. `SoundClipOverlayNotifierAdapter.cs:34-38` passes null handle → `stop_sound` by handle never
  matches. Assets are an island (no picker anywhere; `AssetsScreen.kt:279` copy-URL); delete has no
  used-by guard; limits invisible (8 MB / 10 MB / types); `AssetsApi.kt:43` take=200 drops `hasMore`.
- `SoundController.kt:57-74` upload fixed volume 80, no trigger/cooldown/floor; no clip replace; dead
  `POST sound-clips/{id}/preview`. OBS page ignores OBS's own events (`ObsController.kt:123-129`); exposes
  5 of ~20 verbs; bridge failures → "offline"; `ObsScreen.kt:238-242` edits reset on save. VTS: no
  probe/bridge status (`VtsController.cs`), stored status shown as live; inventory failure → "locked";
  parameter/tint control has no screen; endpoint unprefilled/unvalidated; hardcoded English error
  (`ObsController.kt:260`, `VtsController.kt:177`).

### E3. Code scripts · dev platform · webhooks · custom data · automation · federation
- `OutboundWebhookDispatcher.cs:277` backoff `30×2^(n-1)` uncapped/no jitter (attempt 20 ≈ 182 days);
  `:144,63-64` delivery runs inline on the event-bus thread; `OutboundWebhookAutoDisabledEvent` +
  `AttemptedEvent` zero consumers; `WebhookRetryProcessor.cs:43-47` ignores Result (orphan hot row).
  UI: `WebhooksScreen.kt:1259-1264` no `NextRetryAt`; `:1212` failure → "no deliveries"; `:1211` no
  refresh/paging/replay (body stored for it).
- `CustomDataPollService.cs:143-223` every failure log-only; entity has no LastError/attempt/counter;
  `:164-173` non-allowlisted host silently never polls; no backoff/auto-disable. UI
  `CustomEventsScreen.kt:826-837` field map parsed by string split (silently empties); `:642-648,622-627`
  raw JSON/URL. `CustomDataSource.InboundWebhookEndpointId` dead column.
- Stream Deck: 23 `music-*` actions, no run-pipeline/run-command although `ListPipelinesAsync`/
  `ListCommandsAsync` exist to back a picker. Federation: opt-in `OptInType`/`Direction` unvalidated
  (`FederationOptInService.cs:47-79`); UI `peerId` + `capability` free text, `Direction` never collected.
- Code scripts: capability model invisible (`ScriptCapabilityBroker.cs:32-65` catalogue never shown; one
  denial named at run time `JintScriptExecutor.cs:316`); capabilities inferred by regex (`:200-248`);
  `fetchSdkTypes` failure → "" (no autocomplete, silently); desktop editor = plain JTextArea; create dialog
  blank, no templates; no reverse "used by"; test-run can't set the triggering user; no execution
  history; raw enum strings; `ScriptHostBridge.cs:96` unwired capability returns null.

### E4. Supporters · billing · bundles · pick lists · decoration · pronouns · engagement
- Supporters: 11 adapters shipped, **1 reachable** (`SupportersScreen.kt:677-678` hardcoded Ko-fi);
  capabilities never exposed (no `GET /supporters/sources`); one "secret" field for every mode;
  `Status="error"` rendered "Idle" (`:430-437`); status never resets (`SupporterIngestService.cs:124`);
  `SupporterUserId=null` always → economy rewards never fire (`:110`, `SupporterEconomyRewardHandler.cs:50`),
  and `Units:1` regardless of amount; dedup race on unique index; Patreon/Treatstream dedup key omits event
  type; no source filter; no test/simulate; every ingest failure log-only.
- Billing: Usage panel shows "0 of 100" for count-capped keys (only `sandbox_exec_ms` is metered;
  `SettingsScreen.kt:1916-1936`); raw keys + literal `-1`; `UsageQuotaExceededEvent`/`SubscriptionTierChangedEvent`
  no consumers; `free` tier seeds zero limits (uncapped); downgrade immediate, no period-end choice.
- Bundles: export offers 4 of 12 types (`BundlesScreen.kt:307-310`); "nothing to export" computed from
  those 4; type filter free text (`:634-639`); version unvalidated; tags CSV; no update/re-install path.
- Pick lists: `PickListService.cs:289` repeats (no anti-repeat window); items bare strings (no weight/
  enable); whole-list replace with no concurrency check; one-row-at-a-time editing; no tier key.
- Decoration/pronouns/engagement: no chat-decoration settings surface (5 feature toggles only; 60 s
  hot-path lag unsignalled; HTML fragments boolean-only); pronouns have no channel-level surface;
  engagement milestones CSV, no watch-time config, one global cooldown, malformed milestones silently
  reset, no engagement page.

### E5. Desktop app · deploy · updates
- Desktop: no rescan; mDNS errors to stderr; desktop session expiry unhandled; window state not
  persisted; no app icon, hardcoded package version; macOS data dir wrong.
- Updates: no update check, no rollback (migrations forward-only, no pre-migration snapshot), no backup
  verb in deploy scripts, no prebuilt binaries; `/health/version` always 1.0.0.0 (no stamping);
  `/health/ready` ignores migrations + EventSub and reports Degraded as 200; CI image freshness unverified
  under the no-push rule; `latest` only, no versioned tags; dev Postgres password default survives in old
  `.env`.
- Self-host exe: firewall prompt from a windowless WinExe undocumented; tray Windows-only (no stop/
  indicator on Linux/macOS); tray "Open app" path never populated; log path undocumented, no size cap.
- Nothing emits the saas-restriction notice at boot in saas mode (`docker-compose.yml` and
  `.env.example` carry the marker).

### E6. Cross-cutting — security · i18n · a11y · perf · tests · contract
- Security: bearer validation hardcodes HS256 while the token service supports RS/ES (`Program.cs:256-265`)
  and no `ValidAlgorithms`; **7 controllers bypass `BaseController`** (no rate limit, no `[ApiController]`):
  `DiscordInteractionsController`, `InboundWebhookController`, `OAuthRelayController`, `OverlayHost`/
  `OverlaySdk`/`OverlayVueRuntime`/`ObsBridgeHost`; access tokens 60 min with `sid` never checked
  (logout/impersonation-end don't revoke); `AuthController.cs:552` refresh token in URL fragment;
  `:1079` custody decided by `?client=` query; `SameSite=Lax` is the only CSRF defence; no CSP/HSTS;
  `ENCRYPTION_KEY` rotation silently blanks secrets (`SystemCredentialsProvider.cs:50`); keep the
  HtmlSanitizer/AngleSharp CVE pins.
- i18n: en/nl key parity holds, zero drift; 47 hardcoded `label=`/`placeholder=`/`contentDescription=`
  literals; no locale date/number formatter. A11y: Esc closes ~2 of 383 dialogs; 17 null
  contentDescriptions. Perf: `primaryChannel()` N+1 (41 sites); hub reconnect no jitter;
  no Wasm optimize step. Tests: E2E = 2 facts, env-gated, no in-process host; coverage inverted
  (Infra 3074 vs Application 48 / Domain 46). Contract: 0 typed `ProducesResponseType<T>` → 157/617
  operations with no response schema; 633 routes vs 617 in snapshot; 1 of 93 controllers contract-tested.

What must change (Part E): slices S098–S113 in `SHORTCOMINGS-EXECUTION-PLAN.md`.

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
