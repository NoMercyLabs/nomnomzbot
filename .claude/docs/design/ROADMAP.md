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

## Small decided items

- **Credential component DRY unification** — the client-setup credential components are still duplicated.
- **Multi-channel residuals** — `Provider` discriminator on `Channel`; individual page controllers still call `primaryChannel()` independently instead of per-channel `/effective/me` re-resolution.
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

- SaaS conduit EventSub transport (self-host WebSocket path is complete)
- Custom user groups (owner-deferred, streamerbot-parity batch)
- YouTube/Kick chat platforms (after Twitch is complete)

---

## Grounding sources (read first, per slice)
`frontend-ia.md` (IA), `roles-permissions.md` (floors §7.1), `commands-pipelines.md`, `music-sr.md`, `moderation.md`, `webhooks.md`, the economy specs, `event-store.md`. Re-run the parity audit if the controller↔surface map is stale. `aitm recall` always.
