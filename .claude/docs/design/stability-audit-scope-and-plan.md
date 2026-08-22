# Command / event / timer input-output & state-reliability audit — scope and plan

Full audit against the original ask: "@username replies double-mentioning", plus "many
commands, events and other things need investigation on how they behave for both input
and output", plus "the bot must reliably enable, disable, and ignore events... state
changes must be validated and propagated to the consuming side."

Five investigation lanes ran: (1) reply-mention correctness, (2) `IsEnabled` state
round-trip across every entity, (3) custom/builtin command input+output correctness,
(4) EventSub event-response trigger/condition/variable correctness, (5) timers + config
write-path validation. Two bugs were fixed and merged during the first pass
(`85e7e3ee`, `de582e91`, `f707dce4`); everything below from lanes 3-5 is newly found and
**not yet fixed**.

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

### 1c. `ConfigChanged`/`RewardChanged` SignalR never wired on frontend — FILED
9 backend services publish live config-change events the dashboard never consumes
(`HubEvent.kt` has no case for either method). Filed to `handoff/for-frontend.md` with
exact fix location — `app/` is the frontend track's commit scope, not backend's.

## 2. New findings — not yet fixed, ranked by severity

### CRITICAL

**F1. Pipeline graphs are saved with zero structural/security validation.**
`PipelineService.CreateAsync`/`UpdateAsync` (`Commands/PipelineService.cs:92-159`)
writes `GraphJsonCache` straight to the DB with no check for unknown action/condition
types, no step-count cap, no credential/secret-leakage guard. A validator that enforces
exactly these things already exists (`CommandConfigValidator`,
`Platform/Pipeline/CommandConfigValidator.cs`) but is wired only to the optional
`POST pipelines/validate` endpoint the editor *may* call before save — Create/Update
never call it. Any client that skips that call (or hits the API directly — automation,
marketplace import) can persist a broken/dangerous graph that only fails live in front
of real viewers, at execution time, inside `PipelineEngine`'s fail-closed step handling.
**Fix:** call `CommandConfigValidator` synchronously inside `PipelineService.CreateAsync`/
`UpdateAsync` before save; reject with the existing validator's typed errors through the
Result/RFC7807 path already used elsewhere in this service.

**Sibling check (repo-wide grep for other `GraphJsonCache`/`PipelineJson` writers):**
`AutomationPairingService.cs:463` (`EnsureMusicActionPipelinesAsync`) also writes
`GraphJsonCache` directly, outside `PipelineService` — but it's server-generated from the
live `ICommandAction` registry (`actionType`), not user input, so it carries no
injection/credential-leak surface and does not need the validator. Named here so the F1
fix's scope is deliberate (only `PipelineService`'s two user-facing write paths) rather
than accidentally missing this one. `Reward.PipelineJson` is the only other inline-JSON
pipeline field in the domain model; confirmed no code path writes to it (dead/vestigial —
`commands-pipelines.md` §3.3 already calls for replacing it with `PipelineId`), so it is
not a live sibling and needs no fix.

### HIGH

**F2. No minimum-interval floor on timers.**
`TimerManagementService.CreateAsync`/`UpdateAsync` (`TimerManagementService.cs:99-216`)
copies `IntervalMinutes` straight through — no floor, no ceiling, `int` not even
checked for negative. A timer can be configured to fire every tick (chat-spam /
rate-limit hazard). **Fix:** add a minimum-interval validation (tier-scaled per
`limits-safety-baseline-then-tier` house convention) in both Create and Update.

**F3. Timer-bound pipelines read a cache that can silently go stale.**
`TimerService.FirePipelineAsync` reads `Pipeline.GraphJsonCache` directly and never
queries `PipelineStep` rows — unlike `PipelineEngine.ExecuteAsync` (used by
commands/rewards), which prefers `PipelineStep` rows and only falls back to the cache.
No code path was found that regenerates `GraphJsonCache` automatically when
`PipelineStep` rows are edited via the step-level editor; `PipelineService.UpdateAsync`
only touches `GraphJsonCache` when the caller explicitly supplies it. A timer bound to a
pipeline can keep firing a stale graph indefinitely after an edit that only touched
step rows. **Fix:** either make `TimerService.FirePipelineAsync` resolve through the same
`PipelineStep`-first path `PipelineEngine` uses, or regenerate `GraphJsonCache`
synchronously on every `PipelineStep` write.

### MEDIUM-HIGH

**F4. Failed/oversized chat sends are silently swallowed and reported as "succeeded".**
`ChatMessageHandler.SendResponseAsync` (`ChatMessageHandler.cs:492-502`) discards the
`bool` returned by `SendMessageAsync`; `SendReplyAsync` returns plain `Task` with no
success signal at all. `PublishExecutedAsync` is then called unconditionally with
`succeeded: true` — an oversized (Twitch ~500 char / YouTube ~200 char) or
provider-rejected message is recorded as a successful execution in analytics/the
dashboard feed while the viewer sees nothing in chat; only a server log line has the
truth. Confirmed on all three platforms (Twitch has no local length guard at all; Kick
has a local guard but its result is discarded the same way; YouTube has no guard and a
shorter real limit than Twitch, so a template that fits on Twitch/Kick can silently fail
there). **Fix:** thread the real send outcome through to `PublishExecutedAsync`; give
`SendReplyAsync` a `Task<bool>` return.

**F5. Timer fire-time failures retry every 30s forever instead of respecting the
configured interval.**
`TimerService.FireMessageAsync`/`TickAsync` (`TimerService.cs:118-223`) updates
`LastFiredAt` only *after* a successful send — if template resolution throws (not one of
the internally-degraded lookup helpers, but `ResolveAsync` itself), the exception is
caught by the per-timer try/catch, logged as a warning, and `LastFiredAt` is never
advanced, so the same broken timer retries on **every 30-second poll tick forever**, not
on its configured interval, with no operator-visible signal beyond a server log. (The
pipeline-firing sibling path, `FirePipelineAsync`, already handles this correctly —
advances `LastFiredAt` even on a missing graph, explicitly documented "never in a
30-second error loop.") **Fix:** apply the same `LastFiredAt`-advances-regardless pattern
to `FireMessageAsync`, and surface a dashboard-visible signal (e.g. an `IsEnabled`
auto-pause + notification) after N consecutive failures.

### MEDIUM

**F6. Unresolved/mistyped template variables leak raw `{{var}}` syntax into live chat.**
`TemplateResolver.Resolve`/`ResolveAsync` (`TemplateResolver.cs:91,149`) both fall back
to returning the literal unresolved token when a variable name isn't found, rather than
substituting empty string. A typo'd variable name or a variable valid only in one
trigger context used in another renders raw template syntax directly into chat — visible
to every viewer, no author-time validation of variable names against the known
catalogue exists either. **Fix:** either validate variable names against the known
catalogue at command/event-response/timer save time (preferred — matches this repo's
"validate on write" convention), or degrade unresolved variables to empty string instead
of leaking syntax (a worse but strictly-safer fallback).

**F7. Custom commands can silently shadow reserved builtin command names.**
`CommandService.CreateAsync` (`CommandService.cs:59-67`) only checks name collision
against other custom commands, never against `IBuiltinCommandCatalog`. Since
`ChatMessageHandler` resolves custom commands before builtins, a broadcaster can
(accidentally or not) create `!uptime` as a custom command and permanently, silently
override the builtin with no warning at create time and no indicator in the builtins
list that it's shadowed. **Fix:** check builtin-name collision on create/update and
either reject or surface a clear "this overrides the builtin `!uptime`" confirmation.

**F8. Re-enabling a timer fires it immediately instead of respecting its interval from
re-enable time.**
`TimerManagementService.ToggleAsync`/`UpdateAsync` don't touch `LastFiredAt` when
flipping `IsEnabled`; `ProcessTimerAsync`'s interval math
(`nextFire = (LastFiredAt ?? MinValue).AddMinutes(IntervalMinutes)`) means a timer
disabled for days fires on the very next 30-second tick after re-enable. **Fix:** stamp
`LastFiredAt = now` (or `NextFireAt`) when flipping `IsEnabled` false→true.

**F9. Cooldown check-then-set is non-atomic; race window not closed on every ingest
path.**
`CooldownManager` is a bare `ConcurrentDictionary` with separate `IsOnCooldown`/
`SetCooldown` calls around command execution — a TOCTOU window. Structurally closed for
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
`EventResponseConfiguration.cs:52-54` has a non-unique index on
`(BroadcasterId, EventType)`; `EventResponseExecutor.ExecuteAsync` picks via
`FirstOrDefaultAsync` with no `ORDER BY` — if a duplicate ever exists (nothing at the
service layer prevents it), which one fires is undefined and can shift after
updates/vacuum. **Fix:** enforce a unique (or unique-partial-`WHERE IsEnabled`) index on
`(BroadcasterId, EventType)`, or explicitly define and document ordering semantics if
multiple concurrent responses per trigger are meant to be allowed.

## 4. Second sweep — webhooks, moderation/TTS, sandbox, economy

Covers everything not touched by lanes 1-5: inbound/outbound webhooks, scheduled
pipelines, moderation/chat-filter execution, TTS dispatch, the CodeScript sandbox, and
currency/economy atomicity.

### HIGH

**F11. Currency balance is a read-modify-write race — real double-spend.**
`CurrencyAccountService.AppendAsync` (`Economy/CurrencyAccountService.cs:323-380`) reads
`account.Balance` via a plain `FirstOrDefaultAsync` (no row lock, no concurrency token —
`CurrencyAccount` has neither `RowVersion` nor `[ConcurrencyCheck]`), computes
`newBalance` in memory, validates it, then saves. Two concurrent calls for the same
account (two rapid earns, or an earn racing a purchase debit) can both read the same
starting balance and the second `SaveChangesAsync` silently overwrites the first's
update — one award is lost, or (in `CatalogService.PurchaseAsync`, which inherits the
same unlocked check) a user with exactly enough currency for one item can complete two
near-simultaneous purchases before either debit commits: a genuine double-spend, not
just a stock-count race. The negative-balance guard is correct for a single call but
inherits the same race, so concurrent calls can jointly drive the balance negative
despite the check. **Fix:** replace the read-modify-write with an atomic
`UPDATE ... SET balance = balance + @amount WHERE id = @id AND balance + @amount >= 0`
(via `ExecuteUpdateAsync`/raw SQL, using affected-row-count as the success signal), or
add a `RowVersion` concurrency token with retry-on-conflict.

**Sibling check (repo-wide search for the same shape — "read a balance-like field into
memory, mutate, `SaveChangesAsync`, no lock/concurrency token"):** confirmed via grep
that **no entity in the entire domain model uses `RowVersion`/`[ConcurrencyCheck]`** —
this isn't scoped to `CurrencyAccount`. A targeted search for in-memory
increment/decrement on balance-shaped fields found one more live instance beyond
`CurrencyAccount.Balance` (F11) and `ModerationEscalationState.OffenseCount` (F13):
`SavingsJarService.cs:295,393` (`ContributeAsync`/`WithdrawAsync`) — `jar.Balance +=`/`-=`
against a plain `FindJarAsync` read, with `WithdrawAsync`'s insufficient-balance check
(`jar.Balance < request.Amount`, line 369) reading the same stale in-memory value. Two
concurrent withdrawals from the same jar can both pass the check and jointly overdraw
it — identical race shape to F11, different entity. Folded into the same fix as F11/F13
below (F11b) rather than filed as a separate item, since it's the same root cause and
the same fix pattern applies.

### MEDIUM

**F12. Earning-rule dedup is check-then-act, not atomic (TOCTOU, narrow window).**
`CurrencyEarningService.ApplyEarningAsync` does an `AnyAsync` existence check on
`(BroadcasterId, ViewerUserId, EventId, EntryType)` *before* calling
`PostLedgerEntryAsync`, which opens its own separate transaction to insert the row. No
unique DB constraint enforces this at the database level — only the pre-check. Two
concurrent deliveries of the same event (e.g. an EventSub redelivery landing before the
first post commits) could both pass the check and both credit. **Fix:** add a unique
index on `(BroadcasterId, ViewerUserId, EventId, EntryType)` on the ledger-entries
table so the insert itself fails closed on a duplicate, rather than relying on the
pre-check alone.

**F13. Moderation escalation-ladder increment has the same lost-update shape as F11.**
`ModerationEscalationService.ResolveAndRecordAsync` reads `OffenseCount`, increments in
memory, saves — no concurrency token on `ModerationEscalationState`. Two near-simultaneous
violations from the same user (two filters both configured to escalate, or two rapid
messages) can both read the same starting count and the second write overwrites rather
than compounds — two real offenses collapse into one escalation tier (under-escalation).
**Fix:** same pattern as F11 — atomic increment or a concurrency token with retry.

**F14. TTS `VoiceIdOverride` is interpolated unescaped into SSML.**
`AzureTtsProvider.cs:60` escapes the spoken `text` correctly
(`SecurityElement.Escape`) but interpolates `voiceId` raw:
`<voice name='{voiceId}'>`. Normal flows validate voice IDs against the catalogue
before persisting, but `TtsDispatchService.ResolveVoiceAsync` has a bypass:
`request.VoiceIdOverride`, when set, is used immediately with no catalogue/format
validation. It's populated from broadcaster/mod-authored surfaces only (a pipeline
action config, and the custom-code/JS scripting bridge) — not raw viewer chat — but a
buggy or malicious script could still inject SSML structure via this field. **Fix:**
escape `voiceId` the same way `text` already is, at the point of interpolation (defense
at the source, not reliant on every caller validating).

**F15. TTS dispatch has no request-volume cap.**
`TtsDispatchService.RequestSpeakAsync` synthesizes/dispatches inline per call with no
bounded queue, no per-channel rate limit, no max-pending-requests guard. A cheap
channel-point redemption or command bound to TTS, spammed by chat, fires concurrent
synthesis+storage calls with nothing capping in-flight count — overlay flooding plus
repeated paid-provider (Azure/ElevenLabs) API cost. Failure handling itself is solid
(proper `Result<T>` failures, not the silent-swallow pattern found in F4) — this is
purely a missing volume cap. **Fix:** bound concurrent/pending TTS requests per channel.

**F16. Unbounded JSON-nesting recursion in inbound webhook flattening — stack-overflow DoS.**
`WebhookAdapterHelpers.Flatten` recurses once per nesting level with no depth limit. The
256 KiB body-size cap doesn't bound nesting depth — a payload like `[[[[[...]]]]]`
reaches tens of thousands of nesting levels well under that byte cap, risking an
uncatchable `StackOverflowException` that kills the process. **Fix:** add an explicit
recursion-depth cap to `Flatten`, reject payloads that exceed it.

### LOW

**F17. ChatFilter regex has no ReDoS check at save time** (mitigated at match time by a
100ms per-message timeout that fails the match rather than hanging — so this is a
functional gap, not a stability one: a catastrophic-backtracking pattern just silently
never matches its own target input, with no feedback to the broadcaster that their
filter is effectively dead).

**F18. Multiple matching ChatFilters — only the oldest (`CreatedAt`) ever enforces**,
deterministic and not racy, but a newer, stricter filter can be silently shadowed by an
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
> (pipeline update never invalidates command/trigger caches — the sibling of F3; `Pipeline.IsEnabled`
> never checked), and §A1 (pipeline reports Completed after a broken-out run — ships with F4).

## 5. Updated remediation plan

Ordered by severity across both sweeps:

1. **F11 (HIGH), incl. F11b `SavingsJar.Balance`** — atomic balance update, applied to
   both `CurrencyAccount.Balance` and `SavingsJar.Balance` (same race shape, same fix
   pattern, confirmed via repo-wide sibling search — no entity anywhere uses a
   concurrency token). Real double-spend/overdraw; highest-impact correctness bug in
   the whole audit alongside F1.
2. **F1 (CRITICAL, from §2)** — pipeline graph validation on save.
3. **F3 (HIGH, from §2)** — timer/`GraphJsonCache` staleness.
4. **F12, F13 (MEDIUM)** — earning-rule unique constraint; escalation-ladder atomic
   increment. Same root shape as F11, cheap to batch with it.
5. **F4 (MEDIUM-HIGH, from §2)** — chat-send outcome threading.
6. **F5 (MEDIUM-HIGH, from §2)** — timer retry-storm fix.
7. **F14, F16 (MEDIUM)** — TTS SSML escaping; webhook recursion depth cap. Small,
   isolated, mechanical fixes.
8. **F15 (MEDIUM)** — TTS request-volume cap.
9. **F2, F8 (HIGH/MEDIUM, from §2)** — timer interval floor; re-enable timestamp reset.
10. **F6, F7, F10 (MEDIUM, from §2)** — unresolved-variable leakage; builtin-name
    shadowing; duplicate event-response ordering.
11. **F9 (MEDIUM, from §2)** — atomic cooldown check-and-set (DB write-through for
    cross-instance correctness remains a separate scaling decision, not bundled here).
12. **F17, F18, F19 (LOW)** — ReDoS save-time warning, filter-conflict warning,
    Helix already-actioned handling. Lowest priority, no urgency.

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
  validated (F1, F2, F7), not in how failures are reported once caught.
- **YouTube's `SendReplyAsync` degrading to a plain send** (no reply threading available
  on that platform) — explicitly documented, intentional platform limitation, not a bug.

## 3. Remediation plan, in order

1. **F1 (CRITICAL)** — wire `CommandConfigValidator` into `PipelineService.CreateAsync`/
   `UpdateAsync`. Highest priority: this is the one item where a broken/dangerous
   config can currently reach production undetected.
2. **F3** — fix `TimerService.FirePipelineAsync`'s stale-cache read (align it with
   `PipelineEngine`'s `PipelineStep`-first resolution, or regenerate the cache on every
   step write). Directly caused by the same root issue F1 touches (the graph/step
   dual-representation), worth doing in the same pass.
3. **F4** — thread real chat-send outcomes through to `PublishExecutedAsync` and give
   `SendReplyAsync` a boolean return. User-facing correctness (analytics/dashboard
   currently lie about delivery).
4. **F5** — stop the 30-second retry-storm on a broken timer template; mirror the
   already-correct pipeline-firing pattern.
5. **F2, F8** — timer write-path hardening (interval floor; re-enable timestamp reset).
   Small, mechanical, low-risk fixes.
6. **F6** — decide validate-on-write vs. safe-degrade-on-render for unresolved template
   variables (owner call on which approach fits the template-authoring UX), then
   implement.
7. **F7** — builtin-name collision check on custom command create/update.
8. **F10** — unique index (or documented multi-response ordering) on
   `EventResponse(BroadcasterId, EventType)`.
9. **F9** — atomic cooldown check-and-set (mechanical fix, do anytime); DB write-through
   for cross-instance/restart correctness is a scaling investment — recommend treating
   as a separate, explicitly scoped follow-up rather than bundling into this pass, since
   it's not a correctness bug at today's single-instance deploy.

Everything in lane 2 (`IsEnabled` state round-trip) and the reply-mention sweep is
closed — no remaining items there. F1 through F10 above are the full remaining scope
from the original ask; nothing has been fixed yet in this second pass and nothing was
found to be out of scope.
