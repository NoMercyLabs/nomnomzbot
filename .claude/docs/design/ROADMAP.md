# NomNomzBot — Roadmap (Open Work Only)

This is the **single live backlog**. Finished items are removed, never marked done. Unordered
except where a dependency is named ([[no-fake-priority]]).

**Rules for every item:** ground in the spec + `aitm recall` first; TDD; shadcn + design tokens only; DRY/YAGNI/KISS; explicit types (no `var`); AGPL headers; csharpier + build green (TreatWarningsAsErrors); **verify the rendered client, not 200s**; every floor maps to an already-seeded permission key (`roles-permissions.md` §7.1) — no new permissions; one agent at a time on the shell; **never** two backend agents concurrently; commit each validated slice; never raw-dump or shortcut.

---

## Full-API-coverage rule (owner, 2026-07-04) — every method and event of every integrated API is implemented; missing = ADD, never remove

- **OWNER CONFIRM N/A: org-gated Helix/EventSub surfaces** — Extensions area (12 endpoints), Drops entitlements (2), Get Extension Transactions/Analytics, Get Game Analytics require being a Twitch extension/drops/game organization — a bot cannot exercise them. Twitch-deprecated Tags endpoints excluded outright (docs-verified). Confirm these stay N/A or order them built.
- **§3.10 library READ members (added to spec 2026-07-05: `GetSavedTracksAsync`/`AreTracksSavedAsync`/`GetFollowedAsync` + `MusicFollowDto`)** — wire Spotify (GET /me/tracks read is live; contains-check + follows read need live-verify) and fold YouTube's (liked list, `videos.getRating`, `subscriptions.list`) into the YouTube provider slice. Residuals riding along: `IMusicRemoteProvider` leftovers fold into their owning slices (paged playlists → §3.10, play-context → §3.5.2 sequencer); DI line-1035 Singleton-registry vs Scoped-provider captive-dependency when `IMusicProviderRegistry` lands; `GET /me/player` full-state switch belongs to the §3.5.2 sequencer slice (poller right-sized on `/currently-playing` today). OWNER-CONFIRM-N/A bucket: queue read, recently-played, top-items, markets, albums/artists/audiobooks/episodes/shows/browse (verify which are Spotify-removed vs merely unused — provider header claims Feb-2026 removals the live reference still lists). Spotify has no webhooks — polling is the only pattern (confirmed).
- **Discord: message edit/delete + slash commands (residual after 2026-07-05 interactions build)** — the Ed25519 interactions webhook + PING/PONG + type-3 opt-in routing + guild read endpoints (guild/roles/channels pickers) shipped; still open: message **edit/delete** on the bot's own messages (for re-posting a role-button message when its config changes) via `IDiscordBotGateway`; OWNER-CONFIRM slash commands. N/A bucket unchanged: threads/reactions/scheduled-events/automod/audit-log/voice; gateway WebSocket stream deliberately not used (REST+interactions by spec).
- **YouTube provider build-out (provider is a STUB — zero Data API calls exist)** — implement per `music-sr.md` §3.5/§3.10: `search.list` (app API key), `videos.list` (duration/embeddable/age gates for SR), `videos.rate`, `playlists.*` + `playlistItems.*`, `subscriptions.*`; stub playback methods must return `CAPABILITY_UNSUPPORTED` (YouTube plays via browser-source IFrame by design, not the Data API). OWNER-CONFIRM-N/A: getRating/activities/channels + captions/comments/members/i18n.

## Multi-platform readiness (owner questions 2026-07-04 — audited, login is Twitch-welded today)

- **Platform identity BUILD (spec done: `platform-identity.md`, before YouTube provider code)** — `UserIdentity` table + backfill migration (Sqlite+Postgres, test fakes!), `IUserIdentityService` (Resolve/Link/Unlink/SetPrimary + bare-viewer absorption via auto-discovered `IViewerMergeParticipant`s), `ILoginProviderRegistry` + descriptor-driven `auth/{provider}/device` routes + `GET auth/providers`, `Channel.Provider`/`ExternalChannelId`, `EventJournal.ActorExternalUserId`+`ActorProvider`, JWT `idp` claim; migrate ingest lookups to `ResolveUserAsync`. Chat (`IChatPlatform`) + platform API (`IPlatformApi`) seams remain separate rebuild-design items.
- **Render-manifest endpoint** — one client call returning features (tier-gated!) + integration connection states + scope grants + effective role, replacing the current 4-endpoint fan-out (`features`, `integrations`, `twitch/diagnostics/scopes`, `roles/effective/me`).
- **Dashboard event-class subscriptions (decided 2026-07-04)** — widgets already subscribe per-event (`Widget.EventSubscriptions` + `widget-{channel}-{id}` groups); the dashboard is one flat firehose group. Split into class sub-groups joined/left on page mount — `chat` (the volume), `activity`, `liveops`, `music`, `moderation` — with an **always-on core** every connection keeps (`ConfigChanged`, `StreamStatusChanged`, `AlertTriggered`, `PermissionChanged`) so background pages never go stale. Per-topic granularity deliberately rejected for the dashboard (chatter + missed-event edge cases for no real win). Backend: `DashboardHub` join/leave methods + notifier routing per class; frontend: page-mount subscription in the shell.
- **FeaturesController must consult `IFeatureFlagService` gates** — today it reads only the opt-in rows, bypassing tier/deployment/consent precedence: "visible" ≠ "entitled".

## Security & authorization fixes (audit 2026-07-04 — do FIRST among code slices)

- **OWNER-CONFIRM: Plane-C key mappings (2026-07-05 IAM migration)** — routes with no Plane-C spec row got the coordinator default: `AdminController` users/system/health/events → `iam:manage` (a lighter read key may fit the dashboard reads), `GetAdminStats` → `platform:analytics:read` (family mapping), `FederationController.ListPeers` → `audit:read` (spec offered `iam:manage` OR `audit:read`; least-privilege chosen — both admin bundles include it), and the two bot device-login routes (Streamer.bot-parity additions, absent from identity-auth §5) → `iam:manage` like the rest of the platform-bot surface. Confirm or rename, then name them in the spec §5 tables.
- **OWNER-CONFIRM: Gate-2 keys minted 2026-07-04 (Gate-1 became pure entry; Moderator-floored routes needed keys the specs never named)** — `chat:read` + `chat:send` (Chat GET/POST messages + hub send-as-bot; floor from frontend-ia; rename `chat:read`→`chat:history:read` if the IRC-scope name collision bothers), `music:config:read`, `stream:read`, and mappings `play-context`→`music:remote:control`, `GetChannel`→`dashboard:read`, `SearchUsers`→`community:read`. Also pre-existing key drift found: seeder `chat:announce` vs spec `moderation:announce` (roles-permissions.md:595); `music:config:write` Editor (seeder+§7.1) vs Moderator in music-sr §5.1 rows. Confirm names/floors, then name them in the spec §5 tables.
- **Pipeline `user.role` + `UserRoleCondition` are badge-only** — `BuildInitialVariables` stamps `user.role` from `ChatRole.Resolve` (badges) into every dispatch and `UserRoleCondition` gates on it, so a badge-less Editor fails `user_role` conditions and `{{user.role}}` renders "viewer" (chat COMMAND floors already use the MAX rule). Needs a design decision: lazily-resolved variable vs resolver call per dispatch (hot path). AutoMod exemption levels (`AutoModerationHandler:199`, `AutoModerationEngine:142`) are also badge-only — arguably correct for spam control; decide + document with the same slice.
- **`!permit`/`!unpermit` chat commands are specced but unbuilt** (`roles-permissions.md` §6 pipeline actions) — the only permit surface is HTTP `PermitsController` (correctly gated on `permit:issue`). Build the chat-facing commands as a feature slice.

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
