# Multi-Platform + Parity Build — TODO Tracker

Durable mirror of the active build push (started 2026-07-09). Complements `ROADMAP.md` (the live
backlog). **This file lists OPEN work only** — a finished item (or finished part of an item) is
DELETED outright (owner, 2026-07-17: anything still listed classifies as not done; there is no DONE
ledger and no completion markers — the git history is the record). Every entry below describes only
what REMAINS.

**Owner directives for this push:**
- Slice by slice, hardest → smallest. One validated vertical slice at a time; commit each.
- **Test cadence (owner, 2026-07-09):** during iterative churn run only the *targeted local tests
  that matter*. Reserve full `dotnet test` + push + CI watch for **meaningful checkpoints**.
- Frontend allowed **shadcn only** (no Material). Never touch `aaoa-dev`'s screens except to relocate.

---

## 🔧 Backend — genuinely open (being finished now)

- [ ] **16. TTS `client_edge` dispatch plane** (`tts.md`) — push a `TtsSpeakPayload` over OverlayHub
  for `client_edge` channels (widget synthesizes edge-side); flip the new-channel `Mode` default to
  `client_edge`. *(in flight)*
- [ ] **ID search/autocomplete endpoints** — backend search for user / custom-data-source / channel
  id inputs so the frontend pickers have data to autocomplete against. *(in flight)*
- [ ] **Import — extend beyond StreamElements data** — SE commands+quotes+timers already import.
  Remaining: whether Streamer.bot exports anything machine-readable we can map, and whether
  provider **overlays** can import at all (proprietary formats → may be an owner/design call, see
  below). *(scouting the real surface before building — no blind import)*
- [ ] **Multi-channel moderation fan-out** (`work`) — one ban/timeout → every platform the viewer
  chats on. Per-platform routing exists (`ChatPlatformRouter`); the moderation controller path is
  Twitch-only. The channel-**link** model (how a user's single-provider tenants aggregate) is an
  owner-design call (below); the fan-out execution is buildable once that's decided.
- [ ] **Chat live-push (backend half)** — verify `DashboardHub` actually broadcasts
  `channel.chat.message` live so the dashboard updates without reload; fix if the backend path is
  missing. *(verifying — may be a pure frontend subscribe/render gap)*
- [ ] **Analytics chart data** — confirm whether a chart is a pure frontend render of existing
  series data or needs a new backend time-series endpoint; build the endpoint only if a metric
  lacks a series. *(verifying)*
- [ ] **Games vs commands double-fire** — confirm/fix whether one chat message can fire both a game
  keyword and a command. *(verifying — the clean separation itself is design/frontend, below)*

## 🎨 Frontend track (aaoa) — backend shipped, UI pending (detail in `handoff/for-frontend.md`)

- [ ] **8. Automation API** — tokens screen (issue/rotate/revoke + show-once secret).
- [ ] **9. OBS control** — config page + bridge status + `/obs-bridge` browser source + palette/trigger entries.
- [ ] **10. VTube Studio** — config page (mode/endpoint/authorize/inventory) + palette/trigger entries + `/obs-bridge` VTS leg.
- [ ] **19. Live overlay games** — Games-page live-sessions panel + the two palette actions.
- [ ] **20. Widget gallery** — submit form + admin review-queue screen.
- [ ] **22. Marketplace / bundles** — export picker, import wizard, installed list, marketplace browse/install/publish.
- [ ] **23. GDPR — "My data" privacy screen** (backend erasure/consent/shred + journal encryption all shipped).
- [ ] **TTS voice picker** — searchable voice list + viewer self-service (search / `!voice` / `/me/voice` backends shipped).
- [ ] **Webhooks pickers** — inbound target-picker (pipeline/event) + outbound event-checklist (both directions + validated catalogue shipped).
- [ ] **Pick-list picker** — the search/autocomplete UI + user-facing rename (pipeline action + `{list.pick}` + preview shipped).
- [ ] **Pipeline block palette** — render from `GET /pipelines/actions` (catalogue endpoint with category/description shipped).
- [ ] **Data-source clarity UI** — explain what a source is + wire the pickers (full poll/socket/trigger/template-var runtime shipped).
- [ ] **Community section UI** — surface the truthful watch-hours/commands data (fake-zero fields fixed on the backend).
- [ ] **Music page** rework: player wiring, reorganization by type, share-link button.
- [ ] **Discord section UI** — guild announce flow + "Also DM members" toggle.
- [ ] **Admin panel screens** — Plane-C IAM, tenants + audit, live AdminHub status panel.
- [ ] **Designer reviews** — title-view emojis; chat styling controls (size/time); chat input wrap;
  analytics metrics row balance; analytics charts render; input+button alignment.
- [ ] **Broad UX polish pass** across the platform (owner's "more intuitive" ask — frontend-led).

## 🔒 Owner calls — gated, cannot close autonomously

- [ ] **24d.** Confirm authz key names (Plane-C + Gate-2 buckets).
- [ ] **Code scripts vs vscode editor** — plus **Bamo's JS-over-C# feedback**: decide the
  user-scripting model + a rich built-in helper library so users never touch C#. *(highest leverage)*
- [ ] **YouTube non-BYOC** — register a Google Cloud OAuth client + pass verification; ship as defaults.
- [ ] **Billing / Stripe** — create the Stripe account + seed `StripePriceId`; then the billing UI (frontend).
- [ ] **Design forks on shipped backends** (each needs an owner decision before its UI/next slice):
  pipelines 6-surface unification; community reposition (loyalty view vs merge away); pick-lists
  user-facing rename; games/commands precedence rule; data-sources push-bridge payload contract;
  federation transport (mTLS/OIDC); multi-channel channel-link model; provider **overlay** import
  feasibility.
