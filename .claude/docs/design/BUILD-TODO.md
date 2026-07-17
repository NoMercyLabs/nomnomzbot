# Multi-Platform + Parity Build — TODO Tracker

Durable mirror of the active build push (started 2026-07-09). Complements `ROADMAP.md` (the live
backlog). **This file lists OPEN work only** — a finished item is DELETED outright (owner,
2026-07-17: anything still listed classifies as not done; there is no DONE ledger — the git
history is the record). `[~]` marks an item whose backend is complete with only the other track's
half remaining (per the handoff entry named in its annotation).

**Owner directives for this push:**
- Slice by slice, hardest → smallest. One validated vertical slice at a time; commit each.
- **Test cadence (owner, 2026-07-09):** during iterative churn run only the *targeted local tests
  that matter* — not the full suite, not `gh run watch`, every change. Reserve full `dotnet test` +
  push + CI watch for **meaningful checkpoints** (schema/migration, auth, end-of-slice, full-matrix).
- Frontend allowed **shadcn only** (no Material). Never touch `aaoa-dev`'s screens except to relocate.
- Twitter/X is an extension beyond the current spec enum (`twitch|kick|youtube`) — added in slice 2.

---

## 📋 Pending — open backend work only

### 🤖 StreamElements / Streamer.bot parity (remaining — each = backend + dashboard page)
- [ ] **8. Automation API** (`automation-api.md`) — external tokens, event catalog, data plane,
  WebSocket stream. *(Streamer.bot core.)*
- [ ] **9. OBS control** (`obs-control.md`) — scenes/inputs, ~20 pipeline actions, `obs_event`.
- [ ] **10. VTube Studio** (`vtube-studio.md`) — connect/authorize/bridge, model control, `vts_event`.
- [~] **15. Advanced moderation** (`moderation.md`) — the truthful-reads foundation + the full per-user
  panel (context/notes/warn/suspicious/ban), unban-request queue, network un-nuke, and **viewer reports**
  (first entity leg) all SHIPPED. *Remaining entity legs:* SuperMod platform `moderation:nuke` (tenant-
  wide, distinct from the operator fan-out), shared-ban trust list + propagation (J.9/J.9a), escalation
  ladder (J.10/J.11), the J.4/J.5 history+trust projections.
- [~] **16. TTS advanced** (`tts.md`) — TTS-plays (self_host `play_tts`), per-viewer voice write,
  profanity censor, and the mod approval queue all SHIPPED. *Remaining follow-ons:* `client_edge` mode
  (frontend widget handler), the `TtsConfig` TABLE migration + config re-target (adds `WasCensored`/
  `WasModApproved`/`StreamId`/`OccurredAt` to the ledger), BYOK provider factory (§3.2, vaulted keys),
  tier-cap resolution via `IBillingTierService`.
- [ ] **19. Live overlay games** (`live-games.md`) — session lifecycle + game catalog/manifest.
- [ ] **20. Widget gallery + overlay manifest** (`widgets-overlays.md`) — gallery, `OverlayController`
  manifest, widget versions/build (the compiled-bundle/gallery/import pipeline).
- [ ] **21. Stream Deck** (`stream-deck.md`) — pairing-code flow on automation tokens.
- [ ] **22. Marketplace / bundles** (`marketplace.md`) — export/inspect/import/uninstall;
  browse/install/publish.
- [ ] **23. GDPR/compliance + IPC dev-mode controllers** (`gdpr-crypto.md`, `stream-admin.md`).

### 🔒 Security & small fixes
- [ ] **24d. OWNER-GATED:** confirm authz key names (Plane-C + Gate-2 buckets) — cannot close
  autonomously.

## 🖌️ Designer reviews (open)
- [ ] Dashboard Title view needs to support emojis
- [ ] Chat needs styling controls (size, time and so on, like Twitch)
- [ ] Chat input doesn't wrap but overflows right
- [ ] StreamElements import: let a user grab their overlays, quotes, timers and all
- [ ] Analytics metrics row should balance itself when possible
- [ ] Analytics could benefit from some charts
- [ ] Import things like quotes, timers, overlays from other providers (StreamElements, Streamer.bot, etc)
- [ ] Ensure input + button combo are aligning the input itself and the button right now label+input is centered to button

## work
- [ ] Allow users to customize the appearance of chat messages, including font, color, and background.
- [ ] Allow users to create and manage custom chat commands, including the ability to set permissions for who can use them.
- [ ] multi channel chat merging, for example if a user is streaming on multiple platforms at the same time, they can merge all chat messages into one unified chat window.
- [ ] multi channel chat moderation, for example if a user is streaming on multiple platforms at the same time, they can moderate all chat messages from one unified chat window.
- [ ] Allow users to set up custom alerts for specific events, such as new followers, subscribers, or donations.
- [ ] Allow users to create and manage custom overlays for their streams, including the ability to add text, images, and animations.
- [ ] Allow users to set up automated actions based on specific events, such as triggering a sound effect or displaying a message in chat when a new follower is detected.
- [~] the music page is a disorganized mess and needs a design/UX rework (separation of music types, page organization). *(Backend is COMPLETE for every functional gap listed: skip/pause/resume/seek + shuffle + repeat + now-playing-with-progress endpoints all exist on `MusicController`, and the shareable public link shipped as `public/sr/by-channel/{channelName}` — the player wiring + page reorganization + share button are frontend (handoff 2026-07-17). Remaining here: nothing backend.)*
- [ ] games and commands are overlapping and need to be clearly separated in their functionality and purpose. 
- [ ] pick lists makes no sense to what its purpose is and needs to be reworked to be more user friendly and intuitive.
- [ ] we need way more games.
- [ ] tts needs a full rework, it's not intuitive and there is no way to search for voices properly or even setting your own voice as a viewer.
- [ ] we need WAY more overlays, the current ones are very limited and not very useful for streamers.
- [ ] looks like pipelines need to be reworked into all the other things listed above, and also overhauled to be more user friendly and intuitive and usability.
- [ ] every user id input, source id input, and channel id input needs to be a pick list with search and auto complete functionality.
- [ ] code scripts seems to be completely useless now that we have the proper vscode editor for the overlays.
- [ ] the community section is not useful at all.
- [~] the discord section is not useful at all, there is no way of adding it properly to a guild's channel, and there is no option for a user to get personal live notification dm's. *(Backend COMPLETE: guild announce flow was already built end-to-end (install → streamer-enabled → live channel/role pickers → go_live config → dedupe dispatch + log); personal live DMs shipped — notify roles carry `dmEnabled`, opted-in members get the rendered go-live as a DM, per-member dedupe/best-effort, DM channel cached. Remaining is FRONTEND-ONLY: surface the flow + the "Also DM members" toggle — handoff entry in `handoff/for-frontend.md` 2026-07-17.)*
- [ ] webhooks have no rule, structure and organization and are therefore totally useless and cannot be integrated.
- [ ] federation needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] the entire platform needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] data sources are not clear what they do and how they are used, they need to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] **OWNER-GATED:** youtube needs a non-byoc provider for auth. *(Code is DONE — `GoogleYouTubeLoginProvider` resolves via `ISystemCredentialsProvider` (DB-vaulted → shipped config default), same pattern as Twitch's shared `aajly3` default. Remaining is operational: register a Google Cloud OAuth client, pass Google's app verification for public use, and ship its id/secret as the `YouTube:ClientId/ClientSecret` defaults — only the owner can do this.)*
- [~] the admin pannel for the saas is just show and does not do anything useful, it does not let me change behavior or go into details of things. i can not promote accounts to admin or grant them specific saas permissions. *(Backend COMPLETE across 3 legs: `PlatformIamController` (§5.4) — promote user → principal, role/principal lists, assign/revoke, effective permissions, deactivate/reactivate with `User.IsPlatformPrincipal` wiring; `PlatformAdminController` (stream-admin §3.2/§5) — tenant list/detail, suspend/reinstate (ENFORCED: Gate-1 403 + bot lifecycle drop), audited support access, Plane-C audit search; AdminHub is LIVE — `ReceiveSystemStatus` heartbeat every 15s + channel online/offline registry updates + suspension log lines; `admin/health` now runs the REAL registered probes + bot token-readiness (canned list gone). Feature flags + billing grants were already functional. Remaining is FRONTEND-ONLY: the IAM screen, tenants/audit screens, live panel wiring — handoff entries 2026-07-17.)*
- [ ] billing does not seem to be doing anything. *(Scouted 2026-07-17 — verdict: infrastructure-complete, behavior-inert. SHIPPED since: (1) the Stripe webhook loop CLOSES — verified events are mapped (incl. newer API shapes) and dispatched to the ApplyStripe* appliers, so checkout → local Subscription/Invoice convergence works once Stripe is configured; (2) tier quotas are ENFORCED — custom_commands/timers caps at create, response_variations_per_trigger at create+update, event_responses counts ENABLED responses at the enable path, tts_max_characters clamps the dispatch cap (stricter of streamer setting and plan), and `sandbox_exec_ms` is seeded per tier with the seeder now backfilling new limit keys onto existing tiers (never overwriting tuned values). Remaining decidable leg: self-serve `ChangeTierAsync` (hard stub; needs a Stripe proration call on the gateway). OWNER-GATED: a real Stripe account + seeding `StripePriceId` on the tiers — without it checkout is unreachable by design. The invite/founder/admin-grant path was already end-to-end functional.)*
- 