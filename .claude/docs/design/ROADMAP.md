# NomNomzBot — Roadmap (Open Work Only)

This is the **single live backlog**. Finished items are removed, never marked done. Unordered
except where a dependency is named ([[no-fake-priority]]).

**Rules for every item:** ground in the spec + `aitm recall` first; TDD; shadcn + design tokens only; DRY/YAGNI/KISS; explicit types (no `var`); AGPL headers; csharpier + build green (TreatWarningsAsErrors); **verify the rendered client, not 200s**; every floor maps to an already-seeded permission key (`roles-permissions.md` §7.1) — no new permissions; one agent at a time on the shell; **never** two backend agents concurrently; commit each validated slice; never raw-dump or shortcut.

---

## Full-API-coverage rule (owner, 2026-07-04) — every method and event of every integrated API is implemented; missing = ADD, never remove

- **OWNER CONFIRM N/A: org-gated Helix/EventSub surfaces** — Extensions area (12 endpoints), Drops entitlements (2), Get Extension Transactions/Analytics, Get Game Analytics require being a Twitch extension/drops/game organization — a bot cannot exercise them. Twitch-deprecated Tags endpoints excluded outright (docs-verified). Confirm these stay N/A or order them built.
- **Music residuals (after the 2026-07-05 provider slices — Spotify + YouTube surfaces complete)** — fold `IMusicRemoteProvider` leftovers into their owning slices (paged playlists → §3.10, play-context → §3.5.2 sequencer); resolve the DI line-1035 Singleton-registry vs Scoped-provider captive-dependency when `IMusicProviderRegistry` lands; `GET /me/player` full-state switch belongs to the §3.5.2 sequencer slice (poller right-sized on `/currently-playing` today). OWNER-CONFIRM-N/A bucket: Spotify queue read, recently-played, top-items, markets, albums/artists/audiobooks/episodes/shows/browse (verify which are Spotify-removed vs merely unused). Spotify has no webhooks — polling is the only pattern (confirmed). YouTube: getRating covered; N/A per spec = activities/channels + captions/comments/members/i18n.
- **Discord: message edit/delete + slash commands (residual after 2026-07-05 interactions build)** — the Ed25519 interactions webhook + PING/PONG + type-3 opt-in routing + guild read endpoints (guild/roles/channels pickers) shipped; still open: message **edit/delete** on the bot's own messages (for re-posting a role-button message when its config changes) via `IDiscordBotGateway`; OWNER-CONFIRM slash commands. N/A bucket unchanged: threads/reactions/scheduled-events/automod/audit-log/voice; gateway WebSocket stream deliberately not used (REST+interactions by spec).

## Security & authorization fixes (audit 2026-07-04 — do FIRST among code slices)

- **OWNER-CONFIRM: Plane-C key mappings (2026-07-05 IAM migration)** — routes with no Plane-C spec row got the coordinator default: `AdminController` users/system/health/events → `iam:manage` (a lighter read key may fit the dashboard reads), `GetAdminStats` → `platform:analytics:read` (family mapping), `FederationController.ListPeers` → `audit:read` (spec offered `iam:manage` OR `audit:read`; least-privilege chosen — both admin bundles include it), and the two bot device-login routes (Streamer.bot-parity additions, absent from identity-auth §5) → `iam:manage` like the rest of the platform-bot surface. Confirm or rename, then name them in the spec §5 tables.
- **OWNER-CONFIRM: Gate-2 keys minted 2026-07-04 (Gate-1 became pure entry; Moderator-floored routes needed keys the specs never named)** — `chat:read` + `chat:send` (Chat GET/POST messages + hub send-as-bot; floor from frontend-ia), `music:config:read`, `stream:read`, and mappings `play-context`→`music:remote:control`, `GetChannel`→`dashboard:read`, `SearchUsers`→`community:read`. Also pre-existing key drift found: seeder `chat:announce` vs spec `moderation:announce` (roles-permissions.md section 7.1 (moderation:announce row)); `music:config:write` Editor (seeder+§7.1) vs Moderator in music-sr §5.1 rows. Confirm names/floors, then name them in the spec §5 tables.
## Small decided items

- **Credential component DRY unification** — frontend (one lane under D7; the former `handoff/for-frontend.md` 2026-07-11 entry).
- **Multi-channel residuals** — individual page controllers still call `primaryChannel()` independently instead of per-channel `/effective/me` re-resolution.
- **Whisper→channel routing** — waits for a bot-inbox surface (`user.whisper.message` subscribes once as platform tenant `Guid.Empty`; unresolvable whispers attributed to the platform sentinel at ingest).

## Deferred by explicit decision

- The conduit Helix endpoints and conduit mode are built and opt-in (`EventSub:Conduits:Enabled`, off after the 2026-09-29 deaf-bot deploy). Not built: EventSub webhook controller + HMAC verifier (owner question).
- Custom user groups (owner-deferred, streamerbot-parity batch)

(Kick / YouTube / X Live platform connections are decided scope — PRODUCT-ALIGNMENT D1–D3 — built in `SHORTCOMINGS-EXECUTION-PLAN.md` Tier 6.)

---

## Grounding sources (read first, per slice)
`frontend-ia.md` (IA), `roles-permissions.md` (floors §7.1), `commands-pipelines.md`, `music-sr.md`, `moderation.md`, `webhooks.md`, the economy specs, `event-store.md`. Re-run the parity audit if the controller↔surface map is stale. `aitm recall` always.
