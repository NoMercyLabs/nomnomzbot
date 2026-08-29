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
where a fix requires it; add the new stuff after.** Phases: ~~0-S security first (COMPLETE 2026-08-23)~~ · 0 truth-and-safety · 1 runtime stability ·
2 existing platforms made to work (minimal spine) · 3 form infrastructure · 4 existing-feature truth/
reach · 5 new model (one channel, many platforms; any login) · 6 new features + personas · 7 polish.
Slice IDs are stable; the order is the queue.

---

## AT A GLANCE — what is open, in one screen

Read this block first. It is the only summary; everything below is detail.

**Your asks, and where each one is:**

| Your words | Slice | State |
|---|---|---|
| pipeline page needs love, nested if/and/or, add-remove-reorder | S-PIPE-TREE | engine + named params shipped; nested block-list EDITOR remains |
| make effects and repercussions visible | S-CONSEQ | law recorded, applies to every slice |
| VS Code-web editor, real npm SDK, real event payloads | S-CODE-EDITOR | CLOSED — Monaco shell, real server-generated types, multi-file, 220/220 event sample payloads (77 fixture-sourced + 143 reflection-generated) |
| item pickers show a rich list, not opaque ids | S-RICH-PICKERS | backend building - dashboard half after |
| budget system for payment tiers by resource usage | S-BUDGETS | queued - intent recorded: recover real cost, not upsell |
| old-bot behaviour only from generic blocks | (standing rule) | verified against the spec |
| stream-facing first (commands + overlays) | (ordering) | in force |

**Phases 0-S, 0 and 1 are EMPTY — all closed.** The queue is: DO NEXT, then Phase 2 onward.

**Rules that now bind every future slice** (learned the hard way, each cost rework):
1. A guard that checks only a hand-written list is not a guard — enumerate from the real source.
2. Every model change lands in BOTH migration sets (SQLite AND Postgres) or Postgres deploys break.
3. Never show state that is not actually enforced.
4. Every control says what it does and what changes; destructive saves show a counted blast radius.

---

## MILESTONE 2026-08-25c — candidate for the first milestone push

Owner's cadence is milestone pushes. This batch qualifies: the live box currently replays the current
song on every restart (S-SR-INFLIGHT-DURABLE, 169a52e4) and logs errors during normal operation
(S-CHATTERDAY-LOGNOISE-b, 79b6baba) — both user-visible on stream. Verify HEAD in a throwaway worktree,
then push + `scripts/ship.ps1` + watch.

## FOUND ON THE LIVE BOX — 2026-08-25 (after the first successful deploy)

## LIVE OUTAGE 2026-08-25 — root cause fixed, two follow-ups

The deployed bot was crash-looping (11 restarts) with a 502/503 dashboard, and the music queue
"kept requeueing the same songs". ONE root cause, fixed in 27649f50: a Spotify call hit HttpClient's
100s timeout -> `TaskCanceledException`, which DERIVES from `OperationCanceledException`, so the music
poller's `catch (Exception ex) when (ex is not OperationCanceledException)` did NOT catch it. It escaped
`ExecuteAsync` and `BackgroundServiceExceptionBehavior.StopHost` killed the whole host.

## BLOCKED ON THE OWNER — cannot be solved from this side

**ANSWERED 2026-08-25c (owner, via AskUserQuestion) — these three are no longer blocked:**
1. **Deploy = MILESTONE PUSHES.** Not every green slice, not never: push and ship at a milestone.
   `tdd-local-no-ci` still governs day-to-day (local test-first); a milestone is the trigger to push +
   `scripts/ship.ps1` + watch. The orchestrator decides when a batch constitutes a milestone and says so.
2. **Discord: DISABLE STREAMCORD, TAKE OVER.** Owner turns off Streamcord's live-role and go-live
   announcement for his channel; NomNomzBot becomes the sole driver of role `1388128843147120761`.
   So the go-live ANNOUNCEMENT is now ours to own too, not just the role — Streamcord will no longer
   post it. Still needs the physical steps in item 2 below (install, Manage Roles, role above target,
   account linked, friends' links accepted).
3. **S067 song-request pricing: FREE BY DEFAULT, COST OPTIONAL.** Ships with a max-duration cap and a
   per-user cooldown enforced for everyone; the channel-currency cost is an opt-in per-channel setting,
   default OFF. This is the near-free/abuse-floor side of [[limits-safety-baseline-then-tier]], not a
   paid gate.


These are not "not done"; they are done-as-far-as-code-can-go and need a real-world action or a call
only Stoney can make. Do not burn agent time trying to work around them.

1. **Deploy.** Nothing this session is pushed or deployed (`tdd-local-no-ci`: local test-first, no CI,
   deploy only on the owner's call). The deployed box is a DIFFERENT SYSTEM from this tree — every
   "verified" below means verified at a commit in a throwaway worktree, never on the live instance.
2. **Discord, for tomorrow's stream** — needs the owner in his own server:
   - NomNomzBot must be INSTALLED in the guild and hold **Manage Roles**.
   - The bot's own highest role must sit **ABOVE** role `1388128843147120761` in Server Settings > Roles.
     Streamcord working proves nothing here: ours is a separate member with its own role, which lands at
     the bottom by default.
   - His Discord account must be LINKED to his channel so the bot can resolve which member to mark live.
   - **Streamcord overlap:** it already drives that role and posts go-live announcements. Running both
     double-posts and has two bots fighting over the same role. He must disable Streamcord's live-role +
     announcement for his channel, or point ours at a different role while testing.
   - Friends' channels need an ACCEPTED LINK each (separate tenants); no shortcut exists that preserves
     tenant isolation.
3. **End-to-end Discord verification is impossible from here.** Unit tests prove the add/remove-role call
   is made with the right arguments against a FAKE handler. They can never prove Discord accepted it,
   that the token is valid, that the hierarchy is right, or that the resolved member is really him.
4. **Open call for him:** S-BUDGETS classifies *registering a command* as near-free -> abuse floor, not a
   paid ceiling, per his own stated reason (recover real cost, never manufacture upsell). He cited
   commands as an example of a tier limit, so he may want to overrule. Files/TTS/CPU/bandwidth are
   cost-driving either way.

---

## OWNER OBSERVATIONS 2026-08-29 — fold in at the right stage, do not jump on them

Twelve live observations from using the bot, given as a batch with the instruction to slot each into
its natural phase rather than working them immediately. Each gets its own slice id (`S-OBS-*`) so they
survive independently as the queue is worked top to bottom.

- **S-OBS-01** stale-then-fresh flash — a page loads cached data first, then the real server response
  replaces it a moment later, showing wrong data briefly before the correct data appears. Done-when:
  either the cache is never shown when a fresher fetch is already in flight, or the UI clearly marks
  cached data as loading/stale until the real response lands — no silent wrong-then-right flash.
- **S-OBS-02** multi-chat channel badges don't show the broadcaster's own Twitch chat-color per channel
  (the dashboard's dynamic accent already derives from chat color elsewhere — reuse that mechanism).
  Done-when: each channel's badge in the combined multi-chat view is tinted with that broadcaster's
  real chat color.
- **S-OBS-03** no single place for server errors — errors need to surface both (a) in one consistent
  place (top-of-page banner or snackbar) AND (b) inline at the exact control/location that caused them.
  Done-when: every server error the dashboard receives does both, consistently, everywhere.
- **S-OBS-04** music vs song-request pages have mixed concerns — the Music page shows song-request UI,
  and the dedicated Song-Request page is nearly empty and serves no purpose as currently split. Decide
  the model (likely: Song-Request page owns the SR UI, Music page stays playback/queue-management only)
  and move UI to match. Done-when: each page has one clear, non-overlapping purpose.
- **S-OBS-05** moderation page is not channel-scoped — it shows bans across ALL channels instead of the
  currently-selected channel, making per-channel ban management impractical for a mod-of-many. Done-when:
  the moderation page respects the active channel-switch context like every other tenant-scoped page.
- **S-OBS-06** soundclips have no single-playback enforcement or stop control — multiple clips can play
  concurrently and none can be stopped once started. Done-when: starting a new clip stops any clip
  already playing, and a stop control exists.
- **S-OBS-07** media page's `!media <url>` command works but the resulting media has no click-to-open
  popup, no on-page player, and no overlay-widget playback — it's captured but never actually watchable
  from the dashboard. Done-when: a `!media` result can be opened/played from the dashboard or an overlay.
- **S-OBS-08** bare Twitch clip links (no `!media` prefix) in chat should auto-enqueue into the same
  moderator approval queue `!media` uses, and be playable directly in that queue for review. Done-when:
  a plain clip link posted in chat appears in the approval queue, playable inline, without needing the
  command prefix.
- **S-OBS-09** played clips linger in the approval/media queue instead of being removed once played.
  Done-when: a clip leaves the queue after it has been played.
- **S-OBS-10** the code-scripts intermediate landing page is pointless — it should navigate straight to
  the script editor instead of an in-between page. Done-when: opening code scripts goes directly to the
  editor (this dovetails with the just-shipped Monaco-class editor work, S-CODE-EDITOR).
- **S-OBS-11** replying to a chat message with `!quote` should create a credited quote from the message
  being replied to (quote text + author credited), not just log the invoking user's own line. Done-when:
  `!quote` as a reply captures the replied-to message and credits its author.
- **S-OBS-12** `!quote N` is broken — it should quote the Nth message in that channel's chat history, but
  currently does not work at all. Done-when: `!quote N` returns and stores the actual Nth prior chat
  message.

---

## Phase 2 — existing platforms made to work (Kick / YouTube are shipped features that are broken) — only the spine pieces these fixes REQUIRE

- **S028-remaining-frontend** Kick hygiene backend is fully DONE and verified: unsubscribe-on-disconnect
  (d7ee3232), HTTP retry/backoff on the Kick client + non-chat redelivery dedupe + chat fragments from
  real emote metadata (893d3d36), `type:"bot"` sends with `broadcaster_user_id` correctly omitted
  (5ee8eaa7). Raid and moderation-Result-typing were confirmed already correct — no Kick raid-equivalent
  webhook topic exists, and Ban/Unban/Timeout/DeleteMessage already return truthful `Result`. REMAINING:
  the dashboard's Kick connection card is generic Connected/Not-connected with no login-only distinction
  (a Kick account linked for login but not authorized as a live platform connection) and no visibility
  into `KickEventSubscriptionWorker`'s MISSING_SCOPE backoff state. Done-when: the Kick card in
  Integrations distinguishes login-only from a full platform connection, and surfaces a real (not
  decorative) health/backoff state.
- **S-KICK-BOT-ACCOUNT** found by S028-bot-identity: Kick has no dedicated bot-account connection type
  at all (unlike Twitch's `twitch_bot`) — `KickAccessTokenProvider.IsBotAccount` always resolves false,
  so the just-shipped `type:"bot"` send path can never actually trigger; every Kick send today is
  type:"user" via the streamer's own account (the D5 fallback), permanently, not just until a bot
  account is registered. Done-when: a Kick streamer can register a separate bot account (mirroring the
  Twitch bot-account OAuth flow) and the bot then sends as `type:"bot"`.
- **S029** YouTube writes — `youtube.force-ssl` + re-grant; 403 reason parsing (quota vs scope) +
  quota backoff; refresh-failure signal (U·C3). Done-when: a reply/ban on YouTube succeeds; quota burn
  shows as quota.
- **S030-remaining** `IsLive` from the poll and `snippet.type`-routed translators for super chat/
  sticker/new sponsor/milestone/gift are DONE and verified (76adf380 — `YouTubeLiveChatEventTranslator`
  maps each to the same canonical cross-platform events Kick/Twitch publish, `IYouTubeLiveChatClient`
  carries the real Google-documented field shapes). REMAINING (U·C3): multi-broadcast support (today
  hardcoded to the first `active` broadcast only, confirmed at `YouTubeLiveChatClient.cs:51,67`); an
  own-channel cache (currently re-fetched via `GetOwnChannelAsync` on every liveness transition); a
  "leased poller" abstraction (current design is a single in-process `Dictionary<Guid, PollState>` —
  fine for one instance, not distributed); unban outcome reporting; a `concurrentViewers` sampler (no
  read of `liveStreamingDetails.concurrentViewers` anywhere yet). Also a known bug surfaced during
  S030-a: `YouTubeLiveChatClient.cs` unconditionally maps every 403 to `MISSING_SCOPE`, so a real quota
  exhaustion (`quotaExceeded`/`rateLimitExceeded`) incorrectly triggers the 15-minute scope-backoff path
  instead of quota-specific handling — fold this fix in alongside the exponential-backoff item, they're
  the same error-handling surface. Also `OAuthProviderRegistry.cs:194` never requests
  `youtube.force-ssl`, so every live-chat reply/ban/delete write already 403s today — needs the scope
  added. Done-when: a YouTube member alert fires; viewer count shows; timers post on YouTube; a
  multi-broadcast channel is handled correctly; quota vs scope errors are distinguished.

## Phase 3 — form infrastructure (stabilizes existing authoring; every 'raw text box' finding rides on it)

- **S043** "All helpers" dialog — shared `TemplateHelpersLink` + `Dialog` (search, namespace groups,
  insert) in every template field (commands, event responses, timers, rewards, pipelines, chat
  triggers, giveaways, Discord); en + nl descriptions; remove chip scroller (U·A7, W·§8 i7).
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
