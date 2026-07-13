# Handoff — work for the Backend track (Stoney_Eagle)

The frontend track (`aaoa-dev`) leaves backend work orders here. The backend track picks up
**Open** items automatically at session start. See `CLAUDE.md` → *Handoff TODOs*.

<!-- Entry template — copy under Open:

### YYYY-MM-DD — short title
- **From:** aaoa-dev
- **What:** the concrete change needed (endpoint, field, behavior)
- **Why:** what it unblocks on the frontend
- **Where:** files / endpoints / spec sections involved
- **Done when:** acceptance criteria

-->

## Open

### 2026-07-13 — Let a channel-point reward run a pipeline (for reward-triggered sounds)
- **From:** aaoa-dev (via Claude, frontend track)
- **What:** channel-point rewards currently have no `pipelineId` — a reward is pure Twitch CRUD
  (`CreateRewardBody`/`UpdateRewardBody` carry no pipeline, and there is no reward→pipeline dispatch). Add
  an optional `pipelineId` to the reward create/update DTOs + run that pipeline when the reward is redeemed
  (the redemption event already flows through EventSub). Mirror how timers now dispatch their `pipelineId`.
- **Why:** qtkitte item — "attach a sound to a specific channel-point reward". The frontend shipped the rest
  (timers pipeline binding + rotation list, `play_sound` in the pipeline builder, overlay URLs), and a
  sub/command can already play a sound via an event-response/command pipeline with a `play_sound` step. Only
  the reward path is unreachable because a reward cannot reference a pipeline.
- **Where:** `RewardsController` DTOs + the reward-redemption handler (dispatch the bound pipeline). Refresh
  `server/openapi/v1.json`; the frontend then adds a pipeline picker to the reward form (like the timer one).
- **Done when:** a reward can be bound to a pipeline and redeeming it runs the pipeline (so a `play_sound`
  step fires on redemption).

## Done

_(completed entries move here, with their commit hashes)_
