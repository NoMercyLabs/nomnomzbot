# Command / event / timer input-output & state-reliability audit — scope and plan

Full audit against the original ask: "@username replies double-mentioning", plus "many
commands, events and other things need investigation on how they behave for both input
and output", plus "the bot must reliably enable, disable, and ignore events... state
changes must be validated and propagated to the consuming side."

Five investigation lanes ran: (1) reply-mention correctness, (2) `IsEnabled` state
round-trip across every entity, (3) custom/builtin command input+output correctness,
(4) EventSub event-response trigger/condition/variable correctness, (5) timers + config
write-path validation. Two bugs were fixed and merged during the first pass
(`85e7e3ee`, `de582e91`, `f707dce4`). Status 2026-09-30: only findings still open in the code
remain below; fixed ones were deleted.

## 1. Already fixed and merged

### 1a. `@username` double-mention in `!sr` replies — FIXED (`85e7e3ee`)
4 flavored personality tones hardcoded `@{user}` on top of Twitch's native reply
threading. Mutation-tested. Full backend sweep afterward found no other instance of a
bot-authored hardcoded mention going through the threaded-reply path — `SongRequestAction.cs`
/ `SongWrongAction.cs` hardcode `@user` too but send via plain (non-threaded)
`SendMessageAsync`, which is correct as-is.

### 1b. Dead `Reward.IsEnabled` flag — FIXED (`85e7e3ee`)
Disabling a reward in the dashboard didn't stop its pipeline/response from firing on
redemption. Fixed, mutation-tested. Confirmed the only dead flag among 23 swept
`IsEnabled` entities — the other 22 were already correctly live-gated end-to-end
(write → persist → cache-invalidate-or-live-read → runtime-check), and the
`ChannelRegistry` cache-invalidation write paths for commands/builtins/chat-triggers
have no gaps.

## 2. New findings — not yet fixed, ranked by severity

### HIGH

**F2. No minimum-interval floor on timers.**
`TimerManagementService.CreateAsync`/`UpdateAsync` (`Commands/TimerManagementService.cs:190,257-258`)
copies `IntervalMinutes` straight through — no floor, no ceiling, `int` not even
checked for negative. A timer can be configured to fire every tick (chat-spam /
rate-limit hazard). **Fix:** add a minimum-interval validation (tier-scaled per
`limits-safety-baseline-then-tier` house convention) in both Create and Update.

### MEDIUM-HIGH

**F5. A failed timer send never stamps `LastFiredAt`, so the timer retries every 30 s tick.**
`TimerService.FireMessageAsync` (`Commands/Jobs/TimerService.cs:206-253`) returns false when the
transport send fails or the resolved text is blank, and a throw inside `ResolveAsync` is caught by the
per-timer try/catch in `TickAsync` (`:139`). `ProcessTimerAsync` (`:150-203`) only sets
`timer.LastFiredAt = now` after a true return, so the same broken timer retries on **every 30-second poll
tick**, not on its configured interval, with no operator-visible signal beyond a server log. (The
pipeline sibling `FirePipelineAsync` always returns true, so it stamps `LastFiredAt` even on a missing
graph — "never in a 30-second error loop".) **Fix:** stamp `LastFiredAt` regardless of the send outcome
(keep the round-robin advance tied to a real send) and auto-pause the timer (`IsEnabled = false` plus a
dashboard-visible notice) after N consecutive failures.

### MEDIUM

**F6. Unresolved/mistyped template variables leak raw `{{var}}` syntax into live chat.**
`TemplateResolver.Resolve`/`ResolveAsync` (`Platform/Templating/TemplateResolver.cs:92`, and the
async twin) return the literal unresolved token when a variable name isn't found ("leave unknown
variables as-is") instead of substituting empty string. Save-time helper validation now exists for
commands, timers and event responses (`TemplateHelperValidator`), so a typo is caught there — but a
variable that is valid only in another trigger context, or a surface without the save check, still
renders raw template syntax directly into chat, visible to every viewer. **Fix:** degrade unresolved
variables to empty string at render time (strictly safer), and extend the save check to every
remaining template surface.

**F7. Custom commands can silently shadow reserved builtin command names.**
`CommandService.CreateAsync` (`Commands/CommandService.cs:93-96,517-552`) only checks name collision
against other custom commands, never against `IBuiltinCommandCatalog`. Since
`ChatMessageHandler` resolves custom commands before builtins, a broadcaster can
(accidentally or not) create `!uptime` as a custom command and permanently, silently
override the builtin with no warning at create time and no indicator in the builtins
list that it's shadowed. **Fix:** check builtin-name collision on create/update and
either reject or surface a clear "this overrides the builtin `!uptime`" confirmation.

**F8. Re-enabling a timer fires it immediately instead of respecting its interval from
re-enable time.**
`TimerManagementService.ToggleAsync`/`UpdateAsync` (`Commands/TimerManagementService.cs:261-262,317`)
don't touch `LastFiredAt` when flipping `IsEnabled`; `ProcessTimerAsync`'s interval math
(`Commands/Jobs/TimerService.cs:156`, `nextFire = (LastFiredAt ?? MinValue).AddMinutes(IntervalMinutes)`)
means a timer
disabled for days fires on the very next 30-second tick after re-enable. **Fix:** stamp
`LastFiredAt = now` (or `NextFireAt`) when flipping `IsEnabled` false→true.

**F9. Cooldown check-then-set is non-atomic; race window not closed on every ingest
path.**
`CooldownManager` (`Platform/RateLimiting/CooldownManager.cs:27,77`) is a bare
`ConcurrentDictionary` with separate `IsOnCooldown`/`SetCooldown` calls around command execution — a TOCTOU window. Structurally closed for
Twitch today (`WebSocketEventSubTransport` + `EventBus.PublishAsync` serialize one
broadcaster's message handling), but **not proven safe for Kick webhook ingest** or any
future webhook-based provider, where concurrent deliveries could each independently
reach the handler. Separately, `ICooldownManager` is in-memory only — resets on every
restart/deploy and is incorrect across multiple instances if the bot is ever
horizontally scaled (a known, still-unimplemented spec gap per `commands-pipelines.md`
§3.11, which calls for DB write-through via `CommandCooldownStates`). **Fix:** atomic
try-acquire (`ConcurrentDictionary.AddOrUpdate`/compare-and-swap) instead of
check-then-set; separately, decide whether to implement the DB write-through now or
formally defer it (owner call — this is a scaling investment, not a correctness bug at
current single-instance deploy).

**F10. Two enabled `EventResponse` rows can exist for the same (broadcaster, event
type) with non-deterministic winner selection.**
`Commands/Persistence/EventResponseConfiguration.cs:52-54` has a non-unique index on
`(BroadcasterId, EventType)`; `EventResponseExecutor.ExecuteAsync`
(`Platform/Eventing/EventResponseExecutor.cs:144`) picks via `FirstOrDefaultAsync` with no `ORDER BY` —
if a duplicate ever exists (nothing at the service layer prevents it), which one fires is undefined and can shift after
updates/vacuum. **Fix:** enforce a unique (or unique-partial-`WHERE IsEnabled`) index on
`(BroadcasterId, EventType)`, or explicitly define and document ordering semantics if
multiple concurrent responses per trigger are meant to be allowed.

## 4. Second sweep — webhooks, moderation/TTS, sandbox, economy

Covers everything not touched by lanes 1-5: inbound/outbound webhooks, scheduled
pipelines, moderation/chat-filter execution, TTS dispatch, the CodeScript sandbox, and
currency/economy atomicity.

### MEDIUM

**F15. TTS dispatch has no request-volume cap.**
`TtsDispatchService.RequestSpeakAsync` (`Tts/TtsDispatchService.cs:86`) synthesizes/dispatches
inline per call with no bounded queue, no per-channel rate limit, no max-pending-requests guard. A cheap
channel-point redemption or command bound to TTS, spammed by chat, fires concurrent
synthesis+storage calls with nothing capping in-flight count — overlay flooding plus
repeated paid-provider (Azure/ElevenLabs) API cost. Failure handling itself is solid
(proper `Result<T>` failures) — this is
purely a missing volume cap. **Fix:** bound concurrent/pending TTS requests per channel.

### LOW

**F17. ChatFilter regex has no ReDoS check at save time.** Save now compile-checks the pattern
(`Moderation/ChatFilterService.cs:183`), but nothing probes for catastrophic backtracking. It is
mitigated at match time by a 100 ms per-message timeout that fails the match rather than hanging — so
this is a functional gap, not a stability one: a catastrophic-backtracking pattern just silently never
matches its own target input, with no feedback to the broadcaster that their filter is effectively dead.

**F18. Multiple matching ChatFilters — only the oldest (`CreatedAt`) ever enforces**
(`Moderation/EventHandlers/ChatFilterExecutionHandler.cs:79`), deterministic and not racy, but a newer, stricter filter can be silently shadowed by an
older, looser one with no admin-facing warning about the conflict.

**F19. No idempotency wrapper around moderation Helix calls** — if Twitch's own AutoMod
and a NomNomzBot chat filter both act on the same message, a "already banned/timed out"
error from Helix has no defined handling (would surface as an exception rather than
being treated as an already-satisfied success).

### Clean (checked, no issue)

- Inbound webhook auth: constant-time secret compare, HMAC verification with a bounded
  10-minute replay window.
- Outbound webhook SSRF protection: allowlist enforced at both creation and delivery
  time, DNS resolve-then-pin closes the rebind TOCTOU, redirects disabled — no bypass
  found.
- Outbound webhook retry: `WebhookDeliveryWorker` (registered as a hosted service,
  `DependencyInjection.cs:597`) does drain the `NextRetryAt` queue — confirmed
  independently after the audit lane flagged it as unverified; bounded exponential
  backoff, auto-disable after 20 consecutive failures.
- `ScheduledPipelineService`: live-reads on every fire (no `GraphJsonCache`-style
  staleness), terminal-status-before-dispatch ordering prevents double-fire on a crash.
- CodeScript sandbox (Jint/self-host profile): wall-clock timeout, memory/statement/
  recursion caps, graceful degradation on unhandled script exceptions, capped stdout,
  deny-by-default network access — all enforced. Sandbox output flowing into chat
  templates cannot trigger recursive re-expansion (`Regex.Replace` is single-pass) — no
  injection path found. (Wasmtime/SaaS executor is a documented stub that fails closed
  when unconfigured — an incomplete feature, not a bug.)
- Currency overflow: `long` balance fields, no realistic overflow risk.

> **Cross-reference (2026-08-22):** `usability-shortcomings-audit-scope-and-plan.md` adds runtime
> findings that belong with this plan — §B7 (EventSub zero-delay reconnect, reconnect drops broadcaster
> sessions, unhandled `EventSubRevokedEvent`, four no-backoff workers, SQLite WAL, OAuth refresh lock,
> SignalR no backplane/stateful-reconnect), §B4 (scoped `MusicService` holding the queue), §B1
> (`Pipeline.IsEnabled` never checked), and §A1 (pipeline reports Completed after a broken-out run).

## 5. Updated remediation plan

Ordered by severity, open items only:

1. **F5 (MEDIUM-HIGH)** — timer retry-storm fix: stamp `LastFiredAt` regardless of the send outcome,
   auto-pause after N consecutive failures.
2. **F15 (MEDIUM)** — TTS request-volume cap.
3. **F2, F8 (HIGH/MEDIUM)** — timer interval floor; re-enable timestamp reset.
4. **F6, F7, F10 (MEDIUM)** — unresolved-variable leakage at render time; builtin-name shadowing;
   duplicate event-response ordering.
5. **F9 (MEDIUM)** — atomic cooldown check-and-set (DB write-through for cross-instance correctness
   remains a separate scaling decision, not bundled here).
6. **F17, F18, F19 (LOW)** — ReDoS save-time warning, filter-conflict warning, Helix already-actioned
   handling. Lowest priority, no urgency.

### LOW / informational (no fix needed, or owner-judgment call)

- **In-flight permission de-escalation** (command lane): a demoted user's already-admitted,
  long-running pipeline execution completes under the originally-resolved role — this is
  standard gate-at-admission semantics, not a bug, but flagged since it determines
  whether an in-flight mod-only action can complete after a demotion mid-flight.
- **Argument overflow/underflow**: missing `{{args.N}}` leaks the raw token (same root
  cause as F6, not a separate fix); extra args are uncapped but harmless.
- **Error-surfacing pattern itself** (write-path validation lane): confirmed solid across
  `TimerManagementService`/`CommandService`/`PipelineService` — typed `Result.Failure` →
  RFC7807 problem details, no generic 500s. The gap is entirely in *what* gets
  validated (F2, F7), not in how failures are reported once caught.
- **YouTube's `SendReplyAsync` degrading to a plain send** (no reply threading available
  on that platform) — explicitly documented, intentional platform limitation, not a bug.
