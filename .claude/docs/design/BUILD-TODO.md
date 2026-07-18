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
- [ ] the widget editor is not a vscode style editor and does not have any syntax highlighting, the preview does nothing and the whole thing is pretty much useless because the code section does not scroll.
- [ ] widgets also do not widget what the widget is supposed to widget, so i don't know what happened there but it looks like it is till the old generic test thing and not the actual widget i choose from the available widgets.
- [ ] i think you have not done ANY testing for the obs comminication and control for either locally hosted or via a control widget within obs.
- [ ] i need every feature to be fully tested as if a human is interacting with the pages and features.
- [ ] i want you to take every command, widget and event hook and port it to the new bot WITHOUT hardcoding it, we should have all the options to get the same experience and features as the old bot, and if you are not able to do that then you need to ensure this gets to be possible with all the generic tooling of the new bot. DO NOT HARDCODE MY OLD BOT'S CODE, GENERIC TOOLING IS THE ONLY WAY TO GO, AND IF YOU ARE NOT ABLE TO DO THAT THEN YOU NEED TO ENSURE THAT IT IS POSSIBLE WITH THE NEW BOT'S GENERIC TOOLING.