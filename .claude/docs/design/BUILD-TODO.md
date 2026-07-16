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

## ✅ DONE ledger (backend push)

Everything below shipped and is on `master` (deployed to Proxmox, CI green). Detail lives in the
commits; this is the collapsed record.

- **Foundation** — platform-agnostic `UserIdentity` + migration pairs; `ILoginProviderRegistry`;
  `auth/providers` + generic device flow; `Channel.Provider`/`ExternalChannelId`; nullable Twitch-id
  projections; identity link/unlink/set-primary + merge; platform-agnostic EventJournal attribution.
- **Login providers (slice 2)** — twitch / kick / youtube / x live via `GET /auth/providers`;
  device + auth-code+PKCE seams; credential-guarded providers; compose + `.env.example` wired.
- **Frontend landing + multi-provider auth UI** — provider picker, device/redirect per provider,
  public landing on `/`, channel switcher in the shell.
- **HeldActionKeys in the UI** — page visibility = role clears floor OR holds readActionKey; a
  broadcaster-lowered page reaches a role-less VIP/Sub; quotes:write/delete split.
- **QA correctness pass** — logout clears the refresh cookie; broadcaster can't be banned; reward
  manageability from `only_manageable_rewards`; IAM floors (default = Twitch base, broadcaster lowers
  via override); music OAuth token mirrored to `Service` (fixes re-auth loop); bot "add permission"
  clears on grant; device-code copy-link; i18n bundle cached; CI cancels superseded runs.
- **Live-ops correctness (A–E)** — EventSub subs ride each channel's OWN broadcaster token;
  per-broadcaster WS sessions (one session = one token owner); `stream.offline` subscribed + poll
  backstop; non-chat events carry the actor; title-edit permission elevation (Gate-2 allows on level
  OR a capability grant); duplicate `ChannelMemberships` collapsed + partial unique index.
- **Multi-platform chat (3a–3b)** — `IChatPlatform` seam + `ChatPlatformRouter` (Twitch/YouTube/Kick,
  Twitch fallback; commands+replies work on YouTube); YouTube moderation (ban/timeout/delete) + unban
  ban-id ledger; `IPlatformChannelApi` stream-info write seam; Kick send + native replies + moderation
  + webhook chat read + streamer connect (`kick.chat` scopes).
- **Act on any channel (4)** — `GET /channels` resolves moderated channels via Helix + auto-grants
  Moderator memberships; tenant resolution lets an operator act on any channel they moderate.
- **Render manifest + tier gating + push classes (5)** — `render-manifest` aggregates access/features/
  integrations/scope-gaps; `FeatureService` consults entitlement; `JoinChannelClasses` subset pushes.
- **Combined chat backend (6, 7)** — `ChatMessageReceivedEvent` is THE canonical chat fact for every
  platform; YouTube poll worker provisions a tenant channel on go-live; `DashboardHub` is set-based
  (a connection watches many channels). *(Frontend merged/multi-watch UI → for-frontend.)*
- **qtkitte requests (SR1–SR3)** — rotating auto-shoutouts (timer `PipelineId` dispatch); walk-in
  sounds end-to-end (`OverlayHostController` + audio bus); built-in widget rendering (alerts / now-
  playing / hype-train).
- **Analytics writers** — peak viewers / unique chatters / watch seconds now have real writers.
- **Parity backends** — 11 Media share, 12 Giveaways, 14 Per-viewer data store (+ NamedCounter),
  17 Live-ops schedule & markers, 18 Engagement triggers. *(Each: backend shipped; dashboard → handoff.)*
- **Security & small fixes** — 24a Guest Star verified; 24b `!permit`/`!unpermit` OOTB; 24c-1 pipeline
  `user.role` effective-role (+ `!skip`/`!volume` floor fix); 24c-2a builtin bare-key format; 24c-2b
  twitch-helix spec/code drift; 24c-2d user-plane topic attribution (`user.whisper.message` fixed).

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
- [ ] triggers need to be able to be added to any type of event for example a trigger for when a user joins the channel or leaves the channel, types their first message of the stream we can add personalized welcome overlays and sound alerts for these events. but not limited to just these events, events chain and do multiple followup actions for example when a user joins the channel we can have a trigger that plays a sound alert and then shows an overlay and then sends a message in chat welcoming the user.
- [ ] start, stop and pause a timer for certain reward redemptions that are time limited, for example streamer does x for y amount of time and then the timer stops and the reward is marked as completed.
- [ ] Allow users to customize the appearance of chat messages, including font, color, and background.
- [ ] Allow users to create and manage custom chat commands, including the ability to set permissions for who can use them.
- [ ] Allow users to set up automated responses for specific chat messages or commands.
- [ ] multi channel chat merging, for example if a user is streaming on multiple platforms at the same time, they can merge all chat messages into one unified chat window.
- [ ] multi channel chat moderation, for example if a user is streaming on multiple platforms at the same time, they can moderate all chat messages from one unified chat window.
- [ ] Allow users to set up custom alerts for specific events, such as new followers, subscribers, or donations.
- [ ] Allow users to create and manage custom overlays for their streams, including the ability to add text, images, and animations.
- [ ] Allow users to set up automated actions based on specific events, such as triggering a sound effect or displaying a message in chat when a new follower is detected.
- [ ] Allow users to create and manage custom polls and surveys for their streams, including the ability to set up multiple-choice questions and view results in real-time.
- [ ] the music page is a disorganized mess, it needs to be cleaned up and organized in a way that makes sense for the user. for example, the music page should have a clear separation between the different types of music (e.g. background music, sound effects, etc.) and should have a clear way to manage and organize the different types of music. the player does not update the progress bar when a song is playing, it only updates when the song is paused or stopped. the player does not have a way to skip to the next song in the queue, it only has a way to pause or stop the current song. the player does not have a way to shuffle the songs in the queue, it only plays them in the order they were added. the player does not have a way to repeat the current song or the entire queue, it only plays them once and then stops. there is also no way of sharing the public version of that page because that page link is just a token.
- [ ] games and commands are overlapping and need to be clearly separated in their functionality and purpose. 
- [ ] the event responses need to get the full reaction chain mentioned above, for example if a user joins the channel we can have a trigger that plays a sound alert and then shows an overlay and then sends a message in chat welcoming the user. but not limited to just these events, events chain and do multiple followup actions for example when a user joins the channel we can have a trigger that plays a sound alert and then shows an overlay and then sends a message in chat welcoming the user.
- [ ] pick lists makes no sense to what its purpose is and needs to be reworked to be more user friendly and intuitive.
- [ ] moderation needs to be more robust and allow for more granular control over what actions can be taken against users, for example if a user is banned from the channel they should not be able to send messages in chat or interact with the stream in any way. but if a user is timed out they should still be able to send messages in chat but not be able to interact with the stream in any way. but if a user is muted they should still be able to send messages in chat but not be able to interact with the stream in any way. but if a user is shadow banned they should still be able to send messages in chat but not be able to interact with the stream in any way. but if a user is blacklisted they should not be able to send messages in chat or interact with the stream in any way.
- [ ] we need way more games.
- [ ] tts needs a full rework, it's not intuitive and there is no way to search for voices properly or even setting your own voice as a viewer.
- [ ] we need WAY more overlays, the current ones are very limited and not very useful for streamers.
- [ ] alerts and events need to be merged with event responses and triggers, they are all the same thing and should be treated as such.
- [ ] looks like pipelines need to be reworked into all the other things listed above, and also overhauled to be more user friendly and intuitive and usability.
- [ ] every user id input, source id input, and channel id input needs to be a pick list with search and auto complete functionality.
- [ ] code scripts seems to be completely useless now that we have the proper vscode editor for the overlays.
- [ ] analytics, chat and everything stream related needs to be shown stream by stream and not all-time.
- [ ] the community section is not useful at all.
- [ ] the discord section is not useful at all, there is no way of adding it properly to a guild's channel, and there is no option for a user to get personal live notification dm's.
- [ ] webhooks have no rule, structure and organization and are therefore totally useless and cannot be integrated.
- [ ] federation needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] the entire platform needs to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] data sources are not clear what they do and how they are used, they need to be reworked and overhauled to be more user friendly and intuitive and usability.
- [ ] after redirecting from auth providers, the user is not redirected back to the page they were on before the redirect, instead they are redirected to the home page.
- [ ] youtube needs a non-byoc provider for auth.
- [ ] the bot personality responses need to be better, no emoji bullshit and be properly taken an example from my current bot. my current bots responses are way funnier and sassy and you need to do better.
- [ ] i am missing the pre-filled templates in all the template input fields.
- [ ] the dashboard home screen needs to be more useful in showing important information and stats about the stream, for example the current viewers, chatters, followers, subscribers, donations, platforms streaming to, etc.
- [ ] the bot account is not sending messages in chat with the app token on other channels, this is bad UX.
- [ ] the admin pannel for the saas is just show and does not do anything useful, it does not let me change behavior or go into details of things. i can not promote accounts to admin or grant them specific saas permissions.
- [ ] billing does not seem to be doing anything.
- 