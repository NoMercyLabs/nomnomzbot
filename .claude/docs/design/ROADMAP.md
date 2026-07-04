# NomNomzBot — Roadmap (Open Work Only)

This is the **single live backlog**. Finished items are removed, never marked done. Unordered
except where a dependency is named ([[no-fake-priority]]).

**Rules for every item:** ground in the spec + `aitm recall` first; TDD; shadcn + design tokens only; DRY/YAGNI/KISS; explicit types (no `var`); AGPL headers; csharpier + build green (TreatWarningsAsErrors); **verify the rendered client, not 200s**; every floor maps to an already-seeded permission key (`roles-permissions.md` §7.1) — no new permissions; one agent at a time on the shell; **never** two backend agents concurrently; commit each validated slice; never raw-dump or shortcut.

---

## Audit band — verify + fix existing functionality (goal 2026-07-04)

- **API documentation** — every endpoint fully described in the OpenAPI document (XML summaries, `ProducesResponseType`, problem-details responses); Scalar renders it usable.
- **Event handling, internal** — every source handled the way its spec demands: Twitch (EventSub topic coverage incl. gaps), Spotify, Discord, OBS, and our own dashboard-originated events.
- **Event forwarding to clients** — nothing missed from title change to going live to the last topic; events needing user data are hydrated with FULL user info (incl. pronouns) before fan-out.
- **Onboarding seeding** — onboarding seeds the DB 100% with every piece of broadcaster information.
- **Channel switching** — works end-to-end; per-channel `/effective/me` re-gating correct.
- **Role-scoped usage** — moderators/VIPs/subscribers/viewers can use the bot exactly as far as their permissions and per-role features reach.
- **Chat decoration** — REST and SignalR chat responses carry all fragments the broadcaster enabled; the broadcaster can change those options and it takes effect.

## Full-API-coverage rule (owner, 2026-07-04) — every method and event of every integrated API is implemented; missing = ADD, never remove

- **Helix: last two broadcaster-usable endpoints** — audit (2026-07-04, vs live reference) found the 26 sub-clients cover every broadcaster area 100%; remaining: `GET /helix/clips/download` (verify existence in live reference first — recent addition) → `ITwitchClipsApi`, and `GET /helix/schedule/icalendar` → `ITwitchScheduleApi`.
- **Helix: user-extensions management** — `GET /helix/users/extensions`, `GET+PUT /helix/users/extensions/active` (`user:read:broadcast`/`user:manage:broadcast`) — broadcaster extension-panel management, implement per the full-coverage rule.
- **OWNER CONFIRM N/A: org-gated Helix/EventSub surfaces** — Extensions area (12 endpoints), Drops entitlements (2), Get Extension Transactions/Analytics, Get Game Analytics require being a Twitch extension/drops/game organization — a bot cannot exercise them. Twitch-deprecated Tags endpoints excluded outright (docs-verified). Confirm these stay N/A or order them built.
- **Music provider interface widening (PREREQUISITE for the two items below)** — both providers still implement the legacy string-keyed `IMusicProvider`; build the spec's unified surface: `Guid` key + `MusicProviderCapabilities` flagset + `ResolveTrackAsync` + `IMusicProviderManageApi` (`music-sr.md` §3.5/§3.10).
- **Spotify completeness (spec-promised, audit 2026-07-04)** — add: `PUT /me/player/volume` (`SetVolumeAsync`), `POST /me/player/previous`, `GET /me/player` full state (sequencer poll), `GET /tracks/{id}` (`ResolveTrackAsync`), and the §3.10 manage surface (`/me/tracks`, playlist create/update/items, `/me/following`, playlist followers). OWNER-CONFIRM-N/A bucket: queue read, recently-played, top-items, markets, albums/artists/audiobooks/episodes/shows/browse (verify which are Spotify-removed vs merely unused — provider header claims Feb-2026 removals the live reference still lists). Spotify has no webhooks — polling is the only pattern (confirmed).
- **Discord: interactions webhook (functional gap — opt-in buttons dead-end today)** — public anonymous POST endpoint + mandatory Ed25519 signature verify (401 on invalid; Discord probes), PING→PONG handshake, MESSAGE_COMPONENT type-3 → `custom_id notify_optin:{roleId}` → existing role opt-in/out services, callback type 7/4 response; needs app public key config. Then: guild read endpoints (`GET /guilds/{id}` + `/roles` + `/channels`) for role/channel pickers instead of raw-id entry; message edit/delete for button re-posts. OWNER-CONFIRM: slash commands; N/A bucket: threads/reactions/scheduled-events/automod/audit-log/voice; gateway WebSocket stream deliberately not used (REST+interactions by spec).
- **YouTube provider build-out (provider is a STUB — zero Data API calls exist)** — implement per `music-sr.md` §3.5/§3.10: `search.list` (app API key), `videos.list` (duration/embeddable/age gates for SR), `videos.rate`, `playlists.*` + `playlistItems.*`, `subscriptions.*`; stub playback methods must return `CAPABILITY_UNSUPPORTED` (YouTube plays via browser-source IFrame by design, not the Data API). OWNER-CONFIRM-N/A: getRating/activities/channels + captions/comments/members/i18n.

## Multi-platform readiness (owner questions 2026-07-04 — audited, login is Twitch-welded today)

- **Platform-agnostic identity spec (SPEC FIRST, before YouTube provider code)** — linked external identities per User (identity table, not 1:1 Twitch), login with any linked account, account linking/merge flow, primary platform per channel, `Channel.Provider` discriminator + generic external-channel key; de-Twitch community-standing sourcing and `EventJournal.ActorTwitchUserId`/hub keying. The vault/`IEventSource`/music seams are already provider-generic; chat (`IChatPlatform`) + platform API (`IPlatformApi`) seams still to build per the rebuild design.
- **Render-manifest endpoint** — one client call returning features (tier-gated!) + integration connection states + scope grants + effective role, replacing the current 4-endpoint fan-out (`features`, `integrations`, `twitch/diagnostics/scopes`, `roles/effective/me`).
- **Dashboard event-class subscriptions (decided 2026-07-04)** — widgets already subscribe per-event (`Widget.EventSubscriptions` + `widget-{channel}-{id}` groups); the dashboard is one flat firehose group. Split into class sub-groups joined/left on page mount — `chat` (the volume), `activity`, `liveops`, `music`, `moderation` — with an **always-on core** every connection keeps (`ConfigChanged`, `StreamStatusChanged`, `AlertTriggered`, `PermissionChanged`) so background pages never go stale. Per-topic granularity deliberately rejected for the dashboard (chatter + missed-event edge cases for no real win). Backend: `DashboardHub` join/leave methods + notifier routing per class; frontend: page-mount subscription in the shell.
- **FeaturesController must consult `IFeatureFlagService` gates** — today it reads only the opt-in rows, bypassing tier/deployment/consent precedence: "visible" ≠ "entitled".

## Security & authorization fixes (audit 2026-07-04 — do FIRST among code slices)

- **Ungated mutations (SECURITY)** — `CustomDataSourcesController` (4 endpoints, `:130,160,193,211`) and `SoundClipsController` (4 endpoints, `:110,155,183,200`) have class-level `[Authorize]` only: a Moderator on a moderated channel bypasses the `customdata:write`/`sounds:write` Editor floors. Add `[RequireAction]` per seeded key (+ `sounds:read`/`customdata:read` on the reads); `PronounsController PUT /me` gets `pronouns:self:write` (low sev). Verify `UsersController` PUT profile / DELETE data enforce caller==userId self-scope.
- **Gate-1 blocks ALL non-manager participants (HEADLINE functional gap)** — `CanResolveTenantAsync` (ChannelAccessService.cs:44-77) never consults `ChannelCommunityStandings`, so viewers/subs/VIPs get 403 at tenant resolution on any participant endpoint: the community-plane Gate-2 is correctly built but unreachable below Moderator. Admit community-standing participants at Gate-1 (standing row exists → tenant resolves) and let Gate-2 do the real gating; behavioral tests: a viewer reaches an `Everyone(0)` endpoint, a Moderator is still 403'd on an Editor-floor mutation.
- **Chat commands bypass the MAX rule** — `ChatMessageHandler.cs:317-327` does its own ad-hoc role comparison instead of `IRoleResolver`/`HasCapabilityAsync` (MAX of community standing / management role / permits per `roles-permissions.md`); permits are invisible to chat today. Wire it to the resolver and gate `!permit` on `permit:issue`.
- **Plane-C controllers still on `[Authorize(Roles="admin")]`** — Admin, FeatureFlagAdmin, PlatformAnalytics, and Federation controllers use raw role-string auth instead of the canonical Plane-C IAM (`IPlatformIamService.AuthorizePlatformAsync`); migrate them so platform-admin capability grants/audit apply.

## Small decided items

- **ULID at the API boundary (decided 2026-07-04)** — keep UUIDv7 `Guid` storage (zero migration); encode every owned id as a ULID string at the boundary: JSON converter + route/query binder (accept both ULID and raw-Guid inbound), `Ulid` package already pinned in `Directory.Packages.props`, Kotlin DTO ids already `String`. Refresh `server/openapi/v1.json` + `ApiContractTest` when it lands.
- **Builtin key-format mismatch** — `DefaultCommandsSeeder` writes bang-prefixed `ChannelBuiltinCommand` keys (`"!sr"`) while `BuiltinCommandService`/the dashboard write bare keys (`"sr"`): seeded rows are orphaned from the toggle UI (runtime path already tolerates both via TrimStart). Normalize the seeder to bare keys + a repair migration for existing rows + test.
- **Credential component DRY unification** — the client-setup credential components are still duplicated.
- **Multi-channel residuals** — `Provider` discriminator on `Channel`; individual page controllers still call `primaryChannel()` independently instead of per-channel `/effective/me` re-resolution.
- **User-plane topic attribution with a shared bot account** — `user.update`/`user.whisper.message` conditions carry no broadcaster id, so with one dedicated bot serving multiple channels only the first channel's subscribe succeeds (others 409 harmlessly) and events attribute to that channel; needs a routing decision (per-channel reader identity vs recipient-based demux).
- **twitch-helix spec/code behavior drift** — align spec to shipped code in one pass: return-DTO names (`TwitchChannelInfoDto`/`TwitchStreamInfoDto` vs code `TwitchChannelInformation`/`TwitchStream`), empty `GET /streams` semantics (spec `IsLive=false` vs code `not_found`), `ModifyChannelInformationRequest` field names (`BroadcasterLanguage`/`ContentClassificationLabels`/`Delay`), and giveaways' whisper from-identity claim vs `ITwitchWhispersApi` tenant resolution.

## Specced, no backend yet — each blocks a dashboard page (`docs/bot-capabilities.md` §14)

- **Giveaways** (`giveaways.md`) — CRUD, open/close, draw/redraw, masked secret code pools (~16 endpoints)
- **OBS control** (`obs-control.md`) — connection/bridge, scenes/inputs, ~20 pipeline actions + `obs_event` trigger
- **VTube Studio** (`vtube-studio.md`) — connect/authorize/bridge, model inventory, control ops + `vts_event`
- **Media share** (`media-share.md`) — video request queue approve/reject/skip/reorder + overlay + `!media`
- **Automation API** (`automation-api.md`) — external API tokens, event catalog, data plane + WebSocket stream
- **Stream Deck** (`stream-deck.md`) — pairing-code flow on automation tokens
- **Marketplace / bundles** (`marketplace.md`) — export/inspect/import/uninstall; browse/install/publish
- **Supporter events** (`supporter-events.md`) — Ko-fi/Patreon/etc. connections + events + `supporter.*` triggers
- **Per-viewer data store** (`per-viewer-data.md`) — KV browse/set/delete + pipeline actions + template helpers
- **Engagement triggers** (`engagement.md`) — engagement config + 3 auto-engagement triggers
- **Live overlay games** (`live-games.md`) — session lifecycle + game catalog/manifest (overlay-rendered)
- **Live-ops schedule & markers** (`broadcaster-liveops.md`) — stream schedule CRUD + vacation; markers
- **Widget gallery + overlay manifest** (`widgets-overlays.md`) — gallery, `OverlayController` manifest, widget versions/build
- **TTS advanced** (`tts.md`) — mod approval queue, per-viewer voices, profanity filters, BYOK keys, usage ledger
- **Advanced moderation** (`moderation.md`) — network-nuke, shared-ban trust list, reports + evidence, per-user mod panel, unban-request queue, suspicious users, escalation ladder
- **GDPR/compliance + IPC dev-mode surfaces** (`gdpr-crypto.md`, `stream-admin.md`) — dedicated `GdprController`/`ComplianceController`/`IpcDevModeController` (functionality partly scattered today)

## Deferred by explicit decision

- SaaS conduit/webhook EventSub transport (self-host WebSocket path is complete). Carries with it the 5 structurally-unreachable-today topics (`user.authorization.grant/.revoke`, `drop.entitlement.grant`, `extension.bits_transaction.create`, `conduit.shard.disabled` — all app-token webhook/conduit-only per live docs) and the 6 conduit-management Helix endpoints (DB entities already scaffolded).
- Custom user groups (owner-deferred, streamerbot-parity batch)
- YouTube/Kick chat platforms (after Twitch is complete)

---

## Grounding sources (read first, per slice)
`frontend-ia.md` (IA), `roles-permissions.md` (floors §7.1), `commands-pipelines.md`, `music-sr.md`, `moderation.md`, `webhooks.md`, the economy specs, `event-store.md`. Re-run the parity audit if the controller↔surface map is stale. `aitm recall` always.
