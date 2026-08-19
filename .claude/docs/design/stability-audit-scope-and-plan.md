# Chat-reply mentions & enable/disable reliability — scope and plan

Status snapshot as of this audit. Two confirmed bugs are already fixed and committed
(`85e7e3ee`, `de582e91`); everything else below is scoped but not started.

## 1. Findings (what's actually wrong)

### 1a. `@username` double-mention — FIXED (`85e7e3ee`)
`ToneTemplateCatalog.cs`, the 4 flavored personality tones (Sassy/Hype/Friendly/Chill)
for `!sr` Added/NotFound hardcoded `"@{user} ..."` even though those responses always go
out as a Twitch-threaded reply (`reply_parent_message_id`), which already renders
"Replying to @user". Informative tone and `SongRequestBuiltin`'s own neutral fallbacks
were already correct — only the 8 flavored-tone strings had it. Fixed, mutation-tested,
merged.

**Not yet swept:** whether the same pattern exists anywhere outside
`ToneTemplateCatalog.cs` — custom command templates, quote responses, mod-action
messages, cooldown messages, or any other place a user composes free-text that later
gets sent through `SendReplyAsync`. This needs a targeted grep across
`Application/Commands/**` and `Infrastructure/**/PipelineActions/**` for `@{{user` /
`@{user}` / string-built `"@" + user` patterns, cross-referenced against which of those
call sites actually go through the reply-threading path vs. plain `SendMessageAsync`.

### 1b. Dead `Reward.IsEnabled` flag — FIXED (`85e7e3ee`)
`RewardRedeemedHandler` never checked `reward.IsEnabled` before running the bound
pipeline / `Response` text / generic event-response fallback — disabling a reward in the
dashboard did nothing at the bot-execution layer. Fixed, mutation-tested, merged.

### 1c. `ConfigChanged` / `RewardChanged` SignalR events never wired on the frontend — FILED, not fixed
`DashboardNotifier.SendConfigChangedAsync` / `SendRewardChangedAsync` are called by 9
backend services (Commands, Timers, Pipelines, Webhooks in/out, Economy, Moderation,
TTS, Widgets, Rewards) on every create/update/delete/toggle, but `HubEvent.kt` has no
case for either method name — every one of those live-update pushes is silently dropped.
Effect: toggling/editing any of those 9 surfaces in one open dashboard session does not
live-update a second open session for the same channel; only a manual reload picks it
up. This is `app/` scope (frontend track), filed as a full work order in
`handoff/for-frontend.md` (2026-08-19 entry) with exact file/line targets and acceptance
criteria — not something the backend track fixes directly.

### 1d. `IsEnabled` sweep across the rest of the backend — DONE, 22/23 clean
Every `IsEnabled`-bearing entity besides the ones already covered in a prior pass
(Command, ChannelBuiltinCommand, ChatTrigger, EventResponse, Pipeline, PipelineStep,
Timer, Widget — all confirmed live-gated) was swept:

| Entity | Verdict |
|---|---|
| CodeScript | live-gated (`ScriptRunner.cs:50`) |
| ChannelFederationOptIn | live-gated (`FederationOptInService.cs:124-156`) |
| CustomDataSource | live-gated (`CustomDataIngestService.cs:54`, `CustomDataPollService.cs:76,161`) |
| CatalogItem | live-gated (`CatalogService.cs:236`) |
| CurrencyConfig | live-gated (`CurrencyAccountService.cs:125,192`) |
| GameConfig | live-gated (`LiveGameEngine.cs:80`, `GameService.cs:201,441`) |
| EarningRule | live-gated (`CurrencyEarningService.cs:51`) |
| MediaShareConfig | live-gated (`MediaShareService.cs:66`) |
| ObsConnection | live-gated (`ObsTransportRouter.cs:81`) |
| ChannelFeature | live-gated (`FeatureService.cs:166`) |
| OutboundWebhookEndpoint | live-gated (`OutboundWebhookDispatcher.cs:59`) |
| SupporterConnection | live-gated (`SupporterIngestService.cs:67`) |
| InboundWebhookEndpoint | live-gated (`InboundWebhookDispatcher.cs:59`) |
| SoundClip | live-gated (`SoundClipService.cs:273,277`) |
| **Reward** | **was dead — fixed in `85e7e3ee`** |
| FeatureFlag (IsEnabledGlobally) | live-gated (`FeatureFlagService.cs:95,150`) |
| FeatureFlagOverride | live-gated (`FeatureFlagService.cs:85`) |
| HttpEgressAllowlist | live-gated (`CustomDataPollService.cs:157-168`) |
| VtsConnection | live-gated (`VtsTransportRouter.cs:56`) |
| TtsConfig | live-gated (`TtsDispatchService.cs:101`) |
| ModerationEscalationPolicy | live-gated (`ModerationEscalationService.cs:51`) |
| ChatFilter | live-gated (`ChatFilterExecutionHandler.cs:70`) |
| IpcDevModeKey | live-gated (`IpcDevModeService.cs:60,143`) |

No further dead flags found in the backend. Sweep complete — no open item here.

### 1e. Cache-invalidation write paths — DONE, no gaps found
`ChannelRegistry` (in-memory `ConcurrentDictionary` cache for commands, builtin
toggles, chat triggers, sound triggers) — every write path that flips these flags
(`CommandService`, `BuiltinCommandService`, `ChatTriggerService`) calls the matching
`Invalidate*Async` immediately after `SaveChangesAsync`, no gap. Timers and event
responses read `IsEnabled` fresh from the DB on every tick/fire — no cache exists for
them, so nothing to invalidate. No TOCTOU race found in the examined write paths (all
mutation + save + invalidate happen synchronously, no interleaved await). No open item
here.

## 2. Plan — remaining work, in order

1. **`@mention`-in-reply sweep — DONE, no further bugs found.** Grepped
   `server/src` for every `@{{`, `@{user}`, and string-built `"@" + name` pattern.
   Results: `SongRequestAction.cs` / `SongWrongAction.cs` (pipeline actions) hardcode
   `@{ctx.TriggeredByDisplayName}` but send via plain `SendMessageAsync` — no native
   threading to double up against, correct as-is. `SendReplyAction.cs` (the
   user-configurable "send reply" pipeline block) takes its message body from the
   broadcaster's own template — not a code-owned string, so not an audit target; if a
   streamer types `@user` into their own reply template that's their choice.
   `chat_box.vue` renders a `@mention` span for an already-received chat message in an
   overlay widget — display of incoming chat, not an outbound bot message. No other
   `@{{user` occurrences exist anywhere in `server/src`. `ToneTemplateCatalog.cs` was
   the only source of this bug; nothing else to fix.
2. **Frontend fix for `ConfigChanged`/`RewardChanged`** — owned by the frontend track,
   already filed with concrete acceptance criteria in `handoff/for-frontend.md`. No
   backend action.
3. **Nothing further identified** for the enable/disable round-trip (write → persist →
   invalidate → runtime-read) across commands, event responses, and timers — the
   mechanism is sound end-to-end for every surface checked.

No backend items remain open from the original ask. The only outstanding piece is the
frontend fix in item 2, which is filed and out of this track's commit scope.
