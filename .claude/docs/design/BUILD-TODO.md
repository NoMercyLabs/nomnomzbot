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
- Frontend allowed **shadcn only** (no Material); design bar = the Sleak skill (track split suspended, D7).

---

## 🔧 Backend — open

The open backend work is `SHORTCOMINGS-EXECUTION-PLAN.md`, top to bottom (D8). Nothing is tracked
separately here.

## 🎨 Frontend

**Frontend remainders (open):**
- [ ] **Broad UX polish pass** ("more intuitive" — ongoing, subjective, frontend-led). Everything else
  in this section shipped this session (data-source clarity UI, editor autocomplete + esbuild-wasm
  preview, chat_box typed settings, emoji title + composer polish, billing UI). Billing UI is built +
  renders an honest "not configured" state; it goes live the moment the owner seeds Stripe (below).

## 🔒 Owner calls — gated, cannot close autonomously

- [ ] **24d.** Confirm authz key names — the two OWNER-CONFIRM items (Plane-C key mappings + Gate-2 keys minted 2026-07-04) in `ROADMAP.md` § Security & authorization fixes.
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
  **federation transport** (mTLS/OIDC); **cross-platform ban fan-out** — the grouping model is
  decided: D1 one channel, many platform connections (see `PRODUCT-ALIGNMENT.md`), built in
  `SHORTCOMINGS-EXECUTION-PLAN.md` Tier 6.1 (one ban → every platform connection of the channel);
  **import** feasibility for Streamer.bot (`.sb`) + provider overlays (opaque formats). (Resolved this
  session: pick-lists rename ✓, games/commands precedence ✓.)

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
- [ ] **Old-bot parity — command diff RE-OPENED (2026-08-22).** The earlier "every command covered" claim
  was wrong: 10 legacy commands need backend and have neither builtin nor seed — `!help`, `!commands`,
  `!lurk`/`!unlurk`, `!leaderboard`, `!songhistory`, `!playlist`, `!bansong`, `!whisper`, `!discord`,
  `!accountage` — plus no preset seeds any of the ~28 fun/script commands. Grounded list in
  `usability-shortcomings-audit-scope-and-plan.md` §C7; queued in `SHORTCOMINGS-EXECUTION-PLAN.md`
  Tier 1.3 / 6.7. Still open from before: `{user.messageCount}` stubbed "0" (alias to `{viewer.messages}`
  or drop); confirm each of the 15 code scripts test-runs green on the live channel.





- [ ] individual tokens per widget + rotatable tokens → grounded in `usability-shortcomings-audit-scope-and-plan.md` §B5
- [ ] rendered widget code from the event clicker does not reflect the actual widget. or the rendered widget is correct but the code is not.

## Audit plans (2026-08-20 → 08-22) — the three plans to execute, in this order of reading
- `stability-audit-scope-and-plan.md` (F1–F19) · `widget-quality-audit-scope-and-plan.md` (§1–§8) ·
  `usability-shortcomings-audit-scope-and-plan.md` (Part A = owner-reported 08-22: raid, Spotify SaaS,
  TTS system widget / voice lookup / segment action, Discord go-live UX, template-helper popup; Part B =
  grounded rundown of every other area + runtime stability). Its "Remediation order" section merges all
  three into one sequence.
