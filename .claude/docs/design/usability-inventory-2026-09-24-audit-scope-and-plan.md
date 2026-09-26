# Usability + stability inventory — 2026-09-24 (**V**·A1–A6, B1–B8, C)

Owner ask (2026-09-24): "inventorize the bot and find every surface that is unfinished or half
broken … recreate the list of todo's in a way that the bot will actually be usable by anyone."

Method: 11 non-overlapping read-only lanes over the real user flows, five lenses each (raw text where a
control belongs · backend not exposed · undiscoverable · feedback gaps · stability). Every claim that
would mean "the whole platform is broken" was re-read at the cited lines by the orchestrator and is
marked **VERIFIED**; the rest is lane-reported at `file:line`. Nothing already open in
`SHORTCOMINGS-EXECUTION-PLAN.md`, `usability-shortcomings-audit-scope-and-plan.md`,
`stability-audit-scope-and-plan.md` or `widget-quality-audit-scope-and-plan.md` is repeated here.

Paths: `app/` = `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/`; `srv/` = `server/src/`.

Live ground truth pulled from the box (192.168.2.60, 2026-09-24):
- 25 channel rows; **17 are moderator-mode tenants with `IsOnboarded=false`** (created by a moderator
  entering them; the owner's own login will never activate them — A2.1). `anda_six` IS onboarded.
- **Only `anda_six` has a `PlatformConnections` row.** Every other onboarded channel (owner, qtkitte…)
  has zero, so D1 "one channel, many platforms" has nothing to attach to (B6.1).
- qtkitte's Spotify connection: 429 every ~6s for two days, `ConsecutiveFailureCount = 0` — the 429 is
  recorded as "nothing playing", never as a failure (B5.1). Both channels use distinct client ids.
- Helix `POST eventsub/subscriptions → 403` logged 10–20×/s continuously (B7.1).

---

## Part A — owner-reported items

### A1 · Impersonation ("act as") keeps the admin's own channel — VERIFIED

Current state: `beginImpersonation` (app/core/connection/SessionStore.kt:127-136) swaps only the token
and the flag. `activeChannelId` stays the admin's; every REST call sends it as `X-Channel-Id`
(app/core/di/AppGraph.kt:301) and `TenantResolutionMiddleware.cs:83` obeys it (Gate 1 only checks the
channel is active). `ShellScreen.kt:553-554` keys every page on `activeChannelId`, so nothing remounts.
`reconnectAll` (AppGraph.kt:910-913) rejoins the admin's channel on the hub. If the target does NOT
moderate the admin's channel, the mismatch trips the switching guard and the splash shows forever with
no Exit (banner renders after the early return, ShellScreen.kt:520).

Ending is broken too: `exitImpersonation` (app/feature/admin/state/AdminController.kt:1911-1912) calls
`api.endImpersonation` BEFORE restoring the admin token, so the DELETE carries the target's token,
`PlatformAdminController.cs:192` returns 403, the result is ignored: the grant is never revoked,
`ImpersonationEndedEvent` never fires, the owner never gets the "operator stopped" notice.

What must change (one slice, **V-A1**):
1. Begin/end reset the selected channel and bump a session generation; pages key on generation, not
   only channel; `setDefaultChannel` gets a forced-reset path used only on identity swap
   (ChannelSwitcherController.kt:63-65, SessionStore.kt:99-101).
2. `reResolveIdentity` order: reset channel → reload roster → resolve role (AdminController.kt:1919-27).
3. Restore the admin token first, then revoke the grant; surface a failed revoke.
4. 401/expiry while acting → run the full exit path, never the admin refresh (SessionStore.kt:116, 218-228).
5. `clearActiveSession`/`disconnect` clear the act-as flag and stash (SessionStore.kt:242-261); logout
   ends act-as first (ConnectController.kt:334-337).
6. Suppress the Twitch reauth dialog while acting (App.kt:234, ShellScreen.kt:538); don't persist
   routes while acting (ShellScreen.kt:350); skip `EnsureModeratorMembershipsAsync` under impersonation
   (ChannelsController.cs:101).
7. Expose the owner security notices (`ImpersonationBroadcastHandlers.cs:55,106` write them; no
   endpoint/client reads them) as an inbox entry.

Done-when: act as a user who does NOT moderate the admin's channel → their channel list, home, hub feed
and role render; Exit revokes the grant (server row + `ImpersonationEndedEvent`), admin lands back on
Admin with their own channel; a UI test asserts `X-Channel-Id` changes on begin and end.

### A2 · Supporting another streamer's channel (moderator-of-many)

**A2.1 VERIFIED — un-onboarded moderator-mode tenants never activate.** `ChannelService.cs:593` creates
the tenant with `IsOnboarded=false` when a moderator enters it; the owner's later login
(`AuthService.cs:363-382`) reuses the row and never flips it (PlatformConnection is only written when
`isNewChannel`, :388). `BotLifecycleService.cs:194-201` then excludes it from the working set and
unsubscribes EventSub. 17 live rows are in this state. Fix: on owner login landing on an un-onboarded
tenant, promote it (IsOnboarded, BotJoinedAt, PlatformConnection). Also backfill the 17 rows.

Lane-reported, same slice family (**V-A2**):
- `PermissionChangedEvent` is never published (Domain/Identity/Events/PermissionChangedEvent.cs:15);
  `ManagementRoleChangedEvent`/`PermitGranted`/`PermitRevoked` have no handler → the shell's live
  re-resolve (ShellScreen.kt:380) is dead; mod grants/demotes stay stale until reload.
- `channel.moderator.remove` only logs (RoleBroadcastHandlers.cs:170-220); no grant on `.add`; a
  de-modded user keeps Moderator up to 10 min or forever if the channel token is dead.
- `DashboardHub.JoinChannel` (DashboardHub.cs:128) runs Gate 1 only — any signed-in user joins any
  channel's moderation event class. Client sends the join without invocationId so a denial is silent
  (DashboardHubClient.kt:431-432).
- Roster (`ChannelService.cs:201-228`) is built from ownership + legacy ChannelModerators + Twitch,
  never `ChannelMemberships`; role label hardcoded broadcaster/moderator; returns `OverlayToken` to
  every non-owner; goes through the tenant filter (:203) so the roster differs by active channel.
- `EnterModeratedChannel` (ChannelsController.cs:284-291) overwrites Editor/LeadModerator with
  Moderator; entering is gated by `dashboard:read` on the *current* channel (:225); failures collapse
  to null in `ChannelSwitcherController.kt:105`.
- Gate-1/Gate-2 denials return an empty 403 (ActionAuthorizationHandler.cs:41-46,
  TenantResolutionMiddleware.cs:116) → the client can only say "no permission".
- `ChannelSummaryDto` has no onboarded/bot-installed flag (ChannelDtos.cs:38-49) → moderator-mode
  tenants show the full sidebar with inert pages.
- Every `GET /channels` makes a live Helix call + membership writes; `primaryChannel()` is called from
  47 files per navigation (ChannelsController.cs:84-99, ChannelsApi.kt:77); only the first 100
  moderated channels are read (:88,184,246); Helix failure → empty list, no reason.
- `EnsureModeratedTenantAsync` (ChannelService.cs:576-581) returns soft-deleted/suspended tenants.

### A3 · Viewer usability

**A3.1 VERIFIED — P0 security: any viewer can drain any wallet.** `CurrencyController.cs:230-243`
binds `ActorUserId` to the caller but `TransferAsync` (CurrencyAccountService.cs:206-240) debits
`command.FromViewerUserId` from the body and never compares the two; `economy:transfer:write` is
seeded for Everyone (ActionDefinitionSeeder.cs:518). Fix: server sets sender = caller.

Lane-reported (**V-A3**), security first:
- Leaderboard opt-in/out takes any `viewerUserId` (EconomyLeaderboardsController.cs:109-133).
- Game history open to Everyone, `playerUserId` only a filter (GamesController.cs:91-112).
- Dashboard + public song request skip the min-trust gate and requester id
  (MusicController.cs:171-196, PublicSongRequestController.cs:93-97, MusicService.cs:517-524); the
  public page keys per-user limits on the typed name.
- Viewer landing page calls `/dashboard/{id}/stats` (needs Moderator) → 403 text as the home page
  (ParticipantController.kt:129). Leaderboards page first calls `leaderboards/configs` (Moderator).
- Leaderboard rows: `DisplayName` = raw Twitch numeric id (EconomyLeaderboardService.cs:148-203);
  client reads `points/userId` but server sends `value/subjectUserId` → every row 0 (EconomyApi.kt:602-609).
- `WatchHours`/`CommandsUsed` always 0 (UserService.cs:303-307) — fake tiles.
- Now Playing badges hardcode 1/3/5 and a "Sub lane" that does not exist (NowPlayingScreen.kt:149).
- Transfer-to is free text expecting a GUID (PointsAndStoreScreen.kt:158).
- Games show internal keys; 18+ consent has no viewer UI so 18+ games always fail (GameService.cs:214-217).
- Store: Buy enabled over balance, no confirm, `inputArgs=null`, no purchases/ledger for self.
- Chat: giveaway keyword entry never replies (GiveawayKeywordListener.cs:108-118); cooldown reply
  has no time left (ChatMessageHandler.cs:749-755); no built-in points command; `!sr` gives no
  position; `!queue` shows 5 with no "yours"; viewer replies not localised (BuiltinResponseComposer
  has no language parameter).
- The shared `/sr/@name` / `/sr/{token}` links lead nowhere — no route serves them
  (SongRequestsController.kt:106-110, Program.cs:1010 fallback). Tracker line 1193 is wrong; the
  page is still unbuilt.
- Participant nav ignores which features the channel enabled (ParticipantNav.kt:84-85).

### A4 · Twitch scopes are asked for too late — VERIFIED

Owner: "it constantly fires off an error about not having a permission on Twitch and requiring me to
reauth … it needs to require it when being enabled so it's there when needed."

Current state: `FeatureService.ToggleFeatureAsync` (srv/NomNomzBot.Infrastructure/Platform/
FeatureService.cs:176-224) flips `IsEnabled` and stores `RequiredScopes` as metadata only — nothing
compares them to the granted set. The scope is first discovered when a Helix call fails:
`TwitchHelixTransport.cs:389-391` publishes `TwitchHelixReauthRequiredEvent` on EVERY 401 (Twitch
answers 401 for a missing scope), and `MissingScopeRecordingHandler` only records when a scope name
was parsed (which B7.1 shows never happens). So the dashboard shows a "reconnect Twitch" prompt at
use time, on every use, with no scope named, and re-connecting with the same scope set fixes nothing.
`FeaturesScreen.kt:205-226` shows "Re-grant" even when nothing is missing and runs the global
reconnect (B1).

What must change (**V-A4**, one slice):
1. Enabling a feature = a scope check first: compare `RequiredScopes` against the connection's granted
   scopes; if missing, return a typed `SCOPES_REQUIRED` result with the exact list and do NOT flip the
   flag; the client opens the additive re-grant (device code with only the missing scopes) and flips
   on success. Same check on enabling a command/pipeline action tagged `[RequiresTwitchScope]`.
2. A 401/403 from Helix maps to `missing_scope` only when the body says so; other 401s are token
   death. Both carry the scope name (fix the parser, B7.1) and are recorded once per channel+scope,
   not on every call.
3. The reauth prompt is replaced by the action-required item that names the feature and the scope,
   with one "Grant" button; it is dismissed automatically when the scope arrives.
4. On login, diff granted vs required-by-enabled-features and raise the same item, so a scope added to
   a Helix method by a later release is asked for once, up front.

Done-when: enable a feature whose scope is missing → the toggle stays off and the grant flow opens
with the missing scope; grant → toggle on; no Helix call ever raises a "reconnect" for a scope; a
test proves `ToggleFeatureAsync` refuses without the scope and accepts with it.

### A5 · Links the bot hands out that lead nowhere

Owner: "links listed for a streamer channel and song request page do not answer, there are
probably more."

Current state (grounded): the only outbound public links the dashboard builds are
`{origin}/sr/@{login}` and `{origin}/sr/{token}` (app/feature/songrequests/state/
SongRequestsController.kt:106-110, 266-272). No app route and no server controller serves `/sr`
(public controllers: `overlay`, `oauth-relay`, `obs-bridge`, `voice-listener`). Because
`Program.cs:1010` is an SPA fallback, ANY unknown path answers with the full dashboard instead of a
404 — so a dead link never looks dead to a test, it just lands the viewer on the login screen. The
"streamer channel" link the owner saw was not found by grep in app or server; it must be located in
the rendered client (Part C slice 26 starts by clicking every link on every screen).

What must change (**V-A5**): build the public `/sr/@login` page (A3 already lists it) on the
`PublicSongRequestController` endpoints; add a public channel page route or remove the link; and add a
test that walks every URL the app or a chat reply constructs and asserts a non-fallback route serves
it (the SPA fallback must not answer for `/sr/*` or any documented public path).

### A6 · No hoops: every form creates its related things in place — standing rule

Owner: "forms, modals and all user input need to be accessible and user friendly … never open
something to find out you need to exit and navigate away to add something related first."

This binds every slice from now on, alongside S-CONSEQ: **a picker for a related object always
offers "create new" inline**, saves it, and selects it — the operator never leaves the form. Concrete
instances the lanes found, all owed under this rule:
- Command / trigger / timer / event response / reward / custom event → bind pipeline: only picks an
  existing pipeline (PipelineBindPicker); no inline "new pipeline from this trigger".
- Pipeline step → sound clip, asset, reward, Discord channel/role, OBS scene, VTS model, game:
  raw text or an existing-only picker (B2.6/B2.7).
- Alerts / widgets → sound or image: must be uploaded on Assets first (B6).
- Giveaway → currency cost, Store item → pipeline, Reward → pipeline: existing-only.
- Roles → permit a viewer, Moderation → add moderator: raw id / existing-only (B4).
- Discord notification rule → guild must be connected on another page; live role has no UI at all (B8).
- Song request block-list → raw URI (B5); manual add → typed viewer name.
- Wizard review step drops what you typed on web (B1.2); dialogs close before the result (B1, B4).
Slice family **V-A6**: one shared "create inline" affordance on the picker primitive (frontend Phase 3
form infrastructure), then each picker above adopts it. Done-when: from any form, the related object
can be created without leaving, and the form's own draft survives the detour.

---

## Part B — by area (new findings only)

### B1 · Shell, navigation, settings, onboarding
- **VERIFIED** Settings page: outer Column does not scroll and the stream-info Box takes `weight(1f)`
  (SettingsScreen.kt:314-379); the nine cards below (Appearance … Billing, Journal) are cut off in any
  normal window → Bot account, Permissions, Billing unreachable.
- Web setup wizard: `finish()` (SetupController.kt:256-273) → `onReadyToSignIn` navigates away on
  wasm, so `completeSetup()`/`applyBasics()` never run: basics lost, `system.setup_complete` never set,
  and the credential endpoints (SystemController.cs:312,351,447) stay writable without login.
- Browser Back logs the operator out on the first shell entry (App.kt:182-186, RouteStore.wasmJs.kt:71-72).
- Coerced routes push history → Back loops, no "no access" notice (ShellScreen.kt:349-350).
- Setup nav group toggle needs two clicks, never auto-expands, not persisted (ShellScreen.kt:840-844).
- BYOC card: `configured` reads "secret present" (TwitchAppCredentialsController.kt:111); card shown to
  every broadcaster but 403s on SaaS; no revert-to-shared (IntegrationsScreen.kt:321).
- Home dialogs (title, prediction, raid, commercial) close before the result (HomeScreen.kt:523-581);
  same in Roles (RolesScreen.kt:343-389). Permission matrix shows raw action keys (:1186, :898, :1240).
- Connect retry flashes the sign-in screen (ConnectController.kt:669); Unreachable has no sign-out;
  access state not reset on logout (ShellAccessController.kt:53); channel-list error hides the header
  (ShellScreen.kt:875); compact chip shows user name not channel (:425); language menus don't mark
  current; timezone/locale free text (SettingsScreen.kt:1069-1084); StreamElements import has no UI
  (ImportController.cs:45); Features re-grant shows when nothing missing, toggle not locked, no
  "Configure" link (FeaturesScreen.kt:205-236); review step English (SetupWizardScreen.kt:463);
  hardcoded strings (list in lane); wizard state lost on reload; spec drift on Event Responses/Alerts
  placement and Commands floor (ShellNav.kt:253,291,246 vs frontend-ia.md).

### B2 · Automation authoring (commands, pipelines, triggers, timers, events, scripts)
- **VERIFIED** Typed values silently become defaults: client sends untyped params as strings
  (PipelinesApi.kt:376-399), `GetInt`/`GetBool` return the default for a string
  (ActionDefinition.cs:35-57). Affects shoutout tts/cooldown, raid window, stop_sound all, VTS tint,
  OBS duration — and re-saving an existing pipeline.
- **VERIFIED** Cross-channel pipeline execution: `PipelineId` never checked to belong to the channel
  (CommandService.cs:284-285, ChatTriggerService.cs:92,130); registry loads steps by id with no
  broadcaster filter (ChannelRegistry.cs:560-568).
- Editor throws away backend kind/Options/Required (PipelineCatalogue.kt:788-804); 36 actions fall to
  a blank key/value editor; no Number field kind (PipelinesScreen.kt:2552-2694); `var_compare` hint vs
  engine `comparison` (PipelineCatalogue.kt:729); `ResourceId` has no resource type so OBS/VTS/game/
  discord/role fields are raw text; option providers for DiscordChannel/Role/Reward/Asset have no
  field using them; `POST pipelines/validate` never called; template helper omits step-produced vars.
- Commands: `UserCooldownSeconds` never enforced (ChannelRegistry.cs:511-512); regex not
  compile-checked on save, no tester, no capture groups (CommandService.cs:83-97; ChatMessageHandler.cs:611);
  missing/disabled pipeline → silent no-reply (ChatMessageHandler.cs:416-487); PipelineId cannot be cleared.
- Triggers: permission uses badges only (ChatMessageHandler.cs:1048-1073); overlap order random, no
  priority; disabled pipeline still runs (ChannelRegistry.cs:354-363).
- Timers: "live" = bot connected, no stream-online option (TimerService.cs:103-104); min-chat-lines
  baseline in memory (:167-170).
- Event responses: poll/prediction/hype_train/goal/charity/channel.update/vip/shoutout.receive are
  subscribed but have no trigger (BotLifecycleService.cs:52-108 vs EventResponsePresetCatalog.cs).
- Custom events cannot bind a pipeline (CustomDataTriggerHandler.cs:51-62, no bind UI). Voice triggers
  only feed widgets (VoiceTriggerWidgetEventHandler.cs:36).
- Code scripts: Save & Compile publishes immediately; test runs the published version
  (CodeScriptsController.kt:46-49). No "it fired / failed" feedback for triggers/timers/responses.

### B3 · Rewards, economy, games, giveaways, quotes, polls
- **VERIFIED** Catalog purchase takes the currency and runs nothing: no handler for
  `CatalogItemPurchasedEvent` (CatalogService.cs:399-413).
- **VERIFIED** Watch-time earning never pays: `ApplyWatchTimeBatchAsync` has no caller
  (CurrencyEarningService.cs:170) while onboarding seeds an enabled watch-time rule.
- **VERIFIED** Chat earning uses role scale 0/1/2/3/5 while rules store 0/2/4/10/40
  (ChatEarningHandler.cs:83-95) → sub-only rules pay VIPs, mod rules never pay.
- Economy leaderboard: wrong client model + raw ids (see A3); gift-sub/resub earn nothing
  (EngagementEarningHandler.cs:32-35); no live-only or tier multiplier (BonusConfig dropped).
- Games: default seeding all-or-nothing so pre-07-17 channels lack drop_game/raffle/heist/crash
  (GameService.cs:91-96); `WinChancePercent ?? 0` → duel/play_game always lose (:303-307); HouseEdge
  never read; 18+ consent has no grant UI (GamesController.cs:131-149).
- Giveaways: `GiveawayDrawnEvent`/`GiveawayOpenedEvent` have no handler — chat never told keyword or
  winner (GiveawayService.cs:482-493); no live updates; first-100 entries only.
- Rewards: Fulfil/Refund shown for unowned rewards (RewardsScreen.kt:789-807); pipeline throw leaves
  redemption unfulfilled with no refund (RewardRedeemedHandler.cs:243-250); automatic-reward
  redemption translator missing (AutomaticRewardRedeemedEvent never published); queue first-25 only.
- Chat polls close only on next vote/list (ChatPollService.cs:255-269); Home card polls every 4s
  forever (ChatPollsCard.kt:73-78); no poll events.
- Unconsumed: JarGoalReached, QuoteAdded, GamePlayed, SavingsJarInviteSent. Jar-scope leaderboards
  always empty; `CaptureSnapshotAsync` never called. No chat !buy/!shop/!jar. Quotes: no chat search,
  game free text. Hardcoded English in GameBuiltins/QuoteBuiltin/EconomyController/ScheduleController.

### B4 · Moderation, chat, community, analytics, home
- **VERIFIED** Unban-approve can never succeed: client body has no `confirm`
  (ModerationApi.kt:1175) and the server refuses approve without it (ModerationService.cs:1248).
- **VERIFIED** Bot AutoMod link/caps filters never fire: dashboard saves `link_filter`/`caps_filter`
  with `whitelist`/`maxEmotes` (ModerationService.cs:690-827); enforcer reads `links`/`caps` with
  `allowed_domains`/`max_emotes` (AutoModerationHandler.cs:102-286).
- Bans list reads the local `community/bans` table, not Helix; chat/ladder/Twitch bans never appear;
  unban never removes the row; Helix `GET moderation/bans` has zero callers (ModerationApi.kt:374).
- Profile ban/unban bypass ModerationService (no mod-log, no attribution, broadcaster token)
  (CommunityController.cs:861-966).
- User card only opens from a banned-user row (ModerationScreen.kt:649,2041); two separate
  moderator-notes stores; chat timeout always 600s (ChatScreen.kt:273,777); custom duration null→600s;
  empty feed hides chat-mode + Shield toggles (ChatController.kt:118); slow-mode/followers/unique/
  non-mod delay not editable, slow-mode on sends 0 (ChatScreen.kt:1302-1329); emotes/shield re-fetched
  every reload; network-ban per-channel results hidden; delete fallback reports success blindly
  (ChatController.cs:448-451); history rows lack reply parent and paints; zero-width emotes not
  overlaid, wide emotes squashed (ChatMessageFragments.kt:56-63); emote catalogue ignores sender.
- Home feed: server returns 20 of 40 then client drops engagement/supporter/hype/poll/prediction/
  unban/raid.out/sub.end (HomeController.kt:66-78); gift recipients shown as "X subscribed" and
  double-counted (SubscriptionTranslators.cs:22-40, ChannelAnalyticsDailyProjection.cs:120-125);
  Replay success with 0 widgets; failed follower/sub count shows 0 (DashboardController.cs:112,125);
  raid search only past chatters.
- Analytics: fixed 30d UTC, no range/metric picker (AnalyticsController.kt:83-384).
- AutoMod levels free-text digits (TrustAutomationSection.kt:771-852); spam controls with missing copy
  silently dropped, min/max never shown (SpamDefenseSection.kt:97-191); mod-log rows raw
  (ModerationScreen.kt:3343); profile raw enums/ISO (ViewerProfileScreen.kt:470-819); trust picker
  writes a key nothing enforces (CommunityController.cs:820-852); moderators added by raw Twitch id
  (ModerationScreen.kt:3165); deny sends null note; announcement colour selection invisible
  (ChatScreen.kt:1407-1411). Never called: community/stats, moderation/bans GET/DELETE.

### B5 · Music, TTS, sound, media share, VTS, OBS
- **VERIFIED** Spotify 429 storm (qtkitte): 429 is retryable with `Retry-After: 0` → zero delay ×2
  retries (ResiliencePolicies.cs:466-492) PLUS a manual retry re-entering the pipeline
  (SpotifyMusicProvider.cs:1696-1711) = up to 6 requests per poll; the breaker ignores 429
  (:500-513); a 429 on `/me/player` returns null = "nothing playing" so the poller clears backoff,
  publishes IsPlaying=false, and drops to the 5s quiet cadence (SpotifyMusicProvider.cs:250-254,
  MusicStatePollingService.cs:227-280). Box shows `ConsecutiveFailureCount=0` after two days. Fix: one
  retry owner, floor on Retry-After, typed rate-limited outcome through `RecordFailure`, per-channel
  cooling clock, count 429 in the breaker, skip handover while cooling.
- Player commands, manage calls and token refresh never set `SpotifyRequestTags` → all share the
  `Guid.Empty` partition (SpotifyMusicProvider.cs:1798-1811, 1930-1937, 1562).
- `ChannelSpotifyCredentialsService.SetAsync` swaps client id without invalidating a connection minted
  under another app (Integrations/ChannelSpotifyCredentialsService.cs:48-87).
- anda_six wrong-track/wrong-requester: no clean swap path found; matches the S-SR-STALE signature;
  the instrumentation the tracker asks for is still missing (`SearchAsync` logs nothing on success,
  "Queued track" logs no query/message id). Real races: in-flight check not atomic with push
  (MusicService.cs:756-834, two `!sr` in one tick both push; pushes the NEW entry not the head);
  `RemoveThrough` drops everything ahead of the started track (SongRequestQueueReconciler.cs:68);
  remove/promote/ban/!wrongsong act on stale list positions (MusicService.cs:1248-1357,
  SongWrongAction.cs:75-131); search has no `market=from_token` (SpotifyMusicProvider.cs:312-313).
- Duplicate reply always says "someone" (MusicService.cs:1951-2002); `RequesterUserId` hardcoded null
  (:713) and Cost/RequesterUserId not persisted (SongRequestQueuePersistence.cs:116-130); no explicit
  toggle, trust score unused by SR; `MusicQueueItem` has no in-flight flag; manual add requires typing
  a viewer name; block needs raw URI; Premium/rate-limited state never shown (MusicScreen.kt:464).
- TTS: default voice + Azure region raw text (TtsScreen.kt:2049,2174,1383); overlay test/skip/pause
  return 200 with no presence check (TtsConfigController.cs:100-191).
- Media share: non-numeric → 0 (MediaShareScreen.kt:495-498); cost charged before save, no refund;
  queue count not atomic (MediaShareService.cs:119-182).
- VTS model/expression are Text (VtsActions.cs:131,193); OBS `AckCommand` completes any command from
  any bridge (OBSRelayHub.cs:127-133).

### B6 · Widgets, overlays, alerts, bundles, assets
- **VERIFIED** Alerts fire twice: the SDK's `Event` case feeds the generic overlay stream (payload a
  JSON string) into the same handlers as `WidgetEvent` (OverlaySdkController.cs:375,
  OverlayAlertBroadcast.cs:65-69) → second copy renders "Someone / 0 bits".
- **VERIFIED** Widget settings never reach OBS: `SendSettingsChangedAsync` has zero callers
  (WidgetNotifier.cs:97).
- Alert queue: queued-with-no-overlay stays queued forever, no drain/skip/replay/pause
  (AlertQueueService.cs:97-109); every event kind enters the 100-cap queue (OverlayAlertBroadcast.cs:74);
  replay capture polluted by progress/test events (WidgetAlertHandlers.cs:96); pushes to a disabled
  alerts widget.
- `JintVueSfcCompiler.cs:90,143`: `Rent()` outside try → semaphore leak → every compile hangs after 2 failures.
- Test button: samples use the old shape (WidgetTestEventController.cs:163-195) → "Someone gifted 1
  sub / 0 viewers"; no widgetId → fires at every widget, presence counted across widgets.
- Alerts page: no Test, no OBS URL, one-shot load, raw kinds, cap 5. `OverlayUrl` from
  EventResponsesController/TtsConfigController lacks the origin rewrite (loopback URL).
- `voice_trigger` offered but not in default subscriptions; no alert card for hype_train/shoutout/vip/
  mod; one template for all events, hardcoded English; `event_response` type has no consumer.
- Host page compares widgetId as raw Guid so ULID shows "not live" (OverlayHostController.cs:73-75);
  CSP blocks Google Fonts and self-hosted fonts (:144,150); vanilla page has no CSP; per-widget token
  resolves to the whole channel (WidgetService.cs:1556-1573).
- Schema raw text: resetCadence, provider, rewards JSON, countdown ISO, custom_data source, colours JSON
  (WidgetSettingsSchemaProvider.cs:115-230). Editor has no dirty check (editor.js:1204,1259).
- Bundles: export takes max VersionNumber not ActiveVersionId, drops FilesJson
  (BundleExportService.cs:297-300); import ignores `CompileAsync` Result (BundleImportService.cs:931);
  UI exports 4 of 10 supported kinds.
- Assets: no video/font formats (ChannelAssetService.cs:116-119); name = filename stem, silent replace
  can change kind; no paging; used-by check scans dead versions and misses Settings/event responses.
- **B6.1** (from live data) only one channel has a `PlatformConnections` row — the D1 attach flow has
  no base row for existing channels; backfill + write on every login, not only on create.

### B7 · Runtime stability
- **VERIFIED** 403 EventSub storm root cause: `SafeReadBodyAsync` (TwitchHelixTransport.cs:404-419)
  reduces the body to the plain `message`, but `ExtractMissingScope`
  (TwitchEventSubHostedService.cs:1311-1326) parses it as JSON → always null → the pre-create gate
  (:691-708) never blocks, `TwitchHelixReauthRequiredEvent` never publishes, and the tests at
  `TwitchEventSubReconnectTests.cs:409` feed JSON the transport never produces. Every welcome
  re-POSTs the whole failed slice (:307-312, :1171-1203); a session with zero live subs is closed by
  Twitch (4003) → reconnect ≈32s → repeat forever. Also: "subscription missing proper authorization"
  is never terminal; re-registration runs inline in the receive loop (:285-333); every token refresh
  fires 74 POSTs inline (EventSubResubscribeOnTokenRefreshedHandler.cs:57); every non-2xx logs
  Warning (TwitchHelixTransport.cs:394).
- **VERIFIED** Blue/green tug-of-war: `BotLifecycleService` and `SubscribeAsync → EnsureSessionAsync`
  are not gated on leadership; the losing colour's welcome runs `CleanupOwnerStaleSubsAsync`
  (:1123-1150) which deletes any row whose `SessionId != mine` — the leader's live subscriptions.
- No lease: MusicStatePolling (double Spotify budget, both colours can push the same SR),
  YouTubeLiveChatPollWorker (double-answers), StreamStatusPolling (double journal), token refresh
  sweeps (in-process gate only), Kick/reconcile workers. Conversely ChannelRegistryBootstrap and
  SongRequestQueueRestore ARE leased but guard per-process state → the loser never restores.
- Polly telemetry: no `SeverityProvider`, no Serilog override → every retry/timeout logs WRN/ERR with
  stack traces (ResiliencePolicies.cs, appsettings.json:4-10).
- `EventSubConnectedEventHandler` clears needs_reauth on any welcome (a WS open needs no token) →
  status oscillates. YouTube token provider has no needs_reauth short-circuit; all providers count
  5xx/429 toward the 3-strike. `DECRYPT_FAILED` leaves the connection Connected
  (IntegrationTokenVault.cs:369-375).
- `TenantAccessGrantExpiryService.cs:49-63` delay inside try → hot-spin on error.
- Handled-never-published: `PermissionChangedEvent`, `MessageAutoModdedEvent`. Advisory-lock lease has
  no keepalive (PostgresRunOnceGuard.cs).

### B8 · Integrations, Discord, webhooks, federation, platform admin
- **VERIFIED** Integrations bot row hits the platform bot endpoints: status needs IamManage → 403 →
  "not connected" for every streamer; Disconnect = `DELETE auth/twitch/bot`, the deployment-wide bot
  (BotAuthApi.kt:95-98, AuthController.cs:1307,1339; channel endpoints exist at ChannelBotController.cs:217-233).
- BYOC for YouTube/Discord/Kick writes the deployment slot (SystemController.cs:396-460): 403 for
  streamers, silent global swap for admins; no validation; only Spotify has a per-channel endpoint.
- Client `SystemChecks` lacks `youtube` → YouTube connect always dead-ends (SystemDtos.kt:35-40).
- Federation page visible to every broadcaster but needs AuditRead; gateway/signer have no callers;
  six events have no consumer.
- Twitch itself is absent from the status list (OAuthProviderRegistry.cs:31-40) → a revoked login
  never shows, inbox item lands on a page with no row; Patreon/Shopify/TreatStream same.
- Discord live-role config has no create endpoint or UI (DiscordLiveRoleService.cs:48); "server
  consent" is a typed, unverified Discord user id (DiscordScreen.kt:1542-1548); card says Connected
  with `NeedsReauth=false` hardcoded (IntegrationOAuthService.cs:458-468); no real test send; guild
  events unconsumed.
- YouTube missing-permission/quota only logs, never needs_reauth (YouTubeLiveChatPollWorker.cs:229-242).
  `IntegrationNeedsReauthEvent` has no consumer; `expired` status never written.
- Granted-vs-needed scopes dropped by the client (IntegrationsController.kt:661-668) → re-grant only
  exists for Twitch; declined re-grant closes silently (:626-635).
- Admin: quota overrides, remigrate, erase, export, rotate-encryption-key have no UI
  (PlatformAdminController.cs:235-361, AdminController.cs:367); tenant ids typed by hand
  (AdminScreen.kt:1581,1607, AdminEntitlementGrantSection.kt:89,226); Promote picker limited to the
  loaded page (AdminIamTab.kt:373); Providers tab lists twitter/spotify slots nothing reads.
- Webhooks: rejected inbound not stored (InboundWebhookDispatcher.cs:182); ingest URL no copy, event
  route free text; outbound rows lack last delivery/failure; auto-disable never reaches the inbox.
- EventSub reconcile report discarded (IntegrationsScreen.kt:387); subscription rows never show
  Twitch's own status. Inbox titles built around raw provider keys (ActionRequiredInboxService.cs:228-232).

---

## Part C — merged remediation order

Rule: severity first, then what unblocks the most, generic infrastructure before per-screen fixes.
Owner priority stays streamer → moderator-of-many → viewer, but P0 security and "silently never
worked" beat everything. Each line is one slice; delete it from the tracker when its Done-when is proven.

**Tier 0 — security and data-integrity (this week)**
1. V-A3.1 wallet transfer sender = caller; leaderboard opt-in/out self-only; game history self-only.
2. V-B2.2 pipeline ownership: reject foreign `PipelineId` on save; filter steps by broadcaster.
3. V-A2.4 `DashboardHub.JoinChannel` Gate-2 per event class; tracked invocation on the client.
4. V-B6.10 per-widget token scoped to the widget; roster stops returning `OverlayToken`.
5. V-B1.2 web setup finalises (persist intent across the redirect) so credential endpoints lock.
6. V-B5.13 OBS `AckCommand` scoped to the bridge's channel.

**Tier 1 — the live bot misbehaves (runtime, before any more UI)**
7. V-B7.1 403 EventSub storm: parse the plain message; terminal per channel+topic with a grant
   fingerprint; skip until it changes; one action-required item; fix the test fake.
8. V-B7.2 leadership gates on BotLifecycle/Subscribe/EnsureSession; drop lease on per-process restores;
   lease MusicStatePolling + YouTube poll + StreamStatus + token sweeps.
9. V-B5.1 Spotify 429: single retry owner, Retry-After floor, typed rate-limited outcome, per-channel
   cooling, breaker counts 429, tag every request. (Unblocks qtkitte today.)
10. V-B7.4 Polly `SeverityProvider` + Serilog override; Helix non-2xx at Debug.
11. V-B7.5 needs_reauth truth: clear only on an authenticated success; only invalid_grant counts;
    YouTube short-circuit; `DECRYPT_FAILED` → distinct status + inbox item.
11b. V-A4 scopes required at enable time, not at use time; 401 ≠ reauth unless the token is dead.
12. V-A2.1 promote un-onboarded tenant on owner login + backfill 17 rows; V-B6.1 PlatformConnection
    written on every login + backfill.

**Tier 1b — admin plane + authoring (owner priority 2026-09-26)**
Source: `usability-inventory-2026-09-26-admin-and-authoring.md` (both passes). Runs right after Tier 1.
A0. Actionable errors everywhere (owner rule 2026-09-26): one action-required inbox fed by EVERY
    subsystem through a generic producer contract (today only dead tokens, held chat, unmanaged rewards
    feed ActionRequiredInboxService). Add producers: refused EventSub topics / missing scopes, Spotify and
    other integrations needing reauth, bot not modded, widget compile failures, webhook delivery failures,
    song requests lost at the provider, setup-finish pending failures, DECRYPT_FAILED connections. Live push
    over the dashboard hub. Shell frame shows the inbox on every page (outside the page view); every item
    deep-links to where it is fixed; Home shows all items grouped by severity at the top of the main view.
    Titles/messages are resource keys, never backend English. Done-when: every actionable failure the bot
    detects appears in the frame within seconds, and following it lands on the fix.
A1. Desktop editor parity: the desktop code/widget editor is a plain Swing text area
    (ProjectEditor.jvm.kt:42-50); give it the same Monaco editor as web (embedded browser view), with the
    same types, diagnostics, preview and fire tools. Done-when: one editor, identical on both clients. (Opus)
    Owner decision 2026-09-26: NO embedded Chromium (CEF/KCEF). Use each OS's native web view (WebView2 on
    Windows, WKWebView on macOS, WebKitGTK on Linux), as other desktop products do. An external editor is
    rejected: too slow for a rapid-fire broadcaster edit tool.
A2. SDK guidance in the editor: wire `GET /sdk/event-catalog` (SdkController.cs, zero callers) into a
    docs panel — browse events and API, real sample payloads, "insert handler", hover docs, snippets.
    Done-when: a new user finds and uses an event without leaving the editor.
A3. Generic platform content: open `PlatformContentKinds` (PlatformContentDefinition.cs:61-69) to timers,
    quotes/picklists, reward presets, sound clips and event responses, on the one existing
    author → publish → install flow. Done-when: the admin can author and publish each kind. (Opus: schema)
A4. Platform defaults editable at runtime: event-response defaults, builtin replies, action/permission
    floors (`ActionDefinition` is already a table), TTS voices — admin API + admin UI + blast radius,
    tenants keep their overrides. Done-when: no platform default needs a code change and a redeploy.
A5. Template updates reach tenants: platform content is copied at install and never updated
    (PlatformContentDefinition.cs:17-20). Show "update available" per installed copy; the admin can push
    with a blast-radius preview; a tenant's edits are never overwritten silently.
A6. Admin truth pass: feature-flag override read-back + confirm (AdminScreen.kt:1533-1554); trace
    save → runtime reader for billing, spam defense and trust-safety (owed by the audit).
A7. GDPR admin console: list and monitor export/erasure requests (GdprController.cs) platform-wide.
A8. Publish the SDK types as a versioned npm package built by CI from `SdkTypeEmitter` output.
Owed: announcements-to-tenants surface (not found), OBS/VTS admin presets, automation/IPC keys tab.

**Tier 2 — features that silently never worked (fix or remove the control)**
13. V-B4.2 AutoMod rule key contract (one shared set + round-trip test). 14. V-B4.1 unban-approve `confirm`.
15. V-B3.1 catalog purchase runs the item pipeline (refund on failure). 16. V-B3.2 watch-time earning
sweep. 17. V-B3.3 chat-earning role scale. 18. V-B2.1 typed pipeline params from backend descriptors
(+ accept numeric strings). 19. V-B6.2 push `WidgetSettingsChanged`; V-B6.1 stop the double alert.
20. V-B2.10 `UserCooldownSeconds` enforced. 21. V-B3 giveaway open/draw announced; game seeding per
type; `WinChance ?? 0` refused. 22. V-B8.1 Integrations bot row → channel bot endpoints.
23. V-B5.4 `!sr` instrumentation (query, message id, resolved uri) + atomic in-flight claim + hand over
the head; address queue entries by code, not position.

**Tier 3 — the owner's three flows end to end**
24. V-A1 impersonation (all 7 points, one slice with a UI test).
25. V-A2 moderator-of-many: roster from memberships + real role; onboarded flag in DTO; moderator
add/remove → membership + `PermissionChangedEvent`; typed 403 bodies; roster cached; enter not
tenant-gated; failures shown.
26. V-A3 viewer: public channel summary endpoint; leaderboard model + display names; real or removed
tiles; self ledger/purchases; 18+ consent UI; transfer picker; `/sr` public page built; chat replies
localised with position/cooldown-left; built-in points command; nav by enabled features.

**Tier 4 — form infrastructure (rides Phase 3), then per-screen**
27. Settings scroll (V-B1.1) — trivial, do with 24. 27b. V-A5 dead links: `/sr` page + link-walk
test + SPA fallback excluded for public paths. 28. Resource-typed `ResourceId` + option providers
(OBS/VTS/games/roles/discord) and Number/Enum kinds, **with the V-A6 "create inline" affordance on
the picker primitive** → unlocks B2, B5, B6 raw-text items and the whole A6 list.
29. Typed 403 problem body + client rendering (shared by A2/A3/B1). 30. Dialogs close only on success
(Home, Roles, Moderation approve). 31. Per-provider BYOC per channel + validate on save; client
`SystemChecks.youtube`; Twitch in the status list. 32. Alerts page (test, URL, live queue, controls)
+ test samples on the real shape + widget-targeted presence. 33. Home feed types + gift-sub truth +
nullable counts. 34. Chat page controls (timeout picker, chat-mode fields, controls when empty, user
card from every row, Helix bans list). 35. Remaining raw-text/paging/i18n items per area, in the
lane order above.

**Ruled out / corrected while auditing**
- anda_six's channel is onboarded (the A2.1 trap did not hit him); his symptoms trace to B7.1 (403
  storm on his broadcaster session) and B5.4 — to be confirmed by the instrumentation in slice 23.
- Tracker line 1193 ("public /sr/ page correct") is wrong — the page is unbuilt (A3).
- S072 "no doc drift" is wrong for Event Responses/Alerts placement and the Commands floor (B1).
- Impersonation JWT, tenant middleware fallback, audit rows, channel-list scoping, en/nl key parity,
  EventBus handler isolation, cooldown singleton, chat de-dup, bundle cache busting: checked clean.

Coverage: 6 of 6 owner asks grounded (A1–A6; A5's "streamer channel" link still to be located in
the rendered client) with the live box confirming A2.1/B6.1/B5.1; 8
area lanes complete. Not audited this pass: OBS relay UI beyond the hub, VTS beyond actions, X
platform (S031), supporter cards (S101), live ops (S106).
