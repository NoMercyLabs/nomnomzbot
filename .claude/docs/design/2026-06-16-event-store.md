# Event Store & Replay — Design (DRAFT)

Source: design dialogue 2026-06-16. An append-only event journal (event sourcing, pragmatically scoped).

## What
- An **append-only journal** persists every EventSub message + domain event — immutable, ordered, per-tenant.
- **Read models / projections** (analytics, leaderboards, viewer profiles, currency balances) are derived from the journal **and independently persisted** — so normal operation never requires a replay.

## Why (the three wins + more)
- **Backfill:** a new feature replays history → starts with full data, never empty.
- **DR / reinstall:** the journal is the source of truth — replay to rebuild read models after failure.
- **Retrigger / heal:** a handler or widget that missed/failed an event → re-emit those events to recover.
- **Audit:** the journal is a complete, immutable audit trail (GDPR accountability) for free.
- **Correctness:** the currency ledger becomes a true transaction log (balance = fold over events).

## Fit with what's built
- Rides the **event-bus adapter:** the journal is a durable subscriber (persist every event); replay = re-publish from the journal (local or federated).
- **Snapshots (optional):** replay/backfill is **occasional** (new-feature backfill or a scheduled backup, never hot-path), so snapshots are a perf nice-to-have, not a requirement — add them only if replay-from-zero ever gets slow.
- **Hot/cold storage (optional):** every event is journaled **permanently** — nothing ages out, expires, or is purged. A hot-recent + cold-archive split is purely an optional storage/perf optimization (cheaper cold tier for old events), **not** a retention or expiry mechanism: cold events stay fully replayable forever.

## GDPR — immutability vs erasure (resolved)
- An append-only PII log can't be row-deleted. **Resolved by crypto-shred:** PII inside events is encrypted with a per-subject key; **erasure destroys the key** → the journal stays immutable, the PII becomes permanently unreadable. Same mechanism as token erasure — the designs compose.
- Prefer keeping raw PII out of event bodies (store IDs/refs; PII in the erasable store) where practical.

## Schema evolution
- Events are **versioned**; replay applies **upcasters** (transform old event versions → current) so historical events stay replayable as schemas evolve.

## Scope discipline (pragmatic, not dogmatic)
- Event-source where it earns its keep: **EventSub ingest, analytics, economy ledger, moderation/audit, compliance.**
- Plain CRUD config (settings, etc.) stays plain CRUD — don't force everything through the journal (YAGNI).
