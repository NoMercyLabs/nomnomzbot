# Multi-Platform + Parity Build — TODO Tracker

Durable mirror of the active build push (started 2026-07-09). Complements `ROADMAP.md` (the live
backlog). **This file lists OPEN work only** — a finished item (or finished part of an item) is
DELETED outright (owner, 2026-07-17: anything still listed classifies as not done; there is no DONE
ledger and no completion markers — the git history is the record). Every entry below describes only
what REMAINS.

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
- [ ] **15. Advanced moderation** (`moderation.md`) — remaining: wire the escalation invoker (the
  chat-filter/automod execution path that calls `ResolveAndRecordAsync` when a rule's action is
  `escalate` — the filter execution path itself is not built yet; it belongs with the J.6
  ChatFilter migration).
- [ ] **16. TTS advanced** (`tts.md`) — remaining follow-ons: `client_edge` mode (frontend widget
  handler), the `TtsConfig` TABLE migration + config re-target (adds `WasCensored`/`WasModApproved`/
  `StreamId`/`OccurredAt` to the ledger), BYOK provider factory (§3.2, vaulted keys).
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
- [ ] **(frontend — handoff 2026-07-17)** the music page design/UX rework: player wiring, page
  reorganization by music type, and the share-link button.
- [ ] games and commands are overlapping and need to be clearly separated in their functionality and purpose.
- [ ] pick lists makes no sense to what its purpose is and needs to be reworked to be more user friendly and intuitive.
- [ ] we need way more games.
- [ ] tts needs a full rework, it's not intuitive and there is no way to search for voices properly or even setting your own voice as a viewer.
- [ ] we need WAY more overlays, the current ones are very limited and not very useful for streamers.
- [ ] looks like pipelines need to be reworked into all the other things listed above, and also overhauled to be more user friendly and intuitive and usability.
- [ ] every user id input, source id input, and channel id input needs to be a pick list with search and auto complete functionality.
- [ ] code scripts seems to be completely useless now that we have the proper vscode editor for the overlays.
- [ ] the community section is not useful at all.
- [ ] **(frontend — handoff 2026-07-17)** the discord section UI: surface the guild announce flow
  (connect → enable → announcement wizard → rules list + dispatch log) and the "Also DM members"
  toggle on notify roles.
- [ ] webhooks have no rule, structure and organization and are therefore totally useless and cannot be integrated.
- [ ] federation needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] the entire platform needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] data sources are not clear what they do and how they are used, they need to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] **OWNER-GATED:** youtube non-BYOC auth — register a Google Cloud OAuth client, pass Google's
  app verification for public use, and ship its id/secret as the `YouTube:ClientId/ClientSecret`
  defaults. Only the owner can do this.
- [ ] **(frontend — handoff 2026-07-17)** the admin panel screens: the Plane-C IAM screen
  (promote/roles/assignments/effective permissions), the tenants + audit screens
  (suspend/reinstate/support access), and the live status panel on the AdminHub pushes.
- [ ] **OWNER-GATED + frontend** billing: create the Stripe account and seed `StripePriceId` on the
  tiers (checkout/change-tier are unreachable without it, by design), and build the billing
  dashboard UI (tiers, subscription, invoices, checkout/portal buttons — handoff pending the owner's
  Stripe decision).
