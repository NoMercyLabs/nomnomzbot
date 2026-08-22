# Shortcomings execution plan — one ordered ledger over all three audits

Sources (findings live there; this file only orders them):
- **S** = `stability-audit-scope-and-plan.md` (F1–F19, F11b)
- **W** = `widget-quality-audit-scope-and-plan.md` (§1–§8; §5 verdict table; §8 items 1–11)
- **U** = `usability-shortcomings-audit-scope-and-plan.md` (A1–A7, B1–B7)

Rules of execution: one slice = one validated vertical cut, test-first, committed when proven
(`CLAUDE.md` workflow). Work top to bottom; inside a slice, the order is the bullet order. A slice
is done when every **Done-when** line is true; then delete it from here (tracker = remaining work
only). Owner-gated items are marked 🔒 and skipped until the owner answers.

Priority logic: (1) silent data loss / money / viewer-facing lies, (2) things that make a whole
feature fictional, (3) the generic form infrastructure that every "raw text box" finding rides on,
(4) feature reach (built backend, no UI), (5) polish.

---

## Tier 1 — silent loss, money, lies to viewers

### 1.1 Song-request queue is fictional
- U·B4 bullet 1: `IMusicService` Scoped with queue in a field → singleton queue store (or persist).
- U·B4 bullet 2: provider `AddToQueueAsync`/skip bool discarded; NO_ACTIVE_DEVICE swallowed; search `[]`
  on auth failure → distinct error codes + chat replies.
- U·A2 items 1–2: Spotify 401/403 → visible `needs_reauth`/`forbidden` state; drop the `Services`
  mirror dependency (vault is the one source).
- Done-when: `!sr` → `!queue` → `GET /queue` agree across requests; a revoked Spotify token shows on the
  Integrations card and `!sr` replies with the reason; tests prove each.

### 1.2 Economy races and game money
- S·F11 + F11b: atomic balance update (`CurrencyAccount`, `SavingsJar`).
- S·F12, F13: earning-rule unique index; escalation atomic increment.
- U·B2 bullet 5: `LiveGameEngine` settlement failure → refund or retryable failed state; joiner-can't-pay
  feedback; `LiveGameRunner` consecutive-failure force-cancel+refund.
- Done-when: concurrency tests for all three entities; a forced settle failure refunds every stake.

### 1.3 Pipelines tell the truth
- S·F1: `CommandConfigValidator` on `PipelineService` create/update.
- S·F4 + U·A1 item 1: thread real send outcome; broken-out run = PartiallyFailed, invoker gets one
  reply; cooldown/permission rejections reply (invoker-only, default on).
- U·A1 items 2, 3, 5: shoutout skip not a silent Success; `start_raid` fires first, tolerates
  already-raiding, live pre-check, lookup-vs-not-found, `missing_scope` → re-grant flow, publishes
  `RaidSentEvent`; strip `@` from `{args.N}`.
- U·B1 "Dead config": `PrefixMode/CustomPrefix/MatchMode/MatchPattern` wired or removed; `overlay`
  response type implemented or removed; `Pipeline.IsEnabled` honoured in registry/timer/executor.
- U·B1 "Stale cache" + S·F3: pipeline create/update/delete invalidates command + chat-trigger caches;
  timer fire resolves `PipelineStep`-first.
- Done-when: a pipeline with a failing middle step reports PartiallyFailed and replies; raid preset
  (Tier 4.1) depends on this; editing a pipeline changes the next command run without reconnect.

### 1.4 Moderation accidental-ban + reach of what's built
- U·B3 "Raw text": timeout duration presets + hard block on unparseable (no more null → permanent
  ban); rule/escalation durations validated; nuke reason/matchTerm + display name; community Ban gets
  reason + timeout option; quote speaker = viewer picker.
- U·B3 "Feedback": announcement result; approve-unban confirm; `afterWrite` errors in every state;
  Warn keeps reason on failure; availability flags for escalation/shared-ban/nuke cards; per-viewer
  reads show errors.
- S·F17, F18, F19 + U·B3 "Backend": regex save-time check + tester; filter-conflict warning; Helix
  already-actioned handling; empty allow-list warning; invalid regex not silently literal; stats not
  `Contains("ban")`; `WarningAcknowledgedEvent` handler.
- Done-when: an empty timeout field cannot produce a ban (test); every destructive dialog captures a
  reason; moderation stats match the journal.

## Tier 2 — runtime stability (silent-failure class)

### 2.1 EventSub + hubs
- U·B7 bullets 1–3: backoff on clean-close reconnect; `ReconnectAsync` re-opens every owner session;
  handlers for `EventSubRevokedEvent` (→ `needs_reauth` + dashboard notice) and the two other
  unhandled events; publish `EventSubDisconnectedEvent`; per-owner subscription count; `WaitAsync`
  leak.
- U·B7 bullet 4: `WithStatefulReconnect()`; backplane decision 🔒 (single-instance today —
  document the limit or add Redis backplane + pub/sub for `ChannelRegistry`/`IEventBus`).
- U·B5 last two bullets + S clean list: OverlayHub one-widget-per-connection → set; token not in
  query string / throttle bad tokens.
- Done-when: mock-Twitch close 4003 → exponential backoff observed; revoking at Twitch flips the
  connection status within one tick.

### 2.2 Tokens, workers, DB
- U·B7 bullet 5: per-connection refresh lock (Twitch/Kick/YouTube); refresh sweep runs at boot and
  covers every provider (or the lazy contract is documented + tested).
- U·B7 bullet 6: four workers' delay moved out of the try (YouTube poll, scheduled-pipeline expiry,
  TimerService, redemption-timer expiry).
- U·B7 bullets 7–10: Redis `abortConnect=false` + degrade; health check pings the singleton; SQLite
  WAL + busy timeout; `EnableRetryOnFailure` both providers; `UnitOfWork` nested-begin guard +
  disposable; delete `DatabaseHealthCheck.cs`.
- S·F2, F5, F8 + U·B1 "Timer runtime": interval floor/ceiling; `LastFiredAt` advances on failure; stamp
  on enable/create; `MinChatActivity` snapshot seeded; prune dead timers; rename uniqueness.
- S·F9, F10, F7, F6: atomic cooldown try-acquire (DB write-through 🔒); unique index or ordering on
  `EventResponse(BroadcasterId, EventType)`; builtin-name + alias collision checks; unresolved
  template vars validated on save (needs helper registry, Tier 3.1).
- S·F14, F15, F16: SSML-escape voiceId; TTS per-channel volume cap; webhook flatten depth cap.
- Done-when: a throwing tick sleeps its interval (test per worker); two concurrent refreshes produce
  one Twitch call; SQLite under 25 services shows no "database is locked" in a soak.

## Tier 3 — generic form infrastructure (everything "raw text where a picker belongs" rides on this)

### 3.1 Helper registry + "All helpers" dialog
- U·A7 items 1–3; W·§6 "No variable-picker" + W·§8 item 7; W·§6 "catalogue gaps" (math/string/date
  namespaces, general `{{ns.key:arg}}` grammar, any-step output) + W·§8 item 9; W·§6 dead
  `{{stream.viewers}}`; S·F6 validate-on-save uses the same registry.
- Done-when: `GET /templates/helpers?context=` returns the full valid set per entry type; every
  template field in commands/event-responses/timers/rewards/pipelines/chat-triggers/giveaways/Discord
  opens the same dialog; en + nl descriptions; save rejects unknown keys.

### 3.2 Action catalogue field schema + resource pickers
- U·A6 item 5 + U·A5 item 3: backend `PipelineActionDescriptorDto` carries fields/kinds/options;
  new kinds `number` (rendered numerically — U·B1 "Authoring" bullet 1), `segment`/repeatable,
  resource pickers (`discord_channel`, `discord_role`, `twitch_user`, `reward`, `widget`, `voice`,
  `sound_clip`, `asset`); step form renders them.
- U·B1 "Authoring": regex compile check in chat-trigger dialog; helper insert + create-and-bind
  pipeline everywhere; timer picker/interval presets; show `LastFiredAt`/next index; command rename;
  `code` tier links to Code Scripts.
- U·B1 "Runtime ordering": chat-trigger order column + `continue` on cooldown.
- W·§6 branching + W·§8 item 6: expose `ParentStepId`/`Branch` in the step dialog.
- W·§6 test-run + W·§8 item 2: wire `POST pipelines/{id}/test-run` as a dry-run button on
  pipelines/commands/event-responses/timers.
- Done-when: no `FieldKind.Text` is used for an id/enum/number anywhere in the catalogue; dry-run
  button shows captured side effects.

### 3.3 Feedback + hub events baseline
- U·B1 "Feedback": `Feedback` injected in timers/chat-triggers/event-responses; event-response Delete
  = reset or no re-seed; seed moved out of the GET.
- U·B2 bullet 1: `HubEvent` cases for reward lifecycle + redemption status; pending queue removes on
  fulfil/refund. S·§1c `ConfigChanged`/`RewardChanged` (handoff) closes with this.
- U·B6 hub-state indicator in the top bar (`DashboardHubClient` exposes state); reconnect banner
  shows the reason; `effectiveMe` transient failure = retry state, not viewer surface.
- Done-when: every save surfaces saved/live; a fulfilled redemption leaves the queue without reload;
  a dead socket is visible within 5 s.

## Tier 4 — feature slices (spec first where marked)

### 4.1 Raid preset (after 1.3)
- U·A1 item 4: first-party "Raid helper" preset — shoutout → Helix raid → countdown messages →
  optional OBS scene/stop + Spotify pause (toggleable); seed `channel.raid.out` on onboarding.
- Done-when: `!raid <user>` from a fresh channel runs every step or names the failing one.

### 4.2 TTS slice (spec amendment in `spec/tts.md` §6 first)
- U·A3 items 1–4: system TTS surface (IsSystem widget or channel-level route) with ordered audio
  queue; TTS page shows URL / last-seen / test-through-overlay / queue controls; stale comment fix.
- U·A4 items 1–4: overlay SDK sets `utter.voice`/`lang`; search matches Id + Locale, case-insensitive
  exists; no hardcoded-Aria fallback / keyless-Azure preference; precedence doc = code.
- U·A5 items 1–5: segment-list `play_tts` with per-segment voice mode; one `tts_speak` payload with
  ordered segments; owner's sub-streak preset; `BypassQueue` implemented or removed.
- W·§5 `tts_caption` nit (numeric speaker id → display name) + W·§8 item 10 share.
- Done-when: a sub-streak redeem plays "announce (default voice) + 'they also said:' + message (user's
  voice)" as one utterance in OBS; `!voice en-US-AriaNeural` and `!voice aria` both work.

### 4.3 Discord go-live (spec additions in `spec/discord.md` first)
- U·A6 items 1–4, 6: rule editor pickers (channel/role via `GuildPickerField`, trigger dropdown, ping
  role, embed, helper link + preview, names in list); `go_offline` + `hype_train` triggers with
  handlers; action carries own channel/template/embed/ping role; live-role sync feature; Event
  Responses Discord preset.
- Done-when: "go live → #announcements + roles A,B,C; offline → roles removed; hype train → #hype"
  configurable without typing an id.

### 4.4 Widget runtime correctness (W plan)
- W·§1 + §8 item 1: field-name fix across `recent_followers`/`top_cheerers`/`sub_train`/`goal_bar`/
  `labels`/`alerts`/`event_ticker`; route goal events to `IWidgetNotifier`; contract test;
  `redemption_alert` sound wired or removed; `socials` help text = parser.
- W·§3 + §8 item 3: editor fire-bar sends real per-event samples (`WidgetTestSamples`), chat variants.
  U·B5: fire-bar events from subscriptions not regex; desktop editor gets the fire bar.
- W·§2 + §8 item 4: `chat_box.vue` layout batch (line break, truncation, avatar, contrast, emote
  size, arrival animation, mention highlight).
- Done-when: every widget in W·§5 table reads "solid" against real DTOs (contract test green).

### 4.5 Widget setup experience (U·B5)
- Per-widget tokens + staged rotation + post-rotate URL list (BUILD-TODO items); test button on the
  row (`WidgetTestEventController`); inline preview; `lastRuntimeError`/`lastRanAt` badge; overlay
  last-seen (hub stamps, `WidgetConnectedEvent`); in-overlay banner on rejected token, stop looping;
  state-preserving resume instead of `location.reload()`.
- Settings form by schema availability; colour picker; asset/sound/font field types; explicit
  unsupported-type + invalid-value errors; editable event subscriptions; gallery version/update state
  + search/paging; sound upload limits shown.
- Done-when: add widget → copy URL → test → see it live, all from one row; rotating a token lists
  every URL to re-copy.

### 4.6 Rewards · economy · giveaways reach (U·B2)
- Reward `Response` field in create + update; `ActionType/ActionSettings` exposed or deleted.
- Catalog item full form + edit (PATCH); leaderboard config CRUD + opt-outs + display names; jar role
  dropdown + jar update/delete.
- Giveaway eligibility/weighting/prize pipeline in dialog; `ClosesAt` auto-close or drop; code
  labels; entries endpoint + list; pool picker guard; null-as-empty reads → errors; rewards poll
  backoff.
- Done-when: the store can sell an item with an effect, stock and cooldown from the UI; a weighted
  sub giveaway can be run end to end.

### 4.7 Moderation reach (U·B3 "Built, unreachable")
- Chat-filters screen on the Moderation page; AutoMod settings section; AutoMod held-message queue
  on the chat screen; mod add/remove endpoints + UI; clear chat; full `AutomodConfigDto` (per-filter
  action/duration/min-length/regex/exempt roles); concurrency guard on whole-config POST.
- Live chat: multi-chat moderation + composer; `joinedChannels` preserved on reconnect; watch list
  persisted; mod-log pushes with names + time; shield pushes consumed; chat-settings
  slow/followers/unique/non-mod fields.
- Done-when: a mod can approve/deny a held message from the dashboard; multi-chat can time out.

### 4.8 Music UX (U·B4, after 1.1)
- Admission enforces `MaxQueueSize`/`MaxRequestsPerUser`/`MinTrustLevel`/`AllowX`/`PreferredProvider`/
  `IsEnabled`; trust gate used; one config editor; queue promote/ban-track/refund; cost/max-duration/
  per-user-cooldown fields 🔒 (spec); public `/sr/` page built or affordance removed; token → URL;
  bounded steppers; no blank-means-unchanged; enum lists from API; `public-sr` rate policy;
  `RequestedBy` not the owner key; fewer reloads (hub-driven); polling fan-out bound.
- Done-when: every toggle on the SR screen changes what `!sr` does (test per setting).

### 4.9 Onboarding · settings · shell (U·B6)
- Auto-join semantics (own column or honest label); channel-bot device-code + poll; Integrations
  read-failure state; timezone/language wired or removed; wizard `botUsername` contract; `applyBasics`
  failure reported; notification/action-required centre + Home first-run; scope→feature map + re-grant
  on Settings; swallowed regrant/reconcile failures; IA: Admin via profile menu + chrome swap, theme +
  Account in profile menu, tabbed Settings, MyData on participant rung, spec route list reconciled
  🔒; copy fixes (client secret optional; Dutch wizard 4th line).
- Done-when: a fresh streamer reaches "bot is in my chat" with every status truthful and a next-steps
  Home.

## Tier 6 — multi-platform simultaneous + personas (U·Part C)

Sequenced so the spine lands first; the platform lanes then become mechanical. Owner call 🔒 on
the grouping model is the only blocker, and it blocks 6.1 only.

### 6.1 Platform spine (U·C0) — 🔒 first: confirm the grouping model, rewrite `platform-identity.md §9.4`
- Registry bootstrap provider-agnostic (`ChannelRegistryBootstrapService.cs:50-51`); ensure ctx on
  the first message of any kind on every ingest.
- `ChatPlatformRouter`: honour `ChatMessageReceivedEvent.Provider` for replies; unknown provider =
  honest failure, never cached as Twitch.
- `Provider` on every canonical community/monetization event; `*TwitchUserId` → `*ExternalUserId` +
  `*Provider` on the 18 entities; remove the `provider = Twitch` default on `IUserService`; delete
  `PlatformType`.
- Viewer identity: `LinkAsync` absorption (§3.1a), `IViewerMergeParticipant` + the eight participants,
  `ViewerRowAbsorbedEvent` published; identity-link UI on Me (6.5).
- Owner-level channel group: config domains shared with per-platform targets; per-viewer domains
  resolve to one human (balance, trust, entries, permits, voice).
- Done-when: a linked viewer has ONE balance across Twitch+Kick for one streamer; a Kick chatter gets
  welcome/triggers/timers without typing a command first.

### 6.2 Kick parity (U·C2)
- Go-live publishes canonical online/offline; Kick channel read (viewer count, title/category) +
  `KickPlatformApi` + `channel:read/write`; operator send on Kick; platform-correct error text;
  unsubscribe on disconnect; raid event; backoff/verifier/dedupe/unknown-type/follow-time/fragments
  fixes; moderation returns `Result`; Kick card health + login-only detection; `type: "bot"` identity.
- Done-when: Kick-only streamer gets alerts, timers, viewer count, title change and truthful ban
  results from the dashboard.

### 6.3 YouTube parity (U·C3)
- `youtube.force-ssl` + re-grant; 403 reason parsing + quota backoff; `IsLive` from YouTube poll;
  YouTube event translators (super chat/sticker/member/milestone/gift) + `snippet.type` routing;
  multi-broadcast; length chunking; own-channel cache; exponential backoff; leased poller;
  channel-id keys; unban outcome; refresh-failure signal; connect copy + scope readout + system check
  field + API-key wizard step; `concurrentViewers` sampler; per-platform stream-info results.
- Done-when: YouTube-only streamer gets replies, bans, member alerts and viewer count; quota burn
  shows as quota, not "re-grant".

### 6.4 Combined management (U·C1, after 6.1)
- Combined chat composer + targets + per-target results; badge every line; `provider` on
  `ChannelSummary`; (provider,id) dedupe + reorder window; timers/announcements/responses platform
  target sets with per-platform rate limit + duplicate suppression; supporter events normalised; one
  go-live form with per-platform results; per-platform viewer breakdown + total + cross-platform
  session; owner-scoped "ban on all my platforms"; earning credits the linked person.
- Done-when: live on three platforms, one timer posts to all three once each; one ban bans the human
  everywhere; Home shows per-platform viewers and a total.

### 6.5 Viewer persona (U·C5)
- Switcher source for participants ("channels I appear in"), honest empty state instead of reconnect
  error; MyData on the participant rung; Me gets GDPR, linked accounts (identity API client), own
  voice, standing, profile fields, per-jar contributions; own SR requests + link to public page;
  viewer giveaway entry/my-entries (endpoint + card) or drop from IA; leaderboard opt-in read;
  preview-as-viewer forces Everyone; channel chip shows the channel; analytics errors visible;
  routes/deep links; Kick login path for Kick-only viewers.
- Done-when: a viewer with no role logs in, picks a channel they chat in, sees balance/standing/voice/
  GDPR and can link Kick — on the first run.

### 6.6 Moderator of many (U·C6) — 🔒 spec the persona first (`spec/moderation.md`)
- Per-platform moderated-channel discovery; reconcile covers moderator-mode tenants + "roles last
  synced"; live dot bound to `isLive`; roster refresh on `StreamStatusChanged`; stale active-channel
  pin detected + cleared + explained; `primaryChannel()` hard-fails instead of substituting; roster
  cached; hub joins every roster channel for alert/mod classes; attributed notifications with
  click-through; "my channels" home with per-channel queues; queues re-fetch on `ModAction`;
  switch splash with timeout/error; active role in the sidebar header.
- Done-when: a mod of 4 channels sees which is live, gets attributed alerts from all four, and can
  never act on the wrong channel.

### 6.7 Bot-as-a-bot quality (U·C7) — the first five ship inside Tier 1.3
- Loop guard; `!commands`/`!help` + the other 8 legacy-regression builtins (`!lurk`/`!unlurk`,
  `!leaderboard`, `!songhistory`, `!playlist`, `!bansong`, `!whisper`, `!discord`, `!accountage`) + a
  seeded preset pack for the fun/script commands; per-platform length chunking + duplicate variation; outbound send
  queue/token bucket per channel per platform; reply-or-mention helper + `SendReplyAsync` result +
  fallback; `BuiltinOutcome`; tone on every outbound surface + usage/error slots; one
  `ParseUserMention`; permit via identity; whisper-with-fallback + inbound whisper handler;
  `announce` action/toggle; bot-line marker.
- Done-when: 100 simultaneous subs produce a coalesced, rate-limited chat response; no message
  exceeds a platform limit; a self-host bot never triggers itself.

### 6.8 X and a fourth platform (U·C4) — 🔒 owner: is X cross-posting in scope?
- If yes: `IntegrationProvider.twitter`, `tweet.write`, announcement-target registry generalised from
  the Discord go-live handler. Either way: badge every chat line incl. Twitch (in 6.4).

## Tier 5 — polish and structure
- **Sleak pass** (`sleak-review-2026-08-22.md`, rendered review): accent hierarchy (toggles neutral,
  one accented CTA per screen; chat-colour clamp; accent derivation with lightness/chroma floor);
  Home hero tile + collapsed activity feed; form width cap; destructive confirm + row overflow menu;
  random-responses segmented control; chat-mode on/off state; concentric radius tokens; 13 px
  muted-text contrast; one identity block in the sidebar; tone catalogue per locale. Then re-render
  the six screens + Overlays/Economy/TTS/Integrations/Pipelines and re-run the checklist.
- W·§8 items 5 (drop game redesign 🔒 mechanic), 8 (stacked-transition chat style 🔒 reference),
  10 (per-widget nits), 11 (component splits after 4.4; `WidgetGalleryItem` file-set storage first).
- S LOW/informational list (§ "LOW / informational").
- Remaining U·B2/B3/B5/B6 bullets not named above, batched by file.

**Ordering note (2026-08-22, round two):** Tier 6.1 (platform spine) is pulled up to run right after
Tier 1 — every Tier 2–4 fix on a per-tenant surface would otherwise be built on the per-platform
tenant model and redone. 6.7's first five items ship inside 1.3. Tier 5 stays last.

## Owner calls blocking items above (🔒)
- **Grouping model for a simulcast streamer (6.1):** channel group with per-domain resolution vs a
  designated primary tenant — rewrite `platform-identity.md §9.4` accordingly. Blocks 6.1/6.4.
- **Multi-channel moderator spec (6.6):** aggregate-queue endpoint shape + notification attribution.
- **X cross-posting in scope? (6.8)** — spec currently says login-only.
- SignalR/Redis backplane for multi-replica (2.1) — single-instance acceptable for now?
- Cooldown DB write-through (2.2) — scaling investment, defer?
- Music cost/duration/cooldown fields (4.8) — spec the economy hook.
- IA route list reconciliation (4.9) — regroup sidebar or update `frontend-ia.md`.
- Drop game mechanic; stacked chat transition reference (Tier 5).
