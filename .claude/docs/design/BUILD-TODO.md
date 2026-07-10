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

---

## 📋 Pending

### Foundation tail (deferred from slice 2 — load-bearing only as identity coverage grows)
- [ ] `EventJournal.ActorTwitchUserId` → `ActorExternalUserId` + `ActorProvider` (event-store contracts
  + portability) — **still `ActorTwitchUserId`**.
- [ ] `auth/identities` **link / unlink / set-primary** CRUD (only `GET` list landed) +
  `IViewerMergeParticipant` absorption + chat-ingest → `ResolveUserAsync`.

### 🌐 Multi-platform auth
- [ ] **3. Chat + platform-API seams** — `IChatPlatform` + `IPlatformApi` abstracting send/read/
  channel-ops off Twitch-welded code.

### 🔀 Act on any channel — no install required
- [ ] **4. Moderated-channels resolution + switching** — Helix *Get Moderated Channels*
  (`user:read:moderated_channels`); operate any moderated channel via the caller's own token with the
  bot never installed on it. *(Unblocks the moderated-channels list in the auth UI.)*
- [ ] **5. Render-manifest endpoint** (tier-gated features + integration states + scopes + effective
  role in one call) + `FeaturesController` tier-gating (`IFeatureFlagService`) + dashboard event-class
  subscriptions (chat/activity/liveops/music/moderation + always-on core).

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
