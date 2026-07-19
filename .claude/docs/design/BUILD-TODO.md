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
- [ ] **OBS real-in-the-loop smoke** (needs the owner's machine — the deterministic legs are done and
  committed: `ObsRealSocketIntegrationTests` drives the production `ClientWebSocket` against a mock
  obs-ws v5 server on a real port; bridge leader-election/push-ack + the `/obs-bridge` host page have
  tests). What REMAINS is a human smoke against a REAL OBS: (a) self-host direct — point the bot at a
  local OBS-WS and switch a scene from the dashboard; (b) bridge — paste `/obs-bridge?token=` as a
  browser source in OBS and drive it remotely. Provide the exact steps to the owner.
- [ ] **Every feature human-tested** — screen-by-screen click-through as a human (in progress; editor +
  widgets overlay + commands + widget settings done). Continue: economy/games knobs, webhooks,
  sound-clip config, OBS mixer, roles make-a-mod dialog, music/VTS screens.
- [ ] **Old-bot parity completeness pass** — the 204-item bundle is imported + live on the owner channel
  (all generic tables: 38 commands / 50 pipelines / 15 code scripts / 13 rewards / 34 event responses /
  69 pick lists / 35 widgets — nothing hardcoded). REMAINS: diff the OLD bot's full inventory against
  live to prove NOTHING is missing, and confirm each code script test-runs. Extend generic tooling for
  any behavior it can't yet express (never hardcode).