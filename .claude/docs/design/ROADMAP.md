# NomNomzBot — Roadmap to Fully-Featured (Pending Work)

Handoff for the next session. **Point me to a phase or a line item** (e.g. "do Phase 1 → Moderation", or "the command/reward rebuild") and I'll execute it as committed, validated slices.

**Rules for every item:** ground in the spec + `aitm recall` first; TDD; shadcn + design tokens only; DRY/YAGNI/KISS; explicit types (no `var`); AGPL headers; csharpier + build green (TreatWarningsAsErrors); **verify the rendered client, not 200s**; every floor maps to an already-seeded permission key (`roles-permissions.md` §7.1) — no new permissions; one agent at a time on the shell; **never** two backend agents concurrently; commit each validated slice; never raw-dump or shortcut.

---

## ✅ Done & committed (verified 2026-06-26)

All items below were verified against live source code, not the ROADMAP doc.

**Foundation:**
- 21-page nav shell with all 9 NavGroups (Home/Chat/Moderation/Loyalty/Music/Stream/Community/Connect/Setup) — matches `frontend-ia.md §3` exactly; `ShellNav.kt` is up-to-date
- Role-correct views + per-action write-gating (ManageGate); broadcaster/editor/mod/viewer enforced
- Participant rung (3-rung shell: management/participant/public)
- OAuth redirect uses the access origin (Spotify/tunnel fix)
- Branded connect modal, BYOC credential card, route persistence
- Shell scroll/resize fix; responsive shell

**Event engine:**
- 33k+ legacy events imported (real Twitch ids, 435 viewers; idempotent CLI `--run-legacy-import`)
- `LegacyChannelEventMapper` covers: chat, follow, subscribe, resub, gift, cheer, raid, mod add/remove, ban, redemption, redemption update, channel update, shoutout create/receive, ad break, poll begin/progress/end, hype train begin/progress/end, prediction begin/progress/lock/end, stream online/offline
- `AppendBatchAsync` bulk-inserts + bulk block-allocate (was per-row) — journal-write perf ✅
- `CurrencyBalanceProjection`, `RewardRedemptionProjection`, `WatchStreakProjection`, `OutboundWebhookFanoutHandler` — all exist and are auto-discovered
- `ChatMessagePersistenceHandler` — live chat messages now written to `ChatMessages` (fixes 0-message community stats)
- `ChatEarningHandler`, `EngagementEarningHandler` — follow/sub/cheer/raid/chat triggers currency earning (economy was always 0, now wired)
- `EarningRuleSeedOnOnboardingHandler` — 7 default earning rules seeded disabled per channel (opt-in from Earning Rules page)
- EF `Timer.PipelineId1` shadow FK warning resolved

**Command/Pipeline Big Rock (Slices 1–7+):**
- Re-keyed Command and Pipeline to Guid (backend + Kotlin DTOs)
- `Command.PipelineId` binding; fail-CLOSED engine (unknown action/condition = block)
- `ICommandConfigValidator` broker invariant; `IBuiltinCommandCatalog`; `DefaultCommandsSeeder` seeds `!sr`, `!skip`, `!queue`, `!volume`, `!song`
- Kotlin API contract drift fixed: PipelinesApi, TimersApi, CommandsApi all use String (Guid) ids

**Phase 1 — ALL management surfaces built and wired:**
- Moderation: bans, unban, mod-log, shield-mode, blocked-terms, automod, rules CRUD, performAction, stats, announce
- Economy: currency config, earning rules, store/catalog (create/delete/toggle), savings jars, leaderboard, account admin (adjust, freeze), catalog purchases/refund
- Rewards: list/create/update/delete/sync + redemption queue/fulfill/refund
- Widgets: list/enable/disable/delete/create/rename/clone
- TTS: config, update-config, voices, test-speak
- Discord: connections, configs CRUD, config preview, roles CRUD, role-button, server consent, dispatch-log
- Song Requests: queue, skip, pause, resume, remove, config, SR-page token, rotate token
- Music: queue, skip, pause, resume, remove, config, shuffle, repeat, seek, transfer, devices, playlists, play-context

**Phase 2 — ALL controller pages built:**
- GDPR export/erase, rebuild-projections (EventStore / `JournalPortabilityController`)
- Code editor under Commands (`CodeScriptsScreen`)
- Features toggles, Webhooks, Federation pages (all wired in ShellScreen)
- Quick returning login (device-code flow, `restoreSession()` in App.kt)

**Phase 3 — ALL specced quick-actions built:**
- Group A: polls, predictions, raids, ads (commercial + snooze), clips — `HomeScreen` + `LiveOpsController` ✅ DONE
- Group B/C: announcements (ModerationScreen), VIP add/remove + shoutout (CommunityScreen) ✅ DONE
- Group E: Spotify seek/shuffle/repeat/transfer/devices/playlists (MusicScreen) ✅ DONE

**Profile self-service (2026-06-26):**
- `GET /system/pronouns` (anonymous) + pronoun picker in MeScreen ✅ DONE
- `UserService.GetStatsAsync` message count bug fix ✅ DONE
- `ChatMessagePersistenceHandler`, `ChatEarningHandler`, `EngagementEarningHandler` ✅ DONE

**Pronoun provider — alejo.io integration (2026-06-28):**
- `Pronoun.Key` + `User.AltPronounId` + `User.PronounManualOverride` schema fields ✅ DONE
- `IPronounProvider` / `AlejoPronounProvider` — catalog + per-viewer `/v1/users/{login}` lookup ✅ DONE
- `IPronounResolutionService` — lazy 24h cache-gated auto-apply on chat events ✅ DONE
- `IPronounSelfService` — viewer GET/PUT `/pronouns/me` with alt pronoun + manual-override ✅ DONE
- `PronounsController` — `GET /pronouns/catalog` (anon) + `GET/PUT /pronouns/me` (auth) ✅ DONE
- `PronounsApi.kt` + `UserPronounResponse`/`SetPronounBody` DTOs in Kotlin dashboard ✅ DONE
- `{{user.pronouns}}` template helper uses AltPronoun badge when set ✅ DONE

**Hub event consumers — all 8 types (2026-06-28):**
- `ChatMessage` → ChatController (live feed)
- `StreamStatusChanged` + `ChannelEvent` → HomeController (live/offline badge + activity feed)
- `MusicStateChanged` → MusicController (now-playing updates)
- `RewardRedeemed` → RewardsController (redemption queue ticks up live)
- `ModAction` → ModerationController (mod log prepends on ban/timeout/unban)
- `CommandExecuted` → CommandsController (use-count increments live)
- `AlertTriggered` → ShellScreen frame-level error toast (integration disconnected, etc.) ✅

**Full pipeline catalogue (2026-06-28):**
- Expanded PipelineCatalogue.kt from 12 → 23 action types covering all backend ICommandAction implementations; PipelinesScreen blockLabel()/fieldLabel() exhaustive switches + EN+NL strings updated ✅

**Sidebar accordion nav (2026-06-28):**
- Replaced `LazyColumn` with accordion (`Column`, click-to-expand group labels) — all 8 groups fit on screen without scrolling; active group auto-expands; Lucide `ChevronDown`/`ChevronUp` icons in `ShellGlyphs.kt` ✅

**EventSub subscriptions + channel white-label bot (2026-06-27):**
- `TwitchDiagnosticsApi`: `subscriptions(channelId)` + `reconcile(channelId)` ✅
- `ChannelsApi`: `channelScopes` / `startChannelBotConnect` / `channelBotStatus` / `disconnectChannelBot` ✅
- `IntegrationsController`: loads EventSub subscriptions on refresh; `reconcileEventSub()` ✅
- `IntegrationsScreen`: `EventSubSubscriptionsSection` card — subscription list + Reconcile button ✅
- `ChannelBotController` + `ChannelBotSection` in SettingsScreen — connect/disconnect/scopes ✅
- All 23 `FakeChannelsApi` + `FakeTwitchDiagnosticsApi` test fakes updated; 351 tests green ✅

**Viewer analytics + analytics privacy (2026-06-27):**
- `AnalyticsApi`: daily trends, top viewers, viewer profile, watch streak, opt-out ✅ DONE
- `AnalyticsScreen`: daily trends table + top viewers ranking section ✅ DONE
- `MeScreen`: WatchStreakCard (current/best), AnalyticsPrivacyCard (opt-in/out toggle) ✅ DONE
- Community leaderboard (top-chatters) surfaced in CommunityScreen ✅ DONE

**Event Responses + Games history (2026-06-27):**
- `EventResponsesApi` + `EventResponsesController` + `EventResponsesScreen` ✅ DONE — maps Twitch channel events (follow/sub/cheer/raid/stream.online…) to bot reactions; toggle rows + edit dialog; Moderator+ reads, Editor+ writes; EN+NL i18n; unit tests
- `GamesApi.history()` + `GamesApi.revokeConsent()` + `HistoryRow` on `GamesScreen` ✅ DONE — play-history section visible to Moderator+

---

## Phase 0 — Remaining stabilization

- **#15 participant backend gaps** — ✅ all done: `ids` on `GET /users/{id}/channels`; self community-standing endpoint; `/me` economy-account alias (`accounts/me`); `pronouns:self:write` via `PUT /users/{id}/profile` (verified 2026-06-27).
- **#11 unify client-setup** — ✅ stale undecryptable secret fix done (57cd6bf); credential component DRY unification still deferred.

**Client quality fixes (2026-06-28):**
- `ApiClient` catches `Exception` (not just `SerializationException`) for WasmJs dev-mode JSON crashes; `CancellationException` re-thrown ✅
- HomeScreen `QuickActionButton` icon token `s5` → `s6` (s5 doesn't exist) ✅
- Webpack devServer port 8081 (only port in CORS allowlist) ✅
- `LocalClipboardManager` deprecation suppressed (CopyButton + SongRequestsScreen) ✅
- `@file:OptIn(ExperimentalWasmJsInterop)` on RouteStore.wasmJs.kt ✅
- `HomeControllerTest`: missing `commandsApi` arg fixed + 2 new tests (topCommands sort + graceful failure) — 353 JVM tests green ✅

---

## 🪨 Big rocks (cross-cutting, high-value)

- **Multi-channel role-aware** — ✅ `ChannelSwitcherController` + `SessionStore.activeChannelId` + sidebar picker (2026-06-27). ✅ `/effective/me` re-resolution on channel switch (2026-06-27): `LaunchedEffect(activeChannelId)` in App.kt re-gates management surface immediately on switcher selection. `Provider` discriminator on `Channel` + per-channel `/effective/me` re-resolution for individual page controllers (they still call `primaryChannel()` independently) still deferred.
- **i18n completeness** — ✅ en + nl string files are in sync (1260 keys each, 2026-06-27). No missing translations.
- **SaaS billing / GDPR (Plane-C)** — ✅ Billing tab in Settings (`BillingApi.kt` + `BillingController.kt` + `BillingSection` in SettingsScreen); Admin area (`AdminApi.kt` + `AdminController.kt` + `AdminScreen.kt`, 6-tab: Overview/Channels/Users/System/Feature Flags/Billing — isAdmin-gated sidebar entry + screen). `SubscriptionDto`/`TierDto` etc. now have `ProducesResponseType` → in OpenAPI spec; `ApiContractTest` passes (2026-06-28).
- **OutboundWebhookFanoutHandler** — ✅ backend WebhooksController + InboundWebhookController + WebhooksScreen all complete; the "placeholder" label was stale.
- **Built-in commands in dashboard** — ✅ `BuiltinsApi` + `BuiltinsController` wired; Commands page shows built-in section with enable/disable toggle (2026-06-27).
- **EventSub missing-scope detection** — ✅ 403 from EventSub now parses "Missing required scope" and fires `TwitchHelixReauthRequiredEvent` → dashboard shows action-required (2026-06-27).
- **EventSub topic coverage** — ✅ 18 topics subscribed; 3 blocked by missing `channel:read:hype_train` scope (resolves on re-auth after user grants new scope).
- **Command → pipeline attachment** — ✅ `CommandListItem` now returns `templateResponse` + `pipelineId`; `CreateCommandBody`/`UpdateCommandBody` send both; `CommandFormDialog` has pipeline selector dropdown; edit form seeds response from `templateResponse` (not description) (2026-06-27).
- **Settings channel management** — ✅ Join/Leave/Reset config actions in Settings page, Broadcaster-gated with confirm dialogs (2026-06-27).
- **API coverage audit** — ✅ Complete. All 19+ controllers fully wired, including delete-channel, community top-chatters leaderboard, EventResponsesController, GamesApi history + revokeConsent, EventSub subscriptions (reconcile + list), ChannelBotController (connect/disconnect/scopes) added 2026-06-27. Only remaining gap is SaaS Plane-C (billing/admin).
- **Settings delete-channel** — ✅ ConfirmDialog + `deleteChannel()` → `SettingsState.ChannelDeleted` → `onChannelDeleted()` → re-onboarding (2026-06-27).
- **Community top-chatters leaderboard** — ✅ `GET /community/top-chatters` surfaced as LeaderboardCard on CommunityScreen (2026-06-27).

---

## Grounding sources (read first, per slice)
`frontend-ia.md` (IA), `roles-permissions.md` (floors §7.1), `commands-pipelines.md`, `music-sr.md`, `moderation.md`, `webhooks.md`, the economy specs, `event-store.md`. Re-run the parity audit if the controller↔surface map is stale. `aitm recall` always.
