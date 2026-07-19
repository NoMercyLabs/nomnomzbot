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

## 🔧 Backend — genuinely open

**None.** Every buildable backend item is shipped + deployed (git history is the record). Finished
this session: TTS `client_edge` dispatch plane, id-picker search endpoints (viewer +
custom-data-source), and the games/commands double-fire fix (an active live round now shadows a
same-named command). The rest that briefly lived here reclassified after root-causing:
- **Chat live-push** and **analytics charts** are **frontend-only** — the backend push path is
  confirmed correct and the daily analytics series already exists (both diagnosed into
  `handoff/for-frontend.md`).
- **Import (Streamer.bot / overlays)** and **multi-channel moderation fan-out** are **owner/design
  calls** (opaque export formats; the channel-link model) — see the design-forks bullet below.

## 🎨 Frontend track — BUILT this session (git history is the record; validated jvmTest + wasm, LOCAL)

Nearly the whole frontend surface shipped locally (waves 1+2): automation tokens · OBS/VTS config +
`/obs-bridge` · live-games UI · widget gallery submit/review · bundles + marketplace · GDPR my-data ·
TTS searchable voices + BYOK + config reshape · webhooks pickers · pick-lists "Random Responses"
rename + picker · pipeline palette-from-catalogue · event-responses reaction-chains + preset prefill ·
community per-viewer · music share + player · chat polls · chat triggers · reward timers + pipeline
picker · discord announce flow + DM · kick tile · multi-channel chat watch · moderation panels
(history/trust/escalation/nuke/shared-ban/standing) · admin (IAM/tenants/audit/AdminHub live) ·
analytics charts + per-stream + metrics-row balance · home real stats · supporters one-step connect ·
desktop device-flow scope re-grant · schedule .ics · the multi-file `src/` editor.

**Frontend remainders (open):**
- [ ] **Broad UX polish pass** ("more intuitive" — ongoing, subjective, frontend-led). Everything else
  in this section shipped this session (data-source clarity UI, editor autocomplete + esbuild-wasm
  preview, chat_box typed settings, emoji title + composer polish, billing UI). Billing UI is built +
  renders an honest "not configured" state; it goes live the moment the owner seeds Stripe (below).

## 🔒 Owner calls — gated, cannot close autonomously

- [ ] **24d.** Confirm authz key names (Plane-C + Gate-2 buckets).
- [ ] **Self-host owner = platform admin?** The owner user has NO IAM principal, so platform-IAM-gated
  features 403 for them (found: federation `peers` requires `IamPermissionKeys.AuditRead`; the Federatie
  screen then shows "Forbidden"). Decide: auto-seed the self-host root owner as a platform-admin IAM
  principal (so operator features work), and/or nav-gate platform-admin screens so they don't surface to
  non-admins. Ties into the federation-transport design fork below.
- [ ] **Code scripts vs vscode editor** — plus **Bamo's JS-over-C# feedback**: decide the
  user-scripting model + a rich built-in helper library so users never touch C#. *(highest leverage)*
- [ ] **YouTube non-BYOC** — register a Google Cloud OAuth client + pass verification; ship as defaults.
- [ ] **Billing / Stripe** — create the Stripe account + seed `StripePriceId`; then the billing UI (frontend).
- [ ] **Design forks on shipped backends** (each a genuine owner/product decision — building blind is
  the "rushed/yolo" failure the owner flagged): **pipelines 6-surface unification** (one trigger→action
  model across commands/event-responses/chat-triggers/timers/redemptions/webhooks — a large refactor);
  **community reposition** (loyalty view vs merge away); **data-sources push-bridge** payload contract;
  **federation transport** (mTLS/OIDC); **multi-channel channel-link + cross-platform ban fan-out**
  (the watch UI shipped; the link model + one-ban-to-all-platforms is the design part); **import**
  feasibility for Streamer.bot (`.sb`) + provider overlays (opaque formats). (Resolved this session:
  pick-lists rename ✓, games/commands precedence ✓.)

## new issues found
- [ ] **OBS real-in-the-loop smoke — OWNER-run on a real OBS** (the deterministic legs are done:
  `ObsRealSocketIntegrationTests` drives the production `ClientWebSocket` against a mock obs-ws v5 server
  on a real port; bridge leader-election/push-ack + the `/obs-bridge` host page have tests; the state-read
  500-on-disconnect bug is fixed). Steps for the owner's machine:
  1. **Direct (self-host):** in OBS enable Tools → WebSocket Server (v5, port 4455). Dashboard → OBS →
     mode `direct`, host `127.0.0.1`, port `4455`, password if set, Enable → Save. The connection card
     should go live; switch a scene / toggle a mic in the mixer and confirm OBS reacts.
  2. **Bridge (remote/SaaS):** Dashboard → OBS → "bridge setup" → copy the `/obs-bridge?token=` URL →
     add it as a Browser Source in OBS (any size, e.g. 1×1). Bridge status should flip to a leader online;
     drive a scene switch from the dashboard and confirm OBS reacts.
- [ ] **Every feature human-tested** — swept ~30 screens live as a human (a11y-tree health + error scan).
  Only defect found = the OBS state-read 500 (fixed); federation peers 403s because the self-host owner has
  no platform-IAM principal (see owner-calls below). Deep-verified: widget editor (highlight/scroll/live Vue
  preview renders the real BSOD), commands dialog, widgets overlay render, widget settings. Not yet clicked
  one-by-one: economy/games knobs, webhooks, sound-clip config, OBS mixer, roles make-a-mod, music/VTS.
- [ ] **Old-bot parity — command diff DONE.** Compared the legacy repo's ~55 command scripts against live:
  every user-facing command is covered by a custom command, a **built-in** (music song/skip/volume,
  song-request, voice, quote, stats, uptime, permits, media, gdpr, games), or a subsystem (shoutout,
  pronouns, blocked-tracks). The one real gap — `!followage` returned "unknown" (stubbed template var) — is
  FIXED (real Helix follow-age). Non-chat legacy files (Commands/Editor/Update/Project/Records) are infra,
  not commands. REMAINS: `{user.messageCount}` still stubbed "0" (superseded by `{viewer.messages}` — decide
  whether to alias or drop); widgets were oracle-validated already; confirm each of the 15 code scripts
  test-runs green on the live channel.