# Findings ledger — 2026-09-24 inventory (every finding, one line each)

Companion to `usability-inventory-2026-09-24-audit-scope-and-plan.md` (verified root causes + the
ORDER, Part C). This file is the complete list from every lane, including what the plan doc
condensed. Each line is a checkbox: tick when its fix is committed and proven on the rendered client,
then delete it (tracker = remaining work only). **VERIFIED** = re-read at the lines by the
orchestrator; everything else is lane-reported at `file:line`.

Paths: `app/` = `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/`; `srv/` = `server/src/`.
Tags: `SEC` security · `TRUTH` fake/unenforced state shown · `RESULT` Result/bool ignored · `I18N`
hardcoded user-facing text · `VAR` `var` in C# · `FEEDBACK` no confirmation / failure→empty / dialog
closes early · `RAW` raw text where a control belongs · `HOOPS` must leave the form to create a related
thing · `PAGE` hardcoded cap, no pager · `DEAD` built but unreachable, or event with no consumer/publisher
· `PERF` cost that grows with channel age.

Second-round sweeps still owed (rate-limited 2026-09-24 evening, resume after 23:00): uncapped lists
for automation, rewards/liveops/supporters, music/OBS/VTS, widgets per .vue, integrations/billing,
runtime tables, shell dead-links, backend rule sweep, frontend rule sweep, uncovered areas + uncalled
endpoints, and the onboarding friction walk.

---

## L1 · Impersonation (act-as)

- [ ] `SEC` srv/NomNomzBot.Infrastructure/Identity/PlatformAdminService.cs:353-361 — support-grant lookup never checks the target belongs to `grant.ScopeChannelId`; a grant for tenant A lets the admin act as ANY user; audit names tenant A — refuse unless target is owner/member of the grant's tenant. **VERIFIED**
- [ ] `SEC` PlatformAdminService.cs:375-378 — target tenant claim = unordered `FirstOrDefault` owned channel — order like login, or use the grant's ScopeChannelId.
- [ ] app/core/connection/SessionStore.kt:127-136 — `beginImpersonation` swaps token+flag only; selected channel + saved last-channel stay the admin's — reset on begin/end; never read/write last-channel while acting. **VERIFIED**
- [ ] app/feature/shell/state/ChannelSwitcherController.kt:63-65 + SessionStore.kt:99-101 — `setDefaultChannel` only when none selected — forced reset path for identity swap.
- [ ] app/feature/admin/state/AdminController.kt:1919-1927 — role check before roster reload, against the stale channel — reset → reload → resolve.
- [ ] app/core/network/ChannelsApi.kt:83-87 + App.kt:219 + ShellScreen.kt:286-293 — target not moderating the admin's channel → switching guard → splash forever; banner (ShellScreen.kt:520) renders after the early return, no Exit — guard timeout or banner above splash.
- [ ] AppGraph.kt:301 → srv TenantResolutionMiddleware.cs:83 — every request carries the stale admin channel; Gate 1 admits it. **VERIFIED**
- [ ] ShellScreen.kt:553-554 — pages keyed on `activeChannelId` only → no remount on act-as — key on a session generation.
- [ ] AppGraph.kt:910-913 → srv DashboardHub.cs:128 — hub rejoins the admin's channel with the target token — reconnect after reset.
- [ ] `RESULT` AdminController.kt:1911-1912 — `endImpersonation` sent BEFORE the admin token is restored → 403 (PlatformAdminController.cs:192), Result ignored → grant never revoked, `ImpersonationEndedEvent` never fires — restore first, check, surface. **VERIFIED**
- [ ] AdminController.kt:1913 + SessionStore.kt:142-147 — exit does not reset the channel; a channel switched while acting was saved into the ADMIN's store → endless splash after exit.
- [ ] SessionStore.kt:116, 218-228 + AppGraph.kt:316 — act-as expiry → 401 refresh installs the ADMIN token, flag/name stay → wrong identity — run the full exit path.
- [ ] SessionStore.kt:242-249, 251-261 — `clearActiveSession`/`disconnect` keep the act-as flag + stash → survives logout; Exit writes a stale token over the new session.
- [ ] app/feature/connect/state/ConnectController.kt:334-337 + srv AuthController.cs:1243-1254 — logout while acting sends the act-as token; admin's refresh session not revoked — end act-as first.
- [ ] App.kt:234 + ShellScreen.kt:538 — Twitch health re-runs on the target → "Reconnect" would OAuth under the target — suppress while acting.
- [ ] srv ChannelsController.cs:101 — channel list as the target writes Moderator memberships — skip under impersonation.
- [ ] ShellScreen.kt:350 — coerced route saved while acting → admin lands on Dashboard after exit.
- [ ] `DEAD` srv Api/Hubs/Broadcasters/ImpersonationBroadcastHandlers.cs:55,106 — owner security notices written; no endpoint/client reads them — list/ack endpoint + inbox entry.
- [ ] AdminController.kt:1855-1878 — grant opened, mint can fail → grant left open; retries open more — end on failure; reuse open grant.
- [ ] app/feature/admin/ui/AdminTenantsTab.kt:240-250, 285-290 — Confirm not disabled in flight → double-click = two grants.
- [ ] AdminController.kt:1920-1923 — `/me` failure after swap only toasts; token is the target's, identity the admin's — roll back and report.
- [ ] AdminController.kt:1924-1927 — nullable shell/switcher/reconnect silently skipped — required or fail loudly.
- [ ] AdminTenantsTab.kt:244 + AdminScreen.kt:811-812 — act-as targets the OWNER only; cannot reproduce a moderator's/viewer's view — member picker (needs the scope check).
- [ ] AppGraph.kt:904,914 — admin hub never reopened on exit (`adminHubWasLive` false because the target token failed the gate).
- [ ] ParticipantShell.kt:136-140 (+ ShellScreen.kt:390/402) — Exit on a composable scope the re-resolve unmounts → exit stops half-way — app-level scope.
- [ ] ImpersonationBanner.kt:64-66 — banner vanishes at expiry, nothing ends act-as — auto-exit or "expired" banner with Exit.
- [ ] ShellScreen.kt:520 vs ParticipantShell.kt:137 — banner overlaid (covers sidebar/header) in one shell, in-flow in the other — one placement, above.
- [ ] ShellScreen.kt:448-462 — Logout / Reconnect Twitch / Preview-as-viewer offered while acting; each acts on the target — hide/relabel; Exit primary.
- [ ] `I18N` ImpersonationBanner.kt:92 — sentence assembled in code with " — " — one resource, two placeholders.
- [ ] `VAR` ImpersonationBroadcastHandlers.cs:170 — `var row`.
- [ ] AdminController.kt:1898-1902 — refusals keyed on HTTP status; fixed copy not backed by the server code — key on the error code.
- [x] Clean: act-as JWT carries target sub/roles/tenant (JwtTokenService.cs:86-140); middleware has no admin fallback; writes audited with both actors (EventJournalService.cs:284-301); IAM audit row before mint; channel list scoped; language per device.

## L2 · Moderator of another channel

- [ ] `TRUTH` srv AuthService.cs:363-382 + ChannelService.cs:593 + BotLifecycleService.cs:194-201 — moderator-mode tenant `IsOnboarded=false`; owner login reuses it, never promotes; no PlatformConnection; lifecycle unsubscribes it — promote on owner login; backfill 17 live rows. **VERIFIED, 17 rows live**
- [ ] AuthService.cs:369-382, 492-500 — any login with no owned channel INSTALLS the bot on that user's own channel unasked (onboarded, mod-join, defaults, 74 topics) — separate sign-in from install.
- [ ] `DEAD` srv Domain/Identity/Events/PermissionChangedEvent.cs:15 — never published; `ManagementRoleChangedEvent`/`PermitGranted`/`PermitRevoked` unhandled → live re-resolve (ShellScreen.kt:380) dead — bridge; target the user.
- [ ] srv Api/Hubs/Broadcasters/RoleBroadcastHandlers.cs:170-220 — `channel.moderator.remove` only logs; no grant on `.add` — set/remove TwitchBadge memberships + push.
- [ ] `SEC` srv Api/Hubs/DashboardHub.cs:128 — `JoinChannel` Gate 1 only → any user joins any channel's moderation class — Gate-2 per class. **VERIFIED**
- [ ] app/core/realtime/DashboardHubClient.kt:431-432 — join without invocationId; denial dropped; shows Connected — tracked invocation.
- [ ] `TRUTH` srv ChannelService.cs:201-205, 226 — roster ignores ChannelMemberships; role hardcoded broadcaster/moderator; Editors/LeadMods/permits invisible; no-membership channels say "moderator" — union memberships + permits; real role.
- [ ] ChannelsController.cs:126 vs ChannelService.cs:204 — lazy grant filters `IsOnboarded`, roster does not → listed with no membership, listed twice (ShellScreen.kt:883) — grant on any moderated tenant; dedupe.
- [ ] ChannelsController.cs:284-291 + MembershipService.cs:104-107 — Enter overwrites Editor/LeadModerator with Moderator — insert only when absent.
- [ ] ChannelService.cs:576-581 — returns soft-deleted/suspended tenants → switcher selects a tenant Gate 1 refuses — restore or refuse, typed reason.
- [ ] ChannelsController.cs:225 — Enter gated by `dashboard:read` on the CURRENT channel; ChannelSwitcherController.kt:105 maps every failure to null — drop the gate; show failures.
- [ ] `FEEDBACK` srv Api/Authorization/ActionAuthorizationHandler.cs:41-46 + TenantResolutionMiddleware.cs:116 — denials are an empty 403 — problem body: action key, required, resolved, reason.
- [ ] `TRUTH` srv Application/Identity/Dtos/ChannelDtos.cs:38-49 — no onboarded/bot-installed flag → full sidebar with inert pages — flag + badge + explain/disable.
- [ ] `PERF` ChannelsController.cs:84-99 + ChannelsApi.kt:77 (47 callers) — every `GET /channels` = Helix call + writes, per navigation — cache per user; `primaryChannel` reads the roster.
- [ ] `PAGE` ChannelsController.cs:88,184,246 — first 100 moderated channels only; Helix failure → empty (:185-187, switcher :79).
- [ ] `SEC` ChannelService.cs:228 — roster returns `OverlayToken` to every non-owner — drop from DTO.
- [ ] ChannelService.cs:203 — roster built through the tenant filter → differs by active channel.
- [ ] ChannelSwitcherController.load() with a suspended/deleted pinned channel → roster 403 → stuck Error (adds to S074).
- [ ] `RESULT` ChannelsController.cs:284-291 — `SetManagementRoleAsync` Result ignored → 200 "moderator" on failure.
- [ ] `RESULT` ChannelsController.cs:153-160 — `EnsureModeratorMembershipsAsync` ignores each Result, nothing logged.
- [ ] `TRUTH` ChannelsController.cs:293-303 — Enter returns `isLive=false`, null avatar/colour into the roster (ChannelSwitcherController.kt:110-112).
- [ ] `FEEDBACK` ChannelsController.cs:242-253 — always "You do not moderate this channel" even for no-token / Helix failure.
- [ ] ShellScreen.kt:979-984 — entering an unregistered channel: no indicator, no in-flight guard.
- [ ] ShellScreen.kt:874-875 — SidebarHeader vanishes on empty/Error roster; error never rendered.
- [ ] `I18N` ShellScreen.kt:891, 966 — `typeLabel = "Channel"`.
- [ ] `FEEDBACK` DashboardHub.cs:128-129, 247-256 — bare "Access denied" for Gate 1 and Gate 2.
- [ ] TenantResolutionMiddleware.cs:26-30 — class doc contradicts Gate-1 relaxation (ChannelAccessService.cs:47-57).
- [ ] `TRUTH` ChannelService.cs:171 — `GetAllActiveAsync` hardcodes "broadcaster".
- [ ] `VAR` BotLifecycleService.cs:194; ChannelService.cs:277.
- [ ] ChannelService.cs:586-594 — moderator-mode tenant gets no PlatformConnection/Provider.
- [ ] ChannelsController.cs:551-566 — `ResetChannel` raw DbContext deletes in the controller, no audit/blast radius; `DeleteChannel` has both — move behind the same contract.
- [x] Clean: `X-Channel-Id` on every request (ApiClient.kt:126); no JWT-tenant readers; remount on switch; hub follows the active channel; moderation writes use the operator token; `user:read:moderated_channels` in minimal scopes.

## L3 · Viewer (participant)

- [x] `SEC` srv CurrencyController.cs — transfer sender = caller unless the caller holds `economy:account:adjust` (self-or-Gate-2). Fixed.
- [x] `SEC` EconomyLeaderboardsController.cs — opt-in/out self-only unless the caller holds `economy:leaderboards:config:write`. Fixed.
- [x] `SEC` GamesController.cs — game history scoped to the caller unless the caller holds `economy:ledger:read`. Fixed.
- [ ] `SEC` MusicController.cs:171-196 + PublicSongRequestController.cs:93-97 → MusicService.cs:517-524 — dashboard + public SR skip min-trust + requester id; public cap keyed on typed name.
- [ ] `FEEDBACK` ParticipantController.kt:129 — viewer home calls `/dashboard/{id}/stats` (Moderator) → 403 text as home — public channel-summary endpoint.
- [ ] EconomyApi.kt:229-251 + ParticipantController.kt:206 — Leaderboards page first calls `leaderboards/configs` (Moderator) → 403.
- [ ] `TRUTH` EconomyLeaderboardService.cs:148-154,186,203 — `DisplayName` = raw Twitch id (page + LeaderboardBuiltin.cs:93).
- [ ] `TRUTH` EconomyApi.kt:602-609 vs EconomyDtos.cs:176-182 — client `points/userId` vs server `value/subjectUserId` → all rows 0.
- [ ] `TRUTH` UserService.cs:303-307 — `WatchHours`/`CommandsUsed` always 0; `MessageCount` cross-channel on a per-channel page.
- [ ] `TRUTH` NowPlayingScreen.kt:149 + ParticipantController.kt:113-119,499-503 — "Up to N" hardcoded 1/3/5; "Sub lane" does not exist.
- [ ] `RAW` PointsAndStoreScreen.kt:158 + ParticipantController.kt:326 — Transfer-to free text expecting a GUID.
- [ ] `TRUTH` ParticipantGamesScreen.kt:156,208 — internal game keys; raw history; minBet/maxBet/odds/cooldown never shown; bet unvalidated.
- [ ] `DEAD` GameService.cs:214-217 — 18+ games always fail; consent routes only called from management — participant confirm step.
- [ ] PointsAndStoreScreen.kt:249,305 — Buy above balance, no confirm, `inputArgs=null`, "X of 0", no currency name, no self purchases/ledger (CatalogController.cs:163-164, CurrencyController.cs:165-166 Moderator).
- [ ] `FEEDBACK` GiveawayKeywordListener.cs:108-118 — keyword entry never replies.
- [ ] `FEEDBACK``I18N` ChatMessageHandler.cs:749-755 — cooldown reply without time left (CooldownManager.cs:54 exists); permission reply names no role; English literals.
- [ ] `I18N` BuiltinResponseComposer has no language parameter; SongRequestBuiltin, MusicBuiltins, GdprBuiltins:175-177 (raw enum), QuoteBuiltin, MusicService English; ApiClient.kt:102-104 English-only.
- [ ] `DEAD` no built-in points/balance command.
- [ ] SongRequestBuiltin.cs:190-207 — `!sr` no position/wait; MusicBuiltins.cs:175-178 `!queue` 5 rows, no "yours".
- [ ] ParticipantNav.kt:84-85 — nav ignores enabled features.
- [ ] `DEAD` SongRequestsController.kt:106-110,266-272 — `/sr/@name`, `/sr/{token}` unserved; Program.cs:1010 fallback; tracker line 1193 wrong. **VERIFIED**
- [ ] quotes:read floor Moderator/VIP (ActionDefinitionSeeder.cs:434); no participant quotes page.
- [ ] `I18N` ParticipantShell.kt:358 — "Viewer".
- [ ] `TRUTH` ParticipantController.kt:98-99 + ParticipantShell.kt:128 — Transfer card hidden for viewers though allowed; `canTransfer` reads permit caps only (RoleResolver.cs:62) — gate on `heldActionKeys`.
- [ ] `TRUTH` LeaderboardsScreen.kt RankingCard — "sub leaderboard" is a relabel of the same ranking.
- [ ] LeaderboardsScreen.kt ConsentCard — same string as title and description.
- [ ] `FEEDBACK` ParticipantController.kt loadStore/loadMe/loadMyChannel — all-or-nothing; one failure blanks the page or silently hides cards.
- [ ] UsersController.cs:156-157,172-173 — 401 for "not your data" → client treats as expired session — 403.
- [ ] `TRUTH` UserService.cs:279 — `TwitchUserId!` null for Kick-only viewer → counts 0.
- [ ] `FEEDBACK` ParticipantController.kt submitSongRequest / purchase / contributeToJar / transfer — success only reloads silently.
- [ ] `FEEDBACK` GiveawayKeywordListener.cs:122-137 — winner claim marked Claimed with no reply.
- [ ] `I18N` PublicSongRequestController.cs:91,95,102; MusicController.cs:195; MusicService.cs:513,522,677-689,741 — English literals on public page + every SR refusal.
- [ ] `I18N` MyChannelScreen.kt ActivityTiles, MeScreen.kt tiles, PointsAndStoreScreen.kt BalanceCard — raw `toString`/`%d`, "." decimal.
- [ ] NowPlayingScreen.kt:196-219 — no position/artist/duration; own requests not highlighted.
- [ ] `HOOPS` NowPlayingScreen.kt:163-170 — blind free-text request; no search/preview/cost.
- [ ] Participant pages load once; ignore `MusicStateChanged`/`sr_queue_changed`; balance never updates.
- [ ] MeScreen.kt ChannelsCard — raw `watchTime` string; `followDate` fetched (ParticipantApi.kt:327) never shown.
- [x] Clean: participant strings fully Dutch; me/users/stats/channels/analytics routes caller-only; `!sr` failure codes specific; no raw hex/dp; no numbered levels.

## L4 · Shell, navigation, settings, onboarding (first round; uncapped sweep owed)

- [ ] SettingsScreen.kt:314-379 — outer Column does not scroll; stream-info Box `weight(1f)`; nine cards below cut off (Bot account, Permissions, Billing, Journal unreachable). **VERIFIED**
- [x] `SEC` SetupController.kt:256-273 + wasmJsMain OAuthLauncher.wasmJs.kt:48-50 — web `finish()` navigates away; `completeSetup()`/`applyBasics()` never run → basics lost, `setup_complete` never set, SystemController.cs:312/351/447 stay writable without login; desktop ignores the completeSetup Result. **VERIFIED (server side)**
- [ ] SetupController.kt:290-317 + App.kt:157 — desktop gate switches before `applyBasics` finishes; `SetupError.Basics` renders on an unmounted wizard.
- [ ] App.kt:182-186 + RouteStore.wasmJs.kt:71-72 — browser Back on the first shell entry logs out, no confirm.
- [ ] ShellScreen.kt:349-350 + RouteStore.wasmJs.kt:41-46 — coerced route pushes history → Back loops; no "no access" notice.
- [ ] ShellScreen.kt:840-844, 812 — Setup group toggle needs two clicks; never auto-expands; not persisted; resets on drawer open.
- [ ] `TRUTH` TwitchAppCredentialsController.kt:111 — `configured` = "secret present" (SystemController.cs:127) → BYOC id without secret shows "shared client".
- [ ] IntegrationsScreen.kt:321 — BYOC card for every Broadcaster; 403 on SaaS; no revert-to-shared outside the wizard (SetupController.kt:157).
- [ ] `FEEDBACK` HomeScreen.kt:523,548,559,569,581 — title/prediction/raid/commercial/resolve dialogs close before the result.
- [ ] `FEEDBACK` RolesScreen.kt:343-346,356-362,387-389 — assign/permit/override close before the result.
- [ ] RolesScreen.kt:1186,898,1240 — raw action keys, no grouping.
- [ ] ConnectController.kt:669 + App.kt:148-161 — retry flips to the Connect screen briefly (breaks S050).
- [ ] UnreachableScreen.kt:36-78 — only Retry; no sign-out / other server.
- [ ] ShellAccessController.kt:53 + ConnectController.kt:334-337 — access not reset on logout / load start → previous user's role renders.
- [ ] ShellScreen.kt:875 — roster Error hides the header entirely.
- [ ] `TRUTH` ShellScreen.kt:425 — compact chip shows the user's name, not the active channel.
- [ ] ShellScreen.kt:1213-1227 + LanguagePicker.kt:45-56 — current language not marked.
- [ ] `RAW` SettingsScreen.kt:1069-1084 + SetupWizardScreen.kt:425-438 — timezone/locale free text; typo → UTC.
- [ ] `DEAD` ImportController.cs:45 — StreamElements import has no client/screen.
- [ ] FeaturesScreen.kt:205-215,221-226,236 — Re-grant shown when nothing missing, runs global reconnect; raw scope strings; toggle not locked; no Configure link.
- [ ] `I18N` SetupWizardScreen.kt:463 — review rows use raw backend `step.title`.
- [ ] `I18N` TwitchAppCredentialsCard.kt:179 "Edit"; FeaturesScreen.kt:176; ShellScreen.kt:891/966/1167; RolesScreen.kt:676/1098/1102; "No active channel" duplicated in AlertsController.kt:195, AnalyticsController.kt:327, AutomationController.kt:183.
- [ ] SetupController.kt:67-71,339 — wizard step/fields in memory only; reload drops them.
- [ ] Spec drift: ShellNav.kt:253 (EventResponses under Chat vs Stream), :291 (Alerts separate vs merged), :246 (Commands floor Moderator vs Editor) vs frontend-ia.md; S072 "no drift" wrong.
- [ ] Spec pages with no screen: participant "My Standing"/"My Data", icon-rail collapse (§2), global search.
- [x] Clean: every ShellRoute has a screen and entry; no orphan feature folder; en/nl key sets identical (5384); Settings/Home/Features/Roles gate via ManageGate (disable + reason); first-run and attention deep links valid.

## L5 · Automation authoring (first round; per-action uncapped list owed)

- [ ] PipelinesApi.kt:376-399 + ActionDefinition.cs:35-57 — untyped params sent as strings; `GetInt`/`GetBool` default on string → shoutout tts/global_cooldown_minutes (ShoutoutAction.cs:170,303), raid_window_seconds (StartRaidAction.cs:227), stop_sound all (:57), VTS r/g/b/a (VtsActions.cs:332-335), OBS duration_ms (ObsSceneActions.cs:231) silently default; re-save of existing pipelines too. **VERIFIED**
- [ ] PipelineCatalogue.kt:788-804 — backend kind/Options/Required and unlisted fields dropped; never shown: send_message sender (SendMessageAction.cs:39-44), shoutout global_cooldown_minutes/tts/template, start_raid raid_window_seconds, run_pipeline mode, play_tts as, stop_sound all, playlist_add message, wait milliseconds; require_tier min_tier Enum (RequireTierAction.cs:42-47) shown as text (PipelineCatalogue.kt:347).
- [ ] `RAW` PipelinesScreen.kt:2717-2774 — 36 actions fall to a blank key/value editor: announce (color Enum hidden), wait_for_event, wait_until_raid_fires, return_value, break, continue, set_pronoun, submit_media, tts_synthesize, clear_viewer_data, song_request_favorite, play_track_once, 24 music_*.
- [ ] `RAW` PipelinesScreen.kt:2552-2694 — no `FieldKind.Number` branch → numbers as free text with template link.
- [ ] PipelineCatalogue.kt:729 vs ComparisonCondition.cs — hint `var_compare`, engine `comparison`; offline palette offers a type the engine blocks (PipelineEngine.cs:794-802); operator free text.
- [ ] `RAW` PipelineActionFieldDescriptor.cs:77-85 — `ResourceId` has no resource type → OBS scene/source/input/filter/transition/hotkey (ObsSceneActions.cs:29-148, ObsAudioMediaActions.cs:30-237; lists exist at ObsController.cs:65-80), vts hotkey (VtsActions.cs:162), game_type (PlayGameAction.cs:37, LiveGamePipelineActions.cs:36), discord trigger_type (SendDiscordNotificationAction.cs:39), permit role_or_capability (PermitAction.cs:50; RolePicker only on "min_role" PipelinesScreen.kt:2583), playlist_id, track_uri, message_id all raw text.
- [ ] `DEAD` Discord/Rewards/Assets `*OptionProvider.cs` — no action field uses their kinds.
- [ ] `DEAD` PipelinesController.cs:232-243 — `POST pipelines/validate` never called.
- [ ] PipelinesScreen.kt:2685-2691 — template helper omits step-produced variables (set_variable, check_balance set_var, run_pipeline params, wait_for_event payload).
- [ ] `TRUTH` ChannelRegistry.cs:511-512 — `UserCooldownSeconds` saved, never enforced; form (CommandsScreen.kt:1121-1144) promises both windows.
- [ ] CommandService.cs:83-97,200-215 — regex not compile-checked (ChatTriggerService.cs:208-221 does); bad pattern logged and skipped (ChannelRegistry.cs:496-503); client only checks blank (CommandsScreen.kt:828); no tester; no capture groups as args (ChatMessageHandler.cs:611-614).
- [ ] ChatMessageHandler.cs:416-425,~481-487 — missing/disabled pipeline → warning + no reply; no save-time rule.
- [ ] CommandService.cs:284-285 — PipelineId cannot be cleared (timers/triggers/responses accept Guid.Empty, TimerManagementService.cs:234-236).
- [x] `SEC` CommandService.cs:160,285 + ChatTriggerService.cs:92,130 + ChannelRegistry.cs:564-565 — PipelineId never checked against the channel; steps loaded by id outside any tenant → another channel's pipeline runs if its GUID is known (TimerService.cs:275-277 filters correctly). **VERIFIED**
- [ ] ChatMessageHandler.cs:1048-1053,1368 — trigger permission and `user.role` use badges only; commands use effective role (:335,:433) → permitted/badge-less Editors refused.
- [ ] IChannelRegistry.cs:168 + ChatMessageHandler.cs:1050-1073 — triggers in a ConcurrentDictionary, first match wins → random on overlap; no priority.
- [ ] ChannelRegistry.cs:354-363 — trigger bound to a disabled pipeline still runs (commands check :448-451).
- [ ] `TRUTH` TimerService.cs:103-104 + TimerDtos.cs — "live" = bot connected; timers post offline; no live-only.
- [ ] TimerService.cs:167-170 — min-chat-lines baseline in memory; resets on restart / lease move.
- [ ] `DEAD` BotLifecycleService.cs:52-108 vs EventResponsePresetCatalog.cs — poll, prediction, hype_train, goal, charity start/progress/stop, channel.update, vip add/remove, shoutout.receive subscribed but no trigger; HypeTrainEventHandler/PollEventHandler never call IEventResponseExecutor.
- [ ] `DEAD` CustomDataTriggerHandler.cs:51-62 + EventResponseDefaultsSeeder.cs:75 + CustomEventsScreen.kt:146 — custom events fire `custom.<name>` but no row can exist and no bind UI.
- [ ] `DEAD` VoiceTriggerDtos.cs + VoiceTriggerWidgetEventHandler.cs:36 — voice triggers only feed widgets; no pipeline/response hook; no CurrentCount reset.
- [ ] CodeScriptsController.kt:46-49 + CodeScriptsApi.kt:77 — Save & Compile publishes immediately; test runs the published version; draft/publish endpoints exist unused.
- [ ] `FEEDBACK` CommandsController.kt:349-360 — CommandExecuted only bumps use count; failures ignored; triggers/timers/responses send no fire event → no "fired/failed" feedback anywhere.
- [x] Clean: cooldown state singleton (DependencyInjection.cs:960); engine blocks unknown actions (PipelineEngine.cs:823) and logs step failures; template registry covers resolver keys; command form covers CreateCommandDto; chat-trigger client compile-checks regex.

## L6 · Rewards, economy, games, giveaways, quotes, polls (first round; liveops/supporters uncapped owed)

- [ ] `DEAD` CatalogService.cs:399-413 — no handler for `CatalogItemPurchasedEvent`; purchase takes currency, runs nothing (ICatalogService.cs:57 promises it) — run item.PipelineId; refund on failure. **VERIFIED**
- [ ] `TRUTH` ChatEarningHandler.cs:83-95 — role scale 0/1/2/3/5 vs rules 0/2/4/10/40 (EconomyScreen.kt:2505-2511) → sub-only pays VIPs, mod rules never pay — use ChatRole.Resolve(...).ToLevelValue() as ChatMessageHandler.cs:1408. **VERIFIED**
- [ ] `TRUTH` EconomyDtos.cs:176-182 vs EconomyApi.kt:602-609 — wrong client model → every Economy leaderboard row 0 (EconomyScreen.kt:1048); names = raw ids (EconomyLeaderboardService.cs:154/199).
- [ ] `DEAD` CurrencyEarningService.cs:170 — `ApplyWatchTimeBatchAsync` has no caller; onboarding seeds an enabled watch-time rule (EarningRuleSeedOnOnboardingHandler.cs:44); editor offers WatchTime (EconomyScreen.kt:2479). **VERIFIED**
- [ ] `DEAD` EngagementEarningHandler.cs:32-35 — only Follow/NewSub/Cheer/Raid earn; GiftSubscription rule seeded (:47) with no handler; resubs earn nothing.
- [ ] EconomyApi.kt:526-534 + CurrencyEarningService.cs:27 — BonusConfig dropped client-side and ignored server-side; chat earns offline — live-only flag + tier multipliers.
- [ ] GameService.cs:91-96 — default games seeded only when zero rows; channels seeded before 3b8ce9e3 (07-17) lack drop_game/raffle/heist/crash → GAME_NOT_CONFIGURED (LiveGameEngine.cs:77-81) — seed per missing type.
- [ ] `TRUTH` GameService.cs:303-307 — `WinChancePercent ?? 0` → duel (:54) and any play_game (PlayGameAction.cs:56-73) always lose; HouseEdgePercent saved (:170) never read.
- [ ] `DEAD` GamesController.cs:131-149 — 18+ consent grant has no client/chat command (AgeConsentService.cs:71-86); revoke exists (GamesController.kt:142-145) uncalled; no consent list.
- [ ] `DEAD` GiveawayService.cs:482-493 — `GiveawayDrawnEvent`/`GiveawayOpenedEvent` unhandled: no chat, overlay, hub, pipeline; claim window never prompted (GiveawayKeywordListener.cs:122-138).
- [ ] `PAGE` GiveawaysController.kt — no hub/poll refresh; GiveawaysApi.kt:171 first 100 entries, :120 first 25 giveaways, :186 first 50 pools.
- [ ] `DEAD` EconomyEvents.cs:139 JarGoalReached, QuoteAdded, GamePlayed, SavingsJarInviteSent — published, nothing acts (only analytics counts).
- [ ] `DEAD` AutomaticRewardRedeemedEvent.cs:22 + CustomPowerUpRedeemedEvent — never published; no `channel.channel_points_automatic_reward_redemption` translator.
- [ ] `TRUTH` RewardsScreen.kt:789-807 + RewardService.cs ~486-510 — Fulfil/Refund shown and attempted for unowned rewards (IsManageable=false); Twitch rejects.
- [ ] RewardRedeemedHandler.cs:243-250 — pipeline throw → logged only; redemption unfulfilled, no refund, nothing shown; `Reward.Permission` (Reward.cs:30) stored, in no DTO, unenforced.
- [ ] `PAGE` RewardsApi.kt:147 — redemption queue first 25.
- [ ] ChatPollService.cs:255-269 — timed poll closes only on next vote/list; :103,:288-294 English; no poll events.
- [ ] `PERF` ChatPollsCard.kt:73-78 — Home card reloads every 4 s forever (2 requests each), even with no poll; transient failure = error.
- [ ] QuoteBuiltin.cs:84-93 — no chat search (`!quote cheese` random); :217 ContextGame null; dashboard game free text; :142,:212,:223 English.
- [ ] `I18N` GameBuiltins.cs:117-121,:76 — English, bare numbers without currency name, "Economy → Games" instruction.
- [ ] GamePlayDto (EconomyDtos.cs:213-222) — no player name/game type → GamesScreen.kt:365-378 rows anonymous; filters not in UI; ActiveSessionCard (:421,:428) raw tokens.
- [ ] EconomyLeaderboardService.cs:274-275, 161 — jar-scope boards always empty; `CaptureSnapshotAsync` never called; Economy page ranks first config only (EconomyApi.kt:246-250).
- [ ] `DEAD` catalog purchase only from participant dashboard (ParticipantApi.kt:113); no !buy/!shop/!give/!jar; no purchase pipeline action.
- [ ] `PAGE` EconomyApi.kt:369,387,450 — purchases/ledger/jar history first 50.
- [ ] `I18N` EconomyController.kt:259,425,474; ScheduleController.kt:242 — "No active channel."
- [x] Clean: rewards form ↔ DTO 1:1, reach Twitch, queue live; game config dialog full, role names; giveaway form full; transfer/jar pickers; no hardcoded Text in these UIs; open chat poll survives restart (ChannelRegistry.cs:280).

## L7 · Moderation, chat, community, analytics, home (both rounds)

- [ ] ModerationApi.kt:1175 vs ModerationService.cs:1248 — client body lacks `confirm`; approve always refused. **VERIFIED**
- [ ] `TRUTH` ModerationService.cs:690-827 vs AutoModerationHandler.cs:102-286 — saves `link_filter`/`caps_filter` + `whitelist`/`threshold` 70/`maxEmotes`; enforcer reads `links`/`caps` + `allowed_domains`/ratio 0.7/`max_emotes` → link/caps/whitelist never fire; emote limit always 10; summary says active. **VERIFIED**
- [ ] `TRUTH` ModerationApi.kt:374 + CommunityController.cs:641 — Bans list = local saved records; chat/ladder/Twitch bans absent; unban (ModerationController.cs:161) never removes the row; Helix `GET moderation/bans` (:353) zero callers.
- [ ] CommunityController.cs:861-966 — Profile ban/unban + Moderation Unban (ModerationApi.kt:381) bypass ModerationService: no mod-log/history/hub, broadcaster token.
- [ ] ModerationScreen.kt:649,2041 — user card only from a banned row; chat rows (ChatScreen.kt:680-751), mod-log, AutoMod queue, reports, unban rows cannot open it.
- [ ] Two moderator-notes stores: ModerationController.cs:560 (GUID, ViewerProfileScreen.kt:671) vs :1115 (twitchId) — merge.
- [ ] ChatScreen.kt:273,777 + ChatController.kt:315 — chat timeout always 600 s; reuse preset picker (ModerationScreen.kt:1817).
- [ ] ModerationScreen.kt:1953 + ModerationController.cs:1505 — unparseable duration → null → 600 s silently.
- [ ] ChatController.kt:118 + ChatScreen.kt:235,249 — empty history hides chat-mode + Shield toggles.
- [ ] ChatScreen.kt:1302-1329 + ChatController.cs:141 — slow-mode delay, followers duration, unique, non-mod delay not editable; slow-mode on sends 0 (Twitch needs 3–120).
- [ ] ChatController.kt:117-119 — reload drops emotes + Shield state; re-fetched; toggle flickers.
- [ ] `FEEDBACK` ChatController.kt:325-331; ModerationController.kt:610 — NetworkBanResult ignored; no "N of M"; no success confirmation.
- [ ] `RESULT` ChatController.cs:448-451 — delete fallback via `IChatProvider.DeleteMessageAsync` (void) reports success.
- [ ] ChatController.cs:227-228 — history rows have null reply-parent; 7TV paints sent by hub (DashboardBroadcastHandler.cs:90) never rendered, absent on history.
- [ ] ChatMessageFragments.kt:56-63 — zero-width emotes not overlaid; every emote forced to 24 dp.
- [ ] ChatApi.kt:105-106 — emote catalogue fetched without `senderIdentity` → suggests emotes the bot cannot send.
- [ ] HomeController.kt:66-78,156 + DashboardController.cs:250-270 — 20 of 40 returned then client drops engagement.*, supporter.*, hype train, poll, prediction, unban, raid.out, subscription.end → feed near-empty, money never shows.
- [ ] `TRUTH` SubscriptionTranslators.cs:22-40 + ChannelAnalyticsDailyProjection.cs:120-125 — gift recipients as "X subscribed" (HomeScreen.kt:1001); count inflated; a 50-gift bomb counts as one.
- [ ] `TRUTH` HomeController.kt:283 + DashboardController.cs:371 — Replay success with `widgetsNotified=0`.
- [ ] `TRUTH` DashboardController.cs:112,125; CommunityController.cs:595-608 — Helix follower/sub failure → 0 as real; :152 UTC day.
- [ ] `FEEDBACK` HomeController.kt:156,167; CommunityController.cs:295-299 — failed feed/inbox/followers → "all clear".
- [ ] HomeController.kt:262-268 — raid target search only past chatters — Helix channel search.
- [ ] AnalyticsController.kt:83-84,95,325,379-384 — fixed 30 d UTC; top viewers locked to Messages (server supports WatchSeconds/Commands/Redemptions/CurrencyEarned); ChannelAnalyticsDailyProjection.cs:77 UTC buckets; failures → empty (:120-123).
- [ ] `RAW` TrustAutomationSection.kt:771-852 — AutoMod levels free-text digits ×9; ModerationController.kt:1279 no save confirmation.
- [ ] SpamDefenseSection.kt:144,97,180-191 — missing copy key → control silently dropped; min/max never shown; invalid entry dropped, Save sends old value; ModerationController.kt:1189 no confirmation; SpamDetectionsSection.kt:155-172 no detectedAt.
- [ ] ModerationScreen.kt:3343 — mod-log rows raw action + raw target id, no time/reason/duration (helper at :3405 unused).
- [ ] ViewerProfileScreen.kt:566/819 raw ManagementRole; :654/657 raw actionType + ISO; :470-473 raw standing/ISO; :962 "Voice"; ViewerProfileController.kt:131 failed ban-state read → "not banned"; :119 failed history → empty.
- [ ] `TRUTH` CommunityController.cs:820-852 + ViewerProfileScreen.kt:594 — trust picker (offers "moderator") writes a `trust:` key nothing enforces.
- [ ] `RAW` ModerationScreen.kt:3165 — moderators added by raw Twitch id; :1650 deny sends null note; :1002/2077 Approve without confirmation.
- [ ] ModerationScreen.kt:2208-2233 — AutoMod queue row raw id, raw category, no time.
- [ ] ChatScreen.kt:1407-1411 — selected announcement colour indistinguishable.
- [ ] `DEAD` community/stats, moderation/bans GET+DELETE, analytics top-viewer metrics other than Messages — never called.
- [ ] `PAGE` ModerationApi.kt:374 (bans), :385 (mod log), nuke list `Take=25`.
- [ ] `TRUTH` CommunityController.cs:556-559, 428-433 — FirstSeen/LastSeen fall back to now → "last seen just now", sorted to top (CommunityController.kt:173).
- [ ] `TRUTH` CommunityController.cs:548-549 — members without a User row listed with empty names.
- [ ] `TRUTH` CommunityController.cs:612 — moderator count from onboarding snapshot, not Helix.
- [ ] `PAGE` CommunityController.cs:369-377, 603-608 — VIP tab/count first Helix page only.
- [ ] `TRUTH` ModerationController.kt:1232,1263 — overturnedAt/restoredAt set to the words "overturned"/"restored".
- [ ] HomeController.kt:329-335 + HubEvent.kt:299 — live rows use timestamp as id → Replay 404; `data` dropped (tier, bits, viewers, gift count) until reload.
- [ ] ChatController.kt:204-221; HomeController.kt:325-338 — live append/prepend has no `channelId` filter → multi-chat / previous-channel lines bleed in.
- [ ] `RESULT` ChatFilterExecutionHandler.cs:130,134,205,208,217 — filter/escalation delete/timeout/warn/ban Results discarded; offence count still increments.
- [ ] `RESULT` IChatProvider.cs:83,91,109 — Timeout/Ban/Delete return plain Task; blind callers BanAction.cs:57, DeleteMessageAction.cs:46, TimeoutAction.cs:70, ChatPlatformRouter.cs:327,349,380.
- [ ] `FEEDBACK` ChatController.kt:181,350 — failed emote/settings load swallowed; :260,267 undecodable delete/clear payloads ignored; ModerationController.kt:1076 Clear Chat silent; ChatController.kt:328 ban silent; AnalyticsController.kt:249-262 drill-down failures → null.
- [ ] `RAW` ViewerProfileScreen.kt:794 raw capability keys/enum roles; ModerationScreen.kt:2827 raw `rule.type`; ChatScreen.kt:451 badge a11y = raw setId, `badge.info` never shown; TrustAutomationSection.kt:182 "." decimals.
- [ ] `DEAD` ViewerProfileController.kt:286 — `grantPermitCapability` zero callers; ViewerProfileScreen.kt:820 grants always permanent (null expiry/reason); rows never show expiry.
- [ ] `PAGE` AnalyticsApi streams `take=100`; ChatController.cs:150-169 history latest ≤200, no cursor/"load older".
- [ ] `SEC` ChatMessageFragments.kt:120,164 — chat links open with no confirmation (phishing in one click).
- [ ] `I18N` ChatController.kt:399, CommunityController.kt:205, ViewerProfileController.kt:426, AnalyticsController.kt:327 "No active channel"; typeLabels ModerationController.kt:146, ModerationScreen.kt:1682/2827/2910, MultiChatScreen.kt:263/315, ViewerProfileScreen.kt:962, HomeController.kt:248; server ChatController.cs:316/327, ModerationController.cs:1531, DashboardController.cs:348, ChatFilterExecutionHandler.cs:126 (reason into Twitch's permanent record).
- [ ] Raw hex/dp: ChatScreen.kt:1356-1359 (hex); ChatScreen.kt:442,452,1124,1133,1143,1256,1273,1404-1409, ChatMessageFragments.kt:43, EmoteComposerField.kt:247 (dp).
- [ ] `VAR` CommunityController.cs:308,386,480,736; HelixChatProvider.cs:411,441; KickEventSubscriptionWorker.cs:112,127; ChatPollService.cs:304; LiveWindowResolver.cs:33.
- [ ] `PERF` CommunityController.cs:463-492 — "all members" loads every chatter id per page request; ChatController.cs:191-235 — history enrichment one message at a time (~400 awaits/request); ModerationController.kt:1110 hub mod-log id collides on repeats.
- [x] Clean: chat de-dup + auto-follow; hub delete/clear/purge; Shield "unavailable"; AutoMod-held queue; spam dry-run wording; tier/confidence labels; unban/blocked-term/moderator reads paged; activity-feed double write collapsed by EventId.

## L8 · Music, song requests, TTS, sound, media share, VTS, OBS (first round; per-page uncapped owed)

- [ ] ResiliencePolicies.cs:466-492 — 429 retryable with `Retry-After: 0` → zero delay ×2. **VERIFIED**
- [ ] SpotifyMusicProvider.cs:1696-1711 — manual 429 retry re-enters the pipeline → up to 6 calls/poll. **VERIFIED**
- [ ] ResiliencePolicies.cs:500-513 — breaker ignores 429; limiter counts requests not rejections. **VERIFIED**
- [ ] SpotifyMusicProvider.cs:250-254 + MusicStatePollingService.cs:227-233,272-280,90 — 429 → null → "nothing playing" → backoff cleared, IsPlaying=false published, 5 s quiet cadence → the observed ~6 s; nothing remembers a 429. **VERIFIED (FailureCount=0 on the box)**
- [ ] SpotifyMusicProvider.cs:1798-1811, 1930-1937, 1562 — player commands, manage calls, token refresh never set `SpotifyRequestTags` → all share the `Guid.Empty` partition.
- [ ] ChannelSpotifyCredentialsService.cs:48-87 — client id swapped without invalidating a connection minted under another app.
- [ ] Dev-mode: 403 not 429 for non-allowlisted users, but a dev-mode app has the smaller budget that items above keep tripped.
- [ ] MusicService.cs:1969,1989 + 1878/1889 — each `!sr` = current + queue + search + add; recovery probe every 30 s on the interactive budget; all amplified ×3–6.
- [ ] SongRequestBuiltin.cs:52-67 + MusicService.cs:496-579 — no clean requester/track swap path; matches S-SR-STALE; instrumentation still missing: SearchAsync (SpotifyMusicProvider.cs:306-365) logs nothing on success; "Queued track" (MusicService.cs:864) logs no query/message id.
- [ ] MusicService.cs:756-834 — in-flight check not atomic with push + SetInFlight → two `!sr` in one tick both push; pushes the NEW entry not the head.
- [ ] SongRequestQueueReconciler.cs:68 — `RemoveThrough` drops every entry ahead of the started track; promote (MusicService.cs:1289-1306 / FairQueue.MoveToFront) can put an entry ahead of in-flight.
- [ ] MusicService.cs:1248-1287, 1308-1357 + SongWrongAction.cs:75-131 — remove/promote/ban/!wrongsong act on stale positions; BanQueuedTrackAsync RemoveAt after an awaited write, never clears in-flight — address by code/id.
- [ ] SpotifyMusicProvider.cs:312-313 — search `limit=1`, no `market=from_token`.
- [ ] MusicService.cs:1951-2002 + SongRequestBuiltin.cs:113-115 — duplicate reply always "requested by someone".
- [ ] MusicService.cs:713 — `RequesterUserId: null` hardcoded; SongRequestQueuePersistence.cs:116-130 does not persist Cost/RequesterUserId → refunds lost after restart; per-user limit / !wrongsong match on display name.
- [ ] MusicConfigDtos.cs:16-24 — no explicit toggle; explicit flag parsed (SpotifyMusicProvider.cs:2093) never used; TrustScoreCalculator unused by SR; MinTrustLevel role-only.
- [ ] IMusicService.cs:318 — `MusicQueueItem` has no in-flight flag → page cannot show which row is at Spotify.
- [ ] `RAW``HOOPS` SongRequestsScreen.kt:705-745 — manual add requires typing "requested by" (impersonates a viewer, uses their cap); :940-955 block needs raw URI + typed title.
- [ ] `TRUTH` MusicScreen.kt:464 — Premium-required (capability recorded at SpotifyMusicProvider.cs:1834) and rate-limited never shown; buttons silently disabled; setup strings (strings.xml:134-136) omit Premium + dev-mode allowlist.
- [ ] `RAW` TtsScreen.kt:2049,2174 — default voice raw id (searchable picker exists for per-viewer); :1383 Azure region raw.
- [ ] TtsConfigController.cs:100-130,166-191 — overlay test/skip/clear/pause/resume 200 with no presence check (SoundClipService.cs:49 does it); TtsController.kt:247-268 pause optimistic.
- [ ] MediaShareScreen.kt:495-498 — non-numeric → 0 → generic validation error (MediaShareService.cs:438-447), field not highlighted; EligibilityJson enforced (:92) but not in DTO/UI.
- [ ] MediaShareService.cs:141-182 — cost charged before SaveChanges, no refund on failure; queue count (:119-133) not atomic.
- [ ] `RAW` VtsActions.cs:131 model, :193 expression are Text though inventory exists (:155 uses ResourceId).
- [x] `SEC` OBSRelayHub.cs:127-133 — `AckCommand` completes any commandId from any bridge — scope to the bridge's channel.
- [x] Clean: sound clips invalidate trigger cache on write; preview checks presence.

## L9 · Widgets, overlays, alerts, bundles, assets (first round; per-.vue uncapped owed)

- [ ] OverlayHub.cs:71 + OverlaySdkController.cs:375 + OverlayAlertBroadcast.cs:65-69 — generic-feed `Event` (JSON string payload) emitted to the same `on(type)` handlers as `WidgetEvent` → alerts (and chat_box via DashboardBroadcastHandler.cs:145) fire twice, second copy "Someone / 0 bits"; stale comment at DashboardBroadcastHandler.cs ~152. **VERIFIED**
- [ ] `DEAD` WidgetNotifier.cs:97 — `SendSettingsChangedAsync` zero callers; WidgetService.cs:454-457 only publishes the dashboard event → saved settings never reach OBS (alerts.vue:165, countdown_timer.vue:65 expect it). **VERIFIED**
- [ ] AlertQueueService.cs:97-109 — queued-with-no-overlay stays Queued forever; no drain on JoinWidget; no skip/replay/pause endpoints.
- [ ] `RESULT` OverlayAlertBroadcast.cs:74 + AlertQueueService.cs:43 — every kind (ban, timeout, unban, moderator_removed, poll/prediction/hype progress) enters the 100-cap queue; `EnqueueAsync` Result ignored — allow-list + check.
- [ ] WidgetAlertHandlers.cs:96 — 40-slot replay capture excludes only ChatMessage; now_playing/progress/test fires (WidgetTestEventController.cs:79, channelEventId null) push out real captures.
- [ ] JintVueSfcCompiler.cs:90,143 — `Rent()` outside try; `CreateWarmEngine` throw leaks the semaphore → after 2 failures every compile hangs on blocking `_gate.Wait()`.
- [ ] AlertQueueService.cs:97 — pushes to a disabled alerts widget, marks Delivered.
- [ ] `TRUTH` WidgetTestEventController.cs:163-195 — samples use old `user`/`amount`/`viewers` shape; alerts.vue reads displayName/bits/count/viewerCount/fromDisplayName/amountMinor → "Someone gifted 1 sub", "0 viewers", "0.00 USD"; minBits>0 drops the cheer test.
- [ ] `TRUTH` WidgetTestEventController.cs:79-89,111-122 — no widgetId; fires at every widget; presence counted across widgets → false success.
- [ ] WidgetsController.kt:278 + WidgetsScreen.kt:924-931 — Test tries first subscription only; "nothing played" styled as success; English server string in a localized template.
- [ ] Alerts page: no Test, no OBS URL (`GET event-responses/overlay` never called from AlertsApi.kt); queue loaded once (AlertsController.kt:97); failed load renders nothing (AlertsScreen.kt:270); raw kinds (:337-351); cap 5.
- [ ] EventResponsesController.cs:72 + TtsConfigController.cs:86 — `OverlayUrl` without `WithOverlayOrigin` → loopback URL (WidgetService.cs:68-71).
- [ ] `DEAD` WidgetSettingsSchemaProvider.cs:61 offers `voice_trigger`; FirstPartyWidgetCatalogue.cs:83 omits it from default subscriptions → ticking it does nothing.
- [ ] alerts.vue:114-125 — no card for hype_train_*, shoutout_received, vip_added, moderator_added; one textTemplate for all; no per-event sound/image/video/TTS; hardcoded English ("just followed!", "Tier 1", "Someone"); event_ticker.vue:39-42 same fallback.
- [ ] `DEAD` EventResponseOverlayNotifierAdapter.cs:51 — "overlay" response type sends `event_response` nobody listens to; AlertsController.kt:199 cannot pick it.
- [ ] OverlayHostController.cs:73-75 — widgetId compared as raw Guid → ULID shows "not live" (bundle route accepts both).
- [ ] OverlayHostController.cs:144,150,198-211 — `style-src` lacks https: (Google Fonts blocked); `font-src` lacks 'self'; vanilla page no CSP.
- [x] `SEC` WidgetService.cs:1556-1573,1175-1177 + OverlayHub.cs:89-96 — per-widget token resolves to the whole channel.
- [ ] `RAW` WidgetSettingsSchemaProvider.cs:115 resetCadence, :177 provider, :205 rewards JSON, :213 countdown ISO, :229-230 custom_data source/field, :116/200 colours JSON.
- [ ] countdown_timer.vue:52-53 — duration mode restarts on every reload; no start/pause from the dashboard.
- [ ] editor.js:1204,1259 — close/Esc with no unsaved-changes check; no dirty tracking.
- [ ] BundleExportService.cs:297-300 — exports max VersionNumber not ActiveVersionId; drops FilesJson → multi-file widgets import unbuildable.
- [ ] `RESULT` BundleImportService.cs:931 — `CompileAsync` Result ignored; soundClipId/asset URLs (exporter's channel Guid) not remapped.
- [ ] BundlesController.kt:300-321 — export lists 4 of 10 supported kinds; failed list → empty group.
- [ ] ChannelAssetService.cs:116-119 — no video (mp4/webm) or font formats.
- [ ] AssetsController.kt:63 + ChannelAssetService.cs:173 + AssetsApi.kt:50 — name = filename stem ("logo (1).png" fails); silent replace can change kind; page 1/200 no paging; no used-by.
- [ ] ChannelAssetService.cs:301-317 — delete check scans dead versions (false hits), ignores Widget.Settings + event responses (false misses).
- [ ] `TRUTH` live: only `anda_six` has a `PlatformConnections` row — D1 attach flow has no base row for existing channels — write on every login + backfill.
- [x] Clean: route channelId canonicalised; settings JsonElement normalised (WidgetService.cs:1502); bundle cache busted (`?v=ContentHash` + WidgetReload); presence groups namespaced per broadcaster.

## L10 · Integrations, Discord, webhooks, federation, platform admin (first round; billing/uncapped owed)

- [ ] IntegrationsController.kt:117-119,308,494-501 + BotAuthApi.kt:80,95,98 — bot row uses platform endpoints: status `AuthController.cs:1307` (IamManage → 403 → "not connected"); connect :1339 refuses non-admins; Disconnect `DELETE auth/twitch/bot` removes the shared bot for every tenant, no blast radius; channel endpoints exist (ChannelBotController.cs:217,233; ChannelsApi.kt:131/134). **VERIFIED**
- [ ] IntegrationsController.kt:262-270 → SystemController.cs:396-460 — BYOC for YouTube/Discord/Kick writes the deployment slot: 403 for streamers; silent global swap for admins; no validation; only Spotify has a per-channel endpoint (IntegrationsController.cs:89; ChannelCredentialsResolver.cs:41-47 reads per-channel rows).
- [ ] SystemDtos.kt:35-40 + IntegrationsController.kt:204-207 — client `SystemChecks` lacks `youtube` (server sends it, SystemController.cs:83,166) → YouTube connect always dead-ends.
- [ ] ShellNav.kt:175 + FederationController.cs:39 + FederationController.kt:60-66 — Federation page at Broadcaster level needs AuditRead → full-page error; gateway/signer (DependencyInjection.cs:288-296) uncalled; six events unconsumed.
- [ ] `TRUTH` OAuthProviderRegistry.cs:31-40 — twitch absent from the status list; EventSubRevokedEventHandler.cs:60 marks needs_reauth, nothing renders it; inbox item routes to Integrations (HomeController.kt:547) with no row; patreon/shopify/treatstream same.
- [ ] `DEAD` DiscordLiveRoleService.cs:48 — live-role engine runs; no endpoint/UI creates a config row (sharpens S057).
- [ ] `TRUTH` DiscordScreen.kt:1542-1548 → DiscordGuildService.cs:210-211 — "server consent" = typed unverified Discord user id; error text reuses the role-id string.
- [ ] `TRUTH` IntegrationOAuthService.cs:458-468 — Discord card "Connected as <guild>" for any row; NeedsReauth hardcoded false; dispatcher skips until both sides opt in (DiscordNotificationDispatcher.cs:110-119).
- [ ] YouTubeLiveChatPollWorker.cs:229-242 — missing permission / quota only logged; never needs_reauth (Kick does at KickEventSubscriptionWorker.cs:263).
- [ ] `DEAD` IntegrationTokenVault.cs:267 — `IntegrationNeedsReauthEvent` no consumer; "expired" status never written; ActionRequiredInboxService.cs:53/230 Expired branch dead.
- [ ] IntegrationsController.kt:661-668 + IntegrationDtos.kt:85-95 — granted permission sets/capabilities dropped → "granted vs needed" never shown for Spotify/YouTube/Kick; re-grant only for Twitch; OAuthProviderRegistry.cs:229 claim false.
- [ ] `DEAD` PlatformAdminController.cs:235/255/277,298,325/343,361 + AdminController.cs:367 — quota overrides, remigrate, erase preview/run, export, rotate-encryption-key have no UI (PlatformAdminApi.kt:147-156 covers detail/suspend/reinstate/access).
- [ ] `RAW` AdminScreen.kt:1581,1607; AdminEntitlementGrantSection.kt:89,226 — tenant ids typed by hand; expiry free-text date.
- [ ] `TRUTH` ProviderCredentialService.cs:56-65 + SystemController.cs:150-157 + AdminController.kt:943-955 — Providers tab lists twitter/spotify slots nothing reads; "enables song requests" claim false; no confirmation/validation.
- [ ] `PAGE` AdminIamTab.kt:373 — Promote picker limited to the loaded page (25).
- [ ] InboundWebhookDispatcher.cs:182 — rejected inbound not stored; `lastReceivedAt` (WebhooksApi.kt:163) never shown (WebhooksScreen.kt:541).
- [ ] `RAW` WebhooksScreen.kt:623 URL truncated no copy; :991 event route free text (catalogue exists); no sample payloads per adapter.
- [ ] WebhooksApi.kt:221-225 + WebhooksScreen.kt:684 — outbound rows lack last delivery/success/failure; auto-disabled never reaches the inbox; catalogue failure "unavailable" no reason (WebhooksController.kt:113-117).
- [ ] `RESULT` IntegrationsScreen.kt:387 + IntegrationsController.kt:674-682 — EventSub reconcile report discarded; failure silent; rows never show Twitch's own status.
- [ ] `FEEDBACK` IntegrationsController.kt:626-628,635 — declined/expired scope re-grant closes silently.
- [ ] `I18N` ActionRequiredInboxService.cs:228-232 — inbox titles around raw provider keys; IntegrationsScreen.kt:408 raw key in disconnect dialog; AdminContentPipelineAuthoring.kt:165 English literal.
- [ ] Discord: only a render preview (DiscordController.cs:268-278), no real test send; DiscordGuildLinked/Unlinked/NotificationDispatched unconsumed.
- [ ] Platform truth today: Kick = chat, follows, subs, redemptions, live status, bans, gifted Kicks; YouTube = polled chat + Super Chats + viewer counts (gap above); X = nothing (S031); Patreon/Shopify/TreatStream = OAuth only, no card (S101).
- [x] Clean: Discord rule/role-button pickers use live guild lists; outbound webhooks have rotation, test with result, replay, delivery log; IAM role assignment picker; feature-flag toggles show blast radius.

## L11 · Runtime stability (first round; full tables owed)

- [ ] TwitchHelixTransport.cs:389,404-419 + TwitchEventSubHostedService.cs:1311-1326 — body reduced to plain `message`; parser expects JSON → `missingScope` always null → gate (:691-708) never blocks, `TwitchHelixReauthRequiredEvent` never publishes; TwitchEventSubReconnectTests.cs:409 feeds JSON the transport never produces. **VERIFIED**
- [ ] TwitchEventSubHostedService.cs:691-708,760-765 + MapErrorAsync:380-387 — only "Missing required scope X" gated; "subscription missing proper authorization" re-POSTed forever; gate passes when grant set is null — 403 code, terminal per channel+topic with grant fingerprint, one action-required item.
- [ ] TwitchEventSubHostedService.cs:307-312,1171-1203 + WebSocketEventSubTransport.cs:602,627 + :1113-1164 — every welcome re-POSTs the owner's whole failed slice; all-403 session → 4003 → reconnect ≈32 s → repeat; planned `session_reconnect` also triggers delete-all + re-POST-all though Twitch carries subs over — skip cleanup on reconnect-URL swap; stop reopening fully-blocked sessions.
- [ ] TwitchEventSubHostedService.cs:285-333 + EventBus.cs:64-69 — cleanup/re-register/backfill and all 12 chat handlers run inline in the receive loop → frames unread, 10 s first-sub window missed → 4003 — queued worker.
- [ ] EventSubResubscribeOnTokenRefreshedHandler.cs:57 + IntegrationTokenVault.cs:214 + TwitchHelixTransport.cs:304 — every stored refresh fires 74 POSTs inline, even inside a 401 retry; can recurse — queue/debounce; only on grant change.
- [ ] TwitchHelixTransport.cs:394 — every non-2xx at Warning (409s, gated 403s) — Debug; caller decides.
- [ ] BotLifecycleService.cs:171-244 + TwitchEventSubHostedService.cs:599-600,1123-1150 — losing colour runs Sync → Subscribe → EnsureSession, opens its own sessions bypassing the `eventsub-chat-ingest` lease; its welcome's cleanup deletes the leader's subs (`SessionId != mine`) — gate on leadership. **VERIFIED**
- [ ] No lease/dedupe: MusicStatePollingService (double budget; HandOverNextAsync:1791-1846 in-process in-flight → both colours push the same SR); YouTubeLiveChatPollWorker:514-523 (check-then-publish → double answer, double quota); StreamStatusPollingService:180 (double journal); TokenRefreshService + IntegrationTokenBootSweepService (ConnectionRefreshGate in-process); KickEventSubscriptionWorker, ManagementRoleReconcileService, CommunityStandingReconcileService (duplicate Helix).
- [ ] ChannelRegistryBootstrapService.cs:59-64 + SongRequestQueueRestoreHostedService.cs:64-69 — cluster lease guards PER-PROCESS state → loser never restores — drop the lease.
- [ ] ResiliencePolicies.cs:71,140,189,238,318,395 + appsettings.json:4-10 — no `TelemetryOptions.SeverityProvider`, no Serilog "Polly" override → OnRetry/ExecutionAttempt at Warning, OnTimeout/rate-limiter/circuit at Error with stack traces.
- [ ] ResiliencePolicies.cs:475-490 + SpotifyMusicProvider.cs:1696-1710 — (see L8) clamp Retry-After; one retry layer.
- [ ] ResiliencePolicies.cs:507-512 — breaker ignores 429; nothing remembers a 429 between polls — count 429 or per-channel cooling clock.
- [ ] SpotifyMusicProvider.cs:251-252 + MusicStatePollingService.cs:232,214-218,279,242 — 429 → null → backoff cleared, IsPlaying=false, quiet cadence; HandOverNextAsync each poll adds calls — typed rate-limited result; skip handover while cooling.
- [ ] EventSubConnectedEventHandler.cs — clears needs_reauth + failure count on any welcome (WS open needs no token) → status oscillates — clear only after an authenticated success.
- [ ] YouTubeAccessTokenProvider.cs:98-112,180-190 — no needs_reauth short-circuit (Kick :202, Spotify :1433, Twitch :161 have one) → dead refresh POSTed every tick; TwitchAuthService.cs:242-247, Spotify :1567-1579, YouTube count 5xx/429 toward the 3-strike; TwitchAuthService:241 no try/catch.
- [ ] IntegrationTokenVault.cs:369-375 — `DECRYPT_FAILED` leaves Connected → retried every tick, nothing surfaced — distinct status + action item.
- [ ] TenantAccessGrantExpiryService.cs:49-63 — `Task.Delay` inside try → hot-spin on error (fifth no-backoff worker).
- [ ] `DEAD` PermissionChangedBroadcastHandler.cs:17 (never published); ModerationProjectionHandlers.cs:92 `MessageAutoModdedEvent` (never published).
- [ ] PostgresRunOnceGuard.cs — advisory lock on one idle connection; a dropped connection releases the lease silently — keepalive / re-check.
- [x] Clean: EventBus isolates handler exceptions; Music/StreamStatus/UserProfileHydration/ChatDecorationRefresh/Kick/reconcile workers survive a throwing tick; Redis health uses the shared multiplexer; EventSub off the ready tag; per-connection refresh gate exists for Twitch/Spotify/Kick/YouTube; `Clients.All` only on AdminHub behind iam:manage; ChannelRegistry settings invalidated on write; `IX_Channel_TwitchChannelId` unique.

## L12 · Scopes at enable time (owner A4)

- [ ] FeatureService.cs:176-224 — `ToggleFeatureAsync` flips `IsEnabled`; `RequiredScopes` metadata only, never compared to granted — refuse with `SCOPES_REQUIRED` + list; client opens additive re-grant; flip on success. **VERIFIED**
- [ ] TwitchHelixTransport.cs:389-391 — every 401 publishes `TwitchHelixReauthRequiredEvent` (Twitch answers 401 for a missing scope) → "reconnect Twitch" at use time, every use, no scope named — `missing_scope` only when the body says so; record once per channel+scope.
- [ ] MissingScopeRecordingHandler.cs:44-77 — records only when a scope name was parsed (never, per L11) — fix with the parser.
- [ ] Same check on enabling a command/pipeline action tagged `[RequiresTwitchScope]`; on login diff granted vs required-by-enabled and raise one item.

## L13 · Dead links (owner A5) and no-hoops (owner A6)

- [ ] SongRequestsController.kt:106-110,266-272 — `/sr/@login`, `/sr/{token}` unserved (public controllers: overlay, oauth-relay, obs-bridge, voice-listener only); Program.cs:1010 SPA fallback answers the dashboard for ANY unknown path — build the page; link-walk test; fallback excluded for public paths. **VERIFIED**
- [ ] "Streamer channel" link the owner saw — not found by grep in app or server; locate on the rendered client (shell dead-links sweep owed).
- [ ] `HOOPS` Command/trigger/timer/event response/reward/custom event → PipelineBindPicker existing-only; no inline new pipeline.
- [ ] `HOOPS` Pipeline step → sound clip/asset/reward/Discord channel-role/OBS scene/VTS model/game: raw text or existing-only.
- [ ] `HOOPS` Alerts/widgets → sound/image must be uploaded on Assets first.
- [ ] `HOOPS` Giveaway → currency; Store item → pipeline; Reward → pipeline: existing-only.
- [ ] `HOOPS` Roles → permit viewer; Moderation → add moderator: raw id / existing-only.
- [ ] `HOOPS` Discord rule → guild must be connected elsewhere; live role has no UI.
- [ ] `HOOPS` SR block-list raw URI; manual add typed viewer name.
- [ ] `HOOPS` Wizard review drops web input; dialogs close before result (L4, L7).
- [ ] Standing rule: the picker primitive gets a "create inline" affordance; every picker above adopts it; the form's draft survives.

## L14 · Onboarding friction (owner ask 2026-09-24: "as human friendly and frictionless as possible")

Path 1 — self-host lite
- [ ] DEPLOY.md:65-105 vs README.md:311-313 — two different "run it yourself" stories: `deploy.sh desktop` (needs .NET SDK) vs `dotnet run` (README says it needs `docker compose up -d postgres redis`, which contradicts SelfHostLite-on-SQLite) — one quickstart; cross-link; fix the README claim.
- [x] app/feature/setup/state/SetupController.kt:36-44, 266-274 + ConnectController.kt:568 — web `finish()`: OAuth navigates away, `completeSetup()`/`applyBasics()` never run → basics (:459-464) lost, `setup_complete` never set, credential endpoints stay open (duplicate of L4 #2, root-caused here) — persist "finish" intent before the redirect; resume after reload.
- [ ] `FEEDBACK` SetupController.kt:176-199, 212-249 — bot device-code step: expired / denied / error collapse into one generic "Bot authorization failed: <token>" (:242-249); no countdown toward `expiresIn` — distinct copy per DEVICE_EXPIRED/DENIED/ERROR; visible countdown.
- [ ] `TRUTH` SetupController.kt:122-136 — two "bot account" concepts at once: a `botUsername` text field on the twitch_app step and a device-code OAuth on `platform_bot`, relation never explained; skipping consequence ("bot posts AS YOU with a prefix", D5, CLAUDE.md:659-672) only inferable from code (:301-312) — one concept; say the consequence on the step.
- [ ] SetupController.kt:79-84, 320-322 — wizard state in memory; web reload loses it (compounds the redirect drop; L4 already lists it).
- [ ] SetupController.kt:126-130 + ConnectController.kt:393-403 — BYOC client id without secret accepted silently, but every later reconnect then takes the slower device-code path (redirect only when `twitchApp.ok`) — say so on the step.
- [ ] No "what's next" checklist after the wizard; hands off straight to Home; no "send a test message" / "try a command" / "mod the bot" prompt.

Path 2 — docker
- [ ] DEPLOY.md docker section never mentions `API_HTTP_PORT`/`API_HTTPS_PORT`/`ADMINER_PORT` (.env.example:141-147) — a taken 5080 has no signpost.
- [ ] .env.example:106-112 — YouTube/Kick/X login stays hidden until credentials AND a platform-admin feature flag (`use_youtube_login`…) are set; nothing says where the flag lives; wizard has no toggle (STEP_* :369-373) — name the screen, or fold the flag into the provider step.
- [ ] .env.example:4-6 vs DEPLOY.md:126-130 — manual `cp .env.example` + openssl presented as a parallel path to the script that already generates secrets — promote the script; demote manual to an appendix.
- [x] Clean: TWITCH_CLIENT_ID may be blank (wizard/shared client); JWT/ENCRYPTION/POSTGRES secrets auto-generated by the script; first URL `http://localhost:5080` consistent across .env.example:52, DEPLOY.md:132, ConnectController.kt:797.

Path 3 — hosted
- [ ] `TRUTH` AuthService.cs:369-382,492-500 (L2 #2) — first login silently installs the bot on the user's own channel: no "the bot joined your channel" screen, no "mod it now" one-click, no plain-language scope list.
- [ ] A2.1 — a moderator-invited streamer's own channel is a dead husk (`IsOnboarded=false`) with no on-screen explanation.
- [ ] A4 — first feature toggle needing a scope → unexplained "reconnect Twitch" at use time.
- [ ] DEPLOY.md:180-217 — only the operator's SaaS setup is documented; the end-streamer first-run has no doc.
- [x] Clean: returning users restore silently from cookie/token (ConnectController.kt:668-724); no repeat device code.

Ideal path (the bar):
1. One script → open printed URL → ONE screen: "Sign in with Twitch" (code large, link is a button). BYOC + bot account are opt-in toggles, not steps to skip.
2. Basics optional with correct defaults (prefix `!`, timezone from browser, language from Accept-Language); changeable later; never raw text.
3. `finish()` survives the web redirect; nothing typed is ever lost.
4. First dashboard = short checklist (mod the bot, try a command, connect optional integrations) + inline "send a test message".
5. Docker = same wizard; the script is the one true path.
6. SaaS first login = explicit "bot joined your channel" confirmation + one-click mod + scope list; no silent install, no use-time reauth.

## L5b · Automation authoring — second pass

- [ ] `I18N` srv Commands/Builtins/GameBuiltins.cs:117-121 — win/loss reply is a raw interpolated string returned via `Result.Success`, bypassing `IBuiltinResponseComposer` (siblings at :95-102 use it) → cannot be overridden, retoned or translated; :112-113 pipes `played.ErrorMessage` verbatim.
- [ ] `I18N` MusicBuiltins.cs:235, 271-273 — volume replies + "Failed to set volume." bypass the composer.
- [ ] `I18N` UpdateUserInfoBuiltin.cs:196 — success string hardcoded; failure branch (:167-181) already composes.
- [ ] `I18N` WhisperBuiltin.cs:138-140 — both branches raw English; `sent.ErrorMessage` reaches chat verbatim.
- [ ] `TRUTH` app/core/network/BuiltinsApi.kt:74-88 + srv BuiltinsController.cs — `defaultCooldownSeconds` / `defaultMinPermissionLevel` shown as settings with no write path anywhere (only enable / response / tts endpoints; CommandsController.kt:323-342) → a streamer cannot put `!sr` on a longer cooldown or gate it to subs.
- [ ] PipelinesScreen.kt:2394-2397 — new step defaults `actionType` to the first palette entry, no "unselected" state; switching to a generic block may leave `canSubmit` true with zero params (lead: confirm `blockComplete`'s generic branch).
- [ ] PipelinesScreen.kt:2758-2765 — generic-editor template insert always targets `entries.last()`, not the focused row.
- [ ] VoiceTriggersApi.kt:78-90 + VoiceTriggersScreen.kt:370 — `UpdateVoiceTriggerBody` has no `startingCount`; `currentCount` has no correction path at all → only delete-and-recreate.
- [ ] Owed: Result/bool-ignored sweep of Timers/EventResponses/CustomEvents controllers; ChatTriggers picker/enum fields.
- [x] Clean: CodeScripts and PickLists screens use `stringResource` throughout; no orphaned DTO fields found.

## L6b · Supporters (S101) and live ops (S106) — concrete gaps

- [ ] `DEAD` srv Infrastructure/Supporters/Adapters/*.cs — 11 `ISupporterSource` adapters wired in DI (kofi, shopify, patreon, buymeacoffee, donordrive, pally, streamlabs, tipeee, streamelements, fourthwall, treatstream) but SupportersScreen.kt:667-668 hardcodes ONE tile (Ko-fi) → 10 money sources unreachable — render one tile per backend-known source.
- [ ] `DEAD` srv SupportersController.cs:1-122 — no `GET /supporters/sources`; `ISupporterSource.Capabilities` (SupporterDtos.cs:14-18: Kinds, ConnectionMode, RequiresOAuth) never exposed.
- [ ] SupportersController.kt:76-89 vs SupportersApi.kt:99-103 + SupporterConnectionService.cs:152-179 — client `upsertConnection` has no `integrationConnectionId` → the Patreon OAuth-provision path is dead on the client.
- [ ] SupportersScreen.kt:394-412 — one form shape (secret + Connect) regardless of ConnectionMode (webhook / socket / poll / oauth).
- [ ] ShellNav.kt:169-171 — `Stream` group = Alerts, Schedule, Analytics only; no `StreamScreen` exists; polls/predictions/raids/ads/clips/markers are Home quick-actions; title/game/tags split across Schedule and Settings — one live-ops page.
- [ ] LiveOpsController.kt:51-80 — active poll/prediction/ads loaded once; no hub subscription to poll/prediction progress → totals frozen (fails S106's own done-when).
- [ ] `DEAD` LiveOpsController.cs:567-595 — `POST markers` only; no list → markers are fire-and-forget.
- [ ] `DEAD` LiveOpsController.cs:30-37 — hype train, goals, charity, guest-star have zero controller surface (EventSub subscribed, nothing reads or manages them).
- [ ] `TRUTH` StreamController.cs:204-253 (:249-250) + SettingsController.kt:86-91 — `UpdateStreamInfo` echoes `Language=null`, `LastStreamedAt=null`; client replaces state with the echo → saving title/game blanks language + last-streamed until reload.
- [ ] Owed: fresh uncapped sweep of Rewards/Economy/Games/Giveaways/Quotes/Polls beyond L6 (only Quotes re-checked: no new gaps).

## L9b · Widgets — second pass (the two widgets missing from the 21-widget table)

- [ ] `DEAD` srv Marketplace/LuckyFeatherBundle.cs:226-232 + WidgetService.cs:694-701 — marketplace preset ships 4 settings (idleText, stolenTemplate, bannerDurationMs, accentColor) but no `NaturalKey` is set on import and `WidgetSettingsSchemaProvider` has no entry → `WIDGET_NO_SETTINGS_SCHEMA`, dashboard says "configure through the code editor" (nothing routes there) — NaturalKey + schema for imported first-party bundles, or a generic key/value settings editor.
- [ ] `I18N` lucky_feather.vue:37, 75, 78, 94 — hardcoded English (idle text, "stole the Feather from", "found the Feather!", "Someone").
- [ ] lucky_feather.vue:155, 162, 182 — font-family and card colours hardcoded; only the border follows accentColor.
- [ ] lucky_feather.vue:100-107 — malformed `steal` push (readHolder null) swallowed with no banner.
- [ ] tts_audio.vue:56 — whole visual gated on `showIndicator`; with it off the source renders nothing, so placement cannot be re-verified in OBS.
- [ ] tts_audio.vue:34 — `durationMs` read from `tts_speak`; unverified the dispatch DTO sends it (else every flash is the 1500 ms guess) — check against the real DTO.
- [ ] tts_audio.vue:23 + FirstPartyWidgetCatalogue.cs (~15×) — `#9146ff` literal default duplicated instead of one shared constant.
- [x] Re-confirmed, not re-logged: every L9 item on Widgets page, Alerts, Bundles, Assets, overlay serving. Mechanic note: `ScriptHostBridge.cs:388-418` `EmitWidgetEvent` ignores `EventSubscriptions` (name + enabled only), so `EventSubscriptions = []` is inert for `widget.emit` widgets.

## L10b · Platform admin — second pass

- [ ] `RAW` AdminScreen.kt:1573, 1581-1587, 1607-1611 — per-tenant feature-flag override = free-typed broadcaster id (tenant search-select exists on the Tenants tab).
- [ ] `FEEDBACK` AdminScreen.kt:1536-1553 — per-tenant Enable/Disable/Clear fire with no confirm/preview, while the global kill-switch (:1487-1530) shows a counted blast radius — same pattern, scaled to one tenant.
- [ ] `TRUTH` AdminScreen.kt:1533-1554 — after an override the row shows no current state; no list of existing overrides per flag — write-only box.
- [ ] AdminBillingController.cs:211-216 vs LimitedResourceRegistry.cs:41-81 + AdminBillingTierSection.kt — priced units and enforced limit keys are two lists that never show each other's numbers.
- [ ] AdminBillingTierSection.kt:270-285 — limit editor renders only keys already in `tier.limits`; an enforced registry key with no row is simply absent (resolves unlimited, BillingTierService.cs:113-116) — show every registry key.
- [ ] `TRUTH` AdminTenantsTab.kt:189-190 — drawer opened for a tenant not in the loaded list fabricates an `AdminTenant` with `isLive=false` for the suspend/reinstate dialogs, unmarked.
- [ ] Owed: Discord page field-by-field; Webhooks in/out field-by-field; Federation page; provider-card-by-card scope/BYOC pass; inbox item-type "does fix-it work" pass; AdminIamTab; System*/PlatformAdmin* beyond L10.
- [x] Clean: AdminScreen/AdminTenantsTab/AdminBillingTierSection use `stringResource` throughout; BillingTierService, ResourceQuotaService, LimitedResourceRegistry, FeatureFlagAdminController, AdminBillingController, BillingController — no unenforced-limit-shown, no swallowed Result, no `var`.

## L8b · Music, TTS, sound, media share, VTS, OBS — second pass

- [ ] `DEAD` MusicScreen.kt + MusicController.cs:1-539 — no favourites and no history anywhere in the stack (no endpoint, no UI) — capability missing, not hidden.
- [ ] MusicScreen.kt — no provider switch on the Music page; only in the SR config card — surface the active provider's control or a link.
- [ ] `RAW` SongRequestsScreen.kt:705-745 — "requested by" free text with no viewer lookup (SearchPickerField exists on TTS per-viewer tab); combined with `RequesterUserId: null` → attribution to nobody.
- [ ] `RAW` SongRequestsScreen.kt:894-978 — block form takes raw URI + title with no lookup; a typo blocks nothing silently.
- [ ] `PAGE` TtsQueueController.cs:46-51 + TtsApi.kt:209-213 — approval queue capped at 25, no load-more (Sound uses `getAllPages`, SoundApi.kt:76-79).
- [ ] TtsQueueController.kt:20-24 + TtsScreen.kt:804-853 — approval queue polls once, no hub push, no refresh button.
- [ ] TTS has no cost concept anywhere (grep "cost" = 0 hits) — capability missing.
- [ ] `FEEDBACK` VtsScreen.kt:226-239 — bridge-token rotate fires on one unconfirmed click; previous token dies immediately (VtsController.cs:84-88).
- [ ] `FEEDBACK` ObsScreen.kt:501-555 (:548-552) — same unconfirmed rotate on OBS.
- [ ] `TRUTH` VtsController.cs:56-64 ("full VTS API pass-through") vs VtsScreen.kt:279-417 — UI sends only ModelLoad/HotkeyTrigger/ExpressionActivation; no raw request box (OBS has one, ObsScreen.kt:1513-1638) — add it or fix the claim.
- [ ] `FEEDBACK` VtsScreen.kt:315-325 — null control result silently clears the previous message.
- [ ] `DEAD` SoundClipsController.cs + Application/Sound (grep "hotkey" = 0) — no hotkey binding for sound clips; only chat `triggerWord` (SoundScreen.kt:480-486).
- [ ] SoundClipsController.cs:297-302 — only .mp3/.ogg/.wav by suffix; no upfront rejection or accepted-formats hint (`sound_clips_upload_hint` generic).
- [ ] `PAGE` MediaShareApi.kt:72-83 + PaginatedResponse.cs:37,45-49 — moderator queue silently capped at 25 per status; backend `GetQueue` (MediaShareController.cs:52-72) already accepts paging — client-only fix.
- [ ] MediaShareScreen.kt:447-465; ObsScreen.kt:383-499 — Save with no dirty check (TtsScreen.kt:423 has the pattern).
- [x] Clean: OBS scene/source/mixer/hotkey/screenshot; VTS connection card; Sound row actions/edit; SR queue row actions; TTS General/Voices/PerViewer/Pronunciation; OBS raw pass-through fully wired.
- [ ] Not covered: TtsConfigController.cs <~400, ObsController.cs >~400, SongRequestsController service internals, VtsControlService/VtsPluginAuthorizer, MediaShareService beyond L8.

## L4b · Shell — dead links (A) and second pass (B)

- [x] Every URL the app or server emits was traced; all resolve to a served route except `/sr/*` (L13). Served-outside-api/v1 routes: `oauth-relay`, `obs-bridge`, `overlay*`, `overlay/voice-trigger`, `voice-listener`, `automation/v1`; Program.cs:1010 SPA fallback answers everything else.
- [ ] "Streamer channel" link — NOT found in source. Only candidates: TemplateResolver.cs:603-616 / ShoutoutAction.cs:275 build `twitch.tv/{login}` as chat text (Twitch linkifies), and DiscordNotificationConfigService.cs:363 `https://twitch.tv/SampleStreamer` is a template preview sample — locate on the rendered client with the owner.
- [ ] WebhooksScreen.kt:623 — `ingestUrl` plain Text, not `CopyLinkButton` like every other copyable URL.
- [ ] `I18N` FeaturesController.kt:82; RolesController.kt:184 — "No active channel — reconnect and try again." literals (not yet in the i18n list).
- [ ] `I18N` RolesScreen.kt:1097-1098, 1101-1102 — `typeLabel = "Member"` / `"Grant"`.
- [ ] `TRUTH` HomeController.kt:174-193 — first-run checklist is all-or-nothing: shown only when commands AND pipelines AND integrations are all empty; one test command hides the other two reminders — per-step checklist.
- [ ] HomeController.kt:187-189 + HomeScreen.kt:413,693 — checklist routes by bare string ("Integrations"/"Commands"/"Pipelines"), not typed `ShellRoute`.
- [ ] wasmJsMain/resources/index.html:16 — static `<title>`; never updated per route/channel.
- [ ] wasmJsMain/resources/index.html — no favicon at all.

## L11b · Runtime — second pass

- [ ] No lease (blue/green double-act): Commands/Jobs/ScheduledPipelineExpiryService.cs; Rewards/Jobs/RedemptionTimerExpiryService.cs; Identity/Jobs/TenantAccessGrantExpiryService.cs (on top of its hot-spin); Platform/Scheduling/IntegrationTokenBootSweepService.cs (one-shot, unlike its leased siblings ChannelRegistryBootstrap / SongRequestQueueRestore / DiscordLiveRoleReconciliation).
- [ ] `DEAD` zero-reference event types: ChannelJoinedEvent, ChannelLeftEvent, StreamStatusChangedEvent (StreamStatusPollingService journals directly instead), CommandFailedEvent, IntegrationErrorEvent, FeatureToggledEvent — delete or wire (owner call: several look like the missing publish for feedback items elsewhere on this ledger).
- [ ] AuthService.cs:1079-1083 — `ConnectChannelBotAsync` `IgnoreQueryFilters` without `DeletedAt == null`/`IsActive` (siblings :1137-1140, :1167-1170 have it) → resurrects a soft-deleted ChannelBotAuthorization.
- [ ] Empty catches: Platform/Eventing/IWebSocketChannel.cs:65 `catch (WebSocketException) {}`; TwitchEventSubHostedService.cs:177 and WebSocketEventSubTransport.cs:507 `catch (OperationCanceledException) {}` without checking the stopping token.
- [ ] SQLite/Postgres: PickListService.cs:72-73 and QuoteService.cs:199-202 use `EF.Functions.Like` (case-insensitive on SQLite, sensitive on Postgres) — WidgetGalleryService.cs:75-76 already documents and avoids this; apply the same.
- [ ] Sync-over-async: CustomCode/ScriptHostBridge.cs:165, 276, 288 (`GetAwaiter().GetResult()` on paint resolve, chat send, storage — the Jint bridge blocks its thread); Platform/Deployment/DeploymentModeResolver.cs:43-44 (startup, confirm never mid-request); Obs/Transport/DirectObsTransport.cs:535 + Vts/Transport/DirectVtsTransport.cs:445 (`Dispose` wraps `DisposeAsync`; check the tray host's shutdown thread); JintVueSfcCompiler.cs:143 `_gate.Wait()`.
- [ ] Owed (not swept): provider client methods with zero callers (item 6); singleton-captures-scoped-DbContext beyond SongRequestQueueStore (item 8); per-tick Warning+ logs beyond TwitchHelixTransport.cs:394 (item 10); the full 235-event publish/handle matrix (item 2 is a hand-verified sample).

## L15 · Frontend rule-compliance sweep

- [ ] ScheduleScreen.kt:22,459 — raw `androidx.compose.material3.AlertDialog` bypasses the catalogue; the guard regex does not list Dialog/AlertDialog — replace, and add to `DesignSystemStyleGuardTest`.
- [ ] AdminOpsToolsTab.kt:23 — raw `DropdownMenuItem`; same guard gap.
- [ ] DesignSystemStyleGuardTest.kt — baseline for `shell/ui/ShellGlyphs.kt` is 4 but the file has 0 → lower it (the test's own rule).
- [ ] `I18N` fragment assembly instead of one parameterised resource: AdminIamTab.kt:193,373; ChatPollsCard.kt:256; DiscordScreen.kt:1172; TrustAutomationSection.kt:471; ImpersonationBanner.kt:92; HomeScreen.kt:1871 (`"${len}s"`); EconomyScreen.kt:3630 (hand-built sign); WidgetGalleryReview.kt:169 (placeholder literal).
- [ ] `FEEDBACK` 45 swallowed `ApiResult.Failure -> null/Unit` branches: feature/moderation/state/ModerationController.kt:289,296,327,335,344,352,746; feature/community/state/CommunityController.kt:140; feature/pipelines/state/PipelinesController.kt:206; feature/liveops/state/LiveOpsController.kt:70; feature/economy/state/EconomyController.kt:484,535,565; feature/music/state/MusicController.kt:105; feature/connect/state/ConnectController.kt:457,707; feature/commands/state/CommandsController.kt:151,242; feature/chat/state/ChatController.kt:181,350; feature/shell/state/ChannelSwitcherController.kt:105; feature/eventresponses/state/EventResponsesController.kt:129; feature/timers/state/TimersController.kt:142,196; feature/integrations/state/IntegrationsController.kt:151,167,681; feature/chat/state/MultiChatController.kt:184; feature/codescripts/state/CodeScriptsController.kt:141; feature/vts/state/VtsController.kt:85; feature/tts/state/TtsController.kt:134; feature/home/state/HomeController.kt:151,389,428; feature/settings/state/BillingController.kt:81,112; feature/giveaways/state/GiveawaysController.kt:146; feature/games/state/GamesController.kt:93; feature/obs/state/ObsController.kt:118,123,460; feature/community/state/ViewerProfileController.kt:304; feature/analytics/state/AnalyticsController.kt:249,254,319; 
- [ ] `PAGE` TtsController.kt:84 (pageSize=50), WidgetsController.kt:318 (pageSize=100) — no pager.
- [ ] 18 `!!` on server-derived values: CommunityScreen.kt:304,377; CustomEventsScreen.kt:276; EconomyScreen.kt:1516,1887,2056,2968,3621,3624; ParticipantController.kt:138,221,227,238,325,359; PipelinesScreen.kt:2568; SettingsScreen.kt:2288; ShellScreen.kt:360.
- [x] Clean: no prose `Text("…")` literals; no new raw hex/dp beyond baseline; no `MaterialTheme.colorScheme` direct use; no numbered permission levels; en/nl key parity 5384/5384; no `\'`/`\"`; no GlobalScope; all sampled State types have Ready/Error.
- [ ] Owed (not evidenced): Sleak sibling-primary/accent counts (needs semantic check); English leaking into nl values (byte-identical diff); hide-vs-disable gating sweep; dialog-dismiss-before-result pairing (151 sites); accessibility sweep; `remember` without channel key; DTO duplication + drift vs openapi/v1.json; empty-state presence per screen.

## L16 · Backend rule-compliance sweep

- [ ] `VAR` 80 real `var` uses (excluding embedded JS strings): Api 12 — AdminController.cs:332; CommunityController.cs:308,333,386,415,480,535,736; RewardsController.cs:386,401; ImpersonationBroadcastHandlers.cs:170. Infrastructure 68 across 27 files, incl. LiveWindowResolver.cs:33; BotLifecycleService.cs:194,239; HelixChatProvider.cs:411,441; KickEventSubscriptionWorker.cs:112,127; TimerService.cs:274; ChatPollService.cs:304; GiveawayCodePoolService.cs:278; GiveawayService.cs:647,670,688; AdminSupportService.cs:492,498,506,508; ChannelService.cs:277; ErasureService.cs:518-583 (6); UserService.cs:64,324,349,354,363 (full list: `grep -rIn "[^.]\bvar \w" NomNomzBot.Infrastructure --include=*.cs | grep -vi "jint\|obsbridge\|scripthost"`).
- [ ] License header missing: Application/Widgets/Services/IOverlayPresenceRegistry.cs; all ~145 Migrations.Sqlite migration bodies (e.g. 20260624064552_Initial.cs) carry neither `// <auto-generated />` nor the SPDX header (Designer files do); Infrastructure count pending (see below).
- [ ] Sync-over-async (8): ScriptHostBridge.cs:165,276,288; DirectObsTransport.cs:535; DirectVtsTransport.cs:445; DeploymentModeResolver.cs:43,44; JintVueSfcCompiler.cs:143 (duplicates L11b; ledgered once here as the rule).
- [ ] 15 controllers inject `IApplicationDbContext` directly (rule: repository + IUnitOfWork): AdminController.cs:46, ChannelsController.cs:35, ChatController.cs:45, CommunityController.cs:42, DashboardController.cs:44, DiscordOAuthController.cs:40, IntegrationsController.cs:37, ModerationController.cs:49, PermissionsController.cs:32, RewardsController.cs:36, StreamController.cs:40, SystemController.cs:39, TtsConfigController.cs:40, ViewerDataController.cs:38, WidgetTestEventController.cs:37.
- [ ] `I18N` ModerationController.cs — 9 `Result.Failure("<literal>")` (the only controller with literal failures; `grep -n 'Result.Failure("'`); 4 Infrastructure files call `chatProvider.SendMessageAsync` directly — confirm each goes through the composer.
- [ ] Git: 2 commits with `Co-Authored-By`, 1 with `Claude-Session` trailers slipped past the hook (rule: never) — rewrite only if unpushed; otherwise leave and keep the hook.
- [ ] `NoMercyBot` remains only in comments + the legacy DB locator path (DefaultLegacyDatabaseLocator.cs:16,30; SubscriptionEventHandlers.cs:98; TtsDispatchService.cs:654) — acceptable, but CLAUDE.md should carve out the legacy-locator exception explicitly.
- [x] Clean: no numbered levels in Api/Application text; `ExecuteDeleteAsync` only on non-soft-delete entities (AlertQueueService.cs:158, KickWebhookIngest.cs:137); `.Remove()` calls are interceptor-soft-deleted by design.
- [ ] Owed: per-entity "saved but never read" config sweep; surface-only test sample; Helix/EventSub coverage diff against the official reference.

## L17 · Uncovered areas + built-but-unreachable inventory

- [ ] `DEAD` ComplianceController.cs — admin-side GDPR plane (`GET compliance/erasure/preview`, `GET compliance/erasure`, `GET compliance/export`, `POST compliance/erasure`) has no dashboard UI → a viewer's GDPR request arriving by email cannot be actioned anywhere.
- [ ] `DEAD` SdkController.cs:59-73 — `GET sdk/event-catalog` (wire name + tier + JSON Schema per event) never called; the editor only fetches `types.d.ts` (SdkTypesApi.kt:28) → no event browser / docs panel.
- [ ] `DEAD` AdminController.cs:367 — `POST admin/security/rotate-encryption-key` has no UI (also L10).
- [ ] `DEAD` IpcDevModeController.cs:53,64,75,101 — IPC key CRUD with no UI (confirm intended CLI-only).
- [ ] `DEAD` PlatformAnalyticsController.cs:32 — `GET platform/analytics/stats` no consumer.
- [ ] `DEAD` AdminTrustSafetyController.cs:51 — `GET admin/trust-safety/cross-tenant-signals` no consumer.
- [ ] MyDataController.kt:104-111 + GdprController.cs:109 + GdprBuiltins.cs:82-121 — erasure scope always "deployment"; `RequestErasureRequest.Scope` never selectable.
- [ ] Orphaned KDoc after a mechanical refactor: PickListsController.kt:101-115, CustomEventsController.kt:99-116, CodeScriptsController.kt:403-420 ("Delete a X" comment sits above `fetchBlastRadius`).
- [ ] tools/streamdeck — not re-verified against the current `automation/v1/music/*` routes (AutomationDataController.cs); diff owed.
- [x] Correctly not app-called (external/browser surfaces): AutomationDataController `automation/v1/*`; AutomationPairingController approve page; Billing/Discord/Kick/Inbound webhook receivers; Discord/Integration OAuth callbacks; OAuthRelay / ObsBridgeHost / OverlayHost / OverlaySdk / OverlayTicket / OverlayVueRuntime / VoiceListenerPage / VoiceTriggerReport pages; `api/v1/overlay/*` (overlay runtime); PublicSongRequestController (viewer page — but see L13: no page serves it).
- [x] Clean: feature/emoji; feature/connect (saved connections, mDNS, keychain, restore); picklists/customevents flows; DEPLOY.md/README/.env.example ports+URLs consistent; openapi spot-check (GDPR, Compliance, PickLists, SDK) 1:1 with controllers. Method: 816 actions extracted, 54 with no textual caller, triaged above.

## L16b · Counts closed by the orchestrator

- [x] Infrastructure hand-written .cs missing the SPDX header: 0.
- [ ] Infrastructure `Platform/Persistence/Migrations/*.cs` missing `// <auto-generated />` + header: 257 files (Postgres set) — plus ~145 in Migrations.Sqlite (L16). One scripted pass fixes both.
- [ ] Migration parity (129 Postgres vs 141 SQLite migration bodies): SQLite-only names are expected (`FixSqliteDateTimeOffsetTicksConversion`, `AddGuidNocaseCollationSqlite`, ten early split-outs, `AddRedemptionTimers`+`AddChatTriggers` vs the merged Postgres one). **`AddRewardIsUserInputRequired` exists only in the Postgres set** — confirm the SQLite snapshot carries the column (else self-host lite lacks `Reward.IsUserInputRequired`).
