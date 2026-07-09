# Multi-Platform + Parity Build — TODO Tracker

Durable mirror of the active build push (started 2026-07-09). Complements `ROADMAP.md` (the live
backlog): as a slice lands, its line is **removed** here and in ROADMAP — finished work is deleted,
never marked "done" ([[goal-backend-up-to-snuff]]).

**Owner directives for this push:**
- Slice by slice, hardest → smallest. One validated vertical slice at a time; commit each.
- **Test cadence (owner, 2026-07-09):** during iterative churn run only the *targeted local tests
  that matter* — not the full suite, not `gh run watch`, every change. Reserve full `dotnet test` +
  push + CI watch for **meaningful checkpoints** (schema/migration, auth, end-of-slice, full-matrix).
- Frontend allowed **shadcn only** (no Material). Never touch `aaoa-dev`'s screens except to relocate.
- Twitter/X is an extension beyond the current spec enum (`twitch|kick|youtube`) — add when slice 1 lands.

---

## 🔩 Foundation — DONE (2026-07-09, committed `0ea6195` · `8469be0` · `f4f2289`)
- [x] **1A** — `UserIdentity` table + migration pair (SQLite+Postgres) + 17 test fakes;
  `IUserIdentityService` (Resolve + List); `ILoginProviderRegistry` + descriptors (twitch on;
  youtube/kick flagged off); `GET auth/providers` + generic `auth/{provider}/device[/poll]`; JWT `idp`
  claim; `TwitchIdentityBackfillSeeder`.
- [x] **1B** — `PrimaryIdentityWriter`; identity kept live on streamer login + through `ResolveUserAsync`.
- [x] **Channel discriminator** — `Channel.Provider` + `ExternalChannelId` (unique, backfilled) + migration pair.
- [ ] **Deferred into slice 2 (load-bearing only once a non-Twitch identity exists):** nullable
  `User.TwitchUserId` / `Channel.TwitchChannelId` projections; `EventJournal.ActorTwitchUserId` →
  `ActorExternalUserId` + `ActorProvider` (touches event-store contracts + portability); `auth/identities`
  CRUD (link/unlink/primary) + `IViewerMergeParticipant` absorption + chat-ingest→`ResolveUserAsync`.

## 🌐 Multi-platform auth
- [ ] **2. Login providers** — Kick (OAuth2.1+PKCE), YouTube-as-login (Google OAuth), Twitter/X
  (OAuth2.0+PKCE): descriptors + token vault + identity link + feature flags.
- [ ] **3. Chat + platform-API seams** — `IChatPlatform` + `IPlatformApi` abstracting send/read/
  channel-ops off Twitch-welded code.

## 🔀 Act on any channel — no install required
- [ ] **4. Moderated-channels resolution + switching** — Helix *Get Moderated Channels*
  (`user:read:moderated_channels`); operate any moderated channel via the caller's own token with the
  bot never installed on it.
- [ ] **5. Render-manifest endpoint** (tier-gated features + integration states + scopes + effective
  role in one call) + `FeaturesController` tier-gating (`IFeatureFlagService`) + dashboard event-class
  subscriptions (chat/activity/liveops/music/moderation + always-on core).

## 🖥️ Frontend (shadcn only)
- [ ] **6. Landing page on `/` index route** — public, auth-aware redirect.
- [ ] **7. Multi-platform auth UI + channel switcher** — provider picker (device/redirect per provider);
  moderated-channels list.

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

## 🔒 Security & small fixes (last)
- [ ] **24.** Guest Star restore (4 beta topics + translators); `!permit`/`!unpermit` chat commands;
  pipeline `user.role` badge-only fix; builtin key-format (bare keys + repair migration); twitch-helix
  spec/code drift; credential-component DRY; user-plane topic attribution; **owner-confirm** authz key
  names (Plane-C + Gate-2 buckets).
