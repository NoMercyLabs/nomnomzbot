# Handoff — work for the Frontend track (aaoa-dev)

The backend track (`Stoney_Eagle`) leaves frontend work orders here. The frontend track picks up
**Open** items automatically at session start. See `CLAUDE.md` → *Handoff TODOs*.

<!-- Entry template — copy under Open:

### YYYY-MM-DD — short title
- **From:** Stoney_Eagle
- **What:** the concrete change needed (screen, component, wiring)
- **Why:** what changed on the backend / what this enables
- **Where:** files / endpoints / spec sections involved (incl. server/openapi/v1.json if the contract changed)
- **Done when:** acceptance criteria

-->

## Open

### 2026-07-18 — Chat live-push: live dev re-check (no frontend defect found)
- **From:** Stoney_Eagle (via Claude)
- **What:** the dashboard chat live-push wiring was traced end-to-end and found CORRECT (JoinChannel → All classes incl. chat; ChatMessage handler registered + forwarded; ChatScreen subscribes; shell connects the browser-native HubSocket session-wide). No frontend gap found. Needs a live dev check (open dashboard chat, confirm a new message renders without reload). If it still lags, the cause is elsewhere — re-open with the concrete symptom.
- **Done when:** confirmed on dev that live chat renders without reload (or a new symptom is captured).

### 2026-07-11 — Optional: adopt render-manifest + hub event classes for a lighter dashboard
- **From:** aaoa-dev (via Claude). Optional performance optimization; low priority. Adopt the render-manifest + hub event-class subscriptions to reduce dashboard payload/reconnect cost. Done when the dashboard subscribes only to the event classes a page needs.

## Done

_(completed entries move here, with their commit hashes)_

### 2026-07-19 — Music: blocked-tracks management UI — DONE `6f60d86e` (backend `8a285cca`)
- Built directly on the backend track's session (owner directive: parity-campaign work is not deferred).
  "Blocked songs" section on Music (list/unblock/manual block, role-gated), DTOs in `ApiContractTest`,
  palette hints for `song_pause`/`song_resume`/`song_previous`/`playlist_add`/`song_wrong`/`song_ban`.

### 2026-07-17 — Pipelines: ~40 of 66 actions invisible (drifting hand catalogue) — DONE `806980cc`
- The builder's block palette now renders from the backend registry (`GET pipelines/actions` →
  `PipelineCatalogueDto`) instead of the hand-maintained `PipelineCatalogue.kt`, so its membership can never
  drift again — every registered `ICommandAction` appears grouped by its backend category with its description.
  `PipelineCatalogue.kt` is now the local FIELD-HINT layer only: a block with a matching hint renders typed
  fields (+ role / endpoint / pick-list pickers); a hint-less backend block renders a generic key/value editor
  so all ~66 blocks stay configurable. Added typed hints for `send_webhook`, `pick_from_list`, `stop_sound`,
  `start_live_game`, `cancel_live_game` and the `var_compare` condition. DTOs registered in `ApiContractTest`;
  `PipelinesControllerTest` proves an unmodelled block (`obs_switch_scene`) still surfaces. `jvmTest` +
  `compileKotlinWasmJs` green.
- **Still flagged (NOT built — separate future work, not part of this slice):** the bigger "unify the six
  trigger→action surfaces into one model" architectural call (owner decision); and the longer-term builder-UX
  gaps (visual graph builder / then-else branching in the engine / a variable picker / dry-run test-fire).

### 2026-07-17 — Webhooks: inbound endpoints RUN their target + send_webhook action — DONE `7793b6af` `806980cc`
- The inbound create form now exposes the routing choice — "On receive → run a pipeline" (a pipeline picker
  binding `targetPipelineId`) or "→ trigger an event" (`targetEventType`) — with help text that the payload
  reaches vars as `payload.*` plus `webhook.provider` / `webhook.event_type`; each inbound row shows its
  resolved routing target. `CreateInboundBody` gained `targetPipelineId`/`targetEventType` (registered in
  `ApiContractTest`); the controller loads the channel pipelines to back the picker. The outbound `send_webhook`
  pipeline action ships in the palette (endpoint field = an outbound-endpoint picker) via `806980cc`.
  Note: routing is set at CREATE (the backend update can't clear a target); switching an existing endpoint's
  target = delete + recreate. `jvmTest` + `compileKotlinWasmJs` green.

### 2026-07-16 — Event responses ARE the reaction chains — DONE `e63531ce`
- Binding a pipeline is now a first-class create-and-bind flow from the event row (pick an existing pipeline OR
  "Create a new pipeline" → the controller creates it via `PipelinesApi.createReturning` and binds it by id) —
  no more pasting a pipeline id. Surfaces the new `engagement.session_first_message` (+ `first_time_chatter`)
  triggers; the backend tops up new trigger rows on revisit. `EventResponsesControllerTest` proves the
  create-and-bind loop. `jvmTest` + `compileKotlinWasmJs` green.
- **Still flagged (NOT built — IA decision):** the nav-level consolidation of the separate `feature/alerts`
  page INTO this surface. The Event Responses page already lists every trigger (follow/sub/cheer/raid/… — the
  "alerts") and is the functional reaction-chain surface; merging the two nav entries into one page + retiring
  the Alerts screen is a page-inventory (frontend-ia.md) change left for a deliberate IA pass.

### 2026-07-16 — Pre-fill every template input from the preset catalog — DONE `e63531ce`
- `GET event-responses/catalog` (`EventResponsePresetDto`, registered in `ApiContractTest`) is consumed: the
  event-response message input pre-fills with the preset `defaultTemplate` when empty and offers the event's
  seeded `variables` as insert chips; the edit dialog also loads the stored config so fields open pre-filled
  (fixing a prior always-blank bug). Custom commands pre-fill a sensible default template (`Hello {user}!`) on
  create. `jvmTest` + `compileKotlinWasmJs` green.

### 2026-07-10 — Activity feed: show the actor name on follow/sub/cheer/raid events — DONE (backend, option b)
- Resolved backend-side: `NotifyChannelAsync` was hardcoding the top-level `userId`/`userDisplayName`
  to null; the actor-bearing broadcasters (follow, subscription/resub/gift, cheer, raid, shoutouts,
  moderator/VIP role changes) now pass the actor through, and the Kotlin `HubChannelEvent` already
  parsed those fields — the feed renders names with **zero frontend work**. Anonymous gifts/cheers
  arrive as "Anonymous". No `v1.json` change (hub-only contract).

### 2026-07-10 — i18n string bundle re-fetched ~30× on boot — DONE `b6dbfbb1`
- Done by the backend track directly (owner directed the UI work). `core/i18n/BundleCachingResourceReader.kt`
  caches the `.cvr` bundle once per session behind `LocalAppLocale`; boot now reads the bundle a single time
  instead of per-string. Verified in `:composeApp:jvmTest` (green).

### 2026-07-10 — VIP-lowerable actions + quotes:delete split — DONE (frontend consume + `ba9167a4` `5c56dc05` `8a9e305e`)
- Done by the backend track directly (owner directed the UI work). **Superseded the original framing:** the
  final model is NOT "default floors lowered to VIP". Defaults stay at the Twitch base; the broadcaster
  **lowers via a per-action override** down to a VIP floor for non-harmful actions (`ba9167a4`). The dashboard
  reflects this through the new `ResolvedAccessDto.heldActionKeys` (`8a9e305e`, `GET /roles/effective/me`):
  page visibility = `role clears readFloor` **OR** `readActionKey ∈ heldActionKeys`, so a broadcaster-lowered
  page surfaces to a VIP/Sub without changing the two-plane default. Quote add/edit gate on `quotes:write`,
  delete on `quotes:delete` (`5c56dc05`) via disable-with-reason. `ShellNav`/`ShellAccessController`/
  `QuotesAccess` + tests (`ShellNavTest` 14/0, `QuotesAccessTest` 3/0, `ShellAccessControllerTest` 10/0);
  Kotlin DTO field registered in `ApiContractTest` (1/0). `:composeApp:jvmTest` + `compileKotlinWasmJs` green.

### 2026-07-05 — `ShellNavTest` red after sidebar reorder (18159a7) — DONE (reconciled; jvmTest green 14/0)
- Reconciled as part of the `heldActionKeys` shell work: `ShellNav.pages`, the `NavGroup` order, and
  `ShellNavTest` now agree, and `:composeApp:jvmTest` is green (14/14) — verified locally before push. The
  local-only red is cleared.
