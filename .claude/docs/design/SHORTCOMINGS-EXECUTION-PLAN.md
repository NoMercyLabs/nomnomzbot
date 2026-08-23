# Execution plan — the slice index (one ordered queue, top to bottom)

Binding inputs: `PRODUCT-ALIGNMENT.md` (decisions D1–D9) · findings in
`stability-audit-scope-and-plan.md` (**S**·F1–F19), `widget-quality-audit-scope-and-plan.md`
(**W**·§1–§8), `usability-shortcomings-audit-scope-and-plan.md` (**U**·A1–A7, B1–B7, C0–C7),
`sleak-review-2026-08-22.md` (**K**). This file only orders them.

**How to execute:** one slice at a time, in this order; each slice is the smallest testable vertical
cut (contract → service → data → UI where it applies → test). Test-first, locally (`tdd-local-no-ci`);
commit when the **Done-when** is proven; then **delete the slice from this file** (tracker = remaining
work only). A slice may be split further while executing, never merged. 🔒 = needs the owner first;
skip it and continue. Persona priority (owner): streamer → moderator of many → viewer.

**Ordering rule (owner, 2026-08-22): stabilize the CURRENT feature set first; merge new code only
where a fix requires it; add the new stuff after.** Phases: 0-S security first · 0 truth-and-safety · 1 runtime stability ·
2 existing platforms made to work (minimal spine) · 3 form infrastructure · 4 existing-feature truth/
reach · 5 new model (one channel, many platforms; any login) · 6 new features + personas · 7 polish.
Slice IDs are stable; the order is the queue.

---

## Phase 0-S — security first (owner, 2026-08-22: "security is tight" beats "features start working")

- **S098b** Token validation hardening — access tokens stay 60 min but a cached `sid` revocation check runs per request
  so logout / impersonation-end invalidates in-flight tokens (owner decision); `ValidAlgorithms` pinned and bearer
  validation built from the token-service factory (`Program.cs:256-265`) (U·E6). Done-when: logout invalidates an
  in-flight access token.
- **S098c** Refresh custody — by cookie presence, not `?client=` (`AuthController.cs:1079`); drop the fragment refresh
  path (`:552`); Origin/CSRF check on cookie refresh (U·E6). Done-when: a cross-origin cookie refresh is rejected and no
  refresh token ever appears in a URL.
- **S098e** `ENCRYPTION_KEY` rotation — a REACHABLE re-wrap pass. (Audit correction, traced 2026-08-23: secrets are
  not silently blanked — `TokenProtector.cs:112-120` fails closed with a typed Result and 17 call sites treat the null
  as "needs re-auth". The real gap: `ISubjectKeyService.RotateKeyAsync` (`SubjectKeyService.cs:313`) only retires the
  DEK forward, never re-wraps stored ciphertext, and has zero callers.) Done-when: A→B rotation re-wraps every stored
  secret, is idempotent, reports failures loudly, and is reachable from an admin surface.
- **S115** Repo-wide CSharpier drift — `dotnet csharpier check .` fails on ~230 committed files (2551 checked), so the
  per-commit format gate in `CLAUDE.md` is currently unenforceable. Done-when: `dotnet csharpier check .` is clean on a
  quiet tree and stays the gate.
- **S114** Rate-limit tiers by task type (owner: got rate-limited toggling cheap options — the single `"api"` bucket
  is wrong) — replace the one policy with named tiers: `read` (generous, per user), `write-cheap` (toggles/config, generous),
  `write-expensive` (synthesis, uploads, fan-out, per channel), `auth` (strict, per IP), `anonymous` (overlay/webhook/
  public, per IP), `admin` (per principal); assign every controller/action explicitly; 429 responses carry `Retry-After`
  and the dashboard shows a calm "slow down" instead of an error. Done-when: 50 toggles in a minute never 429; 50 login
  attempts do.
- **S111b** Desktop saved connections — list + add + switch + forget in the Connect/profile UI, wired to the
  committed `SavedConnectionsRepository`; rescan action; mDNS failure surfaced inline instead of stderr (U·E5).
  Done-when: switch changes the active connection and reconnects; forget removes it and its token.
- **S111c** Desktop app polish — firewall hint; log file with a size cap and a documented path; session-expiry
  refresh; window state persisted; app icon + stamped version (`/health/version` is hardcoded 1.0.0.0); macOS
  data dir (U·E5). Done-when: a restart restores window state and the version endpoint reports the build.
- **S086c** IAM audit + guard gaps (U·D2 remainder, untouched by S086) — assign/revoke/create/deactivate/reactivate
  write no `IamAuditLog` row (`PlatformIamService.cs:171-236,324-371`); create is non-transactional and can flush an
  orphan principal (`:106-169`); duplicate/inactive-target assignments allowed (`:200`); the last `iam:manage` holder
  can be removed (`:335` guards only self-deactivation); `platform-billing` can only read while every billing write
  gates on `iam:manage` (`IamCatalogSeeder.cs:101`, `AdminBillingController.cs:55,63,69,86`); `billing:refund` has no
  endpoint; admin GDPR erasure gated on `tenant:access` (`ComplianceController.cs:42`); `IamAccessEvaluatedEvent`,
  `TenantAccessGrantedEvent` and the flag-change sites (`FeatureFlagAdminService.cs:87,136,161`) have zero consumers.
  Done-when: every IAM mutation lands an audit row naming target+role+scope, and the last `iam:manage` holder cannot
  be removed.
- **S088** Suspension enforced — `Channel.Status` checked in tenant resolution; bot parts + EventSub
  session revoked on suspend; handler on `TenantSuspensionChangedEvent` (U·D3). Done-when: a suspended
  tenant's dashboard and bot stop within one tick.
- **S089** Impersonation made safe (owner decision: this is the owner's lowest-level support tool — FULL act-as stays; it is a
  restricted SaaS action held by the platform owner role only) — requires an explicit support session (reason, expiry,
  session id) and the token lifetime is clamped to it; `act` claim honoured only for that principal; every write journalled
  with BOTH actors + session id and mirrored to `IamAuditLog`; mints rate-limited; refresh disabled for act-as tokens;
  tenant owner notified on begin/end; UI confirm + justification; spec amended (U·D4). Done-when: an impersonated write
  shows operator + subject in one audit query; self-host exposes no impersonation route.

## Phase 0 — truth and safety of EXISTING features (data loss, money, lies to viewers)

- **S001** Song-request queue store — `IMusicService` queue out of the scoped instance into a singleton
  store (U·B4 b1). Done-when: `!sr` → `!queue` → `GET /queue` agree across requests (test with two scopes).
- **S002** Provider queue/skip outcomes — `AddToQueueAsync`/skip bool honoured; NO_ACTIVE_DEVICE,
  auth failures, premium-required become distinct error codes + chat replies (U·B4 b2). Done-when: each
  failure class replies differently (tests per class).
- **S003** Spotify visible state — 401/403 → `needs_reauth`/`forbidden` on the integration status +
  Music page; vault is the single token source (drop the `Services` mirror read) (U·A2). Done-when: a
  revoked token shows on the Integrations card and `!sr` says why; music reads no `Services` row.
- **S004** Atomic balances — `CurrencyAccount` + `SavingsJar` update-where (S·F11, F11b). Done-when:
  concurrent double-spend test cannot overdraw.
- **S005** Earning dedupe unique index + escalation atomic increment (S·F12, F13). Done-when: duplicate
  event credit blocked by the DB; two concurrent offenses compound.
- **S006** Live-game money — settle failure refunds or parks retryable; can't-pay joiner feedback;
  runner force-cancel+refund after N tick failures (U·B2 b5). Done-when: forced settle failure refunds
  every stake (test); a stuck runtime self-cancels.
- **S007** Pipeline validation on save — `CommandConfigValidator` in `PipelineService` create/update
  (S·F1). Done-when: unknown action type rejected at save with typed error.
- **S008** Execution truth — real send outcome threaded; `SendReplyAsync` returns a result with
  plain+mention fallback; broken-out run = PartiallyFailed; invoker gets one reply for failure /
  cooldown / permission (S·F4, U·A1 i1, U·C7 reply semantics + `BuiltinOutcome`). Done-when: failing
  middle step → PartiallyFailed + one chat line; analytics records failure.
- **S009** Chat loop guard — per-tenant "sender ids the bot types as" checked on all three ingests
  (U·C7). Done-when: a bot line containing `!cmd` does not self-trigger (test per ingest).
- **S010** Outbound chat shaping — per-platform length chunking (Twitch 500 / YouTube 200 / Kick 500 /
  X 280), duplicate-line variation, per-channel-per-platform token-bucket send queue with coalescing
  (U·C7). Done-when: 100 simultaneous sends → rate-limited, coalesced, none dropped by length.
- **S011** Bot-line prefix (D5) — channel setting `BotLinePrefix` (none/`*`/`#`/emoji) applied on the
  streamer's-own-account sends; Settings field. Done-when: prefix appears on bot-typed lines only.
- **S012** Moderation accidental ban — timeout duration presets + hard block on unparseable; rule and
  escalation durations validated (U·B3 raw text). Done-when: empty duration cannot produce a ban (test).
- **S013** Destructive actions carry reason + confirm — community Ban (reason + timeout option),
  nuke reason/matchTerm + display name, Moderation "Verwijderen" + Commands delete behind confirm
  (U·B3, K). Done-when: every destructive dialog asks; reason lands in the mod log.
- **S014** Moderation feedback — announcement result; approve-unban confirm; `afterWrite` errors in
  every state; Warn keeps reason; availability flags for escalation/shared-ban/nuke cards; per-viewer
  read errors visible (U·B3). Done-when: a failed write is always visible.
- **S015** Filters truth — regex save-time check + tester; invalid regex not silently literal; empty
  allow-list warning; filter-conflict warning; stats not `Contains("ban")`; Helix already-actioned
  handling; `WarningAcknowledgedEvent` handler (S·F17–19, U·B3 backend). Done-when: stats = journal.
- **S016** Dead config honoured or removed — command Prefix/Match modes wired; `overlay` response type
  implemented; `Pipeline.IsEnabled` honoured in registry/timer/executor (U·B1). Done-when: each toggle
  changes runtime behaviour (test per field).
- **S017** Stale caches — pipeline create/update/delete invalidates command + chat-trigger caches;
  timer fire resolves `PipelineStep`-first (U·B1, S·F3). Done-when: edit → next run uses new graph.
- **S018** Raid flow — `start_raid` fires first, tolerates already-raiding, live pre-check,
  lookup-vs-not-found, `missing_scope` → re-grant flow, publishes `RaidSentEvent`; shoutout cooldown
  skip visible; `{args.N}` strips `@` (U·A1). Done-when: raid preset in S044 can run every step or name
  the failing one.

## Phase 1 — runtime stability of EXISTING plumbing

- **S033** EventSub reconnect — backoff on clean close; `ReconnectAsync` re-opens every owner session;
  `WaitAsync` leak; per-owner subscription count (U·B7). Done-when: mock close 4003 → exponential backoff.
- **S034** EventSub revocation — handlers for `EventSubRevokedEvent` (→ `needs_reauth` + notice) and
  the two unhandled events; publish `EventSubDisconnectedEvent` (U·B7). Done-when: revoke at Twitch
  flips status within one tick.
- **S035** SignalR hardening — `WithStatefulReconnect()`; OverlayHub many-widgets-per-connection;
  overlay token out of the query string + throttle (U·B5/B7). 🔒 backplane for multi-replica.
- **S036** Token refresh — per-connection refresh lock (Twitch/Kick/YouTube/X); sweep at boot, every
  provider (U·B7). Done-when: two concurrent refreshes → one provider call.
- **S037** Worker backoff — delay out of the try in YouTube poll / scheduled-pipeline expiry /
  TimerService / redemption-timer expiry (U·B7). Done-when: a throwing tick sleeps its interval (test each).
- **S038** DB/Redis resilience — Redis `abortConnect=false` + degrade; health check pings the singleton;
  SQLite WAL + busy timeout; `EnableRetryOnFailure`; `UnitOfWork` nested-begin guard + disposable;
  delete `DatabaseHealthCheck.cs` (U·B7). Done-when: SQLite soak with all services shows no "locked".
- **S039** Timers correctness — interval floor/ceiling; `LastFiredAt` advances on failure; stamp on
  enable/create; `MinChatActivity` snapshot; prune dead; rename uniqueness (S·F2/F5/F8, U·B1).
- **S040** Cooldown + collisions — atomic cooldown try-acquire (🔒 DB write-through); unique index or
  ordering on `EventResponse(BroadcasterId, EventType)`; builtin-name + alias collision checks;
  chat-trigger order column + `continue` on cooldown (S·F7/F9/F10, U·B1). Done-when: duplicate
  response rows impossible; alias hijack rejected at save.
- **S041** Small caps — SSML-escape voiceId; TTS per-channel volume cap; webhook flatten depth cap
  (S·F14–F16).

## Phase 2 — existing platforms made to work (Kick / YouTube are shipped features that are broken) — only the spine pieces these fixes REQUIRE

- **S020** Registry bootstrap provider-agnostic + ctx ensured on the first message of any kind on
  every ingest (U·C0/C2/C3). Done-when: Kick-only chatter gets welcome/triggers/timers without a `!cmd`.
- **S021** Chat router by origin — `IChatProvider` platform-keyed, reply/send target = message
  provider, unknown provider = honest failure, never cached as Twitch (U·C0/C1). Done-when: a Kick
  `!uptime` answers on Kick while Twitch is also live.
- **S022** `Provider` on every canonical community/monetization event; Kick/YouTube supporter events
  map to the same domain events (U·C1/C2/C3, spec `supporter-events.md` §4.1). Done-when: a Kick sub
  fires the `channel.subscribe` event response with `Provider=kick`.
- **S027** Kick go-live + reads — `livestream.status.updated` publishes canonical online/offline;
  Kick channel read (viewer count, title/category), `KickPlatformApi` + `channel:read/write`; operator
  send on Kick; platform-correct error text (U·C2). Done-when: Kick-only streamer sees live state,
  viewer count, can change title, and reply from the dashboard.
- **S028** Kick hygiene — unsubscribe on disconnect; raid event; backoff/verifier/dedupe/unknown-type/
  follow-time/fragments; moderation ops return `Result`; Kick card health + login-only detection;
  `type:"bot"` identity (U·C2). Done-when: Kick ban result is truthful in the UI; disconnect stops deliveries.
- **S029** YouTube writes — `youtube.force-ssl` + re-grant; 403 reason parsing (quota vs scope) +
  quota backoff; refresh-failure signal (U·C3). Done-when: a reply/ban on YouTube succeeds; quota burn
  shows as quota.
- **S030** YouTube events + liveness — `IsLive` from the poll; translators for super chat/sticker/
  member/milestone/gift; `snippet.type` routing; multi-broadcast; own-channel cache; exponential
  backoff; leased poller; channel-id keys; unban outcome; `concurrentViewers` sampler (U·C3).
  Done-when: a YouTube member alert fires; viewer count shows; timers post on YouTube.

## Phase 3 — form infrastructure (stabilizes existing authoring; every 'raw text box' finding rides on it)

- **S042** Helper registry + endpoint — machine-readable registry drives `TemplateResolver`;
  `GET /templates/helpers?context=`; save-time validation of unknown keys (U·A7, S·F6, W·§6).
  Done-when: endpoint returns the full valid set per entry type; save rejects unknown keys.
- **S043** "All helpers" dialog — shared `TemplateHelpersLink` + `Dialog` (search, namespace groups,
  insert) in every template field (commands, event responses, timers, rewards, pipelines, chat
  triggers, giveaways, Discord); en + nl descriptions; remove chip scroller (U·A7, W·§8 i7).
- **S045** Action field schema — `PipelineActionDescriptorDto` carries fields/kinds/options; kinds
  `number`, repeatable/segment, resource pickers (`discord_channel`, `discord_role`, `twitch_user`,
  `reward`, `widget`, `voice`, `sound_clip`, `asset`); step form renders them (U·A6 i5, U·A5 i3,
  U·B1 authoring). Done-when: no id/enum/number field in the catalogue renders as free text.
- **S046** Authoring ergonomics — regex compile check in chat-trigger dialog; create-and-bind pipeline
  everywhere; timer picker + interval presets + `LastFiredAt`/next index; command rename; `code` tier
  links to Code Scripts; branching (`ParentStepId`/`Branch`) in the step dialog (U·B1, W·§6/§8 i6).
- **S047** Dry-run — `POST pipelines/{id}/test-run` as a Test button on pipelines/commands/event
  responses/timers (W·§6/§8 i2). Done-when: captured side effects shown without sending.
- **S048** Save feedback baseline — `Feedback` in timers/chat-triggers/event-responses; event-response
  Delete = reset or no re-seed; seed out of the GET (U·B1). Done-when: every save surfaces saved/live.
- **S049** Hub events baseline — `HubEvent` cases for reward lifecycle, redemption status,
  `ConfigChanged`, `RewardChanged`; pending queue removes on fulfil/refund; live chat render re-check
  (U·B2 b1, folded handoff). Done-when: second session live-updates; fulfilled redemption leaves queue.
- **S050** Shell truth — hub-state indicator; reconnect banner reason; `effectiveMe` transient failure
  = retry state; remembered-session vs unreachable distinction (U·B6). Done-when: dead socket visible in 5 s.

## Phase 4 — existing features: truth, reach, completeness

- **S052** TTS system surface — auto-provisioned, ordered audio queue playing `audioUrl` segments,
  caption optional; TTS page shows URL / last-seen / test-through-overlay / queue controls;
  `tts_caption` out of the gallery (U·A3, spec `widgets-overlays.md` §1.2). Done-when: a TTS redeem
  is audible in OBS from a fresh channel with no widget install.
- **S053** TTS voice truth — overlay SDK sets `utter.voice`/`lang`; search matches Id + Locale
  case-insensitively; `VoiceExistsAsync` case-insensitive; `!voice` exact-id fallback; no
  hardcoded-Aria / keyless-Azure preference; precedence doc = code (U·A4). Done-when: `!voice
  en-US-AriaNeural` and `!voice aria` both work; the chosen voice is heard on client_edge.
- **S055** Discord rule editor — channel/role pickers via `GuildPickerField`, trigger dropdown, ping
  role, embed, helper link + preview; names in list (U·A6 i1). Done-when: no snowflake typed.
- **S058** Widget runtime fixes — field-name alignment for `recent_followers`/`top_cheerers`/
  `sub_train`/`goal_bar`/`labels`/`alerts`/`event_ticker`; goal events routed; contract test;
  `redemption_alert` sound wired or removed; `socials` help text = parser (W·§1/§8 i1). Done-when: every
  W·§5 row reads "solid" against real DTOs.
- **S060** Editor fire-bar — real per-event samples (`WidgetTestSamples`), chat variants, events from
  subscriptions not regex, desktop gets the bar (W·§3/§8 i3, U·B5).
- **S061** `chat_box.vue` layout batch — line break, truncation, avatar, contrast, emote size, arrival
  animation, mention highlight (W·§2/§8 i4).
- **S062** Widget setup — per-widget tokens + staged rotation + post-rotate URL list; Test button on
  the row; inline preview; error/last-ran badge; overlay last-seen; in-overlay banner on rejected
  token; resume without reload; settings form by schema availability; colour picker; asset/sound/font
  field types; unsupported-type + invalid-value errors; editable subscriptions; gallery version/update
  + search/paging; sound upload limits (U·B5). Done-when: add → copy → test → live from one row.
- **S063** Rewards reach — `Response` field in create + update; `ActionType/ActionSettings` exposed or
  deleted; rewards poll backoff; null-as-empty reads → errors (U·B2).
- **S064** Economy reach — catalog item full form + edit; leaderboard config CRUD + opt-outs + display
  names; jar role dropdown + jar update/delete (U·B2). Done-when: the store can sell an item with an
  effect, stock and cooldown from the UI.
- **S065** Giveaways reach — eligibility/weighting/prize pipeline in dialog; `ClosesAt` auto-close;
  code labels; entries endpoint + list; pool picker guard; zero-value-out gate for code-pool prizes;
  platform-generic DM delivery (U·B2, spec `giveaways.md`). Done-when: a weighted sub giveaway runs
  end to end.
- **S066** Moderation reach — chat-filters screen; AutoMod settings; AutoMod held-message queue; mod
  add/remove endpoints + UI; clear chat; full `AutomodConfigDto`; concurrency guard on whole-config
  POST; chat-settings slow/followers/unique/non-mod fields (U·B3). Done-when: a mod approves a held
  message from the dashboard.
- **S067** Music UX — admission enforces every setting + `IsEnabled` + trust gate + `PreferredProvider`;
  one config editor; queue promote/ban-track/refund; public `/sr/` page built; token → URL; bounded
  steppers; enum lists from API; `public-sr` rate policy; `RequestedBy` not the owner key; hub-driven
  reloads; polling fan-out bound (U·B4). 🔒 cost/max-duration/cooldown fields. Done-when: every SR toggle
  changes what `!sr` does (test per setting).
- **S068** Legacy builtins — `!help`, `!commands`, `!lurk`/`!unlurk`, `!leaderboard`, `!songhistory`,
  `!playlist`, `!bansong`, `!whisper`, `!discord`, `!accountage` + seeded fun-command preset pack +
  on-connect announcement (U·C7). Done-when: a fresh channel has every legacy command or a seed for it.
- **S069** Bot voice everywhere — tone applied to custom commands/timers/event responses/chat triggers/
  `send_message`; tone slots for usage/errors; one reply-or-mention helper; one `ParseUserMention`;
  permit via identity path; whisper-with-fallback for GDPR + inbound whisper handler; `announce`
  action/toggle; tone catalogue per locale (U·C7, K copy). Done-when: same `!sr` sounds the same from
  builtin and pipeline; sassy channel has sassy errors.
- **S070** Settings + onboarding truth — auto-join semantics; Integrations read-failure state;
  timezone/language wired or removed; wizard `botUsername` contract; `applyBasics` failure reported;
  scope→feature map + re-grant on Settings; swallowed regrant/reconcile failures; copy fixes (U·B6).
- **S076** Multi-chat as a tool — moderation actions + composer; `joinedChannels` preserved on
  reconnect; watch list persisted; mod-log pushes with names + time; shield pushes consumed (U·B3).

- **S085** Spec-led contract deltas (the 2026-08-22 realignment now leads the code) — `ResolvedAccessDto`/
  `RoleResolver` rungs by name not int; `IAutomationEventDescriptor` → attribute catalog; `FirstPartyWidgetCatalogue`
  `domain.action` subscription names; `2026-06-16-database-schema.md` changelog for `PlatformConnection`, Provider
  columns, `BotLinePrefix`, `EventJournal.Source`; `economy.md` L.3 `SubjectTwitchUserId`. Done-when: ApiContractTest
  + openapi snapshot refreshed; no int level in any DTO.

## Phase 5 — new model (D1 one channel / D2 any login) — merged only after Phases 0–4; S023/S024 are the minimum the viewer-identity fixes need

- **S019** `PlatformConnection` model — entity (ChannelId, Provider, ExternalChannelId, name, connection,
  IsPrimary, IsLive), `Channel` loses `Provider`, `Platform` enum + `twitter`; migrations (PG + SQLite);
  provisioner creates connections under the owner's one channel; data migration folds existing sibling
  channels into one (U·C0, spec `platform-identity.md`). Done-when: a Twitch+Kick streamer is ONE
  `Channel` with two connections; all tenant-scoped reads unchanged.
- **S023** Viewer identity key sweep — `*TwitchUserId` → `*ExternalUserId + *Provider` on the 18
  entities; remove `provider = Twitch` default on `IUserService`; delete `PlatformType` (U·C0).
  Done-when: build + migration green; no call site defaults the provider.
- **S024** Viewer linking — `LinkAsync` absorption (§3.1a), `IViewerMergeParticipant` + the eight
  participants, `ViewerRowAbsorbedEvent` published (U·C0). Done-when: a viewer who chatted on Kick then
  links Twitch ends with ONE User row and ONE balance (test).
- **S025** Login any platform (D2) — `auth/providers`, `auth/{provider}/device`, `/poll`; Kick/YouTube/X
  login providers shipped; first login creates the channel, others attach (spec `identity-auth.md`).
  Done-when: a Kick-only streamer signs in, onboards, and has a channel with one Kick connection.
- **S026** Onboarding "connect more platforms" stage (non-blocking) + channel-bot connect via device
  code with poll/refresh (U·B6, spec `onboarding-setup.md`). Done-when: wizard attaches a second
  platform; bot-account card updates without reload.
- **S032** Combined management fan-in/out — combined chat composer with target selector + per-target
  result; badge every line incl. Twitch; `provider` on `ChannelSummary`; (provider,id) dedupe +
  reorder window; timers/event responses/announcements platform target sets with per-platform rate
  limit + duplicate suppression; one go-live form with per-platform results; per-platform viewer
  breakdown + total + cross-platform stream session; owner-scoped "ban on all my platforms"; earning
  credits the linked person (U·C1). Done-when: live on three platforms, one timer posts once on each;
  one ban bans the human everywhere; Home shows per-platform viewers + total.

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
- **S057** Discord live-role sync — roles added on online, removed on offline, role picker, spec
  section in `discord.md` (U·A6 i4). Done-when: go live → roles on; offline → roles off.
- **S059** Alert system surface — one alert queue across platforms, not a gallery item (spec
  `widgets-overlays.md` §1.2). Done-when: supporter alert renders without an install.
- **S071** Notification centre + Home — action-required inbox (dead tokens, missing scopes, failed
  timers, held messages, pending unbans) with click-through; Home hero tile + collapsed activity feed
  + first-run next steps (U·B6, K). Done-when: a dead Spotify token is visible on Home within a minute.
- **S072** IA reconciliation — Admin via profile menu + chrome swap; theme + Account in profile menu;
  tabbed Settings; `MyData` on the participant rung; shipped routes listed in `frontend-ia.md`
  (U·B6). 🔒 regroup sidebar vs update spec.
- **S031** X Live as a platform connection (D3) — `IntegrationProvider.twitter`, login + connection,
  chat read/send via X's API to the extent it exposes (document limits), events where available
  (U·C4, spec `platform-identity.md` §10). Done-when: X connection attaches; chat lines carry `x`.
- **S073** Moderated-channel discovery per platform; reconcile covers moderator-mode tenants +
  "roles last synced"; live dot bound to `isLive`; roster refresh on `StreamStatusChanged`; roster
  cached (U·C6).
- **S074** Never act on the wrong channel — stale active-channel pin detected + cleared + explained;
  `primaryChannel()` hard-fails instead of substituting; switch splash with timeout/error; active role
  in the sidebar header (U·C6). Done-when: revoked access yields an explained state, never a 403 loop.
- **S075** Cross-channel awareness — hub joins every roster channel for alert/mod classes; attributed
  notifications with click-through; `GET /me/moderation/queue` + "my channels" home; queues re-fetch
  on `ModAction` (U·C6). Done-when: a mod of 4 channels sees which is live and gets attributed alerts.
- **S077** Viewer entry — switcher source "channels I appear in"; honest empty state; `MyData` on the
  participant rung; channel chip shows the channel; routes/deep links (U·C5). Done-when: a role-less
  viewer's first run lands on a usable Me page.
- **S078** Me page — GDPR export/erase, linked platforms (identity API client), own TTS voice, standing,
  profile fields, leaderboard opt-in read, per-jar contributions, own SR requests + public page link,
  preview-as-viewer forces Everyone (U·C5).
- **S079** Viewer giveaway entry/my-entries endpoint + card (or drop from IA) (U·C5).
- **S051** Design-system catalogue gap — build the 13 catalogued-but-missing primitives (Alert,
  Checkbox, Combobox, Input, Label, Popover, RadioGroup, ScrollArea, Select, Skeleton, Table, Toast,
  Avatar) or re-scope the catalogue; Patterns tier documented (spec `frontend-design-system.md`).

## Phase 4B — the surfaces round four found (U·Part E) — existing features, same stability-first rule

- **S099** Webhooks truth — outbound backoff capped + jittered, per-delivery dead-letter, delivery off the
  publishing thread, Result checked in the drain; auto-disable + attempted events consumed (toast/hub/
  feed); UI `NextRetryAt`, error vs empty, refresh/paging/replay (U·E3).
- **S100** Custom data sources truth — persist last attempt/error/failure count, backoff + auto-disable;
  allowlist checked at save; real JSON field-map parsing with inline errors; key picker from a test fetch;
  drop or wire `InboundWebhookEndpointId` (U·E3).
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
  preview-on-overlay or remove dead endpoint; asset picker wherever a media URL is configured; used-by
  guard; limits shown; paging (U·E2).
- **S105** OBS + VTS truth — OBS page consumes OBS events; scene/source/input pickers in pipeline fields;
  source-visibility + replay-buffer on the control screen; bridge error vs offline; edit-reset fix; VTS
  probe + bridge status; inventory failure vs locked; parameter/tint control; endpoint prefilled +
  validated; i18n of the two hardcoded errors (U·E2).
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
- **S110** Automation + federation — Stream Deck run-pipeline/run-command action with picker; federation
  opt-in validated server-side, peer/capability pickers, Direction collected (U·E3).
- **S112** Self-host ops — version stamping (`/health/version` real); ready = migrations + EventSub, Degraded
  ≠ ready; update check + notice; pre-migration DB snapshot + documented rollback; backup/restore verb in
  deploy scripts; versioned image tags; firewall + log path documented, log size cap; tray parity on
  Linux/macOS or printed URL/PID; `.env` dev-password warning at boot; saas restriction marker on
  `docker-compose.yml` + `.env.example` + boot notice in saas mode (U·E5).
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
- **S095** Admin UI truth — fix the `FeatureFlag` DTO so the tab loads; render `state.error` + every
  slice's failure; writes route through `actionError`; paging on every list; refresh per tab; hub live
  state truthful + connect errors surfaced; Admin entry in profile menu gated on Plane-C roles with chrome
  swap + per-page routes/deep links (U·D6). Done-when: a 403 on any admin write is visible; Flags tab shows
  flags.
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
- **S081** Widget component splits (W·§7/§8 i11) after S058; `WidgetGalleryItem` file-set storage first.
- **S082** Drop game redesign 🔒 mechanic; stacked-transition chat style 🔒 reference (W·§8 i5/i8).
- **S083** Render-manifest + per-page hub event-class subscriptions (folded handoff, optional).
- **S084** Remaining per-widget nits (W·§8 i10), `{user.messageCount}` alias/drop, the 15 code scripts
  test-run on the live channel, S LOW/informational list.

## 🔒 Owner calls still open
- SignalR/Redis backplane for multi-replica (S035) — single-instance acceptable for now?
- Cooldown DB write-through (S040) — scaling investment, defer?
- Music cost / max-duration / per-user cooldown fields (S067) — spec the economy hook.
- Sidebar regroup vs `frontend-ia.md` update (S072).
- Drop game mechanic; stacked chat transition reference (S082).
- Pre-existing from BUILD-TODO: authz key names (Plane-C + Gate-2), self-host owner = platform admin,
  user-scripting model (JS-first), YouTube non-BYOC client, Stripe, pipelines 6-surface unification,
  community reposition, data-sources push-bridge, federation transport, Streamer.bot import.
