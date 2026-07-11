# Multi-Platform + Parity Build — TODO Tracker

Durable mirror of the active build push (started 2026-07-09). Complements `ROADMAP.md` (the live
backlog): as a slice lands, its line collapses to a one-line **DONE** ledger entry with commit
hashes and its granular bullets are removed — finished work is never left as an open checkbox
([[goal-backend-up-to-snuff]]).

**Owner directives for this push:**
- Slice by slice, hardest → smallest. One validated vertical slice at a time; commit each.
- **Test cadence (owner, 2026-07-09):** during iterative churn run only the *targeted local tests
  that matter* — not the full suite, not `gh run watch`, every change. Reserve full `dotnet test` +
  push + CI watch for **meaningful checkpoints** (schema/migration, auth, end-of-slice, full-matrix).
- Frontend allowed **shadcn only** (no Material). Never touch `aaoa-dev`'s screens except to relocate.
- Twitter/X is an extension beyond the current spec enum (`twitch|kick|youtube`) — added in slice 2.

---

## ✅ DONE ledger

### 🔩 Foundation (2026-07-09)
- **1A** `0ea6195` — platform-agnostic `UserIdentity` table + migration pair (SQLite+Postgres) + 17
  test fakes; `IUserIdentityService`; `ILoginProviderRegistry` + descriptors; `GET auth/providers` +
  generic `auth/{provider}/device[/poll]`; JWT `idp` claim; `TwitchIdentityBackfillSeeder`.
- **1B** `8469be0` — `PrimaryIdentityWriter`; identity kept live on login + through `ResolveUserAsync`.
- **Channel discriminator** `f4f2289` — `Channel.Provider` + `ExternalChannelId` (unique, backfilled).
- **Nullable Twitch-id projections** `d79684c` — `User.TwitchUserId` / `Channel.TwitchChannelId` nullable.

### 🌐 Slice 2 — Login providers (2026-07-09, deployed to Proxmox, CI green `2a1e1bc`)
- `05c2dc16` 2a generic non-Twitch login flow · `f8cf3bb3` 2b YouTube-as-login (Google device) ·
  `a86ed24c` 2c auth-code+PKCE seam · `2a1e1bc1` Kick + Twitter/X providers · `db1b0cbf` X registered
  as 4th (login-only) · `9eafa283` `GET auth/identities` (list only) · `f51985f5` verified OAuth
  surfaces + Twitter login-only decision · `37f9ec68` credential-guard providers · `448a87d8` Kick + X
  creds wired through compose + `.env.example` · `b1037ee` Jint deflake.
  All four providers live; `GET /api/v1/auth/providers` returns twitch/kick/youtube/x.

### 🖥️ Frontend — landing + multi-provider auth UI (slices 6 & 7, shadcn)
- `40ec368e` — endpoint-driven multi-provider login (provider picker, device/redirect per provider) +
  public landing page on `/`. Channel switcher present in the shell (`channelSwitcherController`).
  *(Moderated-channels list still pending — blocked on slice 4 backend.)*

### 🔑 Frontend consumes `HeldActionKeys` — broadcaster overrides surface in the UI
- Shell gates page visibility on `role clears readFloor` **OR** `readActionKey ∈ heldActionKeys`
  (`ShellNav`/`ShellAccessController`/`ShellScreen`), so a broadcaster-lowered page reaches a role-less
  VIP/Sub without changing the two-plane default. Quote add/edit gate on `quotes:write`, delete on
  `quotes:delete`. Kotlin DTO field registered in `ApiContractTest`; `jvmTest` + `compileKotlinWasmJs`
  green. Closes the i18n / IAM-floors / ShellNavTest handoff entries.

### 🧹 QA correctness pass (bugs found mid-build — owner-reported)
- `c6dfb509` — sign-out calls `POST /auth/logout`, refresh cookie actually clears.
- `7c322006` — reject banning the broadcaster; record moderation **only** when Twitch enforces.
- `a184cf91` — reward manageability derived from the `only_manageable_rewards` set (no phantom field).
- **IAM floors** `6e89f4c3` → `5c56dc05` (split `quotes:delete`) → `ba9167a4` *(unpushed)* — default
  stays at Twitch base; broadcaster may **lower via override** to a VIP floor for non-harmful actions.
- `8788da8b` + `55599f92` — mirror vaulted music OAuth token → `Service` row on connect + boot backfill
  (fixes the Spotify/YouTube re-auth loop).
- `7a276f00` — bot "add permission" clears once the bot grant completes (reads `twitch_bot` conn;
  IRC scopes de-required).
- `57206ff3` — copy-link button on the device-code panels (incl. bot auth).
- `b6dbfbb1` *(unpushed)* — cache the i18n string bundle (boot stopped re-fetching it ~30×).
- `8a9e305e` *(unpushed)* — `HeldActionKeys` on `/effective/me` so the UI reflects broadcaster overrides.
- `965b7a4f` — CI cancels superseded in-progress runs (concurrency group per ref).

### 🔑 Foundation tail — DONE (2026-07-10, in the batch on master)
- `83187cd6` — EventJournal actor attribution made platform-agnostic (`ActorExternalUserId` + `ActorProvider`) + migration pair + portability round-trip.
- `901de001` + `892bc2fd` — `auth/identities` link / unlink / set-primary + `MergeIdentitiesAsync` (re-parents identity rows on a viewer merge, no orphaned/duplicate primary) + chat-ingest now resolves chatters through `IUserIdentityService.ResolveUserAsync` (get-or-create by provider + external id).

### 🐛 Session QA fixes (2026-07-11, owner-reported live)
- [x] **Title didn't live-update** — the banner renders `stats.streamTitle` but the PUT echo only merged
  `streamInfo`, and the hub's `StreamInfoChanged` push wasn't modelled in the KMP client (decoded to `Unknown`,
  dropped). Both paths now update the banner. `1b44933a` (frontend commit).
- [x] **Analytics all-zero** (followers/subs/commands/SR/currency/games) — projections listened for event names
  nothing publishes. Canonicalized `FollowEvent`, made `CommandExecutedEvent` THE published execution fact
  (hub push + `UseCount`/top-commands were dead too), folded currency ledger events + song requests + games.
  `fe339805`. **Peak viewers / unique chatters / watch seconds still have no writer** (needs a viewer-count
  sampler — pending below).
- [x] **EventSub 403 retry storm** — ~60 scope-blocked topics re-POSTed per channel every 5 min forever;
  ~84k no-op `failed→failed` journal rows/day + reauth spam. Scope-gated creates (self-heal on re-grant) +
  transition-only status journaling. `8ca9f502`. *Data caveat: follows/subs that happened while a channel's
  subscriptions were 403-broken (before 2026-07-10 ~18:30 UTC) never arrived — nothing to backfill from.*
- [x] **!coinflip was dead air** — the game engine had NO chat wiring. `!coinflip|!dice|!slots <bet>` builtins
  → `PlayAsync` (opt-in: seeded disabled; replies "not enabled" until the streamer enables). `d6d63d3d`.

### 🐛 Session QA fixes (2026-07-10, owner-reported live — not build slices)
- Chat live-push restored: hub socket opens for every rung (`a1750141`), refreshes its own JWT, and the SignalR `{}` handshake is read as success (`440a4283`) — verified live (persistent socket, joins channel). Emotes: subscribed-channel emotes wired + case-sensitive dedup (`ccfe973e`). Delete-message attributed to the acting moderator, not the broadcaster (`5eaa894b`).

---

## 📋 Pending

### 🔴 Live-ops correctness (owner-reported 2026-07-10 — "nothing but chat updates live; bot isn't useful")
Root-caused against the live Proxmox DB (`event_sub_subscriptions` statuses + stored token scopes). Evidence:
every broadcaster token already holds the full scope set (`channel:read:subscriptions`, `bits:read`,
`moderator:read:followers`, `channel:manage:broadcast`, …); the **platform bot token holds only chat scopes**.
- [x] **A. EventSub subscriptions ride the wrong token** — DONE `3427229e`. `RequiresBroadcasterToken` was
  hard-`false`, so every topic was created with the **bot** token (chat scopes only) → scoped topics 403,
  cost-1 topics piled onto the bot's single 10-cost budget → 429. Now each channel's subs are created with
  **that channel's own broadcaster token** (chat-read + the bot's whispers stay on the bot token). Verified on
  the live deploy: the bot's OWN channel went 16 → **55 enabled**, and self-host (bot == broadcaster == session
  owner) now gets the full event set.
- [x] **A2. Multi-tenant WebSocket topology — per-broadcaster sessions** DONE. Constraint EMPIRICALLY PROVEN on
  the live deploy: a broadcaster-token subscription on the **bot's** session is rejected with
  `HTTP 400 — "websocket transport cannot have subscriptions created by different users"` (one WS session = one
  Twitch user). Shipped: `WebSocketEventSubTransport` now keeps **one WS session per token owner**
  (`EventSubOwnerKeys`) — the bot session carries every channel's chat-read topics; each broadcaster gets its OWN
  session (their token owns it) for their authorized topics. `TwitchEventSubHostedService` is owner-aware
  (subscribe routes per owner; welcome re-registers only that owner's slice; cleanup + reconcile are per-owner).
  Within Twitch limits — 3 connections + 300 subs are **per user token**, and we use 1 connection per user.
  Conduits remain the SaaS-scale path (tables exist) but aren't needed for self-host + small multi-tenant.
- [x] **B. Dashboard stuck "live"** — DONE. `stream.offline` now subscribes (broadcaster token, cost-0) so the
  offline transition arrives; the existing `StreamStatusPollingService` (2-min Helix poll) is the backstop.
  Verified `stream.online`/`stream.offline` = enabled on the live deploy.
- [x] **C. Non-chat events render with full detail** — DONE (backend). Root cause was server-side after all:
  `NotifyChannelAsync` hardcoded the ChannelEvent's top-level `userId`/`userDisplayName` to null. The
  actor-bearing broadcasters (follow, sub/resub/gift, cheer, raid, shoutouts, mod/VIP role changes) now pass
  the actor through; the Kotlin `HubChannelEvent` already parsed those fields, so the activity feed shows
  names with zero frontend work. Handoff entry moved to Done.
- [x] **D. Title edit 403 (`channel:title:write`) — PERMISSION ELEVATION** DONE. Owner overruled the earlier
  premise ("a mod stays a mod, 403 is correct"): the whole point of the bot is that a broadcaster can delegate an
  action our system controls — even one Twitch's mod role can't do — and the bot performs it **on the broadcaster's
  own token**. Root cause: Gate-2 (`ActionAuthorizationService.AuthorizeActionAsync`) allowed purely on
  `callerLevel ≥ required` and **never consulted per-user capability grants**, so a broadcaster's `!permit @mod
  channel:title:write` (an `IsGrantableViaPermit` action, Editor-floored so un-lowerable by override) was ignored
  by the HTTP gate — it only affected chat commands. Fix: Gate-2 now allows on level **OR** a direct capability
  grant, reusing `IRoleResolver.HasCapabilityAsync` (the canonical rule) — bounded by construction (a grant can
  only exist for a grantable action, so Critical non-delegable actions stay locked). The Helix write already rides
  the tenant broadcaster's token (`TwitchHelixAuth.User` → `GetBroadcasterTokenAsync`), so once the gate passes the
  write lands. Spec updated (roles-permissions §3.2/§3.3). 3 new Gate-2 tests (mod+grant allows / mod-no-grant
  denies / expired-grant denies). The earlier `6cf417fe` reconcile (`ManagementRoleReconcileService`, 10-min
  mod+editor sync) still stands — it makes a Twitch **editor** grant flow in — but the mod-delegation path is this.
- [x] **E. Duplicate `ChannelMemberships` rows** DONE `6cf417fe`. Partial unique index
  `(BroadcasterId, UserId) WHERE DeletedAt IS NULL` + race-safe upsert (adopt the winner on a unique violation);
  migration collapsed the existing duplicates first (kept the most-privileged row per pair). Verified on the live
  deploy: **1339 duplicate rows → 0 dup groups**.

### 🌐 Multi-platform auth
- [x] **3a. Chat seam — SHIPPED 2026-07-11.** `IChatPlatform : IChatProvider` (+ `Provider` key);
  `HelixChatProvider` = the Twitch platform, `YouTubeChatPlatform` = sends via
  `liveChatMessages.insert` on the streamer's own token into the ACTIVE chat
  (`IYouTubeLiveChatSessionRegistry`, written by the poll worker on go-live/offline; offline sends fail
  honestly; replies degrade to plain sends — no threading on the Live Chat API).
  `ChatPlatformRouter` IS the registered `IChatProvider`, routing by `Channel.Provider` with a Twitch
  fallback — commands, pipelines, timers, dashboard all platform-route with zero call-site changes, and
  the `ChatMessageHandler` command gate is REMOVED: **commands now execute and reply in YouTube chat**
  (closes item 6's "bot replies on YouTube"). Read seam = the canonical `ChatMessageReceivedEvent`
  ingest (item 6).
- [x] **3b-1. YouTube moderation surface — SHIPPED 2026-07-11.** Timeout = TEMPORARY live-chat ban
  (`liveChat/bans` with `banDurationSeconds`), ban = permanent, delete = `liveChat/messages` delete —
  all on the streamer's own token against the active session, offline/token-less = honest logged no-op.
- [x] **3b-2a. YouTube unban ban-id bookkeeping — SHIPPED 2026-07-11.** `liveChatBans.delete` only
  accepts the insert-returned resource id, so `BanUserAsync` now returns it (`Result<string>`; an id-less
  2xx fails rather than ledger an unusable key) and every issued ban records into the persisted
  `YouTubeLiveChatBans` ledger (`IYouTubeLiveChatBanLedger`; soft-delete on consume, latest-wins with a
  UUIDv7 tie-break, migration pair `AddYouTubeLiveChatBans` + 17 test-fake sweep). Unban consumes the
  newest record and deletes by ban id on the ledgered PRIMARY channel's token — so it works OFFLINE
  (a permanent ban outlives the session); NOT_FOUND = already gone = fine; no record = honest logged
  no-op. 12 new tests (client wire shape + id parse, ledger contract, platform record/consume/no-op).
- [x] **3b-2b. `IPlatformApi` channel-ops seam — SHIPPED 2026-07-11.** The spec's third platform seam
  (2026-06-16-twitch-rebuild "thin seams"), deliberately WRITE-only + single-op per YAGNI — updating
  stream info is the one channel op exercised cross-platform (reads already degrade honestly).
  `IPlatformChannelApi.UpdateStreamInfoAsync` (Application/Contracts/Platform) + `IPlatformApi` per-platform
  key; `TwitchPlatformApi` absorbs the search-resolve (exact-name beats fuzzy; unresolvable keeps the
  user string, no game id) + Helix modify verbatim from `StreamController`; `YouTubePlatformApi` retitles
  the ACTIVE broadcast (`liveBroadcasts.update`, scheduledStartTime carried over — the PUT replaces the
  snippet; 100-char local cap) on the live session's primary token, offline = honest NOT_FOUND, and
  REJECTS category/tags (`VALIDATION_FAILED` — YouTube has neither; never a silent half-apply);
  `PlatformApiRouter` (ChatPlatformRouter twin) is THE registered `IPlatformChannelApi`. All four
  StreamController write routes (PUT + PATCH title/game/tags) now platform-route; no API contract change.
  11 new tests (Twitch resolve/wire/failure, YouTube retitle/offline/reject/token, router routing +
  twitch fallback, client GET-then-PUT wire + carried start time + caps).
- [x] **3b-2c-1. Kick `IChatPlatform` (send + moderation) — SHIPPED 2026-07-11.** `KickApiClient`
  (api.kick.com/public/v1: send w/ NATIVE `reply_to_message_id`, delete, timeout/ban/unban on
  `/moderation/bans`; 500-char + 100-char caps enforced locally; 401/403→MISSING_SCOPE),
  `KickAccessTokenProvider` (tenant Channel.ExternalChannelId = numeric Kick account id → vaulted login
  connection; OAuth 2.1 refresh with ROTATED refresh-token re-vaulting; failures →
  `MarkRefreshFailureAsync` reauth surface), `KickChatPlatform` registered in the chat router (seconds→
  minutes ceiling-clamped 1–10080; non-numeric target = honest no-op; token-less = honest no-op).
  Works with ZERO public URL. 19 tests. **Caveat:** the login grant is `user:read` only — chat/moderation
  work once the streamer's Kick grant carries `chat:write`/`moderation:*` (scope expansion is part of
  3b-2c-2's connect surface); until then calls fail honestly as MISSING_SCOPE.
- [x] **3b-2c-2. Kick chat READ + streamer connect — SHIPPED 2026-07-11.** The full loop:
  (1) **Connect surface** = one `Kick()` descriptor in `OAuthProviderRegistry` (scope-set `kick.chat` =
  user:read + chat:write + moderation:ban + moderation:chat_message:manage + events:subscribe) — the
  generic descriptor-driven connect/callback/status surface does the rest; the identity probe now reads
  Kick's `data[]` envelope + numeric ids generically. `KickAccessTokenProvider` prefers the tenant-scoped
  connect over the login-plane connection. (2) **Reconcile** = `KickEventSubscriptionWorker` (5-min tick,
  auto-registered hosted worker): tenant-scoped kick connections (the deliberate opt-in — login-plane
  NEVER drives work) provision the Kick presence as its own tenant Channel BEFORE subscribing, ensure the
  `chat.message.sent` v1 webhook subscription (adopt-if-exists; per-item create errors surfaced;
  MISSING_SCOPE → 30-min backoff, self-heals on re-grant). (3) **Ingest** =
  `POST /api/v1/webhooks/kick` (anonymous): RSA SHA-256/PKCS#1v1.5 signature over
  `{message-id}.{timestamp}.{body}` against Kick's cached public key (24h TTL + one forced refetch on
  miss = key-rotation self-heal), ±10-min freshness window on the SIGNED timestamp (replay protection),
  then `KickWebhookIngest` → canonical `ChatMessageReceivedEvent(Provider=kick)` (tenant by numeric
  broadcaster id, redelivery dedupe vs ChatMessages, role flags from badge types, slug as login).
  21 new tests (real-RSA verifier incl. rotation + cache count, ingest shape/dedupe/unknown-skip,
  reconcile opt-in/adopt/backoff, client subscription wire, controller auth gate).
  **OWNER STEP:** set the Kick app's webhook URL in the Kick developer dashboard to
  `{App:BaseUrl}/api/v1/webhooks/kick` and enable webhooks — per app, not per subscription.
  Verified wire facts: send = `POST api.kick.com/public/v1/chat` (user token, scope `chat:write`,
  500-char cap, native replies via `reply_to_message_id`, `type: user|bot`, `broadcaster_user_id` for
  user tokens); delete = `DELETE /public/v1/chat/{message_id}` (`moderation:chat_message:manage`);
  ban/timeout/unban ALL on `/public/v1/moderation/bans` (`moderation:ban`) — POST with `duration`
  (MINUTES 1–10080) = timeout, POST without = ban, DELETE `{broadcaster_user_id, user_id}` = unban
  (**direct — no ban-id ledger needed, unlike YouTube**; seam passes seconds → convert+clamp). Chat READ
  is WEBHOOK-ONLY (`POST /public/v1/events/subscriptions`, `method:"webhook"` enum, scope
  `events:subscribe`; callback URL configured PER APP in the Kick dev dashboard; must be public —
  localhost needs a tunnel; signature verify via Kick's public key). Existing plumbing:
  `KickLoginProvider` (PKCE, vaults tokens via `IIntegrationTokenVault`) but LOGIN-only scope
  `user:read` — the streamer connect flow must request the chat/moderation/events scopes. Kick user ids
  are INTEGERS (seam strings → parse). Build order: (1) Kick token provider + `KickChatPlatform`
  (send/reply/moderation — works without any public URL), (2) webhook ingest controller at
  `{App:BaseUrl}/api/v1/webhooks/kick` (signature-verified) + subscription reconcile → canonical
  `ChatMessageReceivedEvent(Provider=kick)`; self-host without a public URL = send-only Kick with an
  honest degradation notice (same URL the OAuth redirect already needs, so most deployments have one).

### 🔀 Act on any channel — no install required
- [x] **4. Moderated-channels resolution + switching — SHIPPED** (stale checkbox; verified live in code +
  tests 2026-07-11). `GET /channels` resolves the caller's moderated channels via Helix *Get Moderated
  Channels* and auto-grants Moderator memberships (`EnsureModeratorMembershipsAsync`);
  `GET /channels/moderated` lists them; tenant resolution (route/header/query channel target + Gate-2)
  lets an operator act on any channel they moderate with the bot never installed there. The shell's
  channel switcher consumes it (frontend ledger). `ChannelsControllerModeratedTests` cover it.
- [x] **5. Render manifest + tier gating + event classes — DONE** (first two were stale checkboxes,
  verified shipped: `GET channels/{id}/render-manifest` aggregates access + tier-gated features +
  integrations + scope gaps behind per-section Gate-2 floors, `RenderManifestServiceTests`, in
  `openapi/v1.json`; `FeatureService` consults `IFeatureFlagService.EvaluateAsync` — entitlement is
  authoritative over opt-in). The remaining third SHIPPED 2026-07-11: dashboard push classes —
  `JoinChannel` = all classes (unchanged client behavior), `JoinChannelClasses(channelId, classes[])`
  subscribes a subset (`chat`/`activity`/`liveops`/`music`/`moderation`); the notifier routes each push
  to its class group, core pushes (stream status / config / permission / reward invalidations / alerts)
  stay always-on; leave/rejoin/disconnect reconcile exactly the joined groups. Frontend adoption
  (per-page class sets) → handoff.

### 💬 Combined / multi-source chat (owner-specced 2026-07-10)
ONE substrate — a chat feed that **aggregates messages across a SET of channels**, each line tagged by source
(platform + channel), time-ordered — at two scopes mapped to two roles:
- [x] **6. Streamer cross-platform feed — YouTube half SHIPPED** `6beaa12b`. `ChatMessageReceivedEvent` is now
  THE canonical chat fact for every platform (`Provider` tag; Twitch-only consumers gate — commands/auto-mod/
  pronouns/decoration; persistence + hub push + all six projections are provider-agnostic; identity resolution
  threads the provider so YouTube chatters mint `youtube` identities). `YouTubeLiveChatPollWorker` polls each
  YouTube-connected streamer (2-min liveness probe offline, API-directed interval live), provisions their
  YouTube presence as its own tenant `Channel` row on go-live, and publishes live messages through the one
  substrate. Shared `IYouTubeAccessTokenProvider` custody path (extracted from the music provider).
  *Remaining: Kick chat read (needs slice 3's platform seams) + bot replies on YouTube (slice 3 send seam).
  Frontend merged/tagged feed UI → handoff.*
- [x] **7. Viewer+ multi-channel watch — BACKEND DONE** (frontend handed off). The picker data already exists
  (`GET /channels` returns owned + moderated; `GET /channels/moderated`; slice 4's *Get Moderated Channels* +
  auto-granted Moderator membership all shipped and tested). The one real backend gap was the hub: `DashboardHub`
  tracked only ONE channel per connection (last-join-wins), so a connection watching several channels leaked
  groups on disconnect and mis-tracked `LeaveChannel`. Now **set-based** — a connection watches many channels at
  once, `JoinChannel`/`LeaveChannel` are per-channel, disconnect drops them all (3 hub tests). Each push already
  carries `channelId` on `DashboardChatMessageDto` for routing/tagging — **no contract change needed**. Frontend
  multi-pane/merged UI → `handoff/for-frontend.md` (2026-07-10). *Open backend follow-up:* the hub `JoinChannel`
  gates Gate-1 only vs REST's Gate-2 `chat:read` — reconcile + decide the "viewer+" read floor (noted in handoff).
- Backend (this track): the aggregation substrate + DTO source-tag. Frontend (`app/`, aaoa): the multi-pane /
  merged chat UI (select channels, side-by-side or merged, platform+channel tags) → **handoff**.
- Sequence: slice 4 → item 7 (mod multi-watch — fastest value, Twitch works now); slice 3 → item 6
  (cross-platform, the bigger lift).

### 🎯 Streamer requests (qtkitte, 2026-07-11) — backend halves SHIPPED, config UX handed off
- [x] **SR1 backend. Rotating auto-shoutouts** — `80d9c936`. TimerService now dispatches `Timer.PipelineId`
  (specced §I.1, never implemented) with the rotation entry riding as `{timer.message}`; ShoutoutAction
  resolves logins/channel names (leading @ ok) via Helix Get Users. Auto-shoutout = timer(Messages=names)
  + pipeline `shoutout(user_id="{timer.message}")`. Frontend preset UX → `handoff/for-frontend.md`.
- [x] **SR2 backend. Walk-in sounds audible end-to-end** — `cbb7c8de`. The one load-bearing gap was that
  NOTHING consumed `/hubs/overlay`: `OverlayHostController` now serves the OBS browser-source shell at
  `/overlay?widgetId={id}&token={overlayToken}` (the URL shape the widgets API always returned) — hand-rolled
  SignalR JSON client + the PlaySound/StopSound audio bus. Sub→sound = EventResponse(channel.subscribe)=
  pipeline with `play_sound`; reward→sound = that reward's `PipelineJson`. Config UX → handoff.
- [x] **SR3. Built-in widget rendering — SHIPPED** `bd793eee`. The host page now RENDERS what the server
  pushes: transient alert cards (follow/subscription/resub/gift/cheer/raid, queued one at a time), the
  standing now-playing pill, and the hype-train meter; widget settings (`accentColor`, `durationMs`) apply
  on join + live. Payload text renders as text nodes only (no markup injection). The compiled-bundle/
  gallery/import pipeline (widgets-overlays.md) stays the later big phase. Also parked: 36 scope-blocked
  EventSub topics (33 = qtkitte's older grant — her re-grant self-heals; 1×4 = `user.whisper.message`
  needs bot-token `user:read:whispers`).

### 📊 Analytics writers — SHIPPED (2026-07-11)
- [x] **Peak viewers / unique chatters / watch seconds** — the last three dead M.8 columns now have real
  writers. `StreamStatusPollingService` publishes a journaled `StreamViewerCountSampledEvent` per live
  2-min poll (a per-stream viewer time series; PeakViewers folds the daily max).
  `ChannelAnalyticsDailyProjection` owns a new `ChannelChatterDays` anchor table (hashed viewer key, no
  PII, resets with the aggregate): first chat of the day → `UniqueChatters`; consecutive presence
  (chat/command/redemption) inside the SAME live stream → `TotalWatchSeconds` (per-stream first→last
  span, M.2 semantics). Migration pair (Postgres + Sqlite) + 17 test-fake sweep.

## 🤖 StreamElements / Streamer.bot parity (each = backend + dashboard page)
- [ ] **8. Automation API** (`automation-api.md`) — external tokens, event catalog, data plane,
  WebSocket stream. *(Streamer.bot core.)*
- [ ] **9. OBS control** (`obs-control.md`) — scenes/inputs, ~20 pipeline actions, `obs_event`.
- [ ] **10. VTube Studio** (`vtube-studio.md`) — connect/authorize/bridge, model control, `vts_event`.
- [ ] **11. Media share** (`media-share.md`) — video request queue + overlay + `!media`.
- [ ] **12. Giveaways** (`giveaways.md`) — CRUD, open/close, draw/redraw, masked code pools.
- [ ] **13. Supporter events** (`supporter-events.md`) — Ko-fi/Patreon/tips + `supporter.*` triggers.
- [ ] **14. Per-viewer data store** (`per-viewer-data.md`) — KV browse/set/delete + pipeline actions +
  template helpers.
- [ ] **15. Advanced moderation** (`moderation.md`) — network-nuke, shared-ban trust, reports/evidence,
  per-user panel, unban queue, suspicious users, escalation ladder.
- [ ] **16. TTS advanced** (`tts.md`) — mod approval queue, per-viewer voices, profanity filters, BYOK,
  usage ledger.
- [ ] **17. Live-ops schedule & markers** (`broadcaster-liveops.md`) — schedule CRUD + vacation; markers.
- [ ] **18. Engagement triggers** (`engagement.md`) — config + 3 auto-engagement triggers.
- [ ] **19. Live overlay games** (`live-games.md`) — session lifecycle + game catalog/manifest.
- [ ] **20. Widget gallery + overlay manifest** (`widgets-overlays.md`) — gallery, `OverlayController`
  manifest, widget versions/build.
- [ ] **21. Stream Deck** (`stream-deck.md`) — pairing-code flow on automation tokens.
- [ ] **22. Marketplace / bundles** (`marketplace.md`) — export/inspect/import/uninstall;
  browse/install/publish.
- [ ] **23. GDPR/compliance + IPC dev-mode controllers** (`gdpr-crypto.md`, `stream-admin.md`).

## 🔒 Security & small fixes (last) — audited 2026-07-11
- [x] **24a. Guest Star restore** — VERIFIED SHIPPED (stale bullet): the 4 beta topics are in the
  subscription catalogue with `beta` versions (`EventSubConditionBuilder`), translators exist
  (`GuestStarTranslators`), Helix sub-client (`TwitchGuestStarApi`) exists.
- [x] **24b. `!permit`/`!unpermit` out-of-the-box — SHIPPED 2026-07-11.** `PermitBuiltin` /
  `UnpermitBuiltin` (Identity/Builtins): `!permit @user <role|capability> [minutes]` and
  `!unpermit @user [role|capability]` work with zero config — @mention resolves login→id via Helix,
  the invoker is gated on `permit:issue` exactly like the pipeline actions + HTTP surface, and
  `IPermitService` re-asserts no-escalation + `IsGrantableViaPermit`. Role-token parsing shared with
  the pipeline actions (`PermitCommandSupport.TryParseManagementRole`). 6 tests.
- [x] **24c-1. pipeline `user.role` badge-only — FIXED 2026-07-11.** The pipeline's `user.role` variable
  now carries the EFFECTIVE role (`ResolveEffectiveRoleTokenAsync`: MAX of badge and the resolver leg —
  memberships/permits/standing; broadcaster-badge short-circuit; fail-closed to badge) so `user_role`
  conditions honor badge-less Editors and `!permit` elevations exactly like the command gate.
  `AuthorizationLadder.FromLevelValue` added (round-trip tested). **BONUS bug found+fixed in the same
  audit:** `!skip`/`!volume` builtin floors were `2` ("mod+" comment) — on the unified ladder 2 =
  SUBSCRIBER, so any sub could skip tracks; both now floor at 10 (Moderator), and the new permit
  builtins shipped with the correct floor.
- [x] **24c-2a. builtin key-format — SHIPPED 2026-07-11.** `DefaultCommandsSeeder` now writes BARE keys
  (`sr` not `!sr`, matching the dashboard/`BuiltinCommandService`); `NormalizeBuiltinKeys` migration
  pair repairs existing rows (bang rows with a live bare twin are soft-deleted — the dashboard-written
  twin wins; the rest are renamed). Runtime TrimStart tolerance stays as defense in depth.
- [x] **24c-2b. twitch-helix spec/code drift — ALIGNED 2026-07-11** (three spec-editor rounds, all
  verified against shipped code): DTO names (`TwitchChannelInformation`/`TwitchStream`/
  `TwitchChannelFollower`), empty `GET /streams` = `not_found` failure semantics,
  `ModifyChannelInformationRequest` seven-field shape, whisper from-identity = caller-passed tenant
  Guid (`giveaways.md` + `commands-pipelines.md` send_whisper), sub-clients documented as pure Helix
  I/O (false upsert/event/idempotency claims removed; real consumers named), no-NSwag DTO reality,
  orphaned `TwitchChannelInfoSyncedEvent` removed, `GetChannelFollowersAsync` signature + Users-sub-client
  placement fixed. *Deferred to its own catalogue pass:* full §3.x/§4 signature realignment across all
  26 sub-clients + cross-spec stale names (`broadcaster-liveops.md` 308/668, `community-dashboard.md`
  56/148/351, `TwitchUserDto` in §4.2, §2 blockquote upsert claim).
- [→] **24c-2c. credential-component DRY** — frontend track; handed off (`handoff/for-frontend.md`
  2026-07-11 entry).
- [x] **24c-2d. user-plane topic attribution — SHIPPED 2026-07-11.** Trace found the decision half-stale:
  `user.update` was ALREADY per-channel-correct since item A (broadcaster's own token + own `user_id` in
  the condition → distinct per channel, wire `user_id` = the broadcaster → resolver attributes right); it
  stays in the per-channel catalogue. Only `user.whisper.message` was broken — bot-owned, identical
  bot-id condition for every channel (first-channel winner + perpetual 409-pending rows), and its wire
  id (`to_user_id` = the bot) resolved to NO tenant with a dedicated bot, so whispers were silently
  DROPPED. Fix: `BotLifecycleService.PlatformEventTypes` subscribes it ONCE as tenant `Guid.Empty`
  (legacy per-channel rows auto-retired first — same (type+condition) key at Twitch), the
  `EventSubSubscriptions`→Channels FK is dropped (migration pair `DropEventSubSubscriptionChannelFk`;
  soft-delete world, the cascade could never fire), `SubscribeAsync` handles the platform tenant (bot id
  fills the condition; hard-fails without a platform bot), and `OnNotificationAsync` attributes an
  unresolvable whisper to the platform sentinel instead of skipping (whisper-only — unknown-channel
  skips stay intact; single-account self-host still resolves per-channel). Whisper→channel routing
  stays out of scope until a bot-inbox surface exists. 5 new tests (platform subscribe wire shape,
  no-bot hard-fail, sentinel dispatch, unknown-channel skip intact, single-account attribution intact)
  + catalogue-split guard. **Follow-up `603cced2` (live 403 root cause):** `user:read:whispers` was only
  on the STREAMER scope list — moved onto `AuthService.BotScopes` (bot identity owns the topic; streamer
  grant stays as the single-account leg) + listed informational on the bot permission page. Live verify:
  5 legacy per-channel rows retired on startup; platform row parked `failed` until the owner's next bot
  re-grant carries the scope (then the reconcile self-heals it to enabled — no manual step beyond re-auth).
- [ ] **24d. OWNER-GATED:** confirm authz key names (Plane-C + Gate-2 buckets) — cannot close
  autonomously.

## 🖌️ Designer reviews
- [ ] Dashboard viewer count in the top card need to get removed (redundant with metrics row)
- [ ] Dashboard Title view need to support emojis
- [ ] Chat need styling controls (size, time and so on, like twitch)
- [ ] Chat line is top aligned should be center-aligned
- [ ] Chat input doesn't wrap but overflow right
- [ ] StreamElements import feature that let user grab their overlays, quotes, timers and all
- [ ] Channel point page header has overlapping top right buttons
- [ ] Analytics metrics row should balance itslef when possible
- [ ] Analytics could benefit from some charts
- [ ] Discord server un-link icon should eb the trash icon not the minus icon